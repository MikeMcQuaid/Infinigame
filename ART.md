# Visual direction

This is design guidance, not a runtime configuration file. `Models.cs`, `MeshKit.cs` and the faction model builders implement it. The game must look convincing at walking distance and from the cockpit, with smooth curved silhouettes and visible construction details.

Build models in passes: primary proportions and curved volumes, articulated structure, overlapping shell panels and recesses, then fasteners, cables, vents and optics. Use real geometry for silhouette and parallax, and generated normal and roughness detail for small surface marks. Additional triangles alone do not make a convincing model.

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
| Foundry companion with dorsal laser | 189,008 |
| Powered suit, including gauntlets and pack | 209,704 |
| Stalker, including six legs | 235,752 |
| Breacher, including eight legs | 317,188 |
| Spitter, including four legs | 312,344 |
| Flying scout | 146,816 |
| Presence vessel | 382,752 |

These are the complete model counts, not triangles on screen every frame. Instancing shares each enemy mesh across its caste. The Mobile tier reduces the curved anatomy while preserving the caste silhouette.

Recent sound additions include a rising rail-charge loop and a low, modulated mind presence that is muffled by tunnel walls and stops on death. Organic damage no longer uses white-hot metal emission. Minds collapse into separated violet sections; their blood spreads along the sampled terrain, follows slopes and settles with fading ripples. This is bounded surface flow, not a general fluid simulation.

The next useful sound improvements are separate music, speech and effects volume controls; automatic music ducking during instructions; different near/far weapon layers; wet tissue impacts distinct from alloy impacts; and recognisable calls for each hostile caste. Add a quiet repair hum to the multicoloured scanning beams and brief robodog acknowledgements for deployment, target acquisition and victory. A headphone/night-time dynamic-range setting would help quiet listening without losing mission speech. These are proposals, not implemented settings.

## Next fidelity pass

The current close meshes already use hundreds of thousands of triangles. More subdivisions are unlikely to fix their appearance and would make swarm combat harder to sustain. First improve silhouette, proportions, articulation, material separation and lighting. Build one representative enemy and one factory scene to a consistent standard before extending that treatment to the whole cast.

Use close, medium and distant mesh variants with stable screen-size thresholds. Retain silhouette and moving joints; move pores, fine panel relief and scratches into normal/roughness maps. Reuse a few texture families and atlas repeated parts. Keep 2K hero materials on desktop and selective 1K mobile maps until actual device captures justify more. Consider offline baking generated textures and meshes into the build to remove first-use generation stalls, while keeping code as their source.

For sound, the highest-value step is a layered event palette: attack anticipation, discharge, mechanical movement, impact, debris and a separate distant tail. Add several deterministic variations per event, material-specific impacts, cave occlusion/reverb and a strict simultaneous-voice budget. Mix enemy warnings above the swarm, duck music under the companion and compress loud peaks for handheld speakers. These are proposed production steps, not implemented audio features or a claim of AAA quality. Recorded or licensed source material would require changing the repository's current generated-audio policy.

Unity already provides [mesh LOD](https://docs.unity.com/en-us/engine/6000.6/manual/analysis/graphics-performance-profiling/lod/mesh-lod) and [mixer routing, effects and snapshots](https://docs.unity.com/en-us/engine/6000.6/manual/audio/mixer/overview). Use these as reference points for the generated rendering and sound paths before adding middleware.
