# Architecture

Infinigame is a Unity 6.6 (URP) project in which almost nothing is authored in the editor. There is one scene holding one object, `Game`. A worker charts the seeded system and its landing sites. A loading coroutine builds the Unity objects one world per frame and refreshes the visible progress caption at most twice a second. Gameplay components stay disabled until `Game.Ready`. The runtime builds seeded worlds and lays out the interface. It loads finished meshes, textures and audio from Resources. This file says how the pieces fit. `README.md` says what the game is and how to play it, `AGENTS.md` says how to work on it, and `IDEAS.md` holds what may be built later, with notes on where each idea would fit.

## The two halves

| Assembly | Folder | What it is |
|---|---|---|
| `Infinigame.Core` | `Assets/Infinigame/Core` | The simulation as plain C# over `Unity.Mathematics`. No scene, no `MonoBehaviour`, no `UnityEngine` objects. Everything here can be compiled and tested without a Unity licence. |
| `Infinigame.Runtime` | `Assets/Infinigame/Runtime` | Unity components that read input, apply the core’s rules, draw the world and mix shipped audio with live speech. |
| `Infinigame.Editor` | `Assets/Infinigame/Editor` | Asset authoring, project set-up and platform builds, run from `script/`. |
| Tests | `Assets/Infinigame/Tests` | EditMode tests for the core, including the lap times the design promises, and PlayMode tests that drive the real scene. |
| | `Assets/Infinigame/Resources` | Finished models and wrecks, surface maps, visor sprites, shaders, recorded music, sound effects and story text. |

The rule that keeps this useful: a question the game has to answer ("where is the ground here?", "does this bolt hit?", "what does this mission win?") is answered in the core as a pure function or a small state step, and the runtime only asks it. If a feature needs a number, the number lives in the core with a test.

## Worlds

- `SolarSystemDescriptor.Generate(seed)` makes the whole system from one seed: a star, eight planets, their moons, the asteroid belt and the Presence citadel, as an array of `BodyDescriptor`. Sizes are to scale with each other, distances and periods are not. Names are the real ones.
- A body's ground is a height function, `BodyDescriptor.GroundRadius(direction)`, built on `TerrainNoise`. There are no terrain colliders. Walking, flight, landing, bolts, meshes and tests all read the same function, so there is nothing to fall through or stream in. Volcanoes (`BodyDescriptor.volcanoes`) are cones the function raises. Everything that changes the ground afterwards is a `Blast` the function reads: craters from `Dig`, which char, melt or turn to lava by what the ground was made of (`Blasted`), and the level pads that `Level` leaves for a ship to land on. `Terrain.shader` colours each kind and lets molten rock cool by the clock.
- Orbits and spin are on rails: `Orbit.Position(time)` and `BodyDescriptor.Rotation(time)` are pure functions of the system clock, in double precision.

### Frames of reference

The scene is always the frame of the body the player is nearest (`SolarSystem.Reference`). That body sits still at the origin while the rest of the system wheels round it, so the ground underfoot never moves and floats stay precise. Positions of the player, foes and bolts are stored relative to the reference body's centre, in its turning frame. When the reference changes, `SolarSystem.ReferenceChanged` fires and each mover rebases itself with `SolarSystemDescriptor.Rebase`. Out between worlds the reference is the star and the origin rides with the player (`SolarSystem.Anchor`): scene position is always `position - Anchor`.

### Drawing a world

`CelestialBody` draws one body as a cube-sphere quadtree (`CubeSphere`, `QuadtreeLod`). Patches are meshed off the main thread by `PatchMeshJob` (Burst) from the height function and shaded by `Resources/Terrain.shader` with imported material maps loaded by `Textures.cs`. `Scatter` places plants and rocks, `Creatures` and `Wildlife` the animals, `RingField` and `Rings` the rocks of rings and the belt, `Rainfall` and `Weather` the storms. Sea, air, cloud, giants, aurora and stars are shaders in `Resources`. Place skybox vertices at the far depth in both eyes. Draw the opaque gas-giant faces after the starfield and before the rings so the discs remain visible at orbital distances.

