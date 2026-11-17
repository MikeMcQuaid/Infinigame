# Visual direction

This is design guidance, not a runtime configuration file. `Models.cs`, `MeshKit.cs` and the faction model builders implement it. The game must look convincing at walking distance and from the cockpit, with smooth curved silhouettes and visible construction details.

Build models in passes: primary proportions and curved volumes, articulated structure, overlapping shell panels and recesses, then fasteners, cables, vents and optics. Use real geometry for silhouette and parallax, and generated normal and roughness detail for small surface marks. Additional triangles alone do not make a convincing model.

## Ground encounter

The robodog's overlapping ceramic shoulders, three cyan optics and articulated bearings identify it from the side. The stalker's paired back plates, fluted cheek membranes and hooked jaws give it a broader, lower silhouette. Studio development started with an original concept sheet and a generated image exploring how the surfaces could catch light. The first Unity render showed the cheek folds disappearing into the stalker’s brown shell. That led to a forward-facing, ribbed membrane and separate colours for armour, tissue and claws. The final render uses the gameplay meshes and materials. [Concept sheet](docs/art/ground-encounter-concept.jpg) · [Studio design target](docs/art/ground-encounter-lookdev.jpg) · [Original Unity models](docs/art/ground-encounter-before.jpg) · [First mesh pass](docs/art/ground-encounter-first-pass.jpg) · [Revised Unity models](docs/art/ground-encounter.jpg)

The robodog's diagonal feet bear weight for three fifths of each stride. Distance travelled drives the gait and the body and head counterbalance each other. The stalker draws its shell back before attacking, lunges forwards and rocks away from a hit. All three motile castes keep their leg count and silhouette at close, medium and distant detail. Their apparent size on screen selects the mesh. Separate approach and retreat thresholds keep the mesh stable near each boundary.

Each sound in the preview plays twice. In order, the sounds are a patrol footfall, the dorsal emitter, a motile warning, a shell impact and a body collapsing. In play, four spatial voices share these events. Nearby warnings take priority over quiet footsteps. Walls muffle attacks, a tunnel adds a short echo and the music and combat effects dip during speech. [Encounter sound preview](docs/art/ground-encounter-sound.mp3)

Run `script/test --unity -testFilter RenderGroundEncounter` for a matching 1280 × 640 studio image and measurements in `Logs/Screenshots/ground-encounter.txt`. On an Apple M5 Max at the VR detail tier, the median of 20 warmed renders fell from 3.44 ms to 2.96 ms. Each sample includes a synchronous pixel readback. The close pair fell from 424,760 to 197,784 triangles. Use `script/performance` after a normal Mac play session to measure the whole game.

A separate 1920 × 1080 HDR macOS development player ran the factory patrol against one stalker. Across 18 half-second windows on foot, median FPS was 58.5 and median GPU time was 6.5 ms. The slowest entry frame took 1.50 seconds. Burst was disabled for this build. Larger swarms, iPad and VR need their own measurements.

## Models and materials

The traveller has a complete articulated body for third-person play and a matching pair of first-person gauntlets. Do not expose an unambiguous human face or robotic brain. The suit is worn from the opening. Critical power leaves only emergency fists, with visible arcing at the gauntlets.

Friendly quadrupeds and ships share rounded alloy armour, dark moving joints, protected cables and cyan instruments. Enemy ground bodies, scouts, ships and buildings share carapaces, fluted tissue, recessed sensory organs and warmer internal light. Maintain different silhouettes for workers, scouts, armed ground bodies and rooted minds.

Reuse meshes and materials for repeated machines. Device tiers may reduce tessellation and small details, but must retain the same silhouette. Validate close views in the screenshot tour and use real frame logs before changing global terrain budgets.

`RevisionCapturesDetailedModels` takes close review views of the generated machines and enemy bodies with an extra fill light, plus the actual third-person and cockpit cameras. The wider screenshot tour checks their appearance in ordinary play. Keep the useful captures in `docs/`; intermediate PNG captures stay in `Logs/Screenshots`.

The [blast-pool capture](docs/lava.jpg) shows molten rock bounded by the floor of its crater. The liquid surface moves in the terrain shader without spreading beyond the rim.

The current surface library generates shared 2048 × 2048 colour/height and normal maps on PC and VR, or 1024 × 1024 on Mobile. Machined metal has brushed grain and fine scratches; ceramic has granular wear; flexible seals have a woven surface; organic shells have cellular seams, striations and pores. Terrain and bark use the same resolutions. Foliage cards use 1024 px textures on desktop and 512 px on Mobile. Maps have mipmaps and anisotropic filtering; CPU copies are released after upload. Twelve pairs of fully populated uncompressed RGBA maps cost about 512 MiB on desktop or 128 MiB on Mobile, shared across the world. Maps are created only when requested.

Near characters use 48-sided main curves on desktop and 28 on Mobile. Stalkers, breachers and spitters have different anatomies and leg counts, with separate shell and tissue colours. Flying scouts carry a sensory fan; vessels carry ribbed propulsion chambers. Wildlife uses curved bodies, feather geometry and fin rays. Replication factories expose assembly cradles and laser dogs; ship factories add launch rails. Flat panels, crystal faces, foliage cards and small effects remain deliberately economical; adding subdivisions to a flat surface adds no visible detail.

Sound follows the same distinction: friendly equipment uses induction tones, metallic impacts and controlled resonance; enemy fire uses rough, modulated organic pulses. Powered fists no longer play a shotgun reload, and the rail gauntlet no longer plays a bolt-action rifle. The beam has a sustained loop. All effects remain deterministic synthesis at 48 kHz, with no paid service or downloaded samples.

`Logs/Screenshots/model-detail.txt` records the triangle counts and texture sizes from the latest close-view test. These are procedural game assets, not a claim of AAA art quality. Check the actual game captures and profile a normal Burst-enabled player build before raising world-wide visibility or density. iPad and headset frame budgets still need hardware validation.

[Listen to the effect preview](docs/sound-preview.mp3): pulse cannon at 0.5 s, powered fists at 3 s, rail discharge at 6 s, enemy projectile at 9 s, sustained beam at 12 s, ship drive at 17 s and jetpack at 22 s. The game mixes these more quietly under dialogue and music.

Desktop close-view triangle counts from the checked render:

| Model | Triangles |
|---|---:|
| Foundry companion with dorsal laser | 125,008 |
| Powered suit, including gauntlets and pack | 133,672 |
| Stalker, including six legs | 72,776 |
| Breacher, including eight legs | 121,580 |
| Spitter, including four legs | 129,824 |
| Flying scout | 146,816 |
| Presence vessel | 382,752 |

These are the complete model counts, not triangles on screen every frame. Instancing shares each enemy mesh across its caste. The Mobile tier reduces the curved anatomy while preserving the caste silhouette.

Recent sound additions include a rising rail-charge loop and a low, modulated mind presence that is muffled by tunnel walls and stops on death. Organic damage no longer uses white-hot metal emission. Minds collapse into separated violet sections; their blood spreads along the sampled terrain, follows slopes and settles with fading ripples. This is bounded surface flow, not a general fluid simulation.

Future audio work can add distinct calls for each caste, a quiet repair hum and separate music, speech and effects volume controls. Compare the existing spatial mix on headphones and handheld speakers before introducing another audio system.

For later passes, measure first-use stalls before choosing which generated meshes or textures to bake into the build. Extend the screen-size detail treatment to the factory and ships using captures from those encounters.
