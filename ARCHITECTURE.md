# Architecture

Infinigame is a Unity 6.6 (URP) project in which almost nothing is authored in the editor. There is one scene holding one object, `Game`. A staged loading coroutine shows progress before constructing the runtime. Gameplay components stay disabled until `Game.Ready`. Everything else is built from code when the game starts: worlds, models, textures, the interface, sound and music. This file says how the pieces fit. `README.md` says what the game is and how to play it, `AGENTS.md` says how to work on it, and `IDEAS.md` holds what may be built later, with notes on where each idea would fit.

## The two halves

| Assembly | Folder | What it is |
|---|---|---|
| `Infinigame.Core` | `Assets/Infinigame/Core` | The simulation as plain C# over `Unity.Mathematics`. No scene, no `MonoBehaviour`, no `UnityEngine` objects. Everything here can be compiled and tested without a Unity licence. |
| `Infinigame.Runtime` | `Assets/Infinigame/Runtime` | Unity components that read input, call the core, draw what it describes and play what it synthesises. |
| `Infinigame.Editor` | `Assets/Infinigame/Editor` | Project set-up and the build, run from `script/`. |
| Tests | `Assets/Infinigame/Tests` | EditMode tests for the core, including the lap times the design promises, and PlayMode tests that drive the real scene. |
| | `Assets/Infinigame/Resources` | A few base materials and every shader: ground, plants, sea, air, cloud, giants, rings, aurora, rain, fire, smoke and hulls. |

The rule that keeps this useful: a question the game has to answer ("where is the ground here?", "does this bolt hit?", "what does this mission win?") is answered in the core as a pure function or a small state step, and the runtime only asks it. If a feature needs a number, the number lives in the core with a test.

## Worlds

- `SolarSystemDescriptor.Generate(seed)` makes the whole system from one seed: a star, eight planets, their moons, the asteroid belt and the Presence citadel, as an array of `BodyDescriptor`. Sizes are to scale with each other, distances and periods are not. Names are the real ones.
- A body's ground is a height function, `BodyDescriptor.GroundRadius(direction)`, built on `TerrainNoise`. There are no terrain colliders. Walking, flight, landing, bolts, meshes and tests all read the same function, so there is nothing to fall through or stream in. Volcanoes (`BodyDescriptor.volcanoes`) are cones the function raises. Everything that changes the ground afterwards is a `Blast` the function reads: craters from `Dig`, which char, melt or turn to lava by what the ground was made of (`Blasted`), and the level pads that `Level` leaves for a ship to land on. `Terrain.shader` colours each kind and lets molten rock cool by the clock.
- Orbits and spin are on rails: `Orbit.Position(time)` and `BodyDescriptor.Rotation(time)` are pure functions of the system clock, in double precision.

### Frames of reference

The scene is always the frame of the body the player is nearest (`SolarSystem.Reference`). That body sits still at the origin while the rest of the system wheels round it, so the ground underfoot never moves and floats stay precise. Positions of the player, foes and bolts are stored relative to the reference body's centre, in its turning frame. When the reference changes, `SolarSystem.ReferenceChanged` fires and each mover rebases itself with `SolarSystemDescriptor.Rebase`. Out between worlds the reference is the star and the origin rides with the player (`SolarSystem.Anchor`): scene position is always `position - Anchor`.

### Drawing a world

`CelestialBody` draws one body as a cube-sphere quadtree (`CubeSphere`, `QuadtreeLod`). Patches are meshed off the main thread by `PatchMeshJob` (Burst) from the height function and shaded by `Resources/Terrain.shader` with textures painted in code by `Textures.cs`. `Scatter` places plants and rocks, `Creatures` and `Wildlife` the animals, `RingField` and `Rings` the rocks of rings and the belt, `Rainfall` and `Weather` the storms. Sea, air, cloud, giants, aurora and stars are shaders in `Resources`.

Detail tiers are Unity quality levels (Mobile, VR, PC), with per-platform defaults in Project Settings > Quality and budgets in `LodSettings.cs`. Every level-of-detail system decides what to draw by how big a thing looks on screen, and lets it linger past that before letting go, which is how the rule against flicker in `AGENTS.md` is kept.

`Climate` works out how hot it is anywhere, from the star, a giant's depths or a furnace world's air, and `Combat.Scorch` turns heat into damage. The scanner (`Scanner.cs`, with `Core/Scan.cs` choosing what is being looked at and describing it) asks the oracle for a journal entry once per kind of thing per world.

## Expedition progression