Detail tiers are Unity quality levels (Mobile, VR, PC), with per-platform defaults in Project Settings > Quality and budgets in `LodSettings.cs`. Every level-of-detail system decides what to draw by how big a thing looks on screen, and lets it linger past that before letting go, which is how the rule against flicker in `AGENTS.md` is kept.

`Climate` works out how hot it is anywhere, from the star, a giant's depths or a furnace world's air, and `Combat.Scorch` turns heat into damage. The scanner (`Scanner.cs`, with `Core/Scan.cs` choosing what is being looked at and describing it) records the local facts once per kind of subject per world, using the expedition’s prepared wording when available.

### Rendering and HUD regression checks

- Terrain patch jobs sample the seeded height function for vertices and neighbouring normals. Burst compiles these numerical loops for the target CPU while Unity Jobs schedules them on workers. The sandbox account cannot read `/Volumes`, which Burst probes on macOS. `script/unity` disables Burst in that environment. The exporter verifies the native library for the selected platform on a normal Mac account. Terrain performance comparisons require that library.
- Preserve HDR on monitor and mobile. For white combat flashes, inspect both the lit surface and its bloom. The Windows monitor uses bounded diffuse light on alien carapaces, wreckage and brood tissue. Limit light before multiplying the chitin texture so its detail survives. Keep combat lamps and glossy highlights out of the headset path. The desktop bloom clamp and the mobile tone mapper serve different displays. `EncounterRenderTests` renders the actual shell under a 24× light and checks near-white pixel coverage. A simulated headset and a Metal render still need a Windows player and headset check.
- Windows and macOS share the monitor HUD. Deck and touchscreen layouts reserve interaction space, and headset instruments use their own canvas. The input device used last changes prompts, while the platform determines layout. Size touch readouts against the safe area on phone and tablet and keep them above both moving thumbs. One canvas displays suit startup from 0% through the first punch prompt on every platform. Generation stages set the percentage. An unscaled half-second clock refreshes the diagnostic lines during loading. The first punch works as soon as the world is ready, even while the companion is preparing its opening dialogue. Exercise the playable HUD, including zoom and conversations, rather than relying on a canvas preview.
- Test distant gas surfaces on their dim side as well as under direct light. A stable sphere mesh alone cannot verify belt contrast, stereo projection or whether a depth prepass covers the opaque disc. Keep a close-range and a distant rendered check when changing planet shading.

## Expedition progression

`Progression` owns XP thresholds and the five upgrade ranks. `Game` awards combat, objective and discovery XP, pauses for a compulsory five-attribute level screen with confirmation and checkpoints progression at mission completion. `Campaign` separates the three Earth burrows, surface flight and orbital recovery. Version-three saves migrate earlier mission numbers without changing equipment bit positions.

Earth fronts and colonies have regional identifiers. `Frontier` assigns surface positions to the nearest of three brood sites, simulates each population and builds a shipyard with the third Earth factory and every factory on later worlds. New slots choose a fresh seed; the same stored seed still generates the same system. The first ship waits just above the atmospheric boundary. Detecting, recalling and boarding it require the orbital drive issued at the regional shipyard. Changing a mission does not rebuild unchanged factory models. Newly assembled units clone a prepared model and become the patrol itself.

## A frame

`Game.Update` decides who the controls belong to and then drives everything in a fixed order:

1. `Player.controls()` returns a `Controls` struct (`PlayerInput.cs`). Keyboard, mouse and gamepad fill it; tests fill it directly. Menus, conversation and death take the controls away by replacing the struct with an empty one.
2. `Player.Tick` steps the simulation in slices of at most 1/60 s: `Walker.Step` on foot, `Ship.Step` or `Autopilot.Step` in flight. At a planetary mission the autopilot follows the surface curvature to the landing area and turns the cockpit towards the objective. The suit absorbs landing impacts. Ship crashes, heat and radiation can cause damage. The player then places the camera and the ship model.
3. `Outpost.Tick` and `Battle.Tick` run the mission's guards and the fighting: `Combat.Hunt` moves foes, `Combat.Step` moves bolts and reports strikes, and the runtime turns strikes into craters, effects and sound.
4. `SolarSystem.Update` (which runs first, by execution order) advances the clock, picks the reference body and places every body, the sun's light, ambient light and fog.
5. `Hud.LateUpdate` lays out the interface from the state of everything above.

