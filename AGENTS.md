# Working on Infinigame

Read `ARCHITECTURE.md` first for how the code fits together, and `README.md` for what the game is. This file is the short list of things that are easy to get wrong.

## Commands

Every script is listed in `README.md`. The ones used while changing the game:

| Command | Use |
|---|---|
| `script/test` | Compile everything and run the EditMode tests without starting Unity. About half a minute. Run it after every change. |
| `script/test --unity` | EditMode and PlayMode tests inside Unity. About ten minutes. Add `-testFilter 'NameA\|NameB'` to run a few. |
| `script/test --unity -testFilter CapturesScreenshots` | The screenshot tour: writes `Logs/Screenshots/*.png`. Look at the pictures to judge anything visual, and copy the ones the README shows into `docs/`. |

New tooling goes in `script/`, following [scripts to rule them all](https://github.com/github/scripts-to-rule-them-all).

## Rules of the project

- **Everything is made by code.** Terrain, models, textures, materials, shaders, the interface, sound effects and the fallback music are all generated. Do not add downloaded art, fonts, models, packages or Asset Store content. The one exception is music the owner drops into `Assets/Infinigame/Resources/Music`.
- **The core stays pure.** `Assets/Infinigame/Core` has no scene dependencies. Game rules and numbers go there, with an EditMode test. The runtime asks the core and draws the answer.
- **The ground is a function.** `BodyDescriptor.GroundRadius` is the terrain for walking, flying, meshing and tests alike. It is called a great deal: keep it cheap, and never add a second source of truth such as a collider.
- **Nothing drawn may flicker.** Anything with a level of detail appears once it is big enough to see and stays until it is off screen, over the horizon or too small. No popping in and out, no z-fighting, no shimmering lines.
- **The design's promises are tests.** Walk round the home world in under an hour and fly round it in under a minute; walk round the moon in under five minutes and fly round it in under fifteen seconds. `MotionTests` and `ScaleTests` hold these: do not weaken them.
- **A seed is a system.** `SolarSystemDescriptor.Generate` must give the same system for the same seed. Do not change the order or number of its random draws: take new randomness from a generator of its own.
- **The interface is held to a shipped-game standard.** No debug text on screen. The HUD says little until there is something to say.
- **No key is ever built into the game**, and the game never shows a sign-in form. Players bring their own key or sign in with the makers' own tools and pages. See `Oracle.cs`.
- **Tests never spend anyone's money.** PlayMode tests must not reach a real model, browser or speech engine. Set-up swaps `Oracle.Ask`, `Oracle.Tools`, `Oracle.Hosts`, `Oracle.Browse`, `Game.Clipboard` and `Speech.Tool` for stand-ins: keep it that way when adding a new road out of the game.
- **Take feel, never content.** The game borrows mechanics and mood from other games, films and books. It does not use their names, characters, designs, music or levels.
- **Game audio is muted in batch mode**, so tests are silent.

## Conventions

- C# 9, as Unity compiles it. No C# 10 or later features.
- Small diffs. No abstractions for things used once: inline them. Prefer deleting to adding.
- Names carry the meaning; comments are brief and say why, not what.
- UK spelling, no em dashes and no Oxford commas, in code comments, interface text and documents.
- Fix a bug by first writing the test that fails.
- A feature is not finished until `script/test` passes, and anything that touches Unity APIs, shaders or the scene has been run with `script/test --unity`. `script/test` compiles against every engine library, so it will not notice a Unity module missing from `Packages/manifest.json`.
- Tune detail budgets in `LodSettings.cs` from `Logs/frames.log`, which `script/play` writes during real play, not from guesses.
- Temporary files go in the ignored `tmp/` folder at the repository root, which is also where extra checkouts for parallel work belong (`tmp/worktrees/<task>`).
- Never edit files under `Assets/` while a Unity test run or build is in progress on the same checkout.

## Things that bite

- Unity locks an open project: close the editor before running `script/test --unity` or `script/build`.
- Burst cannot run everywhere (some sandboxes forbid it). The scripts cope, but a build made without Burst is slow: `script/play` rebuilds one when it finds it.
- A batch-mode editor has no game window, so screenshots are rendered to a texture from a coroutine. Coroutines run before `LateUpdate`: anything drawn with `Graphics.RenderMeshInstanced` from `LateUpdate` is missing from such a picture.
- Light is blended in linear space, so interface overlays look weaker than their alpha suggests.
- Positions are relative to the body the player is nearest and the scene's origin can ride with the player. Draw at `position - game.System.Anchor`, and rebase anything long-lived when `SolarSystem.ReferenceChanged` fires.
- `out` is a C# keyword and makes a poor variable name.
