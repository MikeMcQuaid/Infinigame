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

Text generation and speaking are separate. The game uses free local macOS speech. New preferences default to **Zarvox**, an intentionally synthetic robot voice; an existing saved choice is preserved. Settings > Spoken voices offers Zarvox, Daniel and Eddy in British English, System voice and Text only. An unavailable named voice falls back to the system voice.

| Platform | Implemented now | Other local options |
|---|---|---|
| macOS | Zarvox, Daniel, Eddy and the system voice | Download a voice in Accessibility > Read & Speak, select it as the system voice and choose System voice in the game. Fred, Ralph and Trinoids are other synthetic character choices where installed. |
| iPhone/iPad | Text replies, native voice input | Apple's downloaded accessibility voices are a possible future AVSpeechSynthesizer integration. Selecting an OS voice does not currently make the game speak. |
| Windows/SteamVR | Text replies, OS dictation input | An installed SAPI voice would be a small future integration. Windows Narrator's natural voices are available for Narrator; they are not automatically game voices. |
| Steam Deck through Proton | Text replies and typed input | Local speech would need a separate Linux/Proton bridge. Nothing downloads a voice model or calls paid speech. |

See [Apple's Mac voice settings](https://support.apple.com/en-euro/guide/mac-help/mchlp2290/27/mac/27) and [Microsoft's Narrator voice settings](https://support.microsoft.com/en-us/accessibility/windows/narrator/chapter-7-customizing-narrator). This pass keeps non-Mac output as text rather than adding several speech backends. Pre-recorded lines or a paid service remain optional future choices.

## Voice input and bounded state

Press Y, Mic on a mobile device or left grip plus right-stick click in VR to start dictation. Press it again to finish. The transcript is sent as a normal companion question. Permission is requested only when you choose to speak. Apple builds require on-device British English recognition; no audio is sent to Apple. If the language model is unavailable, the transcript receives an authored reply. If local dictation is unavailable or permission is denied, type with Return instead. Windows uses its OS speech recogniser and its privacy settings. The Unity editor uses typed input; native voice is enabled in player builds. Tests inject a transcript and never open the microphone.

On each question the game writes `companion-state.json` in Unity's per-user persistent data directory. It contains stage, system depth, location, objective, suit health/power, hull condition, altitude and at most 24 world summaries: alien/robodog/ship populations and factory flags. It includes no keys or audio. The current world comes first. Story instructions are capped at 4,000 characters, history at six 240-character lines and the question at 400 characters. Scanner facts are capped at 1,200 characters. No simulation tick calls an LLM.

Apple's speech permission and local-only requirements are documented in [speech authorisation](https://developer.apple.com/documentation/Speech/asking-permission-to-use-speech-recognition) and [on-device recognition](https://developer.apple.com/documentation/speech/sfspeechrecognitionrequest/requiresondevicerecognition). Windows input uses Unity's [DictationRecognizer](https://docs.unity.com/en-us/engine/6000.7/script-reference/unityengine/windows/speech/dictationrecognizer). Microphone permissions, language availability and recognition quality still require testing on the target hardware.

The phrases “recall ship”, “pick me up”, “come get me” and “land near me” invoke the same ship recall as T. They are handled locally before any model request, whether typed or dictated. Other questions use the selected model when available, with authored guidance as the fallback. Voice input submits the transcript when you finish recording; Return submits typed text. It is not an always-listening assistant.