`Progression` owns XP thresholds, salvage costs and the five upgrade ranks. `Game` awards combat, objective and discovery XP, pauses for a compulsory three-choice level screen and checkpoints progression at mission completion. `Campaign` separates the three Earth burrows, surface flight and orbital recovery. Version-three saves migrate earlier mission numbers without changing equipment bit positions.

Earth fronts and colonies have regional identifiers. `Frontier` assigns surface positions to the nearest of three brood sites, simulates each population and requires all regions to be controlled before upgrading a factory to a shipyard. New slots choose a fresh seed; the same stored seed still generates the same system. Changing a mission does not rebuild unchanged factory models. Newly assembled units clone a prepared model and become the patrol itself.

## A frame

`Game.Update` decides who the controls belong to and then drives everything in a fixed order:

1. `Player.controls()` returns a `Controls` struct (`PlayerInput.cs`). Keyboard, mouse and gamepad fill it; tests fill it directly. Menus, conversation and death take the controls away by replacing the struct with an empty one.
2. `Player.Tick` steps the simulation in slices of at most 1/60 s: `Walker.Step` on foot, `Ship.Step` or `Autopilot.Step` in flight. It applies damage from falls, crashes, heat and radiation, then places the camera and the ship model.
3. `Outpost.Tick` and `Battle.Tick` run the mission's guards and the fighting: `Combat.Hunt` moves foes, `Combat.Step` moves bolts and reports strikes, and the runtime turns strikes into craters, effects and sound.
4. `SolarSystem.Update` (which runs first, by execution order) advances the clock, picks the reference body and places every body, the sun's light, ambient light and fog.
5. `Hud.LateUpdate` lays out the interface from the state of everything above.

## The campaign

`MISSIONS.md` is the mission table, loaded by `Core/Narrative.cs` into `Campaign`. `STORY.md`, `FACTIONS.md` and `COLONIES.md` supply the plot, faction context and building costs. `Editor/ContentImport.cs` packages copies as Resources text assets. Mission locations are fixed before terrain is changed. Ownership is the stable `Player.kit` bit set; `Player.fitted` holds the powered modules, all enabled when recovered. `Suit` validates owned modules; the three Earth broods progressively restore beam power, surface thrusters and orbital flight. `Game` owns progression and saves it.

`Outpost` cuts descending terrain pads and draws enclosing rock shells. Ground movement and terrain meshing share the same pad heights; the shells supply side and ceiling collision. Dry burrow volumes exclude sea rendering and underwater movement without adding another terrain collision source. It spawns the region’s linked minds and their guards, then exposes the power core when every mind dies. The citadel is a ship-sized tunnel with a timed escape after its core is destroyed. The factory phase moves a moon onto the final mind, starts a singularity cinematic and moves the traveller into a pre-generated harder system.

The recovery robot (`Hermit.cs`) briefs each mission from the table, so the story needs no language model. After the first meeting it speaks over the radio; once the ship is recovered it joins the cockpit.

## Language models

`Oracle.cs` turns a persona and a prompt into a reply, for the scanner's journal and the robot's free talk. It never holds a key of its own. For each of its two voices, Claude and ChatGPT, it tries in order:

1. a key the player has given it (environment or clipboard), sent straight to the maker's API with `UnityWebRequest`;
2. for ChatGPT, OpenAI's Sign in with ChatGPT, done by the game in the player's browser;
3. on a desktop, the makers' own command-line tools with the sign-ins they keep: Claude Code for Claude; `codex` for ChatGPT.

The game can start each tool's own sign-in from the settings page. It never shows a sign-in form itself. Every caller goes through `Oracle.Ask`, a delegate that tests replace with a script, and has a fallback for when no model answers.

## Effects

`Effects.cs` draws everything that burns, bursts, smokes or steams: explosions, hits on a hull, steam off water, mushroom clouds and factory lights. It is built from three kinds of thing, each drawn in one instanced call: puffs (`Resources/Puff.shader`), sparks (`Spark.shader`) and tumbling wreckage. `Battle` decides which effect a strike calls for. Harrowers and Skimmers are drawn with `Hull.shader`, which takes damage through `Instances.Add`. Organic wounds reveal violet tissue without the white-hot emission used for metal. `IchorPool` spreads and settles a small surface mesh against the terrain height function.

`Corpses.cs` caches mesh sections and retains a bounded number of articulated remains. `Core/Ragdoll.cs` steps their joints against the same ground function as living bodies; runtime interior collision keeps them inside tunnels. Further hits separate sections, including spacecraft wreckage.