## The campaign

`MISSIONS.md` is the mission table, loaded by `Core/Narrative.cs` into `Campaign`. `STORY.md`, `FACTIONS.md` and `COLONIES.md` supply the plot, faction context and building costs. `Editor/ContentImport.cs` packages copies as Resources text assets. Mission locations are fixed before terrain is changed. Ownership is the stable `Player.kit` bit set; `Player.fitted` holds the powered modules, all enabled when recovered. `Suit` validates owned modules; the three Earth broods progressively restore beam power, surface thrusters and orbital flight. `Game` owns progression and saves it.

`Outpost` cuts descending terrain pads and draws enclosing rock shells. Ground movement and terrain meshing share the same pad heights; the shells supply side and ceiling collision. Dry burrow volumes exclude sea rendering and underwater movement without adding another terrain collision source. It spawns the region’s linked minds and their guards, then exposes the demolition charge when every mind dies. The charge display and audible countdown use the saved detonation time. After demolition an equipment case appears beside the region’s factory and opens when approached on foot. The visor and the expedition summary share the live objective in `Game.MissionAction`. Inside the citadel, the next waypoint is a defender holding the first closed section shield. The citadel is a ship-sized tunnel with a timed escape after its core is destroyed. The factory phase moves a moon onto the final mind, starts black hole system consumption and moves the traveller into a pre-generated harder system.

The recovery robot (`Hermit.cs`) briefs each mission from the table, so the story needs no language model. At spawn it takes its place 4.5 metres beyond the hole exit. The opening dialogue can then arrive while the player is underground or after they emerge. After the first meeting it speaks over the radio; once the ship is recovered it joins the cockpit.

## Language models

`Oracle.cs` turns a persona and a prompt into a reply, for expedition wording and free-form questions. `ExpeditionText.cs` batches stage-appropriate dialogue, mission guidance and field descriptions, then caches validated lines in the active save. It never holds a key of its own. For each of its two voices, Claude and ChatGPT, it tries in order:

1. a key the player has given it (environment or clipboard), sent straight to the maker's API with `UnityWebRequest`;
2. for ChatGPT, OpenAI's Sign in with ChatGPT, done by the game in the player's browser;
3. on a desktop, the makers' own command-line tools with the sign-ins they keep: Claude Code for Claude; `codex` for ChatGPT.

The game can start each tool's own sign-in from the settings page. It never shows a sign-in form itself. Every caller goes through `Oracle.Ask`, a delegate that tests replace with a script, and has a fallback for when no model answers.

## Effects

`Effects.cs` draws everything that burns, bursts, smokes or steams: explosions, hits on a hull, steam off water, mushroom clouds and factory lights. It is built from three kinds of thing, each drawn in one instanced call: puffs (`Resources/Puff.shader`), sparks (`Spark.shader`) and tumbling wreckage. `Battle` chooses the effect for a strike. Small weapon impacts use the billow shader for a cooling core, translucent dust and drifting embers. Harrowers and Skimmers use matte URP surfaces on Windows and in headsets. On other platforms `Hull.shader` reveals violet tissue as they take damage. Close combat in VR also omits blast lamps, additive hit flashes and bloom. `IchorPool` spreads and settles a small surface mesh against the terrain height function.

`Corpses.cs` uses the pre-cut mesh sections and retains a bounded number of articulated remains. `Core/Ragdoll.cs` steps their joints against the same ground function as living bodies; runtime interior collision keeps them inside tunnels. The sections settle on terrain or tunnel floors and wake when their support is destroyed. Further hits separate sections, including spacecraft wreckage.

## Interface, sound and speech

