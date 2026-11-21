# TestFlight through GitHub Actions

Each push to `main` starts the CI workflow. It uploads one signed build for iPhone and iPad and one native Apple silicon Mac build. Internal testers receive each build after processing. External testers use the same builds through the public invitation once Apple approves them. [TestFlight runs](https://github.com/MikeMcQuaid/Infinigame/actions/workflows/release.yml) · [Join the beta](https://testflight.apple.com/join/1GUH8Zjn)

## Unity setup

The GitHub-hosted runners activate Unity Personal online with the pinned GameCI CLI. The two Unity credentials are already configured under **Settings > Secrets and variables > Actions**. [Repository secrets](https://github.com/MikeMcQuaid/Infinigame/settings/secrets/actions) · [Pinned GameCI activation logic](https://github.com/game-ci/cli/blob/v0.1.76/dist/platforms/mac/steps/licensing_method.sh)

| Secret | Value |
|---|---|
| `UNITY_EMAIL` | The email address you use to sign in to the Unity ID that holds your Personal licence. |
| `UNITY_PASSWORD` | The ordinary password for that Unity ID, entered on its own. |

**Two-factor authentication.** GameCI accepts the password and has no input for an authenticator code, recovery code or interactive device verification. If activation reports a challenge, changing the password secret will not resolve it. To use the same account and turn off its authenticator, open **Unity Dashboard > profile > Account > Security > Account security**, click the trash icon beside the authenticator and confirm **Remove**. This removes the second factor for the entire Unity account. Unity may still ask you by email to verify an unfamiliar runner. Check that email and retry the job. [Unity account security](https://docs.unity.com/en-us/cloud/accounts/manage-account-security) · [GameCI’s headless sign-in diagnostics](https://github.com/game-ci/cli/blob/v0.1.76/dist/platforms/mac/steps/licensing_method.sh#L190-L203)

**Local licence files.** Hosted builds need neither a `.ulf` file nor a `UNITY_LICENSE` secret. On macOS, Unity lists `$HOME/Library/Unity/licenses/UnityEntitlementLicense.xml` for named-user licences and `/Library/Application Support/Unity/Unity_lic.ulf` for serial-based licences. To check for the former without displaying its contents, run `ls -l "$HOME/Library/Unity/licenses/UnityEntitlementLicense.xml"`. A quoted leading `~` is literal in zsh. [Unity licence file locations](https://docs.unity3d.com/Manual/ActivationFAQ.html)

### Set up Infinigame in App Store Connect

1. Open **App Store Connect > Apps**. If Infinigame already appears, open it. Otherwise choose **+ > New App**, select **iOS** for both iPhone and iPad, enter `Infinigame` as the name if available and choose your primary language. Select your registered bundle ID `com.mikemcquaid.Infinigame`, enter a unique private SKU such as `INFINIGAME-IOS`, choose user access and click **Create**. [Open Apps](https://appstoreconnect.apple.com/apps) · [Apple’s app record guide](https://developer.apple.com/help/app-store-connect/create-an-app-record/add-a-new-app/)
2. Open **Infinigame > TestFlight**. Click **+** beside **Internal Testing**, name the group (for example, `Playtesting`), enable **automatic distribution** to receive future uploads and click **Create**. Select **Invite Testers**, tick your own App Store Connect account and click **Add**. Other internal testers need an eligible App Store Connect account with access to Infinigame. [Apple’s internal testing guide](https://developer.apple.com/help/app-store-connect/test-a-beta-version/add-internal-testers/)

### Renew the iOS provisioning profile

1. As the Apple Developer Account Holder or an Admin, go to **Certificates, Identifiers & Profiles > Profiles > +**. Under **Distribution**, choose **App Store Connect** and click **Continue**. Select the existing App ID for `com.mikemcquaid.Infinigame`. Choose the **Apple Distribution** certificate whose private key is in the `.p12` you will use for `APP_STORE_CERTIFICATES`. [Open Profiles](https://developer.apple.com/account/resources/profiles/list) · [Apple’s provisioning profile instructions](https://developer.apple.com/help/account/provisioning-profiles/create-an-app-store-provisioning-profile/)
2. Name the profile `Infinigame_App_Store`, choose **Generate** and **Download**. Save it as `~/Downloads/Infinigame_App_Store.mobileprovision`. The same profile covers iPhone and iPad.
3. From the Mac, store the base64-encoded **entire downloaded file** as a repository secret. If the filename differs, adjust the path.

```sh
base64 -i "$HOME/Downloads/Infinigame_App_Store.mobileprovision" |
  gh secret set INFINIGAME_APP_STORE_IOS_PROFILE --repo MikeMcQuaid/Infinigame
```

For the GitHub web form, replace the `gh secret set` command with `pbcopy` and paste into a new secret with that same name. Renew the profile and update the secret when the profile expires or its signing certificate changes.

### Add the native Mac beta

The repository already has the Unity credentials, App Store Connect API key, certificate bundle and password, and both provisioning profile secrets. The workflow selects `INFINIGAME_APP_STORE_MACOS_PROFILE` for Mac and `INFINIGAME_APP_STORE_IOS_PROFILE` for iPhone and iPad. [Repository secrets](https://github.com/MikeMcQuaid/Infinigame/settings/secrets/actions)

1. **Check the existing certificate bundle.** Import the original `.p12` into an empty keychain and open **My Certificates**. Look for **Apple Distribution** (signs the app) and **3rd Party Mac Developer Installer** (the Mac Installer Distribution identity). Expand each entry to confirm it has a private key. If both are present for your team, use the existing secrets as they are. [Apple’s certificate guide](https://developer.apple.com/help/account/certificates/certificates-overview/) · [Mac signing identities](https://developer.apple.com/documentation/xcode/packaging-mac-software-for-distribution)
2. **If the installer identity is missing**, obtain a **Mac Installer Distribution** certificate, then export it together with the existing Apple Distribution identity and both private keys. Replace `APP_STORE_CERTIFICATES` with the base64-encoded bundle. Update `APP_STORE_CERTIFICATES_PASSWORD` only if the export password changes. [Open Certificates](https://developer.apple.com/account/resources/certificates/list) · [Create a signing request](https://developer.apple.com/help/account/certificates/create-a-certificate-signing-request/) · [Export from Keychain Access](https://support.apple.com/guide/keychain-access/import-and-export-keychain-items-kyca35961/mac)

   ```sh
   base64 -i "$HOME/Downloads/Infinigame_Distribution.p12" |
     gh secret set APP_STORE_CERTIFICATES --repo MikeMcQuaid/Infinigame
   ```

   The Mac export selects a valid installer identity from the imported keychain by its SHA-1 fingerprint. If the signing check reports no valid identity, run `security find-identity -v -p basic /path/to/imported.keychain-db` against your test keychain. Look for `3rd Party Mac Developer Installer` with the team ID from the profile. For an identity missing from the valid list, check its private key, expiry and certificate trust. After correcting the bundle, rerun **macOS and iOS**. [Apple’s installer signing guide](https://developer.apple.com/documentation/xcode/packaging-mac-software-for-distribution)

3. **Enable the Mac platform.** In the existing Infinigame app record, choose **Distribution > Add Platform > macOS** if needed. Use the same bundle ID, `com.mikemcquaid.Infinigame`. If the Public Beta invitation uses device criteria, include Mac. [Open Apps](https://appstoreconnect.apple.com/apps) · [Add a platform](https://developer.apple.com/help/app-store-connect/create-an-app-record/add-platforms/)

### TestFlight signing secrets

| Secret | Value |
|---|---|
| `NOTARIZATION_KEY` | Contents of the App Store Connect `.p8` private key, as PEM or base64 |
| `NOTARIZATION_KEY_ID` | API key ID |
| `NOTARIZATION_ISSUER_ID` | API issuer ID |
| `APP_STORE_CERTIFICATES` | Base64-encoded `.p12` containing Apple Distribution and, for macOS, Mac Installer Distribution with their private keys |
| `APP_STORE_CERTIFICATES_PASSWORD` | That export's password |
| `INFINIGAME_APP_STORE_IOS_PROFILE` | Base64-encoded iOS App Store profile for this game's bundle identifier |
| `INFINIGAME_APP_STORE_MACOS_PROFILE` | Base64-encoded Mac App Store profile for the same bundle identifier |

Both platforms use the same team API key. To replace it, go to **App Store Connect > Users and Access > Integrations > Team Keys**, generate a key with upload access and download its `.p8`. Update the three corresponding `NOTARIZATION_*` secrets together. [Apple’s team API key guide](https://developer.apple.com/help/app-store-connect/get-started/app-store-connect-api/)

If the release reports an invalid key, load the original file directly with `gh secret set NOTARIZATION_KEY < /path/to/AuthKey_KEYID.p8`. The key ID and issuer ID must belong to that key.

## Tester groups

- **Internal testers.** Enable automatic distribution on your internal group in App Store Connect. For a manually managed group, the workflow assigns the build to the sole internal group or the one named by the `TESTFLIGHT_INTERNAL_GROUP` Actions variable. Testers accept the invitation with their App Store Connect account. [Internal testing](https://developer.apple.com/help/app-store-connect/test-a-beta-version/add-internal-testers/)
- **Public testers.** Use the external group that owns the invitation. The default name is **Public Beta**. If its name differs, set the `TESTFLIGHT_PUBLIC_GROUP` Actions variable. Complete the beta test information and review contact using the copy in `TESTFLIGHT.md`. Use a team API key with the App Manager or Admin role to manage testers and submit for review. Testers open the invitation on their device and install Infinigame with TestFlight. [Actions variables](https://github.com/MikeMcQuaid/Infinigame/settings/variables/actions) · [External testing](https://developer.apple.com/help/app-store-connect/test-a-beta-version/invite-external-testers/) · [Join the beta](https://testflight.apple.com/join/1GUH8Zjn)

Once the native macOS build is available to a group, open its **Settings** tab and disable **Test iPhone and iPad apps on Apple silicon Macs**. This keeps Mac testers on the native keyboard-and-mouse build. [Apple’s TestFlight group settings](https://developer.apple.com/help/app-store-connect/test-a-beta-version/test-iphone-and-ipad-apps-on-macs-with-apple-silicon/)

## Protecting main

Require these two **GitHub Actions** checks in the default branch ruleset.

| Required status check | What it verifies |
|---|---|
| `Windows` | Unity builds and packages both the Windows and Steam Deck players. |
| `macOS and iOS` | Checks tester groups and release notes, signs the native Mac app and installer and the iPhone and iPad app, then validates both exported packages with Apple. |

Once the checks appear on a PR, open **Settings > Rules > Rulesets > Default Branch** and select the two names above with **GitHub Actions** as the source. Replace the old `platforms / …` checks in the same edit. Remove **Check TestFlight signing** if it is still required. [Default Branch ruleset](https://github.com/MikeMcQuaid/Infinigame/settings/rules/27675799)

For a new ruleset, set enforcement to **Active**, target the default branch and leave the bypass list empty. Enable **Require a pull request before merging** (zero approvals works for a solo maintainer) and **Require branches to be up to date before merging**. PR branches in this repository use the real Unity and Apple credentials. Fork PRs skip the platform jobs because GitHub withholds those secrets. To run these checks for a fork contribution, review the code and open a PR from a repository branch. [GitHub ruleset settings](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/available-rules-for-rulesets)

## Builds and retries

A push builds its tip commit using the Unity project's `bundleVersion`. Each Apple build number starts with the commit count from `script/build-number` and advances past numbers already accepted by Apple or reserved by an earlier run. Successive pushes can use the same marketing version. PRs read build history and validate the signed packages with App Store Connect. Only pushes to `main` reserve build numbers, upload the verified archives and assign tester groups.

After upload, a Linux job waits up to 20 minutes per platform for Apple to process the build, assigns tester groups and submits eligible builds for external review using **What to Test** from `TESTFLIGHT.md`. A processing timeout, group assignment error or rejected submission fails the `main` run. Choose **Re-run failed jobs** on that run once the issue clears. After a successful submission, Apple controls the review outcome. Check **Infinigame > TestFlight > Public Beta > Builds** for approval and start testing there if the group uses manual notification. [App Store Connect](https://appstoreconnect.apple.com/apps) · [Apple’s external testing guide](https://developer.apple.com/help/app-store-connect/test-a-beta-version/invite-external-testers/)

For signing or upload failures, inspect the job log with `gh run view RUN_ID --log-failed`. After fixing the cause, choose **Re-run failed jobs** on the original run. GitHub retains its upload record for 90 days. The shared Apple job checks each platform’s saved number, rebuilds only missing binaries and passes accepted builds to the Linux distribution job. If Apple rejected the binary, push the fix to `main` for a new build number. [TestFlight runs](https://github.com/MikeMcQuaid/Infinigame/actions/workflows/release.yml) · [GitHub workflow reruns](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/re-run-workflows-and-jobs)

## Desktop builds and caching

On a push to `main`, the Actions run keeps **Infinigame-Windows** (also for Steam Deck and SteamVR) and **Infinigame-macOS** for one day. The Mac ZIP contains an ad hoc signed copy of the app from the TestFlight archive. Once Xcode has produced the archive, the ZIP can be downloaded even if Apple validation, upload or tester distribution fails. If macOS prompts on first launch, follow `INSTALL.md` in the ZIP.

Windows builds and packages both Windows players in one Unity invocation on Linux. The Apple job can start at the same time on its own runner. TestFlight distribution depends only on the Apple job. macOS and iOS share one Mac runner, editor installation, Bee cache and speech build directory. Each Unity invocation restores its own target’s `Library`. The iOS archive also caches native IL2CPP objects by Unity dependencies, Xcode, SDK, runner image and generated C++ sources. [GameCI caching](https://game.ci/docs/github/builder/#caching) · [GitHub cache access](https://docs.github.com/en/actions/reference/workflows-and-actions/dependency-caching#restrictions-for-accessing-a-cache)

The shared Mac uses GitHub’s `xcode-27` Apple silicon runner, currently in public preview. CI checks the selected iOS device SDK before installing Unity and reads `DTSDKName` from the archived app before validation and upload. Both must report iOS 27 or later. This sets the build SDK independently of the minimum OS on testers’ devices. [Hosted Xcode and SDK versions](https://github.com/actions/runner-images/blob/main/images/macos/xcode-27-arm64-Readme.md)
