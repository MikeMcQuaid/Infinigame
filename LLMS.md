# Language models and voice

## What calls a model

| Trigger | Sent to the selected model | Gameplay contribution |
|---|---|---|
| Start or advance an expedition with a connected account | The expedition seed, known lore, authored dialogue, mission instructions and world descriptions | Wording is prepared in small batches and kept with the save |
| Choose a suggested robot question or scan something | Nothing | Prepared companion lines and recorded field observations are available immediately |
| Type or dictate a new question | Current briefing, game state, known story and up to three recent exchanges | A short reply to the player's own words |
| Move, fight, generate terrain or build a colony | Nothing | Deterministic game code |

**Expedition language model** is available from the title and Settings. Connect one account and disconnect it to switch providers. The game selects the least expensive supported text model and the lowest reasoning setting for the connection. Claude uses Haiku 5.5 with low effort. ChatGPT uses GPT-6 Luna with no reasoning on the API and low effort through Codex. Free-form conversation uses the same selection.

The robot’s opening dialogue, mission guidance and scanner observations can vary across expeditions. The wake-up sequence and the greeting on arrival in a new system wait for their first batch. If the provider is unavailable, the robot uses recorded guidance. Names, controls and interface labels stay consistent.

`STORY.md` defines the companion's concise universe context. Only `Public`, reached `Stage N` sections and an unlocked `After moonfall` section enter its prompt. `FACTIONS.md` adds the shared facts, excluding art notes. The model cannot change the simulation or unlock equipment. Its reply is flavour and advice; mission progression comes from game rules.

## Connections and costs

For either provider, the player's own API key takes precedence. ChatGPT can also use its browser sign-in. On desktop, Claude Code and Codex can use their own existing sign-ins, with tools disabled for these text requests. On Windows and SteamVR, choosing Claude opens the authorisation page supplied by Claude Code. If the client is missing, the game opens its installation guide. The ant integration has been removed.

The provider page shows the route in use, selected model, requests and token counts. API calls use your account's billing. Signed-in tools use their own plans. There is no bundled key, background model polling or required subscription. Tests replace every external connection with a stand-in.

## Voice choices

Every spoken line uses the bundled **Festival SLT** voice: tutorial, mission guidance, factory announcements and live LLM replies. It runs offline on macOS, Windows/SteamVR, Steam Deck through Proton and iPhone/iPad. No system voice, speech service, subscription or runtime download is needed. Existing saves use this voice too; voice mute and subtitle preferences are preserved.

The acoustic model is the exact [cmu_us_slt_arctic_hts package distributed with Festival](https://www.cstr.ed.ac.uk/downloads/festival/2.1/festvox_cmu_us_slt_arctic_hts.tar.gz), using its original 32 kHz parameters. A small [Flite+HTS engine](https://hts-engine.sourceforge.net/) replaces Festival's larger Scheme front end. It uses the same model on every platform, although pronunciation and phrasing can differ slightly from desktop Festival. SLT is an American English voice. Accents and typographic punctuation are normalised for its English text front end.

**Settings > Robot voice** toggles the local voice, which has a fixed modulation for the robot. Turning off the last active output switches the other one on. Once a reply is displayed you can ask the next question while its audio is still playing. Speech and captions pause with the menu and map, then finish the current line before the next one starts.

Synthesis runs off the game thread. Tests replace `Speech.Render` with a stand-in. `script/test-speech` checks the native engine without playing audio and writes `Build/Festival-preview.wav`.

Source archives, checksums, modifications and licences are recorded in [ThirdParty/Festival/README.md](ThirdParty/Festival/README.md). The voice's licence notices are included inside every app. The owner explicitly approved this bundled speech dependency; it is separate from the game's generated art and sound effects.

## Voice input and bounded state

Connect a language model to enable the microphone. Hold Y, Mic on a mobile device, LT + R3 on a gamepad or Voice on the VR wrist display to dictate. Release to finish. Partial recognition appears in the HUD when available. The transcript becomes a companion question and the reply stays in the HUD without opening a menu. Permission is requested only when you choose to speak. Apple builds require on-device British English recognition; no audio is sent to Apple. If the language model is unavailable, the transcript receives an authored reply. If local dictation is unavailable or permission is denied, type with Return instead. Windows uses its OS speech recogniser and its privacy settings. The Unity editor uses typed input; native voice is enabled in player builds. Tests inject a transcript and never open the microphone.

On each question the game writes `companion-state.json` in Unity's per-user persistent data directory. It contains stage, system depth, location, objective, suit health/power, hull condition, altitude and at most 24 world summaries: alien/robodog/ship populations and factory flags. It includes no keys or audio. The current world comes first. Story instructions are capped at 4,000 characters, history at six 240-character lines and the question at 400 characters. Scanning fills in the local subject and location when the entry is recorded.

Apple's speech permission and local-only requirements are documented in [speech authorisation](https://developer.apple.com/documentation/Speech/asking-permission-to-use-speech-recognition) and [on-device recognition](https://developer.apple.com/documentation/speech/sfspeechrecognitionrequest/requiresondevicerecognition). Windows input uses Unity's [DictationRecognizer](https://docs.unity.com/en-us/engine/6000.7/script-reference/unityengine/windows/speech/dictationrecognizer). Microphone permissions, language availability and recognition quality still require testing on the target hardware.

The phrases “recall ship”, “pick me up”, “come get me” and “land near me” invoke the same ship recall as T. They are handled locally before any model request, whether typed or dictated. Other questions use the selected model when available, with authored guidance as the fallback. Voice input submits the transcript when you finish recording; Return submits typed text. It is not an always-listening assistant.
