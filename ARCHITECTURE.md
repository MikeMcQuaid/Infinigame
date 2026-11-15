# Architecture

Infinigame is a Unity 6.6 (URP) project in which almost nothing is authored in the editor. There is one scene holding one object, `Game`. Everything else is built from code when the game starts: worlds, models, textures, the interface, sound and music. This file says how the pieces fit. `README.md` says what the game is and how to play it, `AGENTS.md` says how to work on it, and `IDEAS.md` holds what may be built later, with notes on where each idea would fit.

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

- `SolarSystemDescriptor.Generate(seed)` makes the whole system from one seed: a star, eight planets, their moons, the asteroid belt and the raiders' station, as an array of `BodyDescriptor`. Sizes are to scale with each other, distances and periods are not. Names are the real ones.
- A body's ground is a height function, `BodyDescriptor.GroundRadius(direction)`, built on `TerrainNoise`. There are no terrain colliders. Walking, flight, landing, bolts, meshes and tests all read the same function, so there is nothing to fall through or stream in. Volcanoes (`BodyDescriptor.volcanoes`) are cones the function raises. Everything that changes the ground afterwards is a `Blast` the function reads: craters from `Dig`, which char, melt or turn to lava by what the ground was made of (`Blasted`), and the level pads that `Level` leaves for a ship to land on. `Terrain.shader` colours each kind and lets molten rock cool by the clock.
- Orbits and spin are on rails: `Orbit.Position(time)` and `BodyDescriptor.Rotation(time)` are pure functions of the system clock, in double precision.

### Frames of reference

The scene is always the frame of the body the player is nearest (`SolarSystem.Reference`). That body sits still at the origin while the rest of the system wheels round it, so the ground underfoot never moves and floats stay precise. Positions of the player, foes and bolts are stored relative to the reference body's centre, in its turning frame. When the reference changes, `SolarSystem.ReferenceChanged` fires and each mover rebases itself with `SolarSystemDescriptor.Rebase`. Out between worlds the reference is the star and the origin rides with the player (`SolarSystem.Anchor`): scene position is always `position - Anchor`.

### Drawing a world

`CelestialBody` draws one body as a cube-sphere quadtree (`CubeSphere`, `QuadtreeLod`). Patches are meshed off the main thread by `PatchMeshJob` (Burst) from the height function and shaded by `Resources/Terrain.shader` with textures painted in code by `Textures.cs`. `Scatter` places plants and rocks, `Creatures` and `Wildlife` the animals, `RingField` and `Rings` the rocks of rings and the belt, `Rainfall` and `Weather` the storms. Sea, air, cloud, giants, aurora and stars are shaders in `Resources`.

Detail tiers are Unity quality levels (Mobile, VR, PC), with per-platform defaults in Project Settings > Quality and budgets in `LodSettings.cs`. Every level-of-detail system decides what to draw by how big a thing looks on screen, and lets it linger past that before letting go, which is how the rule against flicker in `AGENTS.md` is kept.

`Climate` works out how hot it is anywhere, from the star, a giant's depths or a furnace world's air, and `Combat.Scorch` turns heat into damage. The scanner (`Scanner.cs`, with `Core/Scan.cs` choosing what is being looked at and describing it) asks the oracle for a journal entry once per kind of thing per world.

## A frame

`Game.Update` decides who the controls belong to and then drives everything in a fixed order:

1. `Player.controls()` returns a `Controls` struct (`PlayerInput.cs`). Keyboard, mouse and gamepad fill it; tests fill it directly. Menus, conversation and death take the controls away by replacing the struct with an empty one.
2. `Player.Tick` steps the simulation in slices of at most 1/60 s: `Walker.Step` on foot, `Ship.Step` or `Autopilot.Step` in flight. It applies damage from falls, crashes, heat and radiation, then places the camera and the ship model.
3. `Outpost.Tick` and `Battle.Tick` run the mission's guards and the fighting: `Combat.Hunt` moves foes, `Combat.Step` moves bolts and reports strikes, and the runtime turns strikes into craters, effects and sound.
4. `SolarSystem.Update` (which runs first, by execution order) advances the clock, picks the reference body and places every body, the sun's light, ambient light and fog.
5. `Hud.LateUpdate` lays out the interface from the state of everything above.

## The campaign

`Core/Campaign.cs` is a table of missions, each with a world, a guard and the `Gear` it wins. What the player owns is a bit set of `Gear` (`Player.kit`), and everything that gates play asks it: weapons (`Weapon.needs`), the jetpack, the bike, the ship, the orbital drive, the radiation shield and so on. `Game` holds the stage, saves it, and moves it on in `Advance`.

