using System.Collections.Generic;
using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace Infinigame
{
    // Trees and rocks round the camera, drawn with GPU instancing. Each cell of the cube-sphere
    // grid seeds its own random stream, so the same world always grows the same forest.
    public class Scatter : MonoBehaviour
    {
        const float Range = 280f;

        BodyDescriptor body;
        Mesh tree, rock;
        Material foliage, stone;
        int depth, tier = -1;
        readonly Dictionary<NodeId, (Matrix4x4[] trees, Matrix4x4[] rocks)> cells = new Dictionary<NodeId, (Matrix4x4[], Matrix4x4[])>();
        readonly HashSet<NodeId> near = new HashSet<NodeId>();
        readonly List<NodeId> stale = new List<NodeId>();
        Matrix4x4[] trees = new Matrix4x4[0], rocks = new Matrix4x4[0];

        public void Init(BodyDescriptor descriptor)
        {
            body = descriptor;
            tree = Shapes.Tree();
            rock = Shapes.Rock();
            float shift = descriptor.kind == BodyKind.Planet ? (descriptor.hue - 0.5f) * 0.9f : 0f;
            foliage = Shapes.Material("Props", Color.white);
            foliage.mainTexture = Shapes.Swatch(Color.HSVToRGB(0.08f, 0.5f, 0.3f), Color.HSVToRGB(Mathf.Repeat(0.3f + shift, 1f), 0.65f, 0.4f));
            stone = Shapes.Material("Props", Color.HSVToRGB(descriptor.hue, 0.08f, 0.45f));
            // cells of roughly 45 m
            depth = math.max(0, (int)math.round(math.log2(QuadtreeLod.NodeSize(descriptor.radius, 0) / 45f)));
        }

        void Update()
        {
            // only the body the player is on is close enough to need props
            if (transform.position.sqrMagnitude > 1f) return;
            if (tier != LodSettings.Tier)
            {
                tier = LodSettings.Tier;
                cells.Clear();
            }

            float3 camera = Camera.main.transform.position;
            if (math.length(camera) > body.radius + body.terrain.MaxHeight + Range) return;
            float3 up = math.normalize(camera), east = math.normalize(math.cross(up, math.abs(up.y) < 0.9f ? new float3(0, 1, 0) : new float3(1, 0, 0))), north = math.cross(up, east);
            float cell = QuadtreeLod.NodeSize(body.radius, depth);
            int reach = (int)math.ceil(Range / cell);
            near.Clear();
            for (int j = -reach; j <= reach; j++)
            for (int i = -reach; i <= reach; i++)
            {
                if (i * i + j * j > reach * reach) continue;
                int face = CubeSphere.Face(math.normalize(camera + (east * i + north * j) * cell), out float2 uv);
                int2 xy = math.clamp((int2)math.floor((uv + 1f) / 2f * (1 << depth)), 0, (1 << depth) - 1);
                near.Add(new NodeId(face, depth, xy.x, xy.y));
            }

            bool changed = false;
            stale.Clear();
            foreach (var id in cells.Keys)
                if (!near.Contains(id))
                    stale.Add(id);
            foreach (var id in stale) changed |= cells.Remove(id);
            int budget = 3;
            foreach (var id in near)
            {
                if (cells.ContainsKey(id) || budget-- <= 0) continue;
                cells[id] = Grow(id);
                changed = true;
            }
            if (changed)
            {
                var allTrees = new List<Matrix4x4>();
                var allRocks = new List<Matrix4x4>();
                foreach (var grown in cells.Values)
                {
                    allTrees.AddRange(grown.trees);
                    allRocks.AddRange(grown.rocks);
                }
                trees = allTrees.ToArray();
                rocks = allRocks.ToArray();
            }

            var bounds = new Bounds(camera, Vector3.one * (2f * Range + 100f));
            Draw(tree, foliage, trees, bounds);
            Draw(rock, stone, rocks, bounds);
        }

        static void Draw(Mesh mesh, Material material, Matrix4x4[] instances, Bounds bounds)
        {
            var parameters = new RenderParams(material) { worldBounds = bounds, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On, receiveShadows = true };
            for (int start = 0; start < instances.Length; start += 1023)
                Graphics.RenderMeshInstanced(parameters, mesh, 0, instances, math.min(1023, instances.Length - start), start);
        }

        (Matrix4x4[], Matrix4x4[]) Grow(NodeId id)
        {
            var rnd = new Random(math.hash(new float4(body.terrain.offset, id.GetHashCode())) | 1u);
            var grownTrees = new List<Matrix4x4>();
            var grownRocks = new List<Matrix4x4>();
            bool planet = body.kind == BodyKind.Planet;
            int tries = (int)((planet ? 28 : 5) * LodSettings.Current.scatter);
            for (int i = 0; i < tries; i++)
            {
                float3 direction = CubeSphere.Direction(id.face, id.UvMin + rnd.NextFloat2() * id.UvSize);
                float height = body.GroundRadius(direction) - body.radius, kind = rnd.NextFloat();
                Quaternion upright = Quaternion.FromToRotation(Vector3.up, direction) * Quaternion.AngleAxis(rnd.NextFloat(360f), Vector3.up);
                Vector3 position = direction * (body.radius + height - 0.15f);
                bool rocky = !planet || kind < 0.12f;
                if (planet && height < 1.5f) continue;
                if (rocky)
                    grownRocks.Add(Matrix4x4.TRS(position, upright, (Vector3)rnd.NextFloat3(0.5f, 1.8f) * (planet ? 1f : 0.7f)));
                // woods grow in clumps on gentle ground below the tree line
                else if (height < 0.3f * body.terrain.mountainAmp + 8f && noise.snoise(direction * 9f + body.terrain.offset) > -0.15f
                    && math.dot(body.GroundNormal(direction), direction) > 0.9f)
                    grownTrees.Add(Matrix4x4.TRS(position, upright, Vector3.one * rnd.NextFloat(0.8f, 1.6f)));
            }
            return (grownTrees.ToArray(), grownRocks.ToArray());
        }
    }
}
