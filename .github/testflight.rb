# Manage TestFlight distribution without third-party dependencies.
require 'base64'
require 'fileutils'
require 'json'
require 'net/http'
require 'openssl'
require 'time'
require 'timeout'

class TestFlight
  UPLOAD_RECORD = 'tmp/testflight-upload.json'
  PUBLIC_ARTIFACT = /\Atestflight-(?:macos-)?(upload|attempt)-([0-9]+(?:\.[0-9]+){0,2})(?:-v([0-9]+\.[0-9]+\.[0-9]+))?(?:-[0-9]+)?\z/
  PLATFORMS = { 'iOS' => 'IOS', 'macOS' => 'MAC_OS' }.freeze

  def self.record_public
    FileUtils.mkdir_p(File.dirname(UPLOAD_RECORD))
    File.write(UPLOAD_RECORD, JSON.generate(ENV.to_h.slice('BUILD_NUMBER', 'RELEASE_VERSION', 'GITHUB_SHA', 'GITHUB_RUN_ID', 'TESTFLIGHT_PLATFORM')))
  end

  def self.resume_public
    record = JSON.parse(File.read(UPLOAD_RECORD))
    unless record.fetch('TESTFLIGHT_PLATFORM', 'iOS') == ENV.fetch('TESTFLIGHT_PLATFORM', 'iOS') &&
           %w[RELEASE_VERSION GITHUB_SHA GITHUB_RUN_ID].all? { |key| record.fetch(key) == ENV.fetch(key) }
      raise 'The saved TestFlight upload does not match this platform, release, commit or workflow run'
    end
    number = record.fetch('BUILD_NUMBER')
    raise 'Invalid saved build number' unless number.match?(/\A[0-9]+(?:\.[0-9]+){0,2}\z/)
    number
  end

  def self.github_pages(path)
    output = IO.popen(['gh', 'api', '--paginate', '--slurp', "repos/#{ENV.fetch('GITHUB_REPOSITORY')}/#{path}"], &:read)
    raise 'Could not read GitHub upload history' unless $?.success?
    JSON.parse(output)
  end

  def public_artifacts
    self.class.github_pages('actions/artifacts?per_page=100').flat_map { |page| page.fetch('artifacts') }.select do |artifact|
      name = artifact.fetch('name')
      next false unless name.match?(PUBLIC_ARTIFACT) && name.start_with?('testflight-macos-') == (@platform == 'MAC_OS')
      run = artifact['workflow_run']
      next false unless run
      run.fetch('head_branch') == 'main' && run.fetch('head_repository_id') == run.fetch('repository_id')
    end
  end

  def build_uploads(deadline = nil)
    list("/v1/apps/#{@app.fetch('id')}/buildUploads", {}, deadline)
      .select { |upload| upload.dig('attributes', 'platform') == @platform }
  end

  def initialize(platform = ENV.fetch('TESTFLIGHT_PLATFORM', 'iOS'))
    @platform = PLATFORMS.fetch(platform)
    @key = OpenSSL::PKey.read(ENV.fetch('APP_STORE_KEY'))
    apps = list('/v1/apps', 'filter[bundleId]' => 'com.mikemcquaid.Infinigame')
    raise 'Expected one Infinigame App Store Connect record' unless apps.length == 1
    @app = apps.first
  end

  def request(method, path, body = nil, deadline = nil)
    processing_time_left(deadline) if deadline
    uri = URI.join('https://api.appstoreconnect.apple.com', path)
    unless uri.scheme == 'https' && uri.host == 'api.appstoreconnect.apple.com' && uri.port == 443
      raise 'Unexpected App Store Connect pagination URL'
    end
    now = Time.now.to_i
    token = [
      { alg: 'ES256', kid: ENV.fetch('APP_STORE_KEY_ID'), typ: 'JWT' },
      { iss: ENV.fetch('APP_STORE_ISSUER_ID'), iat: now, exp: now + 600, aud: 'appstoreconnect-v1' }
    ].map { |part| Base64.urlsafe_encode64(JSON.generate(part), padding: false) }.join('.')
    # JWT uses fixed-width r and s, rather than OpenSSL's DER encoding.
    signature = OpenSSL::ASN1.decode(@key.sign('SHA256', token)).value.map do |integer|
      [integer.value.to_s(16).rjust(64, '0')].pack('H*')
    end.join
    req = Net::HTTPGenericRequest.new(method, !body.nil?, true, uri.request_uri)
    req['Authorization'] = "Bearer #{token}.#{Base64.urlsafe_encode64(signature, padding: false)}"
    req['Content-Type'] = 'application/json'
    req.body = JSON.generate(body) if body
    response = Timeout.timeout(deadline && processing_time_left(deadline), RuntimeError,
                               'Apple processing did not finish within 20 minutes') do
      Net::HTTP.start(uri.host, uri.port, use_ssl: true, open_timeout: 30, read_timeout: 60) do |http|
        http.request(req)
      end
    end
    processing_time_left(deadline) if deadline
    unless response.is_a?(Net::HTTPSuccess)
      raise "App Store Connect #{method} #{uri.path}: HTTP #{response.code}: #{response.body}"
    end
    response.body.to_s.empty? ? {} : JSON.parse(response.body)
  end

  def list(path, query = {}, deadline = nil)
    path = "#{path}?#{URI.encode_www_form(query.merge('limit' => 200))}"
    records = []
    while path
      processing_time_left(deadline) if deadline
      page = request('GET', path, nil, deadline)
      processing_time_left(deadline) if deadline
      records.concat(page.fetch('data'))
      yield page if block_given?
      path = page.dig('links', 'next')
    end
    records
  end

  def builds(query = {}, deadline = nil)
    list('/v1/builds', {
      'filter[app]' => @app.fetch('id'), 'filter[preReleaseVersion.platform]' => @platform,
      'sort' => '-uploadedDate', 'include' => 'preReleaseVersion,buildBetaDetail'
    }.merge(query), deadline) do |page|
      @versions ||= {}
      @details_by_id ||= {}
      page.fetch('included', []).each do |record|
        @versions[record.fetch('id')] = record.dig('attributes', 'version') if record['type'] == 'preReleaseVersions'
        @details_by_id[record.fetch('id')] = record if record['type'] == 'buildBetaDetails'
      end
    end
  end

  def next_build_number(commit_count)
    versions = builds.map { |build| build.fetch('attributes').fetch('version') }
    versions.concat(build_uploads.map { |upload| upload.fetch('attributes').fetch('cfBundleVersion') })
    versions.concat(public_artifacts.map { |artifact| PUBLIC_ARTIFACT.match(artifact.fetch('name'))[2] })
    major = versions.map do |version|
      raise "Invalid Apple build number: #{version.inspect}" unless version.match?(/\A[0-9]+(?:\.[0-9]+){0,2}\z/)
      version.split('.').first.to_i
    end.max || 0
    [commit_count, major + 1].max.to_s
  end

  def public_group
    name = ENV['TESTFLIGHT_PUBLIC_GROUP'].to_s
    name = 'Public Beta' if name.empty?
    groups = tester_groups.select do |group|
      group.dig('attributes', 'name') == name && group.dig('attributes', 'isInternalGroup') == false
    end
    raise "Expected one external group named #{name.inspect}; complete the TestFlight setup." unless groups.length == 1
    groups.first.fetch('id')
  end

  def tester_groups
    @tester_groups ||= list("/v1/apps/#{@app.fetch('id')}/betaGroups")
  end

  def internal_group
    groups = tester_groups.select { |group| group.dig('attributes', 'isInternalGroup') }
    name = ENV['TESTFLIGHT_INTERNAL_GROUP'].to_s
    groups = groups.select { |group| group.dig('attributes', 'name') == name } unless name.empty?
    # Apple already distributes eligible uploads to these groups during processing.
    return if groups.any? { |group| group.dig('attributes', 'hasAccessToAllBuilds') }
    raise 'Enable internal automatic distribution or set TESTFLIGHT_INTERNAL_GROUP to one internal group.' unless groups.one?
    groups.first.fetch('id')
  end

  def build_identity(build)
    [@versions.fetch(build.dig('relationships', 'preReleaseVersion', 'data', 'id')),
     build.fetch('attributes').fetch('version')]
  end

  def upload_identity(upload)
    [upload.fetch('attributes').fetch('cfBundleShortVersionString'), upload.fetch('attributes').fetch('cfBundleVersion')]
  end

  def public_upload_needed?
    return false unless builds('filter[version]' => ENV.fetch('BUILD_NUMBER'),
                               'filter[preReleaseVersion.version]' => ENV.fetch('RELEASE_VERSION')).empty?
    matching_uploads.empty?
  end

  def matching_uploads(deadline = nil)
    accepted = build_uploads(deadline).select do |upload|
      upload_identity(upload) == [ENV.fetch('RELEASE_VERSION'), ENV.fetch('BUILD_NUMBER')]
    end
    usable = accepted.reject { |upload| %w[FAILED AWAITING_UPLOAD].include?(upload.dig('attributes', 'state', 'state')) }
    return usable unless usable.empty?
    recovery = 'correct the upload problem and push a new build to main.'
    if accepted.any? { |upload| upload.dig('attributes', 'state', 'state') == 'AWAITING_UPLOAD' }
      raise "Apple has not received the binary: AWAITING_UPLOAD; #{recovery}"
    end
    if accepted.any? { |upload| upload.dig('attributes', 'state', 'state') == 'FAILED' }
      raise "Apple rejected this upload; #{recovery}"
    end
    usable
  end

  def processing_time_left(deadline)
    remaining = deadline - Process.clock_gettime(Process::CLOCK_MONOTONIC)
    raise 'Apple processing did not finish within 20 minutes' unless remaining.positive?
    remaining
  end

  def distribute
    deadline = Process.clock_gettime(Process::CLOCK_MONOTONIC) + 1200
    loop do
      processing_time_left(deadline)
      build = builds({ 'filter[version]' => ENV.fetch('BUILD_NUMBER'),
                       'filter[preReleaseVersion.version]' => ENV.fetch('RELEASE_VERSION') }, deadline).first
      processing_time_left(deadline)
      matching_uploads(deadline) unless build
      state = build&.dig('attributes', 'processingState')
      raise "Apple processing failed: #{state}" if %w[FAILED INVALID].include?(state)
      if state == 'VALID'
        raise 'Unexpected build audience' unless build.dig('attributes', 'buildAudienceType') == 'APP_STORE_ELIGIBLE'
        raise 'The uploaded build has expired' if build.dig('attributes', 'expired')
        detail = request('GET', "/v1/builds/#{build.fetch('id')}/buildBetaDetail", nil, deadline).fetch('data')
        processing_time_left(deadline)
        state = detail&.dig('attributes', 'externalBuildState')
        unless state.nil? || state == 'PROCESSING'
          return distribute_public(build.fetch('id'), detail)
        end
      end
      puts "Waiting for Apple processing (#{state || 'not visible yet'})"
      sleep [30, processing_time_left(deadline)].min
    end
  end

  def distribute_public(id, detail, already_assigned: false)
    group = internal_group
    if group
      request('POST', "/v1/betaGroups/#{group}/relationships/builds", data: [{ type: 'builds', id: id }])
    end
    group = public_group
    state = detail.fetch('attributes').fetch('externalBuildState')
    case state
    when 'READY_FOR_BETA_SUBMISSION'
      locale = @app.fetch('attributes').fetch('primaryLocale')
      existing = list("/v1/builds/#{id}/betaBuildLocalizations").find { |item| item.dig('attributes', 'locale') == locale }
      notes = File.read(File.expand_path('../TESTFLIGHT.md', __dir__))[/^## What to Test\s*\n(.+?)(?=^## |\z)/m, 1].to_s.strip
      raise 'Enter What to Test in TESTFLIGHT.md.' if notes.empty?
      if existing
        request('PATCH', "/v1/betaBuildLocalizations/#{existing.fetch('id')}", data: {
          type: 'betaBuildLocalizations', id: existing.fetch('id'), attributes: { whatsNew: notes }
        })
      else
        request('POST', '/v1/betaBuildLocalizations', data: {
          type: 'betaBuildLocalizations', attributes: { locale: locale, whatsNew: notes },
          relationships: { build: { data: { type: 'builds', id: id } } }
        })
      end
      request('PATCH', "/v1/buildBetaDetails/#{detail.fetch('id')}", data: {
        type: 'buildBetaDetails', id: detail.fetch('id'), attributes: { autoNotifyEnabled: true }
      })
      request('POST', '/v1/betaAppReviewSubmissions', data: {
        type: 'betaAppReviewSubmissions', relationships: { build: { data: { type: 'builds', id: id } } }
      })
    when 'READY_FOR_BETA_TESTING', 'BETA_APPROVED', 'WAITING_FOR_BETA_REVIEW', 'IN_BETA_REVIEW', 'IN_BETA_TESTING'
      # Already submitted or available; do not submit twice.
    else
      raise "Build is not ready for external testing: #{state}"
    end
    unless already_assigned
      request('POST', "/v1/betaGroups/#{group}/relationships/builds", data: [{ type: 'builds', id: id }])
    end
    if %w[READY_FOR_BETA_TESTING BETA_APPROVED].include?(state)
      request('POST', '/v1/buildBetaNotifications', data: {
        type: 'buildBetaNotifications', relationships: { build: { data: { type: 'builds', id: id } } }
      })
    end
    puts 'Build added to the configured external group; testers will be notified after any required Apple approval.'
  end

  # Only revisit builds reserved by this repository, including when Apple deferred review.
  def retry_distribution
    identities = public_artifacts.map do |artifact|
      match = PUBLIC_ARTIFACT.match(artifact.fetch('name'))
      [match[3], match[2]] if match[1] == 'upload' && match[3] && !artifact['expired']
    end.compact
    return if identities.empty?
    assigned = list("/v1/betaGroups/#{public_group}/builds", 'fields[builds]' => 'version').map { |build| build.fetch('id') }
    failed = []
    builds.reverse_each do |build|
      id = build.fetch('id')
      next unless build.dig('attributes', 'processingState') == 'VALID' && !build.dig('attributes', 'expired') &&
                  build.dig('attributes', 'buildAudienceType') == 'APP_STORE_ELIGIBLE' && identities.include?(build_identity(build))
      detail = @details_by_id[build.dig('relationships', 'buildBetaDetail', 'data', 'id')]
      state = detail&.dig('attributes', 'externalBuildState')
      next if state.nil? || %w[PROCESSING EXPIRED BETA_REJECTED].include?(state)
      next if assigned.include?(id) && %w[WAITING_FOR_BETA_REVIEW IN_BETA_REVIEW IN_BETA_TESTING].include?(state)
      begin
        distribute_public(id, detail, already_assigned: assigned.include?(id))
      rescue RuntimeError => e
        warn "Build #{id}: #{e.message}"
        failed << id
      end
    end
    raise "Distribution pending for #{failed.join(', ')}" unless failed.empty?
  end
end

if $PROGRAM_NAME == __FILE__
  begin
    case ARGV
    when ['next-build-number'] then puts TestFlight.new.next_build_number(Integer(`script/build-number`, 10))
    when ['record-public'] then TestFlight.record_public
    when ['resume-public'] then puts TestFlight.resume_public
    when ['plan-public']
      api = TestFlight.new
      needed = api.public_upload_needed?
      File.open(ENV.fetch('GITHUB_OUTPUT'), 'a') { |file| file.puts "needed=#{needed}" }
    when ['public'] then TestFlight.new.distribute
    when ['retry-distribution'] then TestFlight.new.retry_distribution
    else raise 'Usage: ruby .github/testflight.rb next-build-number|record-public|resume-public|plan-public|public|retry-distribution'
    end
  rescue StandardError => error
    warn error.message
    exit 1
  end
end