`Outpost` is the place a mission is about: a fort, the guards that turn out of it and the prize they keep. Killing the guard does not win the mission. The prize has to be taken: by hand from its crate, or, for what is too heavy to carry, by landing the ship beside it. The citadel is the last such place.

The hermit (`Hermit.cs`) briefs each mission from the table, so the story needs no language model. Once the ship is won he rides in it as copilot.

## Language models

`Oracle.cs` turns a persona and a prompt into a reply, for the scanner's journal and the hermit's free talk. It never holds a key of its own. For each of its two voices, Claude and ChatGPT, it tries in order:

1. a key the player has given it (environment or clipboard), sent straight to the maker's API with `UnityWebRequest`;
2. for ChatGPT, OpenAI's Sign in with ChatGPT, done by the game in the player's browser;
3. on a desktop, the makers' own command-line tools with the sign-ins they keep: `ant`, then Claude Code, for Claude; `codex` for ChatGPT.

The game can start each tool's own sign-in from the settings page. It never shows a sign-in form itself. Every caller goes through `Oracle.Ask`, a delegate that tests replace with a script, and has a fallback for when no model answers.

## Effects

`Effects.cs` draws everything that burns, bursts, smokes or steams: explosions, hits on a hull, steam off water, mushroom clouds and the hermit's fire. It is built from three kinds of thing, each drawn in one instanced call: puffs (`Resources/Puff.shader`), sparks (`Spark.shader`) and tumbling wreckage. `Battle` decides which effect a strike calls for. Raiders and drones are drawn with `Hull.shader`, which takes how battered each one is through `Instances.Add` and eats glowing holes in it to match.

## Interface, sound and speech

- `Hud.cs` builds one uGUI canvas in front of the camera, from code. It reads state and never changes it. Markers for worlds, foes and mission goals are placed each frame by `Mark`, which hides what a world is in the way of, keeps names from overlapping and holds a guide mark at the edge of the screen when its place is out of view.
- `Core/Synth.cs` computes every sound effect as samples, and `Core/Riffs.cs` writes a song for each system. `Sounds.cs` and `Music.cs` play them. Audio files dropped into `Resources/Music` replace the synthesised song.
- `Speech.cs` speaks the hermit's lines with the operating system's own voice where there is one.
- `Models.cs`, `MeshKit.cs` and `Shapes.cs` build every mesh and material: the ship and its cockpit, the hermit and his camp, raiders, drones and the citadel. `Arms.cs` builds the weapons held in view.
- `Cockpit.cs` drives the ship's instruments. `Models.Ship` names every part that moves or lights, `Player.Tick` fills a `Readings` struct with what the ship is doing, and `Cockpit.Show` eases each needle, lamp, switch and the stick to match. Nothing on the dash is decoration.

## Saving

A save slot is one small JSON file (`Game.Save`): the system's seed, the depth, the mission stage, the gear bit set, the journal and the talk. Worlds are never saved, since the seed remakes them. Settings that belong to the device (detail, music, speech) are `PlayerPrefs`. What the player has given the oracle is a separate file that never travels with a save.

## Tests and tooling

- `script/test` compiles every assembly with the editor's bundled compiler and runs the EditMode tests on its Mono, with no Unity process and no licence.
- `script/test --unity` runs EditMode and PlayMode tests inside Unity. The PlayMode tests load the real scene and drive it through `Controls`, with every road out of the game (models, browser, speech) swapped for a stand-in in set-up. One of them, `CapturesScreenshots`, tours the game and writes pictures to `Logs/Screenshots`, which is how visual work is checked on a machine with no display.
- `FrameLog.cs` writes the frame rate and what the player was doing to `Logs/frames.log` when the game is started by `script/play`.

## Where to put things

| To add | Start in |
|---|---|
| A mission, or something it wins | `Core/Campaign.cs`, then whatever asks `Player.Has` |
| A weapon | `Combat.Guns` or `Combat.Arms`, a voice in `Synth.cs`, a model in `Arms.cs` |
| A foe | `FoeKind`, `Combat.Hunt`, a model in `Models.cs`, drawing in `Battle.Update` |
| An explosion, smoke or fire | `Effects.cs`, called from where `Battle.Tick` handles strikes |
| Something about the ground | `TerrainNoise`, `BodyDescriptor.GroundRadius`, `Terrain.shader` |
| A key | `Controls` and `DesktopControls.Read`, then the list in `Hud.Help` |
| Something on screen | `Hud.Awake` to build it, `Hud.LateUpdate` to drive it |
| A setting | `Game.Items`, `Game.Choose`, `Game.Hint` |