## Interface, sound and speech

- `Hud.cs` builds one uGUI canvas in front of the camera, from code. It reads state and never changes it. Markers for worlds, foes and mission goals are placed each frame by `Mark`, which hides what a world is in the way of, keeps names from overlapping and holds a guide mark at the edge of the screen when its place is out of view.
- `Core/Synth.cs` computes every sound effect as samples, and `Core/Riffs.cs` writes a song for each system. `Sounds.cs` and `Music.cs` play them. Audio files dropped into `Resources/Music` replace the synthesised song. `Playlist` schedules complete tracks with equal-power crossfades; `Music` uses two streaming decks per mood.
- `Speech.cs` renders every spoken line with the bundled Festival SLT model and native Flite+HTS engine, off the main thread. Unity plays the returned 32 kHz PCM and applies the same robot effect on every platform. `script/build-speech` compiles the library, and `VoiceBuild` bundles it and its model. `VoiceInput` handles held microphone input and partial transcripts. Tests substitute both speech and dictation.
- `Models*.cs`, `MeshKit.cs` and `Shapes.cs` build every mesh and material: the ship and its cockpit, the robot and its factories, Harrowers, Skimmers and the citadel. `Arms.cs` builds the powered gauntlets.
- `DeviceControls.cs` merges touch, gamepad, keyboard and tracked controllers. Cockpit hit areas dispatch the same `Controls` intents as buttons. `ForearmConsole.cs` attaches a touchable construction and suit menu to the left tracked hand; interface contact suppresses motion punches.
- `Cockpit.cs` drives the ship's instruments. `Models.Ship` names every part that moves or lights, `Player.Tick` fills a `Readings` struct with what the ship is doing, and `Cockpit.Show` eases each needle, lamp, switch and the stick to match. Nothing on the dash is decoration.

## Saving

A save slot is one small JSON file (`Game.Save`): the system's seed, the depth, the mission stage, the gear bit set, fitted modules, colony resources, presence clock, moonfall, gate state, journal, talk breached outpost walls and armed/destroyed burrow demolition state. Worlds are never saved, since the seed remakes them. Save version 3 migrates earlier campaigns into the regional Earth progression without moving equipment bit positions. Saves also retain XP, salvage, upgrade ranks and the current mission checkpoint. War fronts also retain weapon exposure and whether their controlling mind is dead. Settings that belong to the device (detail, music, speech) are `PlayerPrefs`. Oracle credentials are separate from saves: Keychain on iOS and a local account file on desktop. [PRIVACY.md](PRIVACY.md) describes local storage, optional provider requests, on-device iOS speech recognition and TestFlight diagnostics; [TESTFLIGHT.md](TESTFLIGHT.md) holds public beta submission copy.

## Tests and tooling

- `script/test` first checks TestFlight distribution, release gating and signing cleanup with `script/test-testflight`, then compiles every assembly with the editor's bundled compiler and runs the EditMode tests on its Mono, with no Unity process and no licence.
- `script/test --unity` runs EditMode and PlayMode tests inside Unity. The PlayMode tests load the real scene and drive it through `Controls`, with every road out of the game (models, browser, speech) swapped for a stand-in in set-up. One of them, `CapturesScreenshots`, tours the game and writes pictures to `Logs/Screenshots`, which is how visual work is checked on a machine with no display.
- `FrameLog.cs` records five-second median windows and gradually adjusts terrain LOD and supported dynamic resolution under sustained load. It writes frame timing, activity, memory, build and HDR state to `Logs/frames.log` when the game is started by `script/play`. `script/performance` summarises the latest real play session, excluding menus.

## TestFlight distribution

Pushes to `main` build the project version for iPhone and iPad and for native Apple silicon Mac. Each platform uploads one build eligible for internal and external testing. The workflow serialises pushes and saves each platform's release version, build number, commit and run before signing. Build numbers start at the commit count and increase beyond Apple's upload history and earlier reservations. A rerun recovers the saved identity and checks whether Apple received it before building again.

The distribution job waits up to 20 minutes for processing, assigns the internal group if needed and submits ready builds for external review using **What to Test** from `TESTFLIGHT.md`. An hourly job revisits this repository's unexpired uploads when processing or review limits delayed distribution. Apple controls when public testers receive a build. [CI.md](CI.md)

## Where to put things

