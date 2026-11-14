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
    [BurstCompile]
    public struct PatchMeshJob : IJob
    {
        public int face, quads;
        public float2 uvMin;
        public float uvSize, radius, skirt;
        public TerrainParams terrain;
        public Mesh.MeshData mesh;

        [StructLayout(LayoutKind.Sequential)]
        struct Vertex
        {
            public float3 position, normal;
            public float2 uv; // x: colour elevation 0..1, y: steepness 0..1
        }

        public static int VertexCount(int quads) => (quads + 1) * (quads + 1) + 4 * (quads + 1);
        public static int IndexCount(int quads) => 6 * quads * quads + 24 * quads;

        public void Execute()
        {
            int n = quads + 1, g = n + 2; // g: grid with a one-vertex border so normals match neighbours
            var points = new NativeArray<float3>(g * g, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var colour = new NativeArray<float>(g * g, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            float minHeight = terrain.MinHeight, heightSpan = terrain.MaxHeight + terrain.polarCap - minHeight;
            for (int j = 0; j < g; j++)
            for (int i = 0; i < g; i++)
            {
                float3 dir = CubeSphere.Direction(face, uvMin + (new float2(i, j) - 1f) * (uvSize / quads));
                float height = TerrainNoise.Height(dir, terrain);
                points[j * g + i] = dir * (radius + height);
                colour[j * g + i] = math.saturate((height + terrain.polarCap * math.smoothstep(0.75f, 0.95f, math.abs(dir.y)) - minHeight) / heightSpan);
            }

            var layout = new NativeArray<VertexAttributeDescriptor>(3, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            layout[0] = new VertexAttributeDescriptor(VertexAttribute.Position);
            layout[1] = new VertexAttributeDescriptor(VertexAttribute.Normal);
            layout[2] = new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2);
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
                vertices[j * n + i] = new Vertex
                {
                    position = points[c], normal = normal,
                    uv = new float2(colour[c], math.saturate(3f * (1f - math.dot(normal, math.normalize(points[c]))))),
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
