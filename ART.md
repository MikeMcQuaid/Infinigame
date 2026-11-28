# Visual direction

The flight chassis, suit and robodog share folded titanium armour, exposed bearings and cyan optics. Engine rings show through open service bays between the ship’s swept wings. The gauntlet carries two dorsal rails, a segmented cuff and a recessed palm lens. [Equipment concept](docs/art/flight-and-gauntlet-concept.jpg) · [In-game meshes](docs/art/flight-and-gauntlet.jpg)

`RenderFlightEquipment` photographs both factions’ gameplay meshes under the same studio lights. The Presence carries hooded eye stalks from the ground castes to its living ships. The citadel has branching capillaries beneath folded vaults. [Faction model sheet](docs/art/faction-models.jpg)

The beam shader draws a moving filament inside a softer halo. Sparks brighten towards their head and explosion puffs carry a turbulent hot front. Violet ichor spreads across a terrain-fitted mesh beneath the resting body. Its feathered edge and shallow-angle reflection let the surface underneath show through.

The editor model workshop and the equipment and landscape artwork in `ArtSource/` are the editable sources for the shipped assets. The game must look convincing at walking distance and from the cockpit, with smooth curved silhouettes and visible construction details.

Build models in passes: primary proportions and curved volumes, articulated structure, overlapping shell panels and recesses, then fasteners, cables, vents and optics. Use real geometry for silhouette and parallax, and generated normal and roughness detail for small surface marks. Additional triangles alone do not make a convincing model.

## Ground encounter

The robodog’s shoulder plates part at the waist to expose its linked spine and tension cables. The stalker’s raised eyes, fluted cheek membranes and split claws give it a broader silhouette. Studio development started with an original concept sheet and a generated image exploring how the surfaces could catch light. The first Unity render showed the cheek folds disappearing into the stalker’s brown shell. That led to a forward-facing, ribbed membrane and separate colours for armour, tissue and claws. [Concept sheet](docs/art/ground-encounter-concept.jpg) · [Studio design target](docs/art/ground-encounter-lookdev.jpg) · [Original Unity models](docs/art/ground-encounter-before.jpg) · [First mesh pass](docs/art/ground-encounter-first-pass.jpg) · [Revised Unity models](docs/art/ground-encounter.jpg)

The robodog's diagonal feet bear weight for three fifths of each stride. Distance travelled drives the gait and the body and head counterbalance each other. The stalker draws its shell back before attacking, lunges forwards and rocks away from a hit. All three motile castes keep their leg count and silhouette at close, medium and distant detail. Their apparent size on screen selects the mesh. Separate approach and retreat thresholds keep the mesh stable near each boundary.

The preview plays two takes of each cue. It opens with a patrol footfall and dorsal emitter, followed by the stalker’s warning, the breacher’s double thud and the spitter’s valve chatter. A shell impact and collapse close the sequence. In play, four spatial voices share these events. Nearby warnings take priority over quiet footsteps. Walls muffle attacks, a tunnel adds a short echo and the music and combat effects dip during speech. [Encounter sound preview](docs/art/ground-encounter-sound.mp3)

Run `script/test --unity -testFilter RenderGroundEncounter` for a matching 1280 × 640 studio image and measurements in `Logs/Screenshots/ground-encounter.txt`. The close pair contains 172,148 triangles. A studio run on Apple M5 Max measured a median of 5.09 ms for 20 warmed renders with synchronous pixel readback. Use `script/performance` after a normal Mac play session to measure the whole game.

Before the asset bake, a 1920 × 1080 HDR macOS development player ran the factory patrol against one stalker. Across 18 half-second windows on foot, median FPS was 58.5 and median GPU time was 6.5 ms. The slowest entry frame took 1.50 seconds. Burst was disabled for this build. Larger swarms, iPad and VR need their own measurements.

## Models and materials

The traveller has a complete articulated body for third-person play and a matching pair of first-person gauntlets. Do not expose an unambiguous human face or robotic brain. The suit is worn from the opening. Critical power leaves only emergency fists, with visible arcing at the gauntlets.

Friendly machines have angular armour over dark moving joints. Presence shells are rounded and expose violet tissue between overlapping plates. Violet lenses cap the mobile castes’ sensory stalks. Friendly weapon sights, charge and exhaust run from ice blue to cyan. Neutral wildlife uses earth tones, greens and amber. Amber chambers sit under the ribs of the citadel.

