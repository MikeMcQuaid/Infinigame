# Exercise TestFlight lifecycle decisions without contacting Apple or GitHub.
require 'json'
require 'time'
require 'tmpdir'
require 'fileutils'
require 'yaml'
require 'open3'
require 'rbconfig'
require_relative '../.github/testflight'

def assert(condition, message)
  raise message unless condition
end

def rejects(message)
  begin
    yield
  rescue RuntimeError => error
    assert(error.message.include?(message), "Unexpected error: #{error.message}")
    return
  end
  raise "Expected rejection: #{message}"
end

class TestFlightFixture < TestFlight
  NOW = Time.utc(2026, 11, 18, 10, 5)

  attr_reader :writes
  attr_accessor :records, :groups, :processing, :state, :details, :uploads, :assigned, :fail_group_once, :artifacts

  def initialize(platform = 'iOS')
    @platform = PLATFORMS.fetch(platform)
    @app = { 'id' => 'app', 'attributes' => { 'primaryLocale' => 'en-GB' } }
    @records = []
    @uploads = []
    @assigned = []
    @artifacts = []
    @groups = [{ 'id' => 'public', 'attributes' => { 'name' => 'Public Beta', 'isInternalGroup' => false } }]
    @groups << { 'id' => 'team', 'attributes' => { 'name' => 'Team', 'isInternalGroup' => true, 'hasAccessToAllBuilds' => true } }
    @writes = []
    @processing = []
    @state = 'READY_FOR_BETA_SUBMISSION'
    @details = []
  end

  def request(method, path, body = nil, deadline = nil)
    if method != 'GET'
      @writes << [method, path, body]
      @state = 'WAITING_FOR_BETA_REVIEW' if path == '/v1/betaAppReviewSubmissions'
      if path == '/v1/betaGroups/public/relationships/builds' && @fail_group_once
        @fail_group_once = false
        raise 'Group assignment failed'
      end
      return {}
    end
    uri = URI(path)
    case uri.path
    when '/v1/builds'
      query = URI.decode_www_form(uri.query).to_h
      assert(query['include'] == 'preReleaseVersion,buildBetaDetail', 'Request marketing versions and beta states')
      assert(query.fetch('filter[app]') == 'app', 'Build query must be scoped to this app')
      assert(query.fetch('filter[preReleaseVersion.platform]') == @platform, 'Build query must be scoped to the selected platform')
      if query['filter[version]']
        assert(query['filter[version]'] == ENV.fetch('BUILD_NUMBER'), 'Must match the saved build number')
        assert(query['filter[preReleaseVersion.version]'] == '1.2.3', 'Must match marketing version too')
        return { 'data' => @processing.shift || [] }
      end
      records = @records
      if query['filter[preReleaseVersion.version]']
        records = records.select { |build| build.dig('attributes', 'marketingVersion') == query['filter[preReleaseVersion.version]'] }
      end
      { 'data' => records, 'included' => records.map do |build|
        { 'type' => 'preReleaseVersions', 'id' => build.dig('relationships', 'preReleaseVersion', 'data', 'id'),
          'attributes' => { 'version' => build.dig('attributes', 'marketingVersion') || '1.2.3' } }
      end + records.map do |build|
        { 'type' => 'buildBetaDetails', 'id' => build.fetch('id'), 'attributes' => { 'externalBuildState' => @state } }
      end }
    when '/v1/apps/app/buildUploads'
      { 'data' => @uploads }
    when '/v1/betaGroups/public/builds'
      { 'data' => @assigned.map { |id| { 'id' => id } } }
    when '/v1/apps/app/betaGroups'
      { 'data' => @groups }
    when '/v1/builds/current/betaBuildLocalizations'
      { 'data' => [] }
    when '/v1/builds/current/buildBetaDetail'
      { 'data' => { 'id' => 'detail', 'attributes' => { 'externalBuildState' => @details.shift || @state,
                                                     'internalBuildState' => 'IN_BETA_TESTING' } } }
    else
      raise "Unexpected request: #{path}"
    end
  end

  def public_artifacts
    @artifacts
  end

  def sleep(_seconds)
    raise 'Polling did not terminate' if @processing.empty? && @details.empty?
  end
