# Infinigame privacy policy

Last updated: 21 November 2026.

Infinigame is developed by Mike McQuaid. This policy covers the game, including its iPhone and iPad beta. It has no developer-operated player account service, advertising or tracking analytics. Unity Analytics, Ads and cloud diagnostics are disabled. The developer does not automatically receive your saved games or conversations.

## Saved games and settings

The game stores progress, world state, journal entries, companion conversations and preferences on your device. Diagnostic and performance logs may also be stored locally. These are not uploaded to the developer by the game. Device backups follow your system and storage provider settings.

Optional AI account details are kept separately from saved games. On iPhone and iPad, API keys and sign-in tokens are stored in Keychain. Desktop versions store them in a local account file and may also use provider credentials from the environment or provider command-line tools. No developer API key is bundled with the game.

## Optional AI services

The game works without an AI account, using observed scanner facts and authored companion replies. If you configure Anthropic or OpenAI, the game automatically sends the expedition seed, known lore, authored lines and world descriptions to that provider to prepare mission guidance, companion dialogue and scanner wording. You can also send free-form questions. A request can contain game facts, your current objective and progress, your message and recent conversation context. Anything personal you type or say in a message can therefore reach the selected provider. Requests also expose normal connection and account information, such as your IP address and authentication data.

On iPhone and iPad, requests go directly to the selected provider over HTTPS. On desktop, the game can also use your signed-in Claude Code or Codex tools. Browser sign-in takes place on the provider's pages; the game does not collect your provider password. API keys and tokens authenticate requests to the relevant provider, not to the developer.

Generated replies and journal entries can be saved with your game. Provider retention, training choices and international processing depend on the service, account terms and settings you choose. See [OpenAI's privacy policy][openai] and [Anthropic's privacy policy][anthropic] and the terms applicable to your API or subscription account. These providers' business offerings may be governed by separate agreements.

## Microphone, speech and clipboard

Voice input is available when you connect a language model. On iPhone and iPad, the game asks permission to use the microphone and speech recognition when you use voice input. Recognition requires on-device processing; if that is unavailable, voice input cannot start. Raw microphone audio is not sent to an AI provider or the developer by the game. The resulting text is treated like a typed message and can be sent to your configured AI provider. The companion's spoken replies are synthesised locally.

The game reads clipboard content when you choose **Use the key on the clipboard** to import an API key. It does not monitor the clipboard.

## TestFlight and contact

Apple automatically collects TestFlight crash logs and usage information and shares them with the developer. Feedback you submit can include screenshots, comments, device details and contact information. Public-link participation does not itself reveal your name or email address to the developer. See [TestFlight and privacy][apple].

I use beta diagnostics and information you send me to investigate problems, reply to you and improve Infinigame. I do not sell this information or use it for advertising. TestFlight information shared with me is not passed to third parties. I retain support correspondence and diagnostics only as long as needed for those purposes or legal obligations. Apple's retention and your AI provider's retention are governed by their respective policies.

## Your choices

You can play without configuring an AI provider. To remove stored credentials, open **Expedition language model** on the title screen or in Settings, select the connected provider and choose **Disconnect language model**. On desktop, separately remove any environment credentials or sign out of command-line tools the game can use. Revoke keys or sign-in access with the provider to stop their use completely. Removing credentials does not remove saved dialogue or information already received by a provider.

You can deny or revoke microphone and speech recognition permission in system settings and use typed input instead. Deleting the iOS app removes its local saves and preferences; offloading retains them. Keychain entries can survive app deletion, so remove credentials in the game first. Desktop saves, account files, exported logs and device backups must be removed separately when no longer needed.

Contact [Mike McQuaid](mailto:mike@mikemcquaid.com) with privacy questions or requests to access, correct or delete information you have sent me. Depending on your location, you may also have rights to object to or restrict processing and complain to your data protection authority. I cannot delete data held solely on your devices or by other providers. Avoid including API keys or private conversations in public issues. Changes to this policy will appear here with an updated date.

[openai]: https://openai.com/policies/privacy-policy/
[anthropic]: https://www.anthropic.com/legal/privacy
[apple]: https://www.apple.com/legal/privacy/data/en/test-flight/
