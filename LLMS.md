# Language models and voice

## What calls a model

| Trigger | Sent to the selected model | Gameplay contribution |
|---|---|---|
| Type to the companion, or choose one of its questions | Known story and faction facts, current briefing, location, recent discoveries and the last three exchanges, each line capped at 240 characters | Short contextual answers about the world and what to do |
| Scan a new kind of object on a world | Observed terrain, climate and subject facts | A named field-journal entry, saved for later reading |
| Read a briefing, restore suit power or seed a factory | Nothing | Authored guidance from `MISSIONS.md` and factory guidance |
| Move, fight, generate terrain, build models or simulate colonies | Nothing | Deterministic game code |

The current defaults are Claude Haiku 5.5 and GPT-6 Luna, with low thinking effort. Claude also offers Sonnet 5.5 and Opus 5.5; ChatGPT offers GPT-6.1 Terra, GPT-6.1 Sol and GPT-6 Astra. These are the configured choices in `Oracle.Models`; account access still depends on the provider.

Settings > Claude or ChatGPT selects the provider, exact model and thinking level. The current selection appears during requests. Conversation and scanner cards label generated replies, authored guidance and saved entries. A failed request uses authored companion answers or observed scanner facts. Re-reading a saved scan makes no request.

`STORY.md` defines the companion's concise universe context. Only `Public`, reached `Stage N` sections and an unlocked `After moonfall` section enter its prompt. `FACTIONS.md` adds the shared facts, excluding art notes. The model cannot change the simulation or unlock equipment. Its reply is flavour and advice; mission progression comes from game rules.

## Connections and costs

For either provider, the player's own API key takes precedence. ChatGPT can also use its browser sign-in. On desktop, Claude Code and Codex can use their own existing sign-ins, with tools disabled for these text requests. The ant integration has been removed.

The provider page shows the route in use, selected model, requests and token counts. API calls use your account's billing; signed-in tool usage follows that tool's plan. There is no bundled key, background model polling or required subscription. Tests replace every external connection with a stand-in.

## Voice choices

Text generation and speaking are separate. **Settings > Robot voice** lists the voices installed on this device, with British English first and pages for longer lists. Choosing a voice enables speech, plays a preview and saves its identifier on this device. Reopen the page after installing more voices. Voice off preserves the selection; Robot effect is independent of the selected voice. Robot subtitles have their own toggle in Settings. A removed voice falls back to the system default without forgetting the saved choice. New Mac preferences use **Zarvox**; other platforms use their system voice with the robot effect enabled.

| Platform | Implemented now | Other local options |
|---|---|---|
| macOS | Every voice listed by the local `say` engine, plus System voice | Install voices in Accessibility > Read & Speak. Zarvox, Daniel, Eddy and other voices appear when installed. Robot effect adds light modulation. |
| iPhone/iPad | Installed AVSpeechSynthesizer voices, selected by identifier, plus System voice | Enhanced and Premium variants are labelled. Robot effect lowers the pitch. Only voices Apple exposes to apps appear; Siri voices are not available through this API. |
| Windows/SteamVR | Enabled SAPI voices through PowerShell, plus System voice | Each choice selects its installed name. Robot effect adds light modulation. Narrator-only voices are not necessarily exposed to SAPI. |
| Steam Deck through Proton | Text replies and typed input when speech services are unavailable | No bundled speech engine. Nothing downloads a voice model or calls paid speech. |

See [Apple's speech API and Siri limitation](https://developer.apple.com/videos/play/wwdc2020/10022/) and [Microsoft's selectable installed voices](https://learn.microsoft.com/en-us/dotnet/api/system.speech.synthesis.speechsynthesizer.selectvoice?view=netframework-4.8). Steam Deck keeps text when Windows speech services are unavailable under Proton. Voice discovery runs on opening the picker, with a timeout for desktop tools. Tests use fake catalogues and synthesis tools, never the real engines.

### Portable robot speech candidates

These are research candidates, not dependencies. None supplies Apple's exact Zarvox voice. A shared engine would generate speech locally, including new companion replies, without an LLM or a paid speech service.

| Candidate | Fit and trade-offs |
|---|---|
| [SoLoud Speech](https://solhsa.com/soloud/speech.html) | Adjustable Klatt-style formant synthesis: the most directly mechanical option to audition, but its author warns that intelligibility is limited. Its C API can be wrapped for Unity and its null backend can supply samples to Unity audio. The engine uses zlib terms; its older rsynth-derived speech code has separate provenance notes in the [licence](https://solhsa.com/soloud/legal.html). |
| [Flite](https://github.com/festvox/flite) | Small portable C engine, with [permissive runtime licensing](https://github.com/festvox/flite/blob/master/COPYING). Worth auditioning with a fixed voice and light modulation for clearer robot dialogue. The engine is portable, but a Unity native plugin, iOS cross-build and bundled voice choice still need validation. |
| [eSpeak NG](https://github.com/espeak-ng/espeak-ng) | Compact formant speech with adjustable voices and many accents, including British English. Supports desktop platforms and has an [iOS project](https://github.com/espeak-ng/espeak-ng-ios-app). GPLv3-or-later licensing needs resolution before embedding it in the Unity player. |

For one voice across Mac, iPhone, iPad, Windows, Steam Deck and SteamVR, a bundled engine would need native builds for each operating system and architecture. Deck's current Proton build would use the Windows library; SteamVR uses its host PC's library. Audition SoLoud and Flite before choosing: clarity during combat matters more than an exact resemblance to Zarvox.

## Voice input and bounded state

Hold Y, Mic on a mobile device, LT + R3 on a gamepad or left grip plus right-stick click in VR to dictate. Release to finish. Partial recognition appears in the HUD when available. The transcript becomes a companion question and the reply stays in the HUD without opening a menu. Permission is requested only when you choose to speak. Apple builds require on-device British English recognition; no audio is sent to Apple. If the language model is unavailable, the transcript receives an authored reply. If local dictation is unavailable or permission is denied, type with Return instead. Windows uses its OS speech recogniser and its privacy settings. The Unity editor uses typed input; native voice is enabled in player builds. Tests inject a transcript and never open the microphone.

On each question the game writes `companion-state.json` in Unity's per-user persistent data directory. It contains stage, system depth, location, objective, suit health/power, hull condition, altitude and at most 24 world summaries: alien/robodog/ship populations and factory flags. It includes no keys or audio. The current world comes first. Story instructions are capped at 4,000 characters, history at six 240-character lines and the question at 400 characters. Scanner facts are capped at 1,200 characters. No simulation tick calls an LLM.

Apple's speech permission and local-only requirements are documented in [speech authorisation](https://developer.apple.com/documentation/Speech/asking-permission-to-use-speech-recognition) and [on-device recognition](https://developer.apple.com/documentation/speech/sfspeechrecognitionrequest/requiresondevicerecognition). Windows input uses Unity's [DictationRecognizer](https://docs.unity.com/en-us/engine/6000.7/script-reference/unityengine/windows/speech/dictationrecognizer). Microphone permissions, language availability and recognition quality still require testing on the target hardware.

The phrases “recall ship”, “pick me up”, “come get me” and “land near me” invoke the same ship recall as T. They are handled locally before any model request, whether typed or dictated. Other questions use the selected model when available, with authored guidance as the fallback. Voice input submits the transcript when you finish recording; Return submits typed text. It is not an always-listening assistant.