end

ENV['RELEASE_VERSION'] = '1.2.3'
ENV['BUILD_NUMBER'] = '42.2'
ENV.delete('TESTFLIGHT_PUBLIC_GROUP')
ENV.delete('TESTFLIGHT_INTERNAL_GROUP')
now = TestFlightFixture::NOW
def build(id, time, audience = 'INTERNAL_ONLY', **attributes)
  { 'id' => id, 'type' => 'builds', 'attributes' => {
    'uploadedDate' => time.iso8601, 'buildAudienceType' => audience,
    'processingState' => 'VALID', 'expired' => false, 'version' => '42.2'
  }.merge(attributes.transform_keys(&:to_s)),
    'relationships' => { 'preReleaseVersion' => { 'data' => { 'id' => attributes.fetch(:marketingVersion, '1.2.3') } },
                         'buildBetaDetail' => { 'data' => { 'id' => id } } } }
end

api = TestFlightFixture.new
assert(api.next_build_number(18) == '18', 'Start TestFlight numbering at the main commit count')
api.records = [build('previous', now, version: '18')]
assert(api.next_build_number(23) == '23', 'Commits between uploads advance the build number')
assert(api.next_build_number(18) == '19', 'A second upload of the same commit needs a fresh number')
api.records = [build('old-run', now, version: '42.1'), build('newer-run', now, version: '43.1')]
assert(api.next_build_number(18) == '44', 'A private retry must exceed later uploads')
api.uploads = [
  { 'attributes' => { 'platform' => 'IOS', 'cfBundleVersion' => '44.9.99' } },
  { 'attributes' => { 'platform' => 'MAC_OS', 'cfBundleVersion' => '999' } }
]
assert(api.next_build_number(18) == '45', 'Include unfinished iOS uploads but exclude other platforms')
mac = TestFlightFixture.new('macOS')
mac.uploads = api.uploads
assert(mac.next_build_number(18) == '1000', 'Use the native Mac upload history for Mac builds')
api.records = [build('expired', now, version: '100.2', expired: true)]
assert(api.next_build_number(18) == '101', 'Compare numerically and never reuse expired build numbers')
api.records[0]['attributes']['version'] = 'not-a-number'
rejects('build number') { api.next_build_number(18) }

def artifact(name, time)
  name = name.sub(/\A(testflight-(?:upload|attempt)-[0-9.]+)/, '\1-v1.2.3')
  { 'name' => name, 'created_at' => time.iso8601 }
end

def upload(number, time, state = 'PROCESSING', **attributes)
  { 'id' => "upload-#{number}", 'attributes' => {
    'cfBundleVersion' => number, 'cfBundleShortVersionString' => '1.2.3',
    'platform' => 'IOS', 'createdDate' => time.iso8601, 'uploadedDate' => nil,
    'state' => { 'state' => state }
  }.merge(attributes.transform_keys(&:to_s)) }
end

api = TestFlightFixture.new
api.artifacts = [artifact('testflight-upload-80', now)]
assert(api.next_build_number(18) == '81', 'Unuploaded intents reserve their build number too')

# Reconciliation can initially reuse processing work that later fails.
api = TestFlightFixture.new
api.uploads = [upload('42.2', now)]
assert(!api.public_upload_needed?, 'Reuse the processing upload')
api.uploads[0]['attributes']['state']['state'] = 'FAILED'
rejects('push a new build') { api.distribute }

api = TestFlightFixture.new
api.uploads = [upload('42.2', now)]
api.processing = [[], [build('current', now, 'APP_STORE_ELIGIBLE')]]
api.distribute
assert(api.writes.count { |w| w[1] == '/v1/betaAppReviewSubmissions' } == 1,
       'A resumed upload still needs its first beta review submission')

