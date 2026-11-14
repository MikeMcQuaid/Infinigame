using System.Collections.Generic;
using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Infinigame
{
    // Every mesh, texture and material in the game is made here from code.
    public static class Shapes
    {
        public static Material Material(string resource, Color colour) =>
            new Material(Resources.Load<Material>(resource)) { color = colour };

        public static GameObject Part(Transform parent, string name, Mesh mesh, Material material, Vector3 position, Vector3 scale, bool shadows = true)
        {
            var part = new GameObject(name);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return part;
        }

        // A cube-sphere, so triangles stay even all the way round.
        public static Mesh Sphere(float radius, int quads)
        {
            int n = quads + 1;
            var vertices = new List<Vector3>(6 * n * n);
            var normals = new List<Vector3>(6 * n * n);
            var triangles = new List<int>(36 * quads * quads);
            for (int face = 0; face < CubeSphere.Faces; face++)
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float3 direction = CubeSphere.Direction(face, new float2(i, j) * (2f / quads) - 1f);
                if (i < quads && j < quads)
                {
                    int a = vertices.Count;
                    triangles.AddRange(new[] { a, a + 1, a + n, a + 1, a + n + 1, a + n });
                }
                vertices.Add(direction * radius);
                normals.Add(direction);
            }
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            return mesh;
        }

        public static Mesh Box()
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            for (int face = 0; face < CubeSphere.Faces; face++)
            {
                CubeSphere.Axes(face, out float3 right, out float3 up, out float3 normal);
                int a = vertices.Count;
                vertices.AddRange(new Vector3[] { (normal - right - up) / 2f, (normal + right - up) / 2f, (normal - right + up) / 2f, (normal + right + up) / 2f });
                for (int i = 0; i < 4; i++) normals.Add(normal);
                triangles.AddRange(new[] { a, a + 1, a + 2, a + 1, a + 3, a + 2 });
            }
            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            return mesh;
        }

        // Colour by elevation (x) and steepness (y), matching the UVs PatchMeshJob writes.
        public static Texture2D Palette(BodyDescriptor body)
        {
            const int width = 256, height = 8;
            var terrain = body.terrain;
            float shift = (body.hue - 0.5f) * 0.9f, low = terrain.MinHeight, span = terrain.MaxHeight + terrain.polarCap - low;
            Color Hsv(float h, float s, float v) => Color.HSVToRGB(Mathf.Repeat(h + shift, 1f), s, v);
            Color rock = Hsv(0.08f, 0.15f, 0.42f), snow = new Color(0.94f, 0.96f, 1f);
            var stops = body.kind == BodyKind.Planet
                ? new (float height, Color colour)[]
                {
                    (-terrain.seaDepth, Hsv(0.6f, 0.5f, 0.2f)), (-terrain.riverDepth, Hsv(0.14f, 0.3f, 0.6f)), (1.5f, Hsv(0.13f, 0.35f, 0.85f)),
                    (4f, Hsv(0.27f, 0.6f, 0.58f)), (0.2f * terrain.mountainAmp + 8f, Hsv(0.33f, 0.65f, 0.36f)),
                    (0.4f * terrain.mountainAmp + 10f, rock), (0.6f * terrain.mountainAmp + 12f, rock), (0.7f * terrain.mountainAmp + 14f, snow),
                }
                : new (float height, Color colour)[]
                {
                    (-1.5f * terrain.craterDepth, Color.HSVToRGB(body.hue, 0.1f, 0.28f)), (0f, Color.HSVToRGB(body.hue, 0.07f, 0.55f)),
                    (terrain.hillAmp, Color.HSVToRGB(body.hue, 0.05f, 0.75f)),
                };

            var pixels = new Color[width * height];
            for (int x = 0; x < width; x++)
            {
                float metres = low + (x + 0.5f) / width * span;
                int next = 1;
                while (next < stops.Length - 1 && stops[next].height < metres) next++;
                Color colour = Color.Lerp(stops[next - 1].colour, stops[next].colour, Mathf.InverseLerp(stops[next - 1].height, stops[next].height, metres));
                for (int y = 0; y < height; y++)
                    // cliffs show bare rock, darker on the moon
                    pixels[y * width + x] = Color.Lerp(colour, body.kind == BodyKind.Planet && metres > 2f ? rock : colour * 0.6f,
                        Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.85f, (y + 0.5f) / height)));
            }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        public static Color SeaColour(BodyDescriptor body) =>
            Color.HSVToRGB(Mathf.Repeat(0.56f + (body.hue - 0.5f) * 0.9f, 1f), 0.75f, 0.6f) * new Color(1, 1, 1, 0.82f);

        public static Color SkyColour(BodyDescriptor body, bool dusk) =>
            Color.HSVToRGB(Mathf.Repeat((dusk ? 0.05f : 0.6f) + (body.hue - 0.5f) * 0.9f, 1f), dusk ? 0.7f : 0.65f, 1f);

        // A texture of two flat colours, sampled at u = 0.25 and u = 0.75.
        public static Texture2D Swatch(Color left, Color right)
        {
            var texture = new Texture2D(2, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels(new[] { left, right });
            texture.Apply(false, true);
            return texture;
        }

        // A trunk and two stacked cones, about four metres tall, with its base at the origin.
        public static Mesh Tree()
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            void Cone(float radius, float bottom, float top, float u)
            {
                const int sides = 7;
                for (int i = 0; i < sides; i++)
                {
                    float a = 2f * Mathf.PI * i / sides, b = 2f * Mathf.PI * (i + 1) / sides;
                    triangles.AddRange(new[] { vertices.Count, vertices.Count + 1, vertices.Count + 2 });
                    vertices.AddRange(new[] { new Vector3(0f, top, 0f), new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b)) * radius + Vector3.up * bottom, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius + Vector3.up * bottom });
                    for (int k = 0; k < 3; k++) uvs.Add(new Vector2(u, 0.5f));
                }
            }
            Cone(0.22f, 0f, 1.6f, 0.25f);
            Cone(1.3f, 1f, 3.2f, 0.75f);
            Cone(0.95f, 2.2f, 4.3f, 0.75f);
            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            return mesh;
        }

        // A lumpy boulder about a metre across.
        public static Mesh Rock()
        {
            var mesh = Sphere(0.5f, 2);
            var vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] *= 1f + 0.35f * noise.snoise((float3)vertices[i] * 3.1f);
            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Color SunColour(BodyDescriptor body) => Color.HSVToRGB(Mathf.Lerp(0.04f, 0.16f, body.hue), 0.45f, 1f);

        // A small ship built from stretched spheres and boxes. Forward is +z, the hull's underside sits at -Ship.Clearance.
        public static Transform ShipModel()
        {
            var root = new GameObject("Ship").transform;
            Mesh sphere = Sphere(0.5f, 6), box = Box();
            Material hull = Material("Lit", new Color(0.85f, 0.86f, 0.9f)), trim = Material("Lit", new Color(0.9f, 0.35f, 0.15f));
            Material dark = Material("Lit", new Color(0.12f, 0.13f, 0.16f)), glass = Material("Water", new Color(0.35f, 0.75f, 0.95f, 0.45f));
            Part(root, "Fuselage", sphere, hull, new Vector3(0, 0.1f, 0), new Vector3(2.2f, 1.5f, 6.5f));
            Part(root, "Canopy", sphere, glass, new Vector3(0, 0.55f, 1.2f), new Vector3(1.5f, 1.1f, 2.6f), false);
            Part(root, "Wings", box, hull, new Vector3(0, -0.1f, -0.8f), new Vector3(7f, 0.14f, 1.9f));
            Part(root, "Tail", box, trim, new Vector3(0, 0.9f, -2.6f), new Vector3(0.12f, 1.3f, 1.2f));
            foreach (float side in new[] { -1f, 1f })
            {
                Part(root, "Engine", sphere, trim, new Vector3(side * 1.5f, 0f, -1.9f), new Vector3(0.8f, 0.8f, 2.4f));
                Part(root, "Exhaust", sphere, Material("Glow", new Color(2f, 5f, 8f)), new Vector3(side * 1.5f, 0f, -3f), new Vector3(0.5f, 0.5f, 0.5f), false);
                Part(root, "Wingtip", box, trim, new Vector3(side * 3.5f, 0.15f, -0.9f), new Vector3(0.12f, 0.6f, 1.6f));
                Part(root, "Skid", box, dark, new Vector3(side * 0.9f, -Ship.Clearance + 0.08f, 0.2f), new Vector3(0.18f, 0.16f, 3.2f));
                Part(root, "Strut", box, dark, new Vector3(side * 0.9f, -0.75f, 0.2f), new Vector3(0.1f, 0.8f, 0.1f));
            }
            return root;
        }
    }
}