- `Hud.cs` builds one uGUI canvas in front of the camera, from code. It reads state and never changes it. Markers for worlds, foes and mission goals are placed each frame by `Mark`, which hides what a world is in the way of, keeps names from overlapping and holds a guide mark at the edge of the screen when its place is out of view.
- `AudioWorkshop` renders the effects in `Core/Synth.cs` to 48 kHz stereo files. `Sounds` places and mixes those takes in play. `Music` plays the exploration and combat recordings in `Resources/Music`. `Playlist` schedules complete tracks with equal-power crossfades; `Music` uses two streaming decks per mood.
- `Speech.cs` renders every spoken line with the bundled Festival SLT model and native Flite+HTS engine, off the main thread. Unity queues the returned 32 kHz speech, pauses it with the menu and plays the recovery robot’s nearby voice from its position. `script/build-speech` compiles the library, and `VoiceBuild` bundles it and its model. `VoiceInput` handles held microphone input and partial transcripts. Tests substitute both speech and dictation.
- `Editor/ModelWorkshop*.cs` and `MeshKit.cs` describe the original geometry. `script/bake-assets` saves articulated prefabs, their materials, shared mesh sets, damaged trees and wreck sections. `Runtime/Models.cs` loads the appropriate level of detail. `Arms` drives the first-person gauntlets. Gauntlets and the wrist console share the calibrated grip pose. The suit plants its feet until the player moves and lets the torso follow their gaze. In a headset the collar heat exchangers, shoulder plates and both arm hierarchies are hidden. A tracked punch is measured at the visible knuckles and can contact a burrow plug as the hand slows. Each hand must travel 11 cm at striking speed and retract at least 8 cm to strike again.
- `DeviceControls.cs` merges touch, gamepad, keyboard and tracked controllers. Cockpit hit areas dispatch the same `Controls` intents as buttons. `ForearmConsole.cs` attaches a touchable construction and suit menu to the left tracked hand; interface contact suppresses motion punches.
- `Cockpit.cs` drives the ship's instruments. `Models.Ship` names every part that moves or lights, `Player.Tick` fills a `Readings` struct with what the ship is doing, and `Cockpit.Show` eases each needle, lamp, switch and the stick to match. Nothing on the dash is decoration.

## Saving

A save slot is one small JSON file (`Game.Save`): the system's seed, the depth, the mission stage, the gear bit set, fitted modules, colony resources, presence clock, moonfall, gate state, journal, talk breached outpost walls and armed/destroyed burrow demolition state. Worlds are never saved, since the seed remakes them. Save version 3 migrates earlier campaigns into the regional Earth progression without moving equipment bit positions. Save version 4 numbers levels from one, preserves earned ranks and keeps the wording prepared for that expedition. Saves also retain XP and the current mission checkpoint. War fronts also retain weapon exposure and whether their controlling mind is dead. Settings that belong to the device (detail, music, speech) are `PlayerPrefs`. Oracle credentials are separate from saves: Keychain on iOS and a local account file on desktop. [PRIVACY.md](PRIVACY.md) describes local storage, optional provider requests, on-device iOS speech recognition and TestFlight diagnostics; [TESTFLIGHT.md](TESTFLIGHT.md) holds public beta submission copy.

## Tests and tooling

- `script/test` first checks TestFlight distribution, release gating and signing cleanup with `script/test-testflight`, then compiles every assembly with the editor's bundled compiler and runs the EditMode tests on its Mono, with no Unity process and no licence.
- `script/test --unity` runs EditMode and PlayMode tests inside Unity. The PlayMode tests load the real scene and drive it through `Controls`, with every road out of the game (models, browser, speech) swapped for a stand-in in set-up. One of them, `CapturesScreenshots`, tours the game and writes pictures to `Logs/Screenshots`, which is how visual work is checked on a machine with no display.
- `FrameLog.cs` records five-second median windows and gradually adjusts terrain LOD and supported dynamic resolution under sustained load. It writes frame timing, activity, memory, build and HDR state to `Logs/frames.log` when the game is started by `script/play`. `script/performance` summarises the latest real play session, excluding menus.

## TestFlight distribution

Pushes to `main` build the project version for iPhone and iPad and for native Apple silicon Mac. Each platform uploads one build eligible for internal and external testing. The workflow serialises pushes and saves each platform's release version, build number, commit and run before signing. Build numbers start at the commit count and increase beyond Apple's upload history and earlier reservations. A rerun recovers the saved identity and checks whether Apple received it before building again.

The Apple job signs and validates both platform archives on one Mac. A Linux job waits up to 20 minutes per build for processing, assigns the internal group if needed and submits ready builds for external review using **What to Test** from `TESTFLIGHT.md`. A distribution failure fails the push workflow and can be retried on that run. Apple controls when public testers receive a build. [CI.md](CI.md)