api = TestFlightFixture.new
assert(api.public_upload_needed?, 'An identity missing from both Apple endpoints can upload')
api.processing = [[build('current', now, 'APP_STORE_ELIGIBLE', processingState: 'PROCESSING')]]
assert(!api.public_upload_needed?, 'An accepted build must never be uploaded again')
api.uploads = [upload('42.2', now)]
assert(!api.public_upload_needed?, 'An accepted upload without a build must resume processing')
api.uploads[0]['attributes']['state']['state'] = 'FAILED'
rejects('push a new build') { api.public_upload_needed? }
api.uploads[0]['attributes']['cfBundleShortVersionString'] = '1.2.4'
api.records = [build('other-version', now, 'APP_STORE_ELIGIBLE', marketingVersion: '1.2.4')]
assert(api.public_upload_needed?, 'Reconciliation must match the release version too')
api.uploads[0]['attributes']['cfBundleShortVersionString'] = '1.2.3'
api.uploads[0]['attributes']['platform'] = 'MAC_OS'
assert(api.public_upload_needed?, 'Reconciliation must ignore other platforms')

# Earlier failed transfers must not hide a later accepted upload.
api = TestFlightFixture.new
api.uploads = [upload('42.2', now - 60, 'FAILED'), upload('42.2', now)]
assert(!api.public_upload_needed?, 'Reuse a later transfer after a failed attempt with the same identity')
api.uploads = [upload('42.2', now, 'AWAITING_UPLOAD')]
rejects('AWAITING_UPLOAD') { api.public_upload_needed? }

api = TestFlightFixture.new
api.records = (1..6).map { |n| build("previous-#{n}", now, 'APP_STORE_ELIGIBLE', version: n.to_s) }
assert(api.public_upload_needed?, 'Existing public builds of this marketing version must allow another push')
assert(api.next_build_number(4) == '7', 'Later pushes advance even after multiple public uploads on the same day')

FileUtils.mkdir_p('tmp')
Dir.mktmpdir('testflight-retry.', 'tmp') do |scratch|
  Dir.chdir(scratch) do
    ENV['GITHUB_SHA'] = 'original-commit'
    ENV['GITHUB_RUN_ID'] = '42'
    TestFlight.record_public
    assert(TestFlight.resume_public == '42.2', 'Restore the uploaded build number for this run')
    ENV['GITHUB_SHA'] = 'another-commit'
    rejects('does not match') { TestFlight.resume_public }
    ENV['GITHUB_SHA'] = 'original-commit'
    ENV['RELEASE_VERSION'] = '1.2.4'
    rejects('does not match') { TestFlight.resume_public }
    ENV['RELEASE_VERSION'] = '1.2.3'
    ENV['GITHUB_RUN_ID'] = '43'
    rejects('does not match') { TestFlight.resume_public }
    ENV['GITHUB_RUN_ID'] = '42'

    api = TestFlightFixture.new
    api.fail_group_once = true
    api.processing = [[build('current', now, 'APP_STORE_ELIGIBLE')]] * 2
    rejects('Group assignment failed') { api.distribute }
    previous_writes = api.writes.length
    ENV['BUILD_NUMBER'] = TestFlight.resume_public
    api.distribute
    assert(api.writes.count { |w| w[1] == '/v1/betaAppReviewSubmissions' } == 1, 'A retry must reuse the accepted review submission')
    assert(api.writes.drop(previous_writes).map { |w| w[1] } == ['/v1/betaGroups/public/relationships/builds'],
           'Resume group assignment without rewriting a build already in review')
  end
end

api = TestFlightFixture.new
api.processing = [[build('current', now, processingState: 'INVALID')]]
rejects('INVALID') { api.distribute }
api.processing = [[build('current', now, 'INTERNAL_ONLY')]]
rejects('audience') { api.distribute }

api = TestFlightFixture.new
api.groups.last['attributes']['hasAccessToAllBuilds'] = false
api.processing = [[build('current', now, 'APP_STORE_ELIGIBLE')]]
api.distribute
assert(api.writes.first[1] == '/v1/betaGroups/team/relationships/builds', 'Assign the internal group before attempting external review')
api = TestFlightFixture.new
api.groups.last['attributes']['hasAccessToAllBuilds'] = false
api.groups << { 'id' => 'other', 'attributes' => { 'name' => 'Other', 'isInternalGroup' => true } }
rejects('TESTFLIGHT_INTERNAL_GROUP') { api.internal_group }
ENV['TESTFLIGHT_INTERNAL_GROUP'] = 'Team'
assert(api.internal_group == 'team', 'Resolve the intended internal group by name')
ENV.delete('TESTFLIGHT_INTERNAL_GROUP')

