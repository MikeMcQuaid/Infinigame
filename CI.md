# TestFlight through GitHub Actions

Run **Actions > TestFlight > Run workflow** from `main` with a version such as `0.2.0` and leave **destination** set to `private`. The hosted Mac exports, signs and uploads the iPhone and iPad build for your internal tester group. The workflow runs only when requested and needs no local build.

## Unity setup

The GitHub-hosted runners activate Unity Personal online with the pinned GameCI CLI. Add these two repository secrets under **Settings > Secrets and variables > Actions > New repository secret**. [Repository secrets](https://github.com/MikeMcQuaid/Infinigame/settings/secrets/actions) · [Pinned GameCI activation logic](https://github.com/game-ci/cli/blob/v0.1.76/dist/platforms/mac/steps/licensing_method.sh)

| Secret | Value |
|---|---|
| `UNITY_EMAIL` | The email address you use to sign in to the Unity ID that holds your Personal licence. |
| `UNITY_PASSWORD` | The ordinary password for that Unity ID, entered on its own. |

**Two-factor authentication.** GameCI accepts the password and has no input for an authenticator code, recovery code or interactive device verification. If activation reports a challenge, changing the password secret will not resolve it. To use the same account and turn off its authenticator, open **Unity Dashboard > profile > Account > Security > Account security**, click the trash icon beside the authenticator and confirm **Remove**. This removes the second factor for the entire Unity account. Unity may still ask you by email to verify an unfamiliar runner. Check that email and retry the job. [Unity account security](https://docs.unity.com/en-us/cloud/accounts/manage-account-security) · [GameCI’s headless sign-in diagnostics](https://github.com/game-ci/cli/blob/v0.1.76/dist/platforms/mac/steps/licensing_method.sh#L190-L203)

**Local licence files.** Hosted builds need neither a `.ulf` file nor a `UNITY_LICENSE` secret. On macOS, Unity lists `$HOME/Library/Unity/licenses/UnityEntitlementLicense.xml` for named-user licences and `/Library/Application Support/Unity/Unity_lic.ulf` for serial-based licences. To check for the former without displaying its contents, run `ls -l "$HOME/Library/Unity/licenses/UnityEntitlementLicense.xml"`. A quoted leading `~` is literal in zsh. [Unity licence file locations](https://docs.unity3d.com/Manual/ActivationFAQ.html)

### Set up Infinigame in App Store Connect

1. Open **App Store Connect > Apps**. If Infinigame already appears, open it. Otherwise choose **+ > New App**, select **iOS** for both iPhone and iPad, enter `Infinigame` as the name if available and choose your primary language. Select your registered bundle ID `com.mikemcquaid.Infinigame`, enter a unique private SKU such as `INFINIGAME-IOS`, choose user access and click **Create**. [Open Apps](https://appstoreconnect.apple.com/apps) · [Apple’s app record guide](https://developer.apple.com/help/app-store-connect/create-an-app-record/add-a-new-app/)
2. Open **Infinigame > TestFlight**. Click **+** beside **Internal Testing**, name the group (for example, `Playtesting`), enable **automatic distribution** to receive future uploads and click **Create**. Select **Invite Testers**, tick your own App Store Connect account and click **Add**. Other internal testers need an eligible App Store Connect account with access to Infinigame. [Apple’s internal testing guide](https://developer.apple.com/help/app-store-connect/test-a-beta-version/add-internal-testers/)

### Create the iOS provisioning profile

1. As the Apple Developer Account Holder or an Admin, go to **Certificates, Identifiers & Profiles > Profiles > +**. Under **Distribution**, choose **App Store Connect** and click **Continue**. Select the existing App ID for `com.mikemcquaid.Infinigame`. Choose the **Apple Distribution** certificate whose private key is in the `.p12` you will use for `APP_STORE_CERTIFICATES`. [Open Profiles](https://developer.apple.com/account/resources/profiles/list) · [Apple’s provisioning profile instructions](https://developer.apple.com/help/account/provisioning-profiles/create-an-app-store-provisioning-profile/)
2. Name the profile `Infinigame_App_Store`, choose **Generate** and **Download**. Save it as `~/Downloads/Infinigame_App_Store.mobileprovision`. The same profile covers iPhone and iPad.
3. From the Mac, store the base64-encoded **entire downloaded file** as a repository secret. If the filename differs, adjust the path.

```sh
base64 -i "$HOME/Downloads/Infinigame_App_Store.mobileprovision" |
  gh secret set INFINIGAME_APP_STORE_IOS_PROFILE --repo MikeMcQuaid/Infinigame
```

For the GitHub web form, replace the `gh secret set` command with `pbcopy` and paste into a new secret with that same name. Renew the profile and update the secret when the profile expires or its signing certificate changes.

### TestFlight signing secrets

| Secret | Value |
|---|---|
| `NOTARIZATION_KEY` | Contents of the App Store Connect `.p8` API key |
| `NOTARIZATION_KEY_ID` | API key ID |
| `NOTARIZATION_ISSUER_ID` | API issuer ID |
| `APP_STORE_CERTIFICATES` | Base64-encoded Apple Distribution certificate and private key exported as `.p12` |
| `APP_STORE_CERTIFICATES_PASSWORD` | That export's password |
| `INFINIGAME_APP_STORE_IOS_PROFILE` | Base64-encoded App Store profile for this game's bundle identifier |

If MinMaxCal uses the same Apple team and an API key with permission to upload builds, you can use the original five credential values here too. Add them to the Infinigame repository or grant it access to the corresponding organisation secrets. To create a team key, go to **App Store Connect > Users and Access > Integrations > Team Keys**, generate a key with upload access and download its `.p8`. Keep the original file, Key ID and Issuer ID together. To export your signing identity on the Mac, select **Apple Distribution** under **Keychain Access > My Certificates** and export it as a password-protected `.p12` with its private key. [Apple’s team API key guide](https://developer.apple.com/help/app-store-connect/get-started/app-store-connect-api/)

### Install the private beta

1. Push the workflow to `main`. Open **Actions > TestFlight > Run workflow**, select `main`, enter a three-part version, select `private` and run it. The build number starts at the commit count shown by `script/build-number`. If Apple’s upload history or a saved public upload has reached that number, the workflow uses the next integer above both. The workflow keeps the newest 20 private builds. [Open the TestFlight workflow](https://github.com/MikeMcQuaid/Infinigame/actions/workflows/release.yml)
2. After Apple processes the build, your internal group receives it through automatic distribution. If necessary, open **Apps > Infinigame > TestFlight > Playtesting > Add Builds**, choose the processed build and enter **What to Test**. [Apple’s internal testing guide](https://developer.apple.com/help/app-store-connect/test-a-beta-version/add-internal-testers/)
3. Install **TestFlight** on your iPhone and iPad. Accept the invitation with your Apple Account and tap **Install** beside Infinigame on each device. [Get TestFlight](https://testflight.apple.com/)

## Distribute to external testers

In App Store Connect, create the configured external group (**Public Beta** by default, or the name set in the `TESTFLIGHT_PUBLIC_GROUP` Actions variable) and complete the beta test information and review contact. The team API key used for public distribution needs the **App Manager** or **Admin** role so it can submit for review and manage testers. Use the submission copy in [TESTFLIGHT.md](TESTFLIGHT.md) and publish [PRIVACY.md](PRIVACY.md) at the privacy URL you supply. [Apple’s external testing guide](https://developer.apple.com/help/app-store-connect/test-a-beta-version/invite-external-testers/)

Run **Actions > TestFlight** again, this time selecting `public` with a fresh marketing version. The workflow saves the upload identity before uploading, waits up to 20 minutes for processing, supplies **What to Test** from `TESTFLIGHT.md`, submits a ready build for review and assigns it to the configured external group. It reuses an existing submission on retry. Apple controls review and availability. Enable that group's public invitation link in App Store Connect when ready to share it.

Public uploads and GitHub releases each have a three-per-24-hour limit. A failed public attempt that Apple received still counts; an unconfirmed reservation blocks a slot for 25 hours. Private uploads do not use public slots. If an unrelated manual upload has no known audience yet, wait for Apple to classify it before starting another public upload.

## Other workflow destinations

| Workflow or destination | Result |
|---|---|
| TestFlight · `private` (default) | iPhone/iPad upload for an internal group with automatic distribution |
| TestFlight · `public` | iPhone/iPad upload for the configured external group (**Public Beta** by default) |
| TestFlight · `github` | Builds all platforms and publishes their archives in a versioned GitHub release |
| TestFlight · `github-with-private` | Signs the same iOS export from the platform builds and publishes the GitHub release after the internal upload succeeds |
| CI / Platform builds | Builds the four platform archives on pushes to `main` or manual dispatch |

Both TestFlight-only destinations run only iOS. They create no GitHub release. Platform artifacts and GitHub release assets use the same zip files. Each zip expands to `Infinigame/` and includes installation instructions. The Windows and Steam Deck packages share a Unity invocation. macOS and iOS run separately. All workflows share the Unity and native voice caches for their platform.

CI keeps build artifacts and logs for one day. Platform archives from a release are kept for 30 days. Public upload intents and attempt reservations are kept for 90 days. Within GitHub's 30-day rerun window, choose **Re-run failed jobs** on the original run to recover a public upload: an accepted build resumes processing and distribution without another upload. A ready build is submitted for review only if it has no existing submission. Do not dispatch another public run with the same marketing version. If Apple rejected the public upload, it remains awaiting its binary or the original run is too old to rerun, use a fresh version. Private retries allocate a fresh build number without changing the marketing version, including when reusing a platform archive. Correct the upload problem before retrying. GitHub publication can be retried on its original run if the tag exists but release creation failed; inspect the tag's commit first. Use a fresh tag for a new GitHub release. [GitHub workflow reruns](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/re-run-workflows-and-jobs)