## Where to put things

| To add | Start in |
|---|---|
| A mission, or something it wins | `MISSIONS.md`, then whatever asks `Player.Has` |
| A weapon | `Combat.Guns` or `Combat.Arms`, a voice in `Synth.cs`, a model in `ModelWorkshop` |
| A foe | `FoeKind`, `Combat.Hunt`, a model in `ModelWorkshop`, drawing in `Battle.Update` |
| An explosion, smoke or fire | `Effects.cs`, called from where `Battle.Tick` handles strikes |
| Something about the ground | `TerrainNoise`, `BodyDescriptor.GroundRadius`, `Terrain.shader` |
| A key | `Controls` and `DesktopControls.Read`, then the list in `Hud.Help` |
| Something on screen | `Hud.Awake` to build it, `Hud.LateUpdate` to drive it |
| A setting | `Game.Items`, `Game.Choose`, `Game.Hint` |

## The suit and colony expansion

`Suit` owns the recovered modules and shared energy rules. `Player.kit` still records ownership; `Player.fitted` records the recovered powered modules. Stable gear bit positions preserve equipment from older saves. `Combat.Ray` handles continuous beams and piercing rail shots, while missiles remain bolts. `Shield` rejects incoming fire at the boundary before damage or splash is applied.

`Interior` describes built destructible tunnel shells as boxes in a local frame. `Outpost` draws those same boxes and resolves movement against them. Terrain collision still comes only from `GroundRadius`. A stationary `FoeKind.Mind` controls the outpost; ground minds stay rooted above the current excavated floor. Shield guards are stored with their outpost when leaving its reference frame, rather than also following the player. `Combat.Surface` excludes sea level from weapon collision inside dry burrows; its death opens the prize and scatters the bodies. Sentries observe noise, light and visibility. There is no terminal hacking. Each citadel chamber opens its exit field when its own turrets and relay mind fall. The final mind starts the escape timer.

`War` and saved `Front` populations simulate alien and robot replication and attrition. Saved death counts reduce enemy damage, reinforcement frequency and hostile growth without a difficulty message. `EscortOrders` retains the Follow, Attack or Patrol order. One escort slot serves the called ground dog or a ship. `Frontier` rebases the escort with the player and uses `War.Follow` to keep the ship in formation at autopilot speeds; the wider population still spreads only to neighbouring planets. `Colony` retains factory sites and legacy save fields. `Moonfall` controls the impact. Construction saves the chosen moon and landing site. The moon holds its launch attitude during the burn so the engines face away from the impact planet. The cockpit follows behind the engine bank. Construction executes directly in the region. `Frontier` steps the wider colonies during play, draws the factories, nearby laser dogs and ships and stages the moon impact and black hole. Game saves hold those states, the clock and equipment. Old resource and gate fields remain readable for save compatibility. `SolarSystemDescriptor.Beyond` uses a separate random stream; `Generate` retains the original system's draws. The old gate is no longer appended; original world indices remain stable.

`Radar` draws a transparent sphere and 3D contacts separately from the canvas. `DeviceControls` supplies touch and XR input through `Controls`. The iOS native bridge stores account data in Keychain. Speech uses the same bundled Festival SLT voice on every target, with text fallback if synthesis fails. `Platforms` configures device exports and the Windows OpenXR loader and Valve Index profile. The platform settings are generated by code when building.

Machine prefabs share imported meshes, with separate articulated joints driven by `MachineMotion`. Presence bodies use curved carapaces, sensory organs and rooted neural folds. `MotileCaste` selects stalker, breacher or spitter combat rules within the stable sentry kind; `Outpost` assigns the mix and `Battle` instances each anatomy. `SurfaceWorkshop` bakes the faction material artwork into colour, height and normal maps alongside the terrain library. `Models.Surfaces` builds the panelled interior shells. Terrain blast pools use a level surface bounded by the crater, with slow shader flow.

`SuitHud` draws the helmet instruments. The loading canvas in `Game` stays in place from the first diagnostic through the ready prompt. `VoiceInput` requests native dictation only on a player action, with an injectable test route. `VoiceBuild` compiles the macOS speech bridge and adds permission strings; iOS compiles the same Objective-C++ source. `Hermit` writes a bounded JSON state snapshot before a question. `Frontier` pre-generates the next descriptor and mission sites on a worker task during moonfall.

