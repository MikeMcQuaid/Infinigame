# Visual direction

This is design guidance, not a runtime configuration file. `Models.cs`, `MeshKit.cs` and the faction model builders implement it. The game must look convincing at walking distance and from the cockpit, with smooth curved silhouettes and visible construction details.

Build models in passes: primary proportions and curved volumes, articulated structure, overlapping shell panels and recesses, then fasteners, cables, vents and optics. Use real geometry for silhouette and parallax, and generated normal and roughness detail for small surface marks. Additional triangles alone do not make a convincing model.

The traveller has a complete articulated body for third-person play and a matching pair of first-person gauntlets. Do not expose an unambiguous human face or robotic brain. The workshop's loose suit disappears when collected.

Friendly quadrupeds and ships share rounded alloy armour, dark moving joints, protected cables and cyan instruments. Enemy ground bodies, scouts, ships and buildings share carapaces, fluted tissue, recessed sensory organs and warmer internal light. Maintain different silhouettes for workers, scouts, armed ground bodies and rooted minds.

Reuse meshes and materials for repeated machines. Device tiers may reduce tessellation and small details, but must retain the same silhouette. Validate close views in the screenshot tour and use real frame logs before changing global terrain budgets.

`RevisionCapturesDetailedModels` takes close review views of the generated machines and enemy bodies with an extra fill light, plus the actual third-person and cockpit cameras. The wider screenshot tour checks their appearance in ordinary play. Keep the useful captures in `docs/`; intermediate PNG captures stay in `Logs/Screenshots`.

The [blast-pool capture](docs/lava.jpg) shows molten rock bounded by the floor of its crater. The liquid surface moves in the terrain shader without spreading beyond the rim.
