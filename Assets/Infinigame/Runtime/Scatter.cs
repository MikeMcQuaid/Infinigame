using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace Infinigame
{
    // Trees, boulders and grass, drawn with GPU instancing. Each cell of the cube-sphere grid seeds
    // its own random stream, so the same world always grows the same forest.
    //
    // Nothing here may flicker. A thing is drawn from the moment it is big enough to see and for
    // as long as it stays in sight: it goes only over the horizon, off the edge of the screen or
    // into the distance, shrinking away over the last stretch rather than vanishing.
    public class Scatter : MonoBehaviour
    {
        // A cell stays loaded this much further out than where it would first be grown.
        const float Keep = 1.2f;

        // One kind of thing: what to draw, how finely the ground is gridded for it and where it
        // will grow. Grow is given a direction, the height there and how level the cell is.
        class Layer
        {
            // what the scanner calls one of these, and how tall and wide it is at size one
            public string name;
            public float height, girth;
            public Mesh[] meshes;
            public Material[] materials;
            // a plain stand-in for the distance, where the full shape is a few pixels of wasted triangles
            public Mesh distant;
            public Material distantMaterial;
            public bool shadows;
            public float perCell;
            // Things too many to draw all the way to a pixel are given up this many times sooner.
            public float coarse = 1f;
            public int depth;
            // keeps two kinds of thing that share a grid from growing on the very same spots
            public uint salt;
            public Func<float3, float, float, bool> grows;
            public Vector2 size;
            public readonly Dictionary<NodeId, Cell> cells = new Dictionary<NodeId, Cell>();
            public readonly Instances whole = new Instances(), plain = new Instances();
        }

        class Cell
        {
            public float3 centre;
            // how far round the horizon its tallest thing can be seen from, as a cosine and sine
            public float cosine, sine;
            public Matrix4x4[] things;
            public Task<(Matrix4x4[] things, float ground)> growing;
            public bool whole;
            public int seen;
        }

        // How many things were drawn last frame, for the frame log.
        public int Drawn { get; private set; }

        BodyDescriptor body;
        CelestialBody owner;
        Layer[] layers;
        int tier = -1, growing;
        float floor;
        readonly Dictionary<int3, float> wounds = new Dictionary<int3, float>();
        readonly Instances[] broken = { new Instances(), new Instances(), new Instances() };
        Mesh[][] brokenTrees;
        static int3 TreeKey(Vector3 point) => (int3)math.round(math.normalizesafe((float3)point) * 1000000);
        public bool Strike(float3 from, float3 to, float damage, out float3 hit)
        {
            hit = to; float closest = math.distance(from, to); bool found = false;
            int3 wounded = default; float previous = 0;
            if (layers == null) return false;
            float3 ray = to - from; float length = math.lengthsq(ray);
            foreach (var layer in layers)
            {
                if (layer.name != "conifer tree") continue;
                float cellRadius = QuadtreeLod.NodeSize(body.radius, layer.depth) + 25;
                foreach (var cell in layer.cells.Values)
                {
                    float3 nearCell = from + ray * math.saturate(math.dot(cell.centre - from, ray) / math.max(length, 0.001f));
                    if (cell.things == null || math.distancesq(cell.centre, nearCell) > cellRadius * cellRadius) continue;
                    foreach (var thing in cell.things)
                    {
                        float3 foot = (Vector3)thing.GetColumn(3); var key = TreeKey(foot); wounds.TryGetValue(key, out float hurt);
                        if (hurt >= 80) continue;
                        float3 up = math.normalizesafe(foot);
                        float height = (hurt >= 60 ? 1.4f : hurt >= 40 ? 4 : hurt >= 20 ? 7 : 10) * thing.lossyScale.x;
                        float3 flat = ray - up * math.dot(ray, up), offset = from - foot;
                        float along = math.saturate(-math.dot(offset, flat) / math.max(math.lengthsq(flat), 0.001f));
                        float3 point = from + ray * along;
                        float3 centre = foot + up * math.clamp(math.dot(point - foot, up), 0, height);
                        if (math.distance(point, centre) > math.max(0.5f, thing.lossyScale.x)) continue;
                        float distance = math.distance(from, point);
                        if (distance >= closest) continue;
                        closest = distance; hit = point; found = true; wounded = key; previous = hurt;
                    }
                }
            }
            if (found) wounds[wounded] = previous + damage;
            return found;
        }

        readonly HashSet<NodeId> near = new HashSet<NodeId>();
        readonly List<NodeId> stale = new List<NodeId>();
        readonly Plane[] frustum = new Plane[6];

        public void Init(BodyDescriptor descriptor)
        {
            body = descriptor;
            owner = GetComponent<CelestialBody>();
            var terrain = descriptor.terrain;
            floor = math.max(descriptor.radius + terrain.MinHeight, descriptor.SeaRadius);
            // woods and meadows stop where the cliffs take over
            float treeLine = 0.4f * terrain.mountainAmp + 8f;
            int Depth(float cell) => QuadtreeLod.DepthFor(descriptor.radius, cell);
            var boulders = new Layer
            {
                name = "boulder", height = 0.4f, girth = 0.6f,
                meshes = new[] { Shapes.Rock() }, materials = new[] { Shapes.Solid(Surface.Rock, descriptor.SeaRadius > 0f ? Color.white : Shapes.Colour(descriptor.ground) * 1.5f, true) },
                shadows = true, depth = Depth(45f), perCell = 4f, size = new Vector2(0.5f, 2f),
                grows = (direction, height, level) => descriptor.SeaRadius == 0f || height > 1.5f,
            };
            if (descriptor.SeaRadius == 0f)
            {
                // Dead worlds have growths of their own: crystal spires where there is no air to
                // weather them, and clumps of tube-like vent life in the low ground of the furnace world.
                var growths = new List<Layer> { boulders };
                var crystals = Models.Crystals();
                for (int i = 0; descriptor.air == 0f && i < crystals.Length; i++)
                {
                    float kind = i;
                    growths.Add(new Layer
                    {
                        name = "cluster of crystal spires taller than a person, glassy and sharp-edged", height = 4f, girth = 1.4f, salt = 31u * (uint)(i + 1),
                        meshes = new[] { crystals[i] }, materials = new[] { Shapes.Material("Props", Color.Lerp(Shapes.Colour(descriptor.ground), new Color(0.5f, 0.9f, 1f), 0.75f)) },
                        shadows = true, depth = Depth(70f), perCell = 1.4f, size = new Vector2(0.6f, 1.8f),
                        grows = (direction, height, level) => level > 0.85f && noise.snoise(direction * 14f + terrain.offset + kind * 5f) > 0.35f,
                    });
                }
                if (descriptor.cloud >= 1f && descriptor.heat > 500f)
                    growths.Add(new Layer
                    {
                        name = "clump of tall segmented tubes with feathery mouths, swaying over a hot vent", height = 3f, girth = 1.2f, salt = 7u,
                        meshes = new[] { Models.TubeGrove() }, materials = new[] { Shapes.Material("Props", new Color(0.85f, 0.4f, 0.35f)) },
                        shadows = true, depth = Depth(45f), perCell = 5f, size = new Vector2(0.6f, 1.6f),
                        grows = (direction, height, level) => level > 0.8f && height < 0.15f * terrain.mountainAmp && noise.snoise(direction * 11f + terrain.offset) > 0.2f,
                    });
                layers = growths.ToArray();
                return;
            }
            var (trunk, needles) = Shapes.Conifer();
            brokenTrees = new Mesh[3][];
            for (int stage = 0; stage < 3; stage++)
            {
                brokenTrees[stage] = new Mesh[2];
                var source = new[] { trunk, needles };
                for (int part = 0; part < 2; part++)
                {
                    var vertices = source[part].vertices; var triangles = source[part].triangles; var indices = new List<int>();
                    float cut = 7.5f - stage * 3;
                    for (int t = 0; t < triangles.Length; t += 3)
                        if (vertices[triangles[t]].y < cut && vertices[triangles[t + 1]].y < cut && vertices[triangles[t + 2]].y < cut) indices.AddRange(new[] { triangles[t], triangles[t + 1], triangles[t + 2] });
                    var mesh = Instantiate(source[part]); mesh.triangles = indices.ToArray(); mesh.RecalculateBounds(); brokenTrees[stage][part] = mesh;
                }
            }
            layers = new[]
            {
                boulders,
                new Layer
                {
                    name = "conifer tree", height = 10f, girth = 1.7f,
                    meshes = new[] { trunk, needles }, materials = new[] { Shapes.Solid(Surface.Bark, Color.white, true), Shapes.Cutout(Shapes.Needles()) },
                    distant = Shapes.Spire(), distantMaterial = Shapes.Material("Props", new Color(0.09f, 0.2f, 0.08f)),
                    shadows = true, depth = Depth(45f), perCell = 22f, size = new Vector2(0.6f, 1.5f),
                    // woods grow in clumps on gentle ground
                    grows = (direction, height, level) => height > 2.5f && height < treeLine && level > 0.9f && noise.snoise(direction * 9f + terrain.offset) > -0.15f,
                },
                new Layer
                {
                    name = "tussock of grass", height = 0.5f, girth = 0.5f,
                    meshes = new[] { Shapes.Tuft() }, materials = new[] { Shapes.Cutout(Shapes.Blades()) },
                    depth = Depth(6f), perCell = 55f, coarse = 4f, size = new Vector2(0.7f, 1.4f),
                    grows = (direction, height, level) => height > 1.9f && height < treeLine + 10f && level > 0.8f,
                },
            };
        }

        void Update()
        {
            Drawn = 0;
            // only the body the player is on is near enough for any of this to be more than a speck
            if (!owner.IsReference) return;
            var lod = LodSettings.Current;
            if (tier != LodSettings.Tier)
            {
                tier = LodSettings.Tier;
                foreach (var layer in layers) layer.cells.Clear();
                growing = 0;
            }

            var view = Camera.main;
            float3 camera = view.transform.position, up = math.normalize(camera);
            float height = math.length(camera), top = body.radius + body.terrain.MaxHeight;
            // how far round the world the eye can see at all
            float reach = height <= floor ? 0f : math.acos(floor / height);
            math.sincos(reach, out float eyeSine, out float eyeCosine);
            GeometryUtility.CalculateFrustumPlanes(view, frustum);
            int frame = Time.frameCount;

            foreach (var layer in layers)
            {
                float tall = layer.height * layer.size.y, cellSize = QuadtreeLod.NodeSize(body.radius, layer.depth);
                // as far as the thing can be made out, though never past where this stops being the body the player is at
                float range = math.min(lod.Range(tall) / layer.coarse, 0.85f * (body.soiRadius - body.radius));
                // the full shape is kept for where it can be made out
                float fine = lod.Range(tall) / 8f;
                layer.whole.Clear();
                foreach (var fragments in broken) fragments.Clear();
                layer.plain.Clear();
                if (height - top < range)
                {
                    // nothing beyond the furthest horizon can matter, however far the range
                    float highest = math.acos(math.min(1f, floor / (top + tall))), round = math.min(math.PI, reach + highest);
                    float horizon = math.sqrt(height * height + body.radius * body.radius - 2f * height * body.radius * math.cos(round)) + cellSize;
                    QuadtreeLod.Near(camera, body.radius, layer.depth, math.min(range * Keep, horizon), near);
                    foreach (var id in near)
                    {
                        // Until a cell has grown, all that is known of its ground is that it is no higher than
                        // the highest there is. The terrain itself is only ever read off the main thread.
                        if (!layer.cells.TryGetValue(id, out var cell))
                            layer.cells[id] = cell = new Cell { centre = id.Centre * body.radius, cosine = math.cos(highest), sine = math.sin(highest) };
                        cell.seen = frame;
                        float distance = math.distance(camera, cell.centre);
                        // over the horizon it can wait
                        bool above = height <= floor || math.dot(up, cell.centre) / math.length(cell.centre) > eyeCosine * cell.cosine - eyeSine * cell.sine;
                        if (cell.things == null && cell.growing == null && above && distance < range && growing < 48)
                        {
                            float density = layer.perCell * lod.scatter;
                            cell.growing = Task.Run(() => Grow(layer, id, density));
                            growing++;
                        }
                        if (cell.growing != null && cell.growing.IsCompleted)
                        {
                            float ground;
                            (cell.things, ground) = cell.growing.Result;
                            cell.growing = null;
                            growing--;
                            // now its own height says how far round the horizon it shows
                            float over = math.acos(math.min(1f, floor / (ground + tall + 0.5f * cellSize)));
                            (cell.centre, cell.cosine, cell.sine) = (id.Centre * ground, math.cos(over), math.sin(over));
                        }
                        float size = Detail.Fade(distance, range);
                        if (cell.things == null || cell.things.Length == 0 || !above || size <= 0f || !InView(cell.centre, cellSize + tall + 30f)) continue;
                        cell.whole = layer.distant == null || distance < fine * (cell.whole ? 1.15f : 1f);
                        if (layer.name != "conifer tree" || wounds.Count == 0) (cell.whole ? layer.whole : layer.plain).Add(cell.things, size);
                        else foreach (var thing in cell.things)
                        {
                            wounds.TryGetValue(TreeKey(thing.GetColumn(3)), out float hurt);
                            if (hurt < 20) (cell.whole ? layer.whole : layer.plain).Add(thing);
                            else if (hurt < 80) broken[Mathf.Clamp((int)(hurt / 20) - 1, 0, 2)].Add(thing);
                        }
                    }
                }
                stale.Clear();
                foreach (var pair in layer.cells)
                    if (pair.Value.seen != frame)
                        stale.Add(pair.Key);
                foreach (var id in stale)
                {
                    if (layer.cells[id].growing != null) growing--;
                    layer.cells.Remove(id);
                }

                var bounds = new Bounds(camera, Vector3.one * (2f * range + 200f));
                for (int m = 0; m < layer.meshes.Length; m++) layer.whole.Draw(layer.meshes[m], layer.materials[m], layer.shadows, bounds);
                if (layer.name == "conifer tree")
                    for (int part = 0; part < 2; part++) for (int stage = 0; stage < 3; stage++) broken[stage].Draw(brokenTrees[stage][part], layer.materials[part], layer.shadows, bounds);
                if (layer.distant != null) layer.plain.Draw(layer.distant, layer.distantMaterial, false, bounds);
                Drawn += layer.whole.Count + layer.plain.Count;
            }
        }

        // Whether a ball this big is on screen, or near enough beside it to throw a shadow into view.
        bool InView(float3 centre, float radius)
        {
            foreach (var plane in frustum)
                if (plane.GetDistanceToPoint(centre) < -radius)
                    return false;
            return true;
        }

        // A blast has left a crater: whatever grew in reach of it is gone, and the ground round it is sown afresh.
        public void Clear(float4 crater)
        {
            foreach (var layer in layers)
            {
                float reach = 1.1f * crater.w, cellSize = QuadtreeLod.NodeSize(body.radius, layer.depth);
                if (layer.name == "conifer tree")
                {
                    foreach (var cell in layer.cells.Values)
                    {
                        if (cell.things == null || math.distance(math.normalizesafe((float3)cell.centre), crater.xyz) * body.radius > cellSize + crater.w + 1) continue;
                        foreach (var tree in cell.things)
                        {
                            Vector3 foot = tree.GetColumn(3);
                            if (math.distance(math.normalizesafe((float3)foot), crater.xyz) * body.radius > crater.w + 1) continue;
                            var key = TreeKey(foot); wounds.TryGetValue(key, out float hurt); wounds[key] = hurt + math.max(12, crater.w * 8);
                        }
                    }
                    continue;
                }
                foreach (var cell in layer.cells.Values)
                {
                    if (math.distance(math.normalizesafe((float3)cell.centre), crater.xyz) * body.radius > cellSize + reach) continue;
                    if (cell.growing != null) { cell.growing = null; growing--; }
                    if (cell.things == null) continue;
                    int survivors = 0;
                    for (int i = 0; i < cell.things.Length; i++)
                    {
                        var plant = cell.things[i];
                        if (math.distance(math.normalizesafe((float3)(Vector3)plant.GetColumn(3)), crater.xyz) * body.radius <= reach) continue;
                        if (survivors != i) cell.things[survivors] = plant;
                        survivors++;
                    }
                    if (survivors != cell.things.Length) Array.Resize(ref cell.things, survivors);
                }
            }
        }

        // Everything growing or lying within the scanner's reach of a point.
        public void Collect(float3 eye, List<Scannable> into)
        {
            foreach (var layer in layers)
            foreach (var cell in layer.cells.Values)
            {
                if (cell.things == null || math.distance(cell.centre, eye) > Scan.Range + QuadtreeLod.NodeSize(body.radius, layer.depth)) continue;
                foreach (var thing in cell.things)
                {
                    float3 foot = (Vector3)thing.GetColumn(3);
                    if (math.distancesq(foot, eye) > Scan.Range * Scan.Range) continue;
                    float size = thing.lossyScale.x;
                    into.Add(new Scannable { name = layer.name, foot = foot, height = layer.height * size, girth = layer.girth * size });
                }
            }
        }

        // Runs off the main thread: nothing here may touch the engine, only arithmetic.
        (Matrix4x4[], float) Grow(Layer layer, NodeId id, float tries)
        {
            var rnd = new Random((math.hash(new float4(body.terrain.offset, id.GetHashCode())) + layer.salt) | 1u);
            var craters = body.craters;
            // one slope reading serves the whole cell
            float level = math.dot(body.GroundNormal(id.Centre), id.Centre);
            var grown = new List<Matrix4x4>();
            for (int i = 0; i < (int)tries; i++)
            {
                float3 direction = CubeSphere.Direction(id.face, id.UvMin + rnd.NextFloat2() * id.UvSize);
                float height = body.GroundRadius(direction) - body.radius, size = rnd.NextFloat(layer.size.x, layer.size.y), turn = rnd.NextFloat(2f * math.PI);
                if (!layer.grows(direction, height, level)) continue;
                // nothing is left standing in a crater
                bool blasted = false;
                foreach (float4 crater in craters) blasted |= (layer.name != "conifer tree" || crater.w >= 4) && math.distance(direction, crater.xyz) * body.radius < 1.1f * crater.w;
                if (blasted || body.Dry(direction * (body.radius + height))) continue;
                float3 along = math.normalize(math.cross(direction, math.abs(direction.y) < 0.9f ? new float3(0, 1, 0) : new float3(1, 0, 0)));
                grown.Add(float4x4.TRS(direction * (body.radius + height - 0.1f), quaternion.LookRotation(math.rotate(quaternion.AxisAngle(direction, turn), along), direction), size));
            }
            return (grown.ToArray(), body.GroundRadius(id.Centre));
        }
    }
}