api = TestFlightFixture.new
api.records = [build('current', now, 'APP_STORE_ELIGIBLE'), build('manual', now, 'APP_STORE_ELIGIBLE', version: '0'),
               build('old', now, 'APP_STORE_ELIGIBLE', expired: true, version: '41')]
api.artifacts = %w[42.2 41].map { |n| artifact("testflight-upload-#{n}", now) }
api.retry_distribution
assert(api.writes.any? { |w| w[1] == '/v1/betaGroups/public/relationships/builds' } &&
       api.writes.none? { |w| %w[manual old].any? { |id| w.inspect.include?(id) } }, 'Retry only live builds reserved by main')
api.assigned = ['current']
api.state = 'IN_BETA_TESTING'
api.writes.clear
api.retry_distribution
assert(api.writes.empty?, 'Hourly checks leave distributed builds alone')
api.state = 'READY_FOR_BETA_TESTING'
api.retry_distribution
assert(api.writes.map { |w| w[1] } == ['/v1/buildBetaNotifications'], 'Notify approved builds already in the public group')

api = TestFlightFixture.new
api.processing = [[build('current', now, 'APP_STORE_ELIGIBLE')]]
api.distribute
assert(api.writes.any? { |w| w[1] == '/v1/betaGroups/public/relationships/builds' && w[2][:data] == [{ type: 'builds', id: 'current' }] },
       'Release build must be added to the external group')
assert(api.writes.any? { |w| w[1] == '/v1/buildBetaDetails/detail' && w[2].dig(:data, :attributes, :autoNotifyEnabled) == true },
       'Future testers must be notified automatically')
assert(api.writes.any? { |w| w[1] == '/v1/betaBuildLocalizations' && w[2].dig(:data, :attributes, :locale) == 'en-GB' },
       'Supply What to Test in the app locale')
assert(api.writes.count { |w| w[1] == '/v1/betaAppReviewSubmissions' } == 1, 'Submit exactly once for review')
assert(api.writes.index { |w| w[1] == '/v1/betaAppReviewSubmissions' } <
       api.writes.index { |w| w[1] == '/v1/betaGroups/public/relationships/builds' },
       'Submit the build before assigning it to external testers')
%w[WAITING_FOR_BETA_REVIEW IN_BETA_REVIEW IN_BETA_TESTING READY_FOR_BETA_TESTING BETA_APPROVED].each do |state|
  api = TestFlightFixture.new
  api.state = state
  api.processing = [[build('current', now, 'APP_STORE_ELIGIBLE')]]
  api.distribute
  assert(api.writes.none? { |w| w[1] == '/v1/betaAppReviewSubmissions' }, 'Do not resubmit reviewed builds')
  if %w[READY_FOR_BETA_TESTING BETA_APPROVED].include?(state)
    assert(api.writes.index { |w| w[1] == '/v1/betaGroups/public/relationships/builds' } <
           api.writes.index { |w| w[1] == '/v1/buildBetaNotifications' },
           'Notify testers of an approved build after adding their group')
  end
end
api = TestFlightFixture.new
api.state = 'BETA_REJECTED'
api.processing = [[build('current', now, 'APP_STORE_ELIGIBLE')]]
rejects('BETA_REJECTED') { api.distribute }
api = TestFlightFixture.new
api.details = ['PROCESSING', 'READY_FOR_BETA_SUBMISSION']
api.processing = [[build('current', now, 'APP_STORE_ELIGIBLE')]] * 2
api.distribute
assert(api.writes.count { |w| w[1] == '/v1/betaAppReviewSubmissions' } == 1,
       'Wait for TestFlight processing even after archive processing is VALID')
