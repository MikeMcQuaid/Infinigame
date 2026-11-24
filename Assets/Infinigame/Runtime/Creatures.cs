using System.Collections.Generic;
using System.Threading.Tasks;
using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;

namespace Infinigame
{
    // Herds, flocks and schools on a living world, drifters in a giant's sky and striders on a cold
    // desert, drawn with GPU instancing. Wildlife in the core
    // decides which troop lives where and where each member is at any moment; this finds the troops
    // in sight and draws them. Like the plants they never flicker: a troop is drawn from the moment
    // it is big enough to see until it is out of sight, shrinking away with distance.
    public class Creatures : MonoBehaviour
    {
        class Kind
        {
            public Creature creature;
            public string name;
            // how big it looks, how far its members stray from the troop's spot and the furthest it is ever drawn
            public float size, stray, furthest, height, girth;
            public int depth;
            public Vector3 proportions = Vector3.one;
            public Mesh body, lights;
            public Material material, glow;
            // A cell maps to the search for its troop, which ends with the troop or with nothing.
            // Searching reads the terrain many times over, so it is done off the main thread.
            public readonly Dictionary<NodeId, Task<Troop?>> cells = new Dictionary<NodeId, Task<Troop?>>();
            public readonly Instances bodies = new Instances();
        }

        // Every animal drawn this frame, for the scanner.
        public readonly List<Scannable> Seen = new List<Scannable>();

        readonly HashSet<(Creature, NodeId, int)> killed = new HashSet<(Creature, NodeId, int)>();
        readonly List<(Creature, NodeId, int)> members = new List<(Creature, NodeId, int)>();

        public bool Strike(float3 from, float3 travel, float radius = 0)
        {
            bool hit = false;
            for (int i = Seen.Count - 1; i >= 0; i--)
            {
                var animal = Seen[i];
                if (members[i].Item1 == Creature.Kelp || !Combat.Crosses(from, travel, animal.foot + math.normalizesafe(animal.foot) * animal.height * 0.4f, math.max(0.25f, animal.girth) + radius)) continue;
                hit |= killed.Add(members[i]);
            }
            return hit;
        }

        BodyDescriptor body;
        CelestialBody owner;
        SolarSystem system;
        Kind[] kinds;
        Mesh wing, butterflyWing;
        float reach;
        readonly Instances wings = new Instances();
        readonly HashSet<NodeId> near = new HashSet<NodeId>();
        readonly List<NodeId> stale = new List<NodeId>();
        readonly Plane[] frustum = new Plane[6];

        public void Init(BodyDescriptor descriptor)
        {
            body = descriptor;
            owner = GetComponent<CelestialBody>();
            system = GetComponentInParent<SolarSystem>();
            var (bird, birdWing) = Shapes.Bird();
            wing = birdWing;
            var butterfly = Models.Parts("Butterfly");
            butterflyWing = butterfly[1];
            Kind Make(Creature creature, string name, float size, float stray, float furthest, float height, float girth, Mesh mesh, Color colour) => new Kind
            {
                creature = creature, name = name, size = size, stray = stray, furthest = furthest, height = height, girth = girth, body = mesh,
                depth = QuadtreeLod.DepthFor(descriptor.radius, Wildlife.CellSize(creature)), material = Shapes.Solid(Surface.Chitin, colour, true),
            };
            var (strider, striderLights) = Models.Strider();
            var all = new[]
            {
                Make(Creature.Grazer, "grazing animal, hoofed and horned, about a metre at the shoulder, one of a small herd", 1.4f, 20f, 600f, 1.4f, 0.8f, Shapes.Grazer(), new Color(0.5f, 0.36f, 0.24f)),
                Make(Creature.Bird, "soaring bird with a two metre wingspan, one of a flock wheeling overhead", 2.4f, 70f, 900f, 0.3f, 1.4f, bird, new Color(0.2f, 0.19f, 0.2f)),
                // the sea is too murky to see a fish from far
                Make(Creature.Gull, "pale coastal glider with long narrow wings", 3, 80, 900, 0.4f, 1.6f, bird, new Color(0.85f, 0.88f, 0.8f)),
                Make(Creature.Swift, "blue dartwing, a small fast flocking bird", 1.2f, 70, 600, 0.2f, 0.7f, bird, new Color(0.12f, 0.36f, 0.65f)),
                Make(Creature.ReefFish, "striped amber reef fish", 0.6f, 8, 120, 0.3f, 0.5f, Shapes.Fish(), new Color(0.9f, 0.55f, 0.12f)),
                Make(Creature.Ray, "broad blue sea ray with winglike fins", 1.4f, 8, 150, 0.3f, 1.2f, Models.Parts("Ray")[0], new Color(0.2f, 0.5f, 0.7f)),
                Make(Creature.Butterfly, "violet butterfly feeding above the meadow", 0.5f, 8, 90, 0.2f, 0.3f, butterfly[0], new Color(0.65f, 0.3f, 0.9f)),
                Make(Creature.Kelp, "rooted seaweed forest sheltering small fish", 3, 5, 180, 3, 1, Shapes.Seaweed(), new Color(0.15f, 0.4f, 0.18f)),
                Make(Creature.Fish, "silver fish half a metre long, one of a school", 0.5f, 8f, 120f, 0.2f, 0.5f, Shapes.Fish(), new Color(0.75f, 0.68f, 0.55f)),
                Make(Creature.Drifter, "vast creature like a living airship, a pale dome trailing tendrils through the cloud tops, one of a slow herd", 40f, 520f, 9000f, 40f, 12f, Models.Drifter(), Color.white),
                Make(Creature.Strider, "tall three-legged walker with a sail on its back, crossing the dust on stilt legs, one of a small band", 6f, 60f, 1500f, 6f, 1.5f, strider, new Color(0.5f, 0.38f, 0.27f)),
            };
            // a drifter is half clear, seen from inside its bell as well as out, and a strider carries lights of its own
            var drifter = System.Array.Find(all, kind => kind.creature == Creature.Drifter);
            var walker = System.Array.Find(all, kind => kind.creature == Creature.Strider);
            drifter.material = Shapes.Material("Water", new Color(0.45f, 0.95f, 0.9f, 0.5f));
            drifter.material.SetFloat("_Cull", 0f);
            drifter.material.enableInstancing = true;
            walker.lights = striderLights;
            walker.glow = Shapes.Material("Glow", new Color(1f, 3.5f, 4.5f));
            foreach (var kind in all)
                kind.proportions = kind.creature == Creature.Gull ? new Vector3(1.4f, 0.8f, 1.1f)
                    : kind.creature == Creature.Swift ? new Vector3(0.65f, 0.65f, 0.9f)
                    : kind.creature == Creature.ReefFish ? new Vector3(0.7f, 1.4f, 0.75f)
                    : Vector3.one;
            kinds = System.Array.FindAll(all, kind => Wildlife.Lives(descriptor, kind.creature));
            reach = 0f;
            foreach (var kind in kinds) reach = math.max(reach, kind.furthest);
        }