| To add | Start in |
|---|---|
| A mission, or something it wins | `MISSIONS.md`, then whatever asks `Player.Has` |
| A weapon | `Combat.Guns` or `Combat.Arms`, a voice in `Synth.cs`, a model in `Arms.cs` |
| A foe | `FoeKind`, `Combat.Hunt`, a model in `Models.cs`, drawing in `Battle.Update` |
| An explosion, smoke or fire | `Effects.cs`, called from where `Battle.Tick` handles strikes |
| Something about the ground | `TerrainNoise`, `BodyDescriptor.GroundRadius`, `Terrain.shader` |
| A key | `Controls` and `DesktopControls.Read`, then the list in `Hud.Help` |
| Something on screen | `Hud.Awake` to build it, `Hud.LateUpdate` to drive it |
| A setting | `Game.Items`, `Game.Choose`, `Game.Hint` |

## The suit and colony expansion

`Suit` owns the recovered modules and shared energy rules. `Player.kit` still records ownership; `Player.fitted` records the recovered powered modules. Stable gear bit positions preserve equipment from older saves. `Combat.Ray` handles continuous beams and piercing rail shots, while missiles remain bolts. `Shield` rejects incoming fire at the boundary before damage or splash is applied.

`Interior` describes built destructible tunnel shells as boxes in a local frame. `Outpost` draws those same boxes and resolves movement against them. Terrain collision still comes only from `GroundRadius`. A stationary `FoeKind.Mind` controls the outpost; ground minds stay rooted above the current excavated floor. Shield guards are stored with their outpost when leaving its reference frame, rather than also following the player. `Combat.Surface` excludes sea level from weapon collision inside dry burrows; its death opens the prize and scatters the bodies. Sentries observe noise, light and visibility. There is no terminal hacking. The citadel has a ship-sized tunnel layout and an escape timer.

`War` and saved `Front` populations simulate alien and robot replication and attrition. Saved death counts reduce enemy damage, reinforcement frequency and hostile growth without a difficulty message. `EscortOrders` retains the requested wing size and Follow, Attack or Patrol order. `Frontier` rebases up to three escorts with the player and uses `War.Follow` to keep formation at autopilot speeds; the wider population still spreads only to neighbouring planets. `Colony` retains factory sites and legacy save fields. `Moonfall` controls the impact. `Frontier` steps the colonies only during play, draws the factories, nearby laser dogs and ships and stages the moon impact and singularity. Game saves hold those states, the clock and equipment. Old resource and gate fields remain readable for save compatibility. `SolarSystemDescriptor.Beyond` uses a separate random stream; `Generate` retains the original system's draws. The old gate is no longer appended; original world indices remain stable.

`Radar` draws a transparent sphere and 3D contacts separately from the canvas. `DeviceControls` supplies touch and XR input through `Controls`. The iOS native bridge stores account data in Keychain. Speech uses the same bundled Festival SLT voice on every target, with text fallback if synthesis fails. `Platforms` configures device exports and the Windows OpenXR loader and Valve Index profile. The platform settings are generated by code when building.

Procedural machine models share cached meshes, with separate articulated joints driven by `MachineMotion`. Presence bodies use curved carapaces, sensory organs and rooted neural folds. `MotileCaste` selects stalker, breacher or spitter combat rules within the stable sentry kind; `Outpost` assigns the mix and `Battle` instances each anatomy. `Textures` generates shared metal, ceramic, seal and chitin maps alongside the terrain library. `Models.Surfaces` builds the panelled interior shells. Terrain blast pools use a level surface bounded by the crater, with slow shader flow.

`SuitHud` adds the helmet instruments and twelve-second cold start. `VoiceInput` requests native dictation only on a player action, with an injectable test route. `VoiceBuild` compiles the macOS speech bridge and adds permission strings; iOS compiles the same Objective-C++ source. `Hermit` writes a bounded JSON state snapshot before a question. `Frontier` pre-generates the next descriptor and mission sites on a worker task during moonfall.

`DeviceControls` merges direct Input System touch regions with keyboard, mouse, gamepad and Index controls. Its canvas only draws the controls; button state does not depend on IMGUI events. `SuitTutorial` gates the initial control recovery. First-person renderer masking and head-relative VR body placement share `Player`; independent tracked gauntlets remain in `Arms`. The main HUD follows the head and exposes a plane for Index ray selection.

`FactoryDefence` aims each factory's visible mount and fires through the shared combat path. `Scatter` retains tree damage and draws shortened trunk/crown meshes. `Hull.shader` adds violet wounds to organic models, while bounded ichor pools and suit stains keep combat readable without red blood.