# Exercise real pagination, JWT signing and HTTP errors with a fake transport.
original_http = Net::HTTP.method(:start)
key = OpenSSL::PKey::EC.generate('prime256v1')
Dir.mktmpdir('api-key-', 'tmp') do |dir|
  path = File.join(dir, 'AuthKey.p8')
  pem = key.to_pem
  [pem, Base64.strict_encode64(pem), pem.gsub("\n") { '\n' }].each do |secret|
    _, error, status = Open3.capture3({ 'APP_STORE_KEY' => secret }, RbConfig.ruby, '.github/testflight.rb', 'write-key', path)
    assert(status.success?, "Could not prepare a signing key for Xcode: #{error}")
    assert(OpenSSL::PKey.read(File.read(path)).public_key == key.public_key, 'Preserve the Apple signing key')
  end
  ['invalid-key-fixture', OpenSSL::PKey::RSA.generate(1024).to_pem].each do |bad|
    output, error, status = Open3.capture3({ 'APP_STORE_KEY' => bad }, RbConfig.ruby, '.github/testflight.rb', 'write-key', path)
    assert(!status.success? && error.include?('NOTARIZATION_KEY') && !(output + error).include?(bad), 'Reject invalid secrets without printing them')
  end
end
ENV['APP_STORE_KEY'] = Base64.strict_encode64(key.to_pem)
ENV['APP_STORE_KEY_ID'] = 'test-key'
ENV['APP_STORE_ISSUER_ID'] = 'test-issuer'
responses = [
  { 'data' => [{ 'id' => 'app', 'attributes' => { 'primaryLocale' => 'en-GB' } }] },
  { 'data' => [{ 'id' => 'first' }], 'links' => { 'next' => 'https://api.appstoreconnect.apple.com/v1/builds?cursor=next' } },
  { 'data' => [{ 'id' => 'second' }] }
]
transport = Object.new
transport.define_singleton_method(:request) do |req|
  header, payload, signature = req['Authorization'].delete_prefix('Bearer ').split('.')
  assert(JSON.parse(Base64.urlsafe_decode64(header))['alg'] == 'ES256', 'JWT must use ES256')
  claims = JSON.parse(Base64.urlsafe_decode64(payload))
  assert(claims['aud'] == 'appstoreconnect-v1' && claims['iss'] == 'test-issuer', 'JWT audience and issuer')
  assert(claims['exp'] - claims['iat'] == 600, 'JWT should expire after ten minutes')
  raw = Base64.urlsafe_decode64(signature)
  assert(raw.bytesize == 64, 'JWT signature must contain fixed-width r and s')
  der = OpenSSL::ASN1::Sequence.new([raw[0, 32], raw[32, 32]].map do |part|
    OpenSSL::ASN1::Integer.new(OpenSSL::BN.new(part.unpack1('H*'), 16))
  end).to_der
  assert(key.verify('SHA256', der, "#{header}.#{payload}"), 'JWT signature does not verify')
  response = if responses.empty?
               Net::HTTPForbidden.new('1.1', '403', 'Forbidden')
             else
               Net::HTTPOK.new('1.1', '200', 'OK')
             end
  response.body = JSON.generate(responses.shift || { 'errors' => [{ 'detail' => 'Denied' }] })
  response.instance_variable_set(:@read, true)
  response
end
Net::HTTP.define_singleton_method(:start) { |*_args, &block| block.call(transport) }
begin
  api = TestFlight.new
  assert(api.list('/v1/builds').map { |b| b['id'] } == %w[first second], 'Read every page')
  rejects('HTTP 403') { api.public_group }
  rejects('pagination URL') { api.request('GET', 'https://example.org/steal-token') }
ensure
  Net::HTTP.define_singleton_method(:start, original_http)
end

# A late page cannot trigger another GET, including upload-history pages.
original_clock = Process.method(:clock_gettime)
%w[/v1/builds /v1/apps/app/buildUploads].each do |late_path|
  clock = 0
  paths = []
  Process.define_singleton_method(:clock_gettime) { |_clock| clock }
  transport = Object.new
  transport.define_singleton_method(:request) do |req|
    path = URI(req.path).path
    paths << path
    clock = 1200 if path == late_path
    response = Net::HTTPOK.new('1.1', '200', 'OK')
    page = { 'data' => [] }
    page['links'] = { 'next' => "https://api.appstoreconnect.apple.com#{path}?cursor=next" } if path == late_path
    response.body = JSON.generate(page)
    response.instance_variable_set(:@read, true)
    response
  end
  Net::HTTP.define_singleton_method(:start) { |*_args, &block| block.call(transport) }
  api = TestFlight.allocate
  api.instance_variable_set(:@key, key)
  api.instance_variable_set(:@app, { 'id' => 'app' })
  begin
    rejects('20 minutes') { api.distribute }
    expected = late_path == '/v1/builds' ? ['/v1/builds'] : ['/v1/builds', late_path]
    assert(paths == expected, 'Do not fetch a second page after the processing deadline')
  ensure
    Net::HTTP.define_singleton_method(:start, original_http)
    Process.define_singleton_method(:clock_gettime, original_clock)
  end