        void Update()
        {
            Seen.Clear(); members.Clear();
            var view = Camera.main;
            float3 camera = view.transform.position, up = math.normalize(camera);
            float height = math.length(camera), floor = math.max(body.SeaRadius, body.radius + body.terrain.MinHeight);
            if (!owner.IsReference || height > body.radius + body.terrain.MaxHeight + reach) return;
            var lod = LodSettings.Current;
            GeometryUtility.CalculateFrustumPlanes(view, frustum);
            double time = system.Clock;
            float beat = (float)(time % 1000.0);
            wings.Clear();
            var bounds = new Bounds(camera, Vector3.one * (2f * reach + 400f));
            foreach (var kind in kinds)
            {
                float range = math.min(kind.furthest, lod.Range(kind.size));
                QuadtreeLod.Near(camera, body.radius, kind.depth, range + kind.stray, near);
                stale.Clear();
                foreach (var id in kind.cells.Keys)
                    if (!near.Contains(id))
                        stale.Add(id);
                foreach (var id in stale) kind.cells.Remove(id);

                kind.bodies.Clear(); wings.Clear();
                foreach (var id in near)
                {
                    if (!kind.cells.TryGetValue(id, out var search))
                    {
                        var creature = kind.creature;
                        kind.cells[id] = search = Task.Run(() => Wildlife.Find(body, creature, id, out var troop) ? troop : (Troop?)null);
                    }
                    if (!search.IsCompleted || search.Result is not Troop here) continue;
                    float3 spot = here.centre * here.level;
                    float shrink = Detail.Fade(math.distance(camera, spot), range);
                    if (shrink <= 0f || !Detail.OverHorizon(camera, here.centre, here.level + kind.stray, floor) || !InView(spot, kind.stray + 30f)) continue;
                    for (int i = 0; i < here.count; i++)
                    {
                        if (killed.Contains((kind.creature, id, i))) continue;
                        Wildlife.Member(body, here, i, time, out float3 position, out float3 forward);
                        if (body.Dry(position)) continue;
                        // each has its own size and its own beat
                        float quirk = math.frac((here.seed % 1000u) * 0.37f + i * 0.61f), size = 0.8f + 0.4f * quirk;
                        Quaternion facing = Quaternion.LookRotation(forward, math.normalize(position));
                        if (Wildlife.IsFish(kind.creature)) facing *= Quaternion.AngleAxis(14f * math.sin(beat * 7f + quirk * 40f), Vector3.up);
                        // Whole populations dwindle as the presence consumes a world, recovering under protection.
                        float survival = math.saturate((1 - body.blight - quirk * 0.85f) * 8);
                        if (survival <= 0) continue;
                        if (kind.creature == Creature.Kelp) facing *= Quaternion.Euler(math.sin(beat + quirk * 6) * 6, 0, math.cos(beat * 0.8f + quirk * 5) * 5);
                        var matrix = Matrix4x4.TRS(position, facing, kind.proportions * (size * shrink * survival));
                        kind.bodies.Add(matrix);
                        members.Add((kind.creature, id, i));
                        Seen.Add(new Scannable { name = kind.name, foot = position, height = kind.height * size, girth = kind.girth * size });
                        if (!Wildlife.IsBird(kind.creature) && kind.creature != Creature.Butterfly) continue;
                        // a few slow beats, then a long glide
                        float flap = kind.creature == Creature.Butterfly ? 65 * math.sin(beat * 35 + quirk * 40) : 32f * math.sin(beat * 6f + quirk * 40f) * math.saturate(1.5f * math.sin(beat * 0.5f + quirk * 9f)) + 6f;
                        wings.Add(matrix * Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, flap)));
                        wings.Add(matrix * Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, 180f - flap)));
                    }
                }
                kind.bodies.Draw(kind.body, kind.material, kind.creature != Creature.Drifter, bounds);
                if (kind.lights != null) kind.bodies.Draw(kind.lights, kind.glow, false, bounds);
                if (Wildlife.IsBird(kind.creature) || kind.creature == Creature.Butterfly) wings.Draw(kind.creature == Creature.Butterfly ? butterflyWing : wing, kind.material, true, bounds);
            }
        }

        bool InView(float3 centre, float radius)
        {
            foreach (var plane in frustum)
                if (plane.GetDistanceToPoint(centre) < -radius)
                    return false;
            return true;
        }
    }
}
