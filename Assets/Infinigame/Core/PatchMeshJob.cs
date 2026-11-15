using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Infinigame.Core
{
    // Builds one terrain patch off the main thread. Vertices are in body-local space.
    // Compiling synchronously means the first patches are already fast in the editor.
    [BurstCompile(CompileSynchronously = true)]
    public struct PatchMeshJob : IJob
    {
        public int face, quads;
        public float2 uvMin;
        public float uvSize, radius, skirt;
        public TerrainParams terrain;
        // the volcanoes and the blasts that reach this patch
        [ReadOnly, DeallocateOnJobCompletion] public NativeArray<Volcano> volcanoes;
        [ReadOnly, DeallocateOnJobCompletion] public NativeArray<Blast> blasts;
        public Mesh.MeshData mesh;

        [StructLayout(LayoutKind.Sequential)]
        struct Vertex
        {
            public float3 position, normal;
            // What blasts and volcanoes have left here. r: soot. g: rock that was molten, the nearer 1
            // the sooner it ran here. b: water, which up to a half has only wetted the ground and over
            // that stands on it, deeper with each of the four metres it can be. a: how much of that
            // rock there is.
            public Color32 paint;
            public float2 uv; // x: colour elevation 0..1, y: steepness 0..1
            // x: when the rock was struck, on the clock of whoever dug it. y: a volcano's lake of lava, which never cools.
            public float2 heat;
        }

        public static int VertexCount(int quads) => (quads + 1) * (quads + 1) + 4 * (quads + 1);
        public static int IndexCount(int quads) => 6 * quads * quads + 24 * quads;

        public void Execute()
        {
            int n = quads + 1, g = n + 2; // g: grid with a one-vertex border so normals match neighbours
            var points = new NativeArray<float3>(g * g, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var colour = new NativeArray<float>(g * g, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var paints = new NativeArray<float4>(g * g, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var heats = new NativeArray<float2>(g * g, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            // a giant has no relief at all, so guard the division
            float minHeight = terrain.MinHeight, heightSpan = math.max(terrain.MaxHeight + terrain.polarCap - minHeight, 1e-3f);
            // about how far apart the vertices are, in metres
            float spread = uvSize / quads * radius * (math.PI / 4f);
            // How far off each blast can show, squared and as a share of the radius, so that the many
            // out of reach cost a vertex next to nothing. A mark too small for this patch is spread wider.
            var reach = new NativeArray<float>(blasts.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            for (int b = 0; b < blasts.Length; b++) reach[b] = math.square((TerrainNoise.Reach(blasts[b]) + 5f * spread) / radius);
            for (int j = 0; j < g; j++)
            for (int i = 0; i < g; i++)
            {
                float3 dir = CubeSphere.Direction(face, uvMin + (new float2(i, j) - 1f) * (uvSize / quads));
                float height = TerrainNoise.Height(dir, terrain), pool = float.MinValue;
                float2 cone = 0f, flat = 0f, heat = new float2(TerrainNoise.Cold, 0f);
                float4 paint = 0f;
                for (int v = 0; v < volcanoes.Length; v++)
                {
                    float2 here = TerrainNoise.Cone(dir, volcanoes[v], radius, out float across);
                    if (here.y <= 0f) continue;
                    cone = math.max(cone, here);
                    float2 flows = TerrainNoise.Flows(dir, volcanoes[v], across);
                    heat.y = math.max(heat.y, flows.x);
                    // old flows are rock that ran long ago, where anything molten now is still to come
                    if (flows.y > 0f) paint.yw = math.max(paint.yw, new float2(TerrainNoise.Ran, flows.y));
                }
                height = height * (1f - cone.y) + cone.x;
                for (int b = 0; b < blasts.Length; b++)
                {
                    if (math.distancesq(dir, blasts[b].crater.xyz) > reach[b]) continue;
                    TerrainNoise.Shape(dir, blasts[b], radius, ref height, ref pool, ref flat);
                    TerrainNoise.Paint(dir, blasts[b], radius, spread, ref paint, ref heat.x);
                }
                // a pad is scorched, and dry whatever stood there
                paint.x = math.max(paint.x, 0.85f * flat.y);
                paint.z = math.lerp(math.max(paint.z, math.saturate(0.5f + (pool - height) / 8f)), 0f, flat.y);
                height = math.lerp(math.max(height, pool), flat.x, flat.y);
                points[j * g + i] = dir * (radius + height);
                colour[j * g + i] = math.saturate((height + terrain.polarCap * math.smoothstep(0.75f, 0.95f, math.abs(dir.y)) - minHeight) / heightSpan);
                paints[j * g + i] = paint;
                heats[j * g + i] = heat;
            }

            var layout = new NativeArray<VertexAttributeDescriptor>(5, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            layout[0] = new VertexAttributeDescriptor(VertexAttribute.Position);
            layout[1] = new VertexAttributeDescriptor(VertexAttribute.Normal);
            layout[2] = new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4);
            layout[3] = new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2);
            layout[4] = new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2);
            mesh.SetVertexBufferParams(VertexCount(quads), layout);
            mesh.SetIndexBufferParams(IndexCount(quads), IndexFormat.UInt16);
            var vertices = mesh.GetVertexData<Vertex>();
            var indices = mesh.GetIndexData<ushort>();

            float3 min = float.MaxValue, max = float.MinValue;
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int c = (j + 1) * g + i + 1;
                float3 normal = math.normalize(math.cross(points[c + 1] - points[c - 1], points[c + g] - points[c - g]));
                int4 paint = (int4)(math.saturate(paints[c]) * 255f + 0.5f);
                vertices[j * n + i] = new Vertex
                {
                    position = points[c], normal = normal, paint = new Color32((byte)paint.x, (byte)paint.y, (byte)paint.z, (byte)paint.w),
                    uv = new float2(colour[c], math.saturate(3f * (1f - math.dot(normal, math.normalize(points[c]))))), heat = heats[c],
                };
                min = math.min(min, points[c]);
                max = math.max(max, points[c]);
            }

            int index = 0;
            for (int j = 0; j < quads; j++)
            for (int i = 0; i < quads; i++)
            {
                int a = j * n + i;
                Quad(indices, ref index, a, a + 1, a + n, a + n + 1);
            }

            // Skirts: each border vertex is copied towards the centre, and the wall between
            // hides cracks against neighbours at another level of detail.
            for (int edge = 0; edge < 4; edge++)
            {
                int first = edge switch { 0 => 0, 1 => n * (n - 1), 2 => 0, _ => n - 1 };
                int step = edge < 2 ? 1 : n, skirtFirst = n * n + edge * n;
                for (int k = 0; k < n; k++)
                {
                    var v = vertices[first + k * step];
                    v.position -= math.normalize(v.position) * skirt;
                    vertices[skirtFirst + k] = v;
                    if (k == quads) continue;
                    int a = first + k * step, s = skirtFirst + k;
                    // bottom and right walls wind one way, top and left the other, so all face outwards
                    if (edge == 0 || edge == 3) Quad(indices, ref index, a, s, a + step, s + 1);
                    else Quad(indices, ref index, a, a + step, s, s + 1);
                }
            }

            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, index)
            {
                vertexCount = VertexCount(quads),
                bounds = new Bounds((min + max) / 2f, max - min + skirt),
            }, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
        }

        // Two triangles (a, b, c) and (b, d, c).
        static void Quad(NativeArray<ushort> indices, ref int index, int a, int b, int c, int d)
        {
            indices[index++] = (ushort)a;
            indices[index++] = (ushort)b;
            indices[index++] = (ushort)c;
            indices[index++] = (ushort)b;
            indices[index++] = (ushort)d;
            indices[index++] = (ushort)c;
        }
    }
}