end

# The total request budget covers connection setup and reading the response.
%w[connect response].each do |phase|
  completed = false
  transport = Object.new
  transport.define_singleton_method(:request) do |_req|
    sleep 1
    completed = true
  end
  Net::HTTP.define_singleton_method(:start) do |*_args, &block|
    if phase == 'connect'
      sleep 1
      completed = true
    end
    block.call(transport)
  end
  api = TestFlight.allocate
  api.instance_variable_set(:@key, key)
  begin
    rejects('20 minutes') do
      api.request('GET', '/v1/builds', nil, Process.clock_gettime(Process::CLOCK_MONOTONIC) + 0.05)
    end
    assert(!completed, "The deadline must interrupt #{phase} rather than wait for the HTTP timeout")
  ensure
    Net::HTTP.define_singleton_method(:start, original_http)
  end
end

original_clock = Process.method(:clock_gettime)
clock = 0
Process.define_singleton_method(:clock_gettime) { |_clock| clock += 1201 }
begin
  rejects('20 minutes') { TestFlightFixture.new.distribute }
ensure
  Process.define_singleton_method(:clock_gettime, original_clock)
end

# Responses arriving at the deadline must not trigger distribution.
['/v1/builds', '/v1/apps/app/buildUploads', '/v1/builds/current/buildBetaDetail'].each do |slow_path|
  clock = 0
  reads = []
  Process.define_singleton_method(:clock_gettime) { |_clock| clock }
  api = TestFlightFixture.new
  api.processing = [[build('current', now, 'APP_STORE_ELIGIBLE')]] unless slow_path.include?('buildUploads')
  api.define_singleton_method(:request) do |method, path, body = nil, deadline = nil|
    reads << URI(path).path
    clock = 1200 if URI(path).path == slow_path
    super(method, path, body, deadline)
  end
  begin
    rejects('20 minutes') { api.distribute }
    assert(api.writes.empty?, 'A late processing response must never distribute')
    assert(reads.last == slow_path, 'Do not start another request after a late response')
  ensure
    Process.define_singleton_method(:clock_gettime, original_clock)
  end
end

clock = 0
sleeps = []
Process.define_singleton_method(:clock_gettime) { |_clock| clock }
api = TestFlightFixture.new
api.processing = [[build('current', now, 'APP_STORE_ELIGIBLE', processingState: 'PROCESSING')],
                  [build('current', now, 'APP_STORE_ELIGIBLE')]]
api.define_singleton_method(:request) do |method, path, body = nil, deadline = nil|
  clock = 1199 if clock.zero?
  super(method, path, body, deadline)
end
api.define_singleton_method(:sleep) do |seconds|
  sleeps << seconds
  clock += seconds
end
begin
  rejects('20 minutes') { api.distribute }
  assert(sleeps == [1], 'Sleep only for the remaining processing window')
  assert(api.processing.length == 1, 'Do not poll again once the deadline is reached')
  assert(api.writes.empty?, 'Do not distribute a build found after the deadline')
  clock = 0
  api.distribute
  assert(!api.writes.empty?, 'A build ready before the deadline must still distribute')
ensure
  Process.define_singleton_method(:clock_gettime, original_clock)
end