Reuse meshes and materials for repeated machines. Device tiers may reduce tessellation and small details, but must retain the same silhouette. Validate close views in the screenshot tour and use real frame logs before changing global terrain budgets.

`RevisionCapturesDetailedModels` takes close review views of the shipped machines and enemy bodies with an extra fill light, plus the actual third-person and cockpit cameras. The wider screenshot tour checks their appearance in ordinary play. Keep the useful captures in `docs/`; intermediate PNG captures stay in `Logs/Screenshots`.

The [blast-pool capture](docs/lava.jpg) shows molten rock bounded by the floor of its crater. The liquid surface moves in the terrain shader without spreading beyond the rim.

The current surface library supplies shared 2048 × 2048 colour/height and normal maps on PC and VR, or 1024 × 1024 on Mobile. Machined metal has brushed grain and fine scratches; ceramic has granular wear; flexible seals have a woven surface; organic shells have cellular seams, striations and pores. Terrain and bark use the same resolutions. Foliage cards use 1024 px textures on desktop and 512 px on Mobile. The importer preserves relief and height, builds mipmaps and selects BC7 colour and BC5 normals on desktop or ASTC on Apple mobile devices. The four equipment surfaces use original brushwork, heat patina, ceramic chips, woven fibre and pale capillary striations from the faction material atlas. The landscape atlas adds clover, wind-rippled sand, fractured stone, snow crystal, pine bark, grit, capillary waves and basalt. Opposite edges blend into one another when the ground tiles repeat.

Near characters use 48-sided main curves on desktop and 28 on Mobile. Stalkers, breachers and spitters have different anatomies and leg counts, with separate shell and tissue colours. Flying scouts carry a sensory fan; vessels carry ribbed propulsion chambers. Wildlife uses curved bodies, layered flight feathers, swept ray fins and veined butterfly wings. Replication factories expose assembly cradles and laser dogs; ship factories add launch rails. Flat panels, crystal faces, foliage cards and small effects remain deliberately economical; adding subdivisions to a flat surface adds no visible detail.

Sound follows the same distinction: friendly equipment uses induction tones, metallic impacts and controlled resonance; enemy fire uses rough, modulated organic pulses. Powered fists no longer play a shotgun reload, and the rail gauntlet no longer plays a bolt-action rifle. The beam has a sustained loop. The delivered 48 kHz effects include separate takes for gait, impacts and each caste’s attack call. Their distance, direction and occlusion follow the scene.

`Logs/Screenshots/model-detail.txt` records the triangle counts and texture sizes from the latest close-view test. Check the actual game captures and profile a normal Burst-enabled player build before raising world-wide visibility or density. iPad and headset frame budgets still need hardware validation.

The 26-second preview opens with a three-shot pulse cannon burst. Two powered punches follow at 3 s, the rail discharge at 6 s and alien fire at 9 s. The sustained beam starts at 12 s, the ship drive at 17 s and the jetpack at 22 s. [Weapon and engine preview](docs/sound-preview.mp3)

Desktop close-view triangle counts from the checked render:

| Model | Triangles |
|---|---:|
| Foundry companion with dorsal laser | 84,476 |
| Powered suit, including gauntlets and pack | 135,980 |
| Stalker, including six legs | 87,672 |
| Breacher, including eight legs | 109,212 |
| Spitter, including four legs | 140,164 |
| Flying scout | 32,804 |
| Presence vessel | 91,640 |

These are the complete model counts, not triangles on screen every frame. Instancing shares each enemy mesh across its caste. The Mobile tier reduces the curved anatomy while preserving the caste silhouette.

Recent sound additions include a rising rail-charge loop and a low, modulated mind presence that is muffled by tunnel walls and stops on death. Organic damage no longer uses white-hot metal emission. Minds collapse into separated violet sections; their blood spreads along the sampled terrain, follows slopes and settles with fading ripples. This is bounded surface flow, not a general fluid simulation.

Future audio work can add a quiet repair hum and separate music, speech and effects volume controls. Compare the existing spatial mix on headphones and handheld speakers before introducing another audio system.
