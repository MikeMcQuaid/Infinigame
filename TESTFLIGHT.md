# Infinigame public beta submission

Draft copy for **App Store Connect > Infinigame > TestFlight**, under **Additional > Test Information**. Copy each field without its heading. Review the beta terms before adopting them. They cover the distributed beta and do not add an open-source licence to this repository.

## Beta App Description

Wake inside damaged powered armour on an occupied Earth. Fight your way out, meet your robot companion and explore a miniature solar system on foot and by ship. Recover equipment, descend into alien burrows, build robot factories and take the fight to other worlds. Beyond the system's singularity, new procedural systems await.

Scan your surroundings and keep a journal of your discoveries. Play without an account using built-in scanner facts and companion replies, or optionally connect your own supported Anthropic or OpenAI account for generated descriptions and dialogue. AI services require an internet connection and may charge for usage. Voice input is optional.

The beta runs on iPhone, iPad and Apple silicon Macs. Please help test controls, the opening objectives, exploration, combat, saving and performance. Expect bugs and changes to saved-game compatibility. The game contains science-fiction violence and alien blood and dismemberment.

## What to Test

Please test the opening suit-recovery sequence, movement and aiming with touch on iPhone and iPad or keyboard and mouse on Mac, the first alien burrow, scanner and companion dialogue, saving and resuming, and sustained performance on your device. Try the game without an AI account first. If you test optional AI or voice input, report connection and permission problems without sharing credentials. Include your device model, OS version and reproduction steps.

## Beta App Review Notes

Infinigame is a single-player science-fiction exploration and combat game for iPhone, iPad and Apple silicon Macs. Core gameplay requires no login, subscription, AI provider account or external hardware. Use touch on iPhone and iPad or keyboard and mouse on Mac.

1. Launch the game and start a new game from the title menu. The opening begins with a black screen while the damaged suit restores its systems; this is an intentional narrative sequence, not a login or network wait. Follow the on-screen controls to break out of the debris.
2. Meet the nearby robot companion and follow the first objective towards an alien mind burrow. Test movement, aiming, attacks and the scanner. Later objectives unlock equipment, factories and space flight.
3. Talk to the companion using typed input. Without AI credentials, the game uses authored replies and observed scanner facts. Test saving and resuming through the game menu.
4. Voice input is optional and requests microphone and speech recognition permissions. It requires on-device speech recognition. Denying permissions leaves typed dialogue available. Spoken replies are synthesised on-device.
5. Optional AI settings are under **Settings > Claude** or **ChatGPT**. A user can import their own API key; ChatGPT also offers browser sign-in. A configured provider receives game context, the player's message and recent conversation to generate replies. No developer key is supplied. The game remains playable without enabling this feature. **Forget key and sign-in** removes credentials stored by the game.

The game contains combat, weapons, purple alien blood, tissue and breakable remains. AI-generated dialogue is optional. There is no player-to-player chat or public content feed. If review of provider-backed features needs additional access, contact mike@mikemcquaid.com.

## Beta Licence Agreement

Infinigame is provided by Mike McQuaid for testing and evaluation through Apple's TestFlight service. The application is licensed, not sold. Apple's Standard Licensed Application End User License Agreement applies to the application, together with Apple's TestFlight terms for the testing service: https://www.apple.com/legal/internet-services/itunes/dev/stdeula/.

This is pre-release software. Features may change or be removed, builds expire and saved-game compatibility is not guaranteed. Keep copies of any progress you want to preserve. Participation does not promise a final release, ongoing access or support. These beta notices do not limit any consumer rights that cannot lawfully be excluded.

Infinigame and its original content remain the property of their rights holders. Bundled third-party software, including the Festival, Flite and HTS speech components and SLT voice, retains its own licences and notices. Nothing here restricts rights those licences grant or grants a new licence to the game's source repository.

Optional AI services operate under your own provider account and terms and may incur provider charges. Their output can be inaccurate. The game's privacy policy is at https://github.com/MikeMcQuaid/Infinigame/blob/main/PRIVACY.md.

Feedback is voluntary. Do not include API keys or other confidential information. There is no confidentiality requirement for this public beta. Contact: Mike McQuaid, mike@mikemcquaid.com.

## Other fields and preparation

- **Feedback email:** `mike@mikemcquaid.com`.
- **Review contact:** Mike McQuaid and that email; enter your reachable telephone number privately in App Store Connect.
- **Sign-in:** no game account is required for the core review path. Describe the optional provider sign-in in notes and arrange access if Apple requests it; do not promise that reviewers receive an API key.
- **Privacy Policy URL:** publish `PRIVACY.md` on `main`, then use `https://github.com/MikeMcQuaid/Infinigame/blob/main/PRIVACY.md`. Confirm it opens without signing in. Add an accessible privacy link inside the game as well.
- **Marketing/support URL:** `https://github.com/MikeMcQuaid/Infinigame`.
- **AI sharing:** before submission, provide a clear in-game explanation and explicit permission before sending personal text to Anthropic or OpenAI. Importing a key or publishing this policy alone does not supply that explanation. The current provider menu has no separate consent step. See [App Review guideline 5.1.2][review].
- **Age rating:** disclose fantasy violence, weapons, alien blood/gore and dismemberment at their actual frequency, together with the optional AI dialogue. Let Apple's questionnaire determine the rating. Do not select the Kids category or describe the game as suitable for all ages.
- **App Privacy and encryption:** complete the questionnaires for the actual build and provider data handling. On-device saves and speech are different from messages sent to an AI provider. The current export declares no non-exempt encryption; resolve any compliance prompt before submitting the build.

For an eventual App Store listing, choose **Games**, with **Adventure** and **Action** as its game subcategories. Suggested subtitle: **Explore. Fight. Leave orbit.** Capture actual iPhone and iPad gameplay in the supported orientation: the suit-recovery surface scene, a burrow encounter, scanner/journal, ship cockpit and a planet seen from orbit. Show the real touch interface. The desktop README screenshots can guide composition but should not stand in for iOS captures. An app preview video is optional; a short recording of the opening is useful context for review.

App Store screenshots and categories are not prerequisites for the first external TestFlight beta. Enhanced invitations use screenshots and category from the latest approved App Store version, if one exists. See [Apple's test-information guide][test-info].

## Public build and invitation

Each push to `main` uploads the iPhone and iPad build and the native Apple silicon Mac build for internal and external testers. GitHub Actions uses **What to Test** above and submits eligible builds to the **Public Beta** group after processing. Keep the group's beta information and review contact current in App Store Connect. [Workflow and signing setup](CI.md) · [Apple's external testing guide](https://developer.apple.com/help/app-store-connect/test-a-beta-version/invite-external-testers/)

The public invitation stays the same across builds. Testers open it on their device to install the latest build available to them. [Join the Infinigame beta](https://testflight.apple.com/join/1GUH8Zjn)

[test-info]: https://developer.apple.com/help/app-store-connect/test-a-beta-version/provide-test-information/
[review]: https://developer.apple.com/app-store/review/guidelines/#data-use-and-sharing