workflow = YAML.load_file('.github/workflows/release.yml')
steps = workflow.fetch('jobs').fetch('testflight').fetch('steps')
helper = File.expand_path('../.github/testflight.rb', __dir__)
Dir.mktmpdir('testflight-workflow.', 'tmp') do |scratch|
  Dir.chdir(scratch) do
    FileUtils.mkdir_p(%w[bin script ProjectSettings])
    File.write('ProjectSettings/ProjectSettings.asset', "  bundleVersion: 1.2.3\n")
    File.write('script/testflight', <<~'SH')
      #!/bin/bash
      printf '%s %s\n' "${BUILD_NUMBER}" "$*" >>uploads
      if [[ "$*" == --external ]]; then
        [[ -f saved-intent ]] || exit 99
        [[ "${APPLE_ACCEPTS:-1}" == 0 ]] || touch accepted
      fi
      exit "${UPLOAD_STATUS:-0}"
    SH
    File.write('bin/ruby', <<~'SH')
      #!/bin/bash
      case "$2" in
        next-build-number) echo "${NEXT_NUMBER}" ;;
        plan-public)
          [[ "${PLAN_STATUS:-0}" == 0 ]] || exit 1
          if [[ -f accepted ]]; then echo 'needed=false'; else echo 'needed=true'; fi >>"${GITHUB_OUTPUT}"
          ;;
        record-public|resume-public) exec "${TEST_RUBY}" "${TEST_HELPER}" "$2" ;;
        *) exit 99 ;;
      esac
    SH
    FileUtils.chmod(0755, %w[script/testflight bin/ruby])
    env = {
      'PATH' => "#{Dir.pwd}/bin:#{ENV.fetch('PATH')}",
      'TEST_RUBY' => RbConfig.ruby, 'TEST_HELPER' => helper,
      'GITHUB_SHA' => 'original-commit', 'GITHUB_RUN_ID' => '42',
      'GITHUB_OUTPUT' => File.expand_path('outputs'),
      'RELEASE_VERSION' => '1.2.3', 'BUILD_NUMBER' => '42.2', 'NEXT_NUMBER' => '44'
    }
    execute = lambda do |id|
      File.write('outputs', '')
      File.write('workflow-step.sh', steps.find { |item| item['id'] == id }.fetch('run'))
      _out, err, status = Open3.capture3(env, 'bash', '-euo', 'pipefail', 'workflow-step.sh')
      outputs = File.readlines('outputs', chomp: true).map { |line| line.split('=', 2) }.to_h
      [status.success?, outputs, err]
    end
    success, outputs, err = execute.call('prepare')
    assert(success, err)
    env['BUILD_NUMBER'] = outputs.fetch('build_number')
    assert(outputs.fetch('fresh') == 'true', 'Save a new intent before uploading')
    receipt = File.read(TestFlight::UPLOAD_RECORD)
    assert(!File.exist?('accepted'), 'Preparation must not upload to Apple')
    File.write('saved-intent', receipt)
    success, outputs, err = execute.call('plan')
    assert(success && outputs.fetch('needed') == 'true', err)
    env['UPLOAD_STATUS'] = '1'
    assert(!execute.call('upload')[0], 'Simulate failure after Apple accepted the upload')
    uploaded = File.read('uploads')
    assert(File.read('saved-intent') == receipt, 'Failed uploads must already have a durable recovery identity')

    # A clean retry restores the pre-upload artefact and reconciles acceptance.
    FileUtils.rm_rf('tmp')
    FileUtils.mkdir_p('tmp')
    FileUtils.cp('saved-intent', TestFlight::UPLOAD_RECORD)
    env['NEXT_NUMBER'] = '46'
    success, outputs, err = execute.call('prepare')
    assert(success && outputs.fetch('build_number') == '44', err)
    assert(!outputs.key?('fresh'), 'A retry must preserve the original intent')
    success, outputs, err = execute.call('plan')
    assert(success && outputs.fetch('needed') == 'false', err)
    upload_step = steps.find { |item| item['id'] == 'upload' }
    assert(upload_step.fetch('if') == "steps.plan.outputs.needed == 'true'" && outputs.fetch('needed') == 'false',
           'An accepted upload must skip the upload step')
    assert(File.read('uploads') == uploaded, 'Public retry must not upload another build')
    assert(File.read(TestFlight::UPLOAD_RECORD) == receipt, 'Preserve the identity across clean retries')

    env['TESTFLIGHT_PLATFORM'] = 'macOS'
    assert(!execute.call('prepare')[0], 'A Mac job must reject a legacy iOS upload intent')
    env.delete('TESTFLIGHT_PLATFORM')
    env['GITHUB_SHA'] = 'another-commit'
    assert(!execute.call('prepare')[0], 'Mismatched recovery must fail')
    assert(File.read('uploads') == uploaded, 'Mismatched recovery must not upload')
    env['GITHUB_SHA'] = 'original-commit'
    FileUtils.rm_f('accepted')
    success, outputs, err = execute.call('plan')
    assert(success && outputs.fetch('needed') == 'true', 'Retry an intent only when both Apple endpoints are empty')
    env['PLAN_STATUS'] = '1'
    assert(!execute.call('plan')[0], 'Reconciliation API errors must fail closed')
    FileUtils.rm_f(TestFlight::UPLOAD_RECORD)
    File.write('ProjectSettings/ProjectSettings.asset', "  bundleVersion: invalid\n")
    assert(!execute.call('prepare')[0], 'Failed preflight must stop preparation')
    assert(!File.exist?(TestFlight::UPLOAD_RECORD), 'Do not create an intent when preparation fails')
  end
