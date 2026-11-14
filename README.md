# Infinigame

A tiny procedural solar system to explore on foot and by ship: a sun, an earth-like planet and its moon, all true spheres with their own gravity. Find every relic in a system to charge the warp drive, then warp to the next seed. There is always a next seed.

Built with Unity 6.6 (6000.6.9f1) and URP. Everything is generated from code: terrain, meshes, materials, textures and sound.

## Scripts

These follow [scripts to rule them all](https://github.com/github/scripts-to-rule-them-all).

| Script | What it does |
|---|---|
| `script/bootstrap` | Checks Unity Hub and the pinned editor version are installed. |
| `script/setup` | Bootstraps, then makes sure the current user has a Unity licence. See Setup below. |
| `script/server` | Opens the game in the Unity editor, on the game scene. Press Play. |
| `script/test` | Compiles every assembly and runs the EditMode tests. Needs no Unity licence. |
| `script/test --unity` | Also runs the EditMode and PlayMode tests inside Unity and saves pictures to `Logs/Screenshots`. |
| `script/build` | Builds `Build/Infinigame.app` for macOS. |
| `script/unity` | Runs the pinned editor against this project with any arguments. |

`script/test --unity` and `script/build` need a licence for whoever runs them and the project closed in the editor.

## Setup

Unity licences belong to one operating-system user, so each user who runs Unity needs their own.

1. As your normal user, sign in to Unity Hub, then run `script/setup`. It installs the Unity CLI with Homebrew if it is missing and confirms your licence.
2. As any other user that should run Unity from the command line, such as a sandbox user, run `script/setup` in a terminal. It prints a sign-in URL to open in any browser, including one belonging to your normal user, then activates Unity Personal for that user. Two-factor accounts work because the sign-in happens in the browser.

`script/test` on its own needs none of this.

## Play

| Input | On foot | In the ship |
|---|---|---|
| W A S D | walk | thrust and strafe |
| Mouse | look | steer |
| Space | jump, hold for jetpack | lift |
| Ctrl or C | | descend |
| Shift | sprint | boost |
| Q / E | | roll |
| F | board the ship | land, then leave |
| Z | | flight assist on or off |
| V | | cockpit or chase view |
| Tab (hold) | view the whole system | |
| G | | warp, once every relic is found and you are in open space |
| R | jump to the next seed | |
| [ and ] | lower or raise detail | |

With flight assist on, the ship hovers, follows the horizon and climbs over terrain. With it off the ship is purely Newtonian and can orbit.

The planet turns once in about ten minutes on a tilted axis and takes over an hour to circle the sun. The moon is tidally locked, so the planet hangs still in its sky.

## Layout

- `Assets/Infinigame/Core`: the simulation as plain C# with no scene dependencies. Cube-sphere maths, the terrain height function, orbits and spin, gravity, LOD selection, walking, flight and relic placement.
- `Assets/Infinigame/Runtime`: Unity components that draw the bodies, read input and drive the simulation.
- `Assets/Infinigame/Resources`: materials and the two sky shaders.
- `Assets/Infinigame/Tests`: EditMode tests for the core, including the lap times the design promises, and PlayMode tests that drive the real scene.

Detail tiers are Unity quality levels (Mobile, VR, PC) with per-platform defaults in Project Settings > Quality. Their terrain budgets live in `LodSettings.cs`.