`DeviceControls` merges direct Input System touch regions with keyboard, mouse, gamepad and Index controls. Its canvas only draws the controls; button state does not depend on IMGUI events. `SuitTutorial` gates the initial control recovery. First-person renderer masking and head-relative VR body placement share `Player`; independent tracked gauntlets remain in `Arms`. The outer breastplates and collar grilles belong to the third-person suit. The articulated suit mirrors the left glove. Both thumbs face up for punches and the palms turn down for wrist weapons. The beam, rail and missile sight share a right-wrist mount with a separate model and aperture for each. In VR the aperture’s forward axis sets the sight, damage ray and rail release direction after glove calibration. The desktop wrist sights converge on the crosshair. A blue round grows between the rails as they charge. The backpack stays hidden in the headset while its launch sockets still place missiles and hemisphere beams. The gauges, mission and weapon share one visor layout. On boarding, the pilot recentres and these instruments move to the windscreen. Menus expose a nearby plane for Index ray selection.

`FactoryDefence` aims each factory's visible mount and fires through the shared combat path. Its targets must be inside the local atmosphere. The kennel and flight deck have separate assembly timers. Piloted ships patrol above the atmosphere. `Rings.Trace` checks the flight path against rocks in the current reference frame and its parent belts, including the star’s belt around the citadel. `Scatter` draws severed branches using the bark and needle triangles removed from the standing tree and animates uprooted grass. `Hull.shader` adds violet wounds to organic models, while bounded ichor pools keep combat readable without red blood.

## Development commands

| Script | What it does |
|---|---|
| `script/bootstrap` | Installs Unity Hub, the Unity CLI, Claude Code and OpenAI's `codex` from the `Brewfile`, then the pinned editor version. Only Unity is required: with no model to ask, the scanner reports observed facts and the robot uses authored replies. |
| `script/setup` | Bootstraps, makes sure the current user has a Unity licence and, if they can reach neither Claude nor ChatGPT, signs them in to Claude for the scanner and the robot. |
| `script/editor` | Opens the game in the Unity editor, on the game scene. Press Play. |
| `script/play` | Plays the game as a standalone app, building it first if the project has changed or the last build has no Burst-compiled code. The game's log goes to `Logs/Player.log` and its frame rate to `Logs/frames.log`. |
| `script/package macOS` | Packages existing output; also accepts `Windows` or `iOS`. |
| `script/testflight --dry-run` | Checks signing of an existing iOS export or a native Mac export with `TESTFLIGHT_PLATFORM=macOS`. GitHub Actions distributes internal and public betas on pushes to `main`. See [CI.md](CI.md). |
| `script/bake-assets` | Rebuilds the model, texture and sound libraries. Add `surfaces` to refresh the artwork alone. |
| `script/build-number` | Prints the Git commit count used for macOS and iOS builds. |
| `script/build-ios` | Exports `Build/iOS`, then compiles and links the device app with Xcode. Reuses the existing signing team and prints installation steps. |
| `script/build-windows` | Builds `Build/Windows/Infinigame.exe` with OpenXR and packages `Build/Infinigame-Windows.zip`, with installation steps. |
| `script/build-steam-deck` | Builds the shared Windows, SteamVR and Proton archive. |
| `script/performance` | Reports the latest real Mac play session from `Logs/frames.log`, excluding menus. |
| `script/build` | Builds `Build/Infinigame.app` for macOS. Unity's build log goes to `Logs/build.log`. |
| `script/build-speech [macOS\|Windows\|iOS]` | Builds the offline Festival voice library; ordinary builds call this automatically. |
| `script/test-speech` | Checks native speech without playing sound and writes `Build/Festival-preview.wav`. |
| `script/test-testflight` | Checks release gating and signing cleanup without credentials or uploads. |
| `script/test` | Checks TestFlight automation, compiles every assembly and runs the EditMode tests. Needs no Unity licence. |
| `script/test --unity` | Also runs the EditMode and PlayMode tests inside Unity and saves pictures to `Logs/Screenshots`. |
| `script/unity` | Runs the pinned editor against this project with any arguments. |
