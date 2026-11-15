# Language models and voice

## What calls a model

| Trigger | Sent to the selected model | Gameplay contribution |
|---|---|---|
| Type to the companion, or choose one of its questions | Known story and faction facts, current briefing, location, recent discoveries and the last six exchanges | Short contextual answers about the world and what to do |
| Scan a new kind of object on a world | Observed terrain, climate and subject facts | A named field-journal entry, saved for later reading |
| Read a briefing, take equipment or build something | Nothing | Authored guidance from `MISSIONS.md` and `COLONIES.md` |
| Move, fight, generate terrain, build models or simulate colonies | Nothing | Deterministic game code |

The current defaults are Claude Haiku 5.5 and GPT-6 Luna, with low thinking effort. Claude also offers Sonnet 5.5 and Opus 5.5; ChatGPT offers GPT-6.1 Terra, GPT-6.1 Sol and GPT-6 Astra. These are the configured choices in `Oracle.Models`; account access still depends on the provider.

Settings > Claude or ChatGPT selects the provider, exact model and thinking level. The current selection appears during requests. Conversation and scanner cards label generated replies, authored guidance and saved entries. A failed request uses authored companion answers or observed scanner facts. Re-reading a saved scan makes no request.

`STORY.md` defines the companion's concise universe context. Only `Public`, reached `Stage N` sections and an unlocked `After moonfall` section enter its prompt. `FACTIONS.md` adds the shared facts, excluding art notes. The model cannot change the simulation or unlock equipment. Its reply is flavour and advice; mission progression comes from game rules.

## Connections and costs

For either provider, the player's own API key takes precedence. ChatGPT can also use its browser sign-in. On desktop, Claude Code and Codex can use their own existing sign-ins, with tools disabled for these text requests. The ant integration has been removed.

The provider page shows the route in use, selected model, requests and token counts. API calls use your account's billing; signed-in tool usage follows that tool's plan. There is no bundled key, background model polling or required subscription. Tests replace every external connection with a stand-in.

## Voice choices

Text generation and speaking the resulting words are separate. The game currently uses **free local macOS speech**, defaulting to Daniel in British English at natural pitch with the previous modulation removed. Settings > Spoken voices offers System voice, Daniel, Eddy and Text only. An unavailable named voice falls back to the system voice. Windows and iPad show text.

For another free voice, download one under macOS Accessibility > Read & Speak, select it as the system voice, then choose System voice in the game. See [Apple's voice settings](https://support.apple.com/en-euro/guide/mac-help/mchlp2290/27/mac/27).

Options for a later voice pass:

| Option | Benefit | Cost and work |
|---|---|---|
| Local downloaded Mac voice | Offline, no network latency or usage charges | Already supported; quality depends on the installed voice |
| Pre-generated ElevenLabs mission lines | Audition a consistent character voice and reuse recordings | Authoring/import work; paid commercial plan for shipped audio. Dynamic replies can remain text |
| Live ElevenLabs speech | The same voice reads improvised replies | Another key, network call, playback delay and usage bill |
| OpenAI Realtime speech | Conversational audio | A separate Realtime integration and audio billing; more work than the current text pipeline |

For a small next step, audition a few pre-generated mission lines before adding live speech. [ElevenLabs currently lists Starter at $6/month with 30,000 credits and a commercial licence](https://elevenlabs.io/pricing), excluding tax; generation allowances vary by model. No paid speech service is enabled in the game.

OpenAI's older `tts-1`, `tts-1-hd` and `gpt-4o-mini-tts` speech models are scheduled to shut down on 6 January 2027. Its recommended replacement uses the Realtime API, so it is not a drop-in change to the old speech endpoint. See [OpenAI's current speech guide](https://developers.openai.com/api/docs/guides/text-to-speech). Voice options checked on 15 November 2026.