end
# The real GitHub reader must inspect every page and ignore other workflows' identities.
Dir.mktmpdir('testflight-history.', 'tmp') do |scratch|
  FileUtils.mkdir_p("#{scratch}/bin")
  File.write("#{scratch}/bin/gh", <<~'SH')
    #!/bin/bash
    printf '%s\n' "$*" >"${HISTORY_ARGUMENTS}"
    [[ "${HISTORY_STATUS:-0}" == 0 ]] || exit 1
    cat "${HISTORY_FILE}"
  SH
  FileUtils.chmod(0755, "#{scratch}/bin/gh")
  run = { 'head_branch' => 'main', 'repository_id' => 1, 'head_repository_id' => 1 }
  intent = artifact('testflight-upload-50', now).merge('workflow_run' => run)
  attempt = artifact('testflight-attempt-50-1', now).merge('workflow_run' => run)
  mac_intent = intent.merge('name' => 'testflight-macos-upload-50-v1.2.3')
  other_branch = intent.merge('workflow_run' => run.merge('head_branch' => 'feature'))
  fork = intent.merge('workflow_run' => run.merge('head_repository_id' => 2))
  unrelated = { 'name' => 'Mac app' }
  missing_run = intent.reject { |key, _| key == 'workflow_run' }
  null_run = intent.merge('workflow_run' => nil)
  File.write("#{scratch}/history.json", JSON.generate([
    { 'artifacts' => [unrelated, missing_run, null_run, intent, mac_intent, other_branch] }, { 'artifacts' => [fork, attempt] }
  ]))
  env = { 'PATH' => "#{File.expand_path(scratch)}/bin:#{ENV.fetch('PATH')}",
          'GITHUB_REPOSITORY' => 'test/repo', 'HISTORY_FILE' => File.expand_path("#{scratch}/history.json"),
          'HISTORY_ARGUMENTS' => File.expand_path("#{scratch}/arguments") }
  command = [RbConfig.ruby, '-r', helper, '-e', 'puts JSON.generate(TestFlight.allocate.public_artifacts)']
  out, err, status = Open3.capture3(env, *command)
  assert(status.success?, err)
  assert(JSON.parse(out) == [intent, attempt], 'Read all pages and trust only same-repository main upload records')
  assert(File.read("#{scratch}/arguments").strip == 'api --paginate --slurp repos/test/repo/actions/artifacts?per_page=100',
         'Read quota records across workflow runs, with pagination')
  command[-1] = "api = TestFlight.allocate; api.instance_variable_set(:@platform, 'MAC_OS'); puts JSON.generate(api.public_artifacts)"
  out, err, status = Open3.capture3(env, *command)
  assert(status.success? && JSON.parse(out) == [mac_intent], "Read only native Mac intents: #{err}")
  _out, err, status = Open3.capture3(env.merge('HISTORY_STATUS' => '1'), *command)
  assert(!status.success? && err.include?('Could not read GitHub'), 'History API errors must fail closed')
end
puts 'TestFlight automation tests passed'
