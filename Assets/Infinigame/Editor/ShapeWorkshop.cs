using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Unity.Mathematics;
using Infinigame.Core;

namespace Infinigame
{
    public static class ShapeWorkshop
    {
        public static Mesh Seaweed()
        {
            var mesh = new MeshKit();
            for (int stem = 0; stem < 5; stem++)
            {
                var points = new Vector3[17];
                for (int i = 0; i < points.Length; i++) points[i] = new Vector3(Mathf.Sin(i * 0.35f + stem) * 0.3f + (stem - 2) * 0.2f, i * 0.19f, Mathf.Cos(i * 0.3f + stem) * 0.25f);
                mesh.Tube(points, 0.035f, 12, 0.01f);
                for (int i = 3; i < 15; i += 2)
                {
                    float side = i % 4 < 2 ? -1 : 1;
                    mesh.Ball(points[i] + new Vector3(side * 0.2f, 0.12f, 0), new Vector3(0.24f, 0.35f, 0.025f), 16);
                }
            }
            return mesh.Mesh("Seaweed fronds");
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

        static Texture2D Strokes(Color background, IEnumerable<(float2 from, float2 to, float2 width, Color colour)> strokes)
        {
            const int size = 1024;
            var pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(background.r, background.g, background.b, 0f);
            foreach (var (from, to, full, colour) in strokes)
            {
                float2 a = from * size, b = to * size, width = full * (size / 512f);
                int2 min = (int2)math.max(math.floor(math.min(a, b) - math.cmax(width)), 0), max = (int2)math.min(math.ceil(math.max(a, b) + math.cmax(width)), size - 1);
                for (int y = min.y; y <= max.y; y++)
                for (int x = min.x; x <= max.x; x++)
                {
                    float2 point = new float2(x, y) + 0.5f;
                    float along = math.saturate(math.dot(point - a, b - a) / math.max(math.lengthsq(b - a), 1e-6f));
                    float cover = math.saturate(math.lerp(width.x, width.y, along) / 2f + 0.5f - math.distance(point, math.lerp(a, b, along)));
                    if (cover <= pixels[y * size + x].a) continue;
                    pixels[y * size + x] = new Color(colour.r, colour.g, colour.b, cover);
                }
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 };
            texture.SetPixels(pixels);
            texture.Apply(true, false);
            return texture;
        }

        // painted once: it takes a moment, and every system's firs and grasses are the same


        // A conifer bough from above: a twig up the middle and side shoots that fork again, all thick with needles.
        public static Texture2D Needles()
        {
            var rnd = new Unity.Mathematics.Random(5);
            var needles = new List<(float2, float2, float2, Color)>();
            var twigs = new List<(float2, float2, float2, Color)>();
            void Shoot(float2 from, float2 to, float thick, int forks)
            {
                float2 along = math.normalize(to - from), across = new float2(-along.y, along.x);
                float length = math.distance(from, to);
                twigs.Add((from, to, new float2(thick, 1f), new Color(0.22f, 0.15f, 0.09f)));
                for (float at = 0.015f; at < length; at += 0.0022f)
                {
                    float2 root = from + along * at;
                    // needles sweep forward along the shoot; this year's growth at its tip is paler
                    float side = rnd.NextBool() ? 1f : -1f, green = rnd.NextFloat(0.17f, 0.32f) * (0.75f + 0.5f * at / length);
                    needles.Add((root, root + (along * rnd.NextFloat(0.5f, 1f) + across * side * rnd.NextFloat(0.5f, 1f)) * rnd.NextFloat(0.02f, 0.036f),
                        new float2(2f, 0.8f), new Color(green * 0.55f, green, green * 0.5f)));
                }
                for (int i = 0; i < forks; i++)
                {
                    float at = (i + 0.7f) / (forks + 0.5f), side = i % 2 == 0 ? 1f : -1f;
                    float2 root = from + along * (at * length);
                    Shoot(root, root + (along * 0.75f + across * side * 0.66f) * (length * 0.45f * (1.1f - 0.6f * at)), 2f, forks > 6 ? 3 : 0);
                }
            }
            Shoot(new float2(0.5f, 0f), new float2(0.5f, 0.985f), 5f, 0);
            for (int i = 0; i < 15; i++)
            {
                float y = 0.03f + 0.058f * i, side = i % 2 == 0 ? 1f : -1f, reach = 0.47f * (1f - math.pow(y, 1.7f)) * math.smoothstep(-0.08f, 0.12f, y);
                Shoot(new float2(0.5f, y), new float2(0.5f + side * reach, y + 0.5f * reach + 0.02f), 3f, 7);
            }
            // needles lie over the twigs they grow from
            needles.AddRange(twigs);
            return Strokes(new Color(0.1f, 0.24f, 0.09f), needles);
        }

        // A tuft of grass: blades fanning up from the bottom edge, each bowed over, tapering and paler towards its tip.
        public static Texture2D Blades()
        {
            var rnd = new Unity.Mathematics.Random(9);
            var strokes = new List<(float2, float2, float2, Color)>();
            for (int i = 0; i < 120; i++)
            {
                float x = rnd.NextFloat(0.1f, 0.9f), lean = rnd.NextFloat(-0.2f, 0.2f) + 0.25f * (x - 0.5f), height = rnd.NextFloat(0.4f, 0.98f), green = rnd.NextFloat(0.3f, 0.5f);
                // a few have dried to straw
                var colour = rnd.NextFloat() < 0.12f ? new Color(0.62f, 0.56f, 0.3f) : new Color(green * 0.72f, green, green * 0.3f);
                float2 from = new float2(x, 0f);
                const int parts = 5;
                for (int part = 0; part < parts; part++)
                {
                    float t = (part + 1f) / parts;
                    // the further up, the further it bows
                    float2 to = new float2(x + lean * t * t, height * math.sin(t * 1.35f) / math.sin(1.35f));
                    strokes.Add((from, to, new float2(6f, 6f) * (1f - 0.75f * (t - 1f / parts)) - new float2(0f, 6f * 0.75f / parts), colour * (0.62f + 0.5f * t)));
                    from = to;
                }
            }
            return Strokes(new Color(0.22f, 0.36f, 0.1f), strokes);
        }

        // A conifer about ten metres tall, its base at the origin: a tapering trunk, and whorls of
        // boughs that carry the needle texture. No two boughs are alike. Each lifts a little and then
        // droops under its own weight, and its sprays hang to either side of its spine, so it has
        // depth from any side.
        public static (Mesh trunk, Mesh needles) Conifer()
        {
            const float height = 10f;
            int sides = LodSettings.Tier == 0 ? 16 : 28;
            const int rings = 12, whorls = 18, branches = 7;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            Mesh Finish()
            {
                var mesh = new Mesh();
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(triangles, 0);
                vertices.Clear();
                normals.Clear();
                uvs.Clear();
                triangles.Clear();
                return mesh;
            }

            for (int ring = 0; ring <= rings; ring++)
            for (int side = 0; side <= sides; side++)
            {
                float angle = 2f * Mathf.PI * side / sides, radius = Mathf.Lerp(0.32f, 0.04f, ring / (float)rings);
                var around = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                if (ring < rings && side < sides)
                {
                    int a = vertices.Count;
                    triangles.AddRange(new[] { a, a + sides + 1, a + 1, a + 1, a + sides + 1, a + sides + 2 });
                }
                vertices.Add(around * radius + Vector3.up * (height * ring / rings));
                normals.Add(around);
                uvs.Add(new Vector2(2f * side / sides, 4f * ring / rings));
            }
            Mesh trunk = Finish();
            // for the furrows of its bark to catch the light
            trunk.RecalculateTangents();

            var rnd = new Unity.Mathematics.Random(3);
            for (int whorl = 0; whorl < whorls; whorl++)
            for (int branch = 0; branch < branches; branch++)
            {
                float t = whorl / (whorls - 1f), length = Mathf.Lerp(3.4f, 0.5f, t) * rnd.NextFloat(0.8f, 1.15f), droop = rnd.NextFloat(0.35f, 0.6f);
                float angle = whorl * 1.3f + 2f * Mathf.PI * (branch + rnd.NextFloat(-0.3f, 0.3f)) / branches, foot = Mathf.Lerp(1.6f, height - 0.4f, Mathf.Pow(t, 0.9f)) + rnd.NextFloat(-0.15f, 0.15f);
                Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)), sideways = Vector3.Cross(Vector3.up, outward);
                for (int step = 0; step <= 3; step++)
                {
                    float s = step / 3f;
                    Vector3 spine = outward * (length * s) + Vector3.up * (foot + length * (0.15f * s - droop * s * s));
                    if (step < 3)
                    {
                        int a = vertices.Count;
                        triangles.AddRange(new[] { a, a + 3, a + 1, a + 1, a + 3, a + 4, a + 1, a + 4, a + 2, a + 2, a + 4, a + 5 });
                    }
                    for (int edge = -1; edge <= 1; edge++)
                    {
                        vertices.Add(spine + sideways * (edge * length * 0.4f) + Vector3.down * (Mathf.Abs(edge) * length * 0.14f));
                        // lit as if facing the sky, which is how a canopy catches light, each side leaning a little its own way
                        normals.Add((Vector3.up + outward * 0.5f + sideways * (edge * 0.35f)).normalized);
                        uvs.Add(new Vector2(edge * 0.5f + 0.5f, s));
                    }
                }
            }
            return (trunk, Finish());
        }

        // What a conifer looks like from too far off to tell its boughs apart: three dark skirts the same size.
        public static Mesh Spire()
        {
            const int sides = 6;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            foreach (var (top, foot, radius) in new[] { (10f, 6f, 1.5f), (7.4f, 3.6f, 2.4f), (5f, 1.6f, 3.1f) })
            {
                int tip = vertices.Count;
                vertices.Add(new Vector3(0f, top, 0f));
                normals.Add(Vector3.up);
                for (int side = 0; side < sides; side++)
                {
                    float angle = 2f * Mathf.PI * side / sides + top;
                    var around = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    vertices.Add(around * radius + Vector3.up * foot);
                    // lit as the canopy is: mostly from above
                    normals.Add((Vector3.up + around * 0.5f).normalized);
                    triangles.AddRange(new[] { tip, tip + 1 + (side + 1) % sides, tip + 1 + side });
                }
            }
            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            return mesh;
        }

        // How full a planet's rings are from their inner edge to their outer one, for the shader that
        // draws them from afar. The rocks themselves are dealt out by the same measure.
        public static Mesh Tuft()
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (int card = 0; card < 3; card++)
            {
                var turn = Quaternion.Euler(0f, 120f * card, 0f);
                Vector3 across = turn * Vector3.right * 0.5f, foot = turn * Vector3.forward * 0.12f, top = foot + turn * Vector3.forward * 0.25f + Vector3.up * 0.68f;
                int a = vertices.Count;
                vertices.AddRange(new[] { foot - across, foot + across, top - across, top + across });
                uvs.AddRange(new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
                triangles.AddRange(new[] { a, a + 1, a + 2, a + 1, a + 3, a + 2 });
            }
            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetNormals(new List<Vector3>(System.Linq.Enumerable.Repeat(Vector3.up, vertices.Count)));
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            return mesh;
        }

        // A lumpy boulder about a metre across, wrapped in the rock texture. Fewer quads make a plainer one.
        public static Mesh Rock(int quads = 0)
        {
            var mesh = Shapes.Sphere(0.5f, quads > 0 ? quads : LodSettings.Tier == 0 ? 6 : 12);
            var vertices = mesh.vertices;
            var uvs = new Vector2[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                uvs[i] = new Vector2(Mathf.Atan2(vertices[i].z, vertices[i].x) / Mathf.PI, vertices[i].y + 0.5f);
                vertices[i] *= 1f + 0.3f * noise.snoise((float3)vertices[i] * 3.1f) + 0.1f * noise.snoise((float3)vertices[i] * 7.3f + 5f);
            }
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        // A flat ring in the xz plane. UV x runs from its inner edge to its outer one.


        public static Mesh[] Grazer(out Vector3[] hips)
        {
            int sides = LodSettings.Tier == 0 ? 24 : 40;
            var parts = new Mesh[6];
            hips = new Vector3[4];
            int index = 0;
            var eyes = new MeshKit();
            var body = new MeshKit { crease = 70 };
            body.Ball(new Vector3(0, 0.83f, 0), new Vector3(0.31f, 0.34f, 0.65f), sides);
            body.Ball(new Vector3(0, 1, 0.4f), new Vector3(0.27f, 0.38f, 0.32f), sides);
            body.Tube(MeshKit.Path(24, new Vector3(0, 0.95f, 0.45f), new Vector3(0, 1.25f, 0.63f), new Vector3(0, 1.3f, 0.85f)), 0.2f, sides, 0.14f);
            body.Ball(new Vector3(0, 1.27f, 0.91f), new Vector3(0.17f, 0.18f, 0.29f), sides);
            body.Ball(new Vector3(0, 1.19f, 1.11f), new Vector3(0.15f, 0.09f, 0.14f), sides);
            foreach (float side in new[] { -1f, 1f })
            {
                eyes.Ball(new Vector3(side * 0.162f, 1.32f, 0.98f), new Vector3(0.035f, 0.048f, 0.053f), 24);
                body.Tube(MeshKit.Path(24, new Vector3(side * 0.13f, 1.37f, 0.77f), new Vector3(side * 0.24f, 1.66f, 0.65f), new Vector3(side * 0.2f, 1.72f, 0.85f)), 0.055f, 24, 0.004f);
                body.Ball(new Vector3(side * 0.25f, 1.34f, 0.74f), new Vector3(0.15f, 0.035f, 0.075f), sides);
                foreach (float end in new[] { -1f, 1f })
                {
                    Vector3 hip = new Vector3(side * 0.22f, 0.79f, end * 0.42f), knee = new Vector3(side * 0.23f, 0.4f, end * 0.34f), hoof = new Vector3(side * 0.22f, 0.06f, end * 0.43f);
                    hips[index] = hip;
                    var leg = new MeshKit { crease = 70 };
                    leg.Ball(hip, new Vector3(0.13f, 0.22f, 0.19f), sides);
                    leg.Tube(MeshKit.Path(24, hip, knee, hoof), 0.11f, sides, 0.055f);
                    leg.Ball(knee, 0.085f, 24);
                    leg.Box(hoof + Vector3.forward * 0.025f, new Vector3(0.12f, 0.11f, 0.19f), 0.025f);
                    parts[2 + index++] = leg.Mesh("Hoof and leg " + index);
                }
            }
            body.Tube(MeshKit.Path(24, new Vector3(0, 0.99f, -0.57f), new Vector3(0.08f, 0.73f, -0.78f), new Vector3(0.13f, 0.6f, -0.72f)), 0.045f, 20, 0.015f);
            parts[0] = body.Mesh("Grazer anatomy");
            parts[1] = eyes.Mesh("Grazer eyes");
            return parts;
        }

        public static (Mesh body, Mesh wing) Bird()
        {
            int sides = LodSettings.Tier == 0 ? 20 : 32;
            var body = new MeshKit(); var wing = new MeshKit();
            body.Ball(Vector3.zero, new Vector3(0.1f, 0.095f, 0.3f), sides);
            body.Ball(new Vector3(0, 0.045f, 0.26f), new Vector3(0.075f, 0.08f, 0.1f), sides);
            body.Tube(MeshKit.Path(12, new Vector3(0, 0.04f, 0.32f), new Vector3(0, 0.025f, 0.4f), new Vector3(0, 0.01f, 0.43f)), 0.035f, 20, 0.002f);
            for (int i = 0; i < 18; i++)
            {
                float t = i / 17f, x = 0.06f + t * 1.02f;
                wing.Place(MeshKit.Move(new Vector3(x, 0.018f * Mathf.Sin(t * Mathf.PI), -0.02f - t * t * 0.14f)) * MeshKit.Turn(0, -t * 35, 0), () =>
                    wing.Ball(Vector3.zero, new Vector3(0.075f, 0.012f, 0.18f * (1 - t * 0.45f)), sides));
            }
            for (int i = -3; i <= 3; i++)
                body.Ball(new Vector3(i * 0.026f, -0.015f, -0.31f), new Vector3(0.027f, 0.01f, 0.15f), sides);
            return (body.Mesh("Bird anatomy"), wing.Mesh("Layered flight feathers"));
        }

        public static Mesh Fish()
        {
            int sides = LodSettings.Tier == 0 ? 24 : 40;
            var body = new MeshKit();
            body.Ball(Vector3.zero, new Vector3(0.06f, 0.11f, 0.26f), sides);
            body.Ball(new Vector3(0, 0.005f, 0.2f), new Vector3(0.047f, 0.075f, 0.085f), sides);
            foreach (float side in new[] { -1f, 1f })
            {
                body.Ball(new Vector3(side * 0.046f, 0.035f, 0.21f), 0.014f, 24);
                body.Ball(new Vector3(side * 0.065f, -0.035f, 0.04f), new Vector3(0.06f, 0.006f, 0.06f), sides);
            }
            for (int i = -8; i <= 8; i++)
            {
                float fan = i / 8f;
                body.Tube(MeshKit.Path(12, new Vector3(0, 0, -0.2f), new Vector3(0, fan * 0.09f, -0.29f), new Vector3(0, fan * 0.14f, -0.35f + 0.05f * Mathf.Abs(fan))), 0.009f, 12, 0.003f);
            }
            body.Ball(new Vector3(0, 0.105f, -0.025f), new Vector3(0.009f, 0.06f, 0.11f), sides);
            return body.Mesh("Fish anatomy and fin rays");
        }

        public static Mesh Ray()
        {
            int detail = LodSettings.Tier == 0 ? 16 : 32;
            var animal = new MeshKit();
            animal.Ball(Vector3.zero, new Vector3(0.19f, 0.08f, 0.38f), detail);
            animal.Tube(MeshKit.Path(detail, new Vector3(0, 0.02f, -0.2f), new Vector3(0, -0.04f, -0.57f),
                new Vector3(0.09f, 0.055f, -0.92f)), 0.045f, detail, 0.003f);
            animal.Mirror(() =>
            {
                var sections = new Vector3[detail + 1][];
                for (int i = 0; i <= detail; i++)
                {
                    float t = i / (float)detail;
                    float chord = 0.3f * Mathf.Pow(1 - t, 0.68f), edge = -0.19f * t * t;
                    sections[i] = new Vector3[detail];
                    for (int j = 0; j < detail; j++)
                    {
                        float a = j * Mathf.PI * 2 / detail;
                        sections[i][j] = new Vector3(0.08f + 0.61f * t, 0.055f * t * t - 0.08f * chord * Mathf.Sin(a), edge + chord * Mathf.Cos(a));
                    }
                }
                animal.Skin(sections);
                animal.Ball(new Vector3(0.08f, 0.062f, 0.19f), new Vector3(0.027f, 0.026f, 0.045f), detail);
                animal.Tube(MeshKit.Path(10, new Vector3(0.062f, 0.016f, 0.24f), new Vector3(0.1f, 0.05f, 0.38f),
                    new Vector3(0.075f, 0.07f, 0.46f)), 0.039f, detail / 2, 0.013f);
            });
            return animal.Mesh("Sea ray with swept pectoral fins");
        }

        public static (Mesh body, Mesh wing) Butterfly()
        {
            int sides = LodSettings.Tier == 0 ? 12 : 20;
            var body = new MeshKit(); var wing = new MeshKit();
            body.Ball(Vector3.zero, new Vector3(0.012f, 0.015f, 0.075f), sides);
            body.Ball(Vector3.forward * 0.06f, 0.018f, sides);
            body.Mirror(() => body.Tube(MeshKit.Path(9, new Vector3(0.008f, 0.01f, 0.07f), new Vector3(0.018f, 0.033f, 0.09f),
                new Vector3(0.039f, 0.042f, 0.104f)), 0.0022f, 6, 0.0015f));
            wing.Place(MeshKit.Turn(90, 0, 0), () =>
            {
                wing.Plate(new[] { new Vector2(0.006f, 0.025f), new Vector2(0.105f, 0.105f), new Vector2(0.191f, 0.126f),
                    new Vector2(0.22f, 0.113f), new Vector2(0.214f, 0.078f), new Vector2(0.182f, 0.034f), new Vector2(0.123f, -0.019f),
                    new Vector2(0.15f, -0.038f), new Vector2(0.143f, -0.064f), new Vector2(0.113f, -0.081f), new Vector2(0.084f, -0.076f),
                    new Vector2(0.061f, -0.109f), new Vector2(0.054f, -0.075f), new Vector2(0.024f, -0.058f), new Vector2(0.006f, -0.018f) }, 0.003f);
            });
            foreach (var tip in new[] { new Vector2(0.207f, 0.118f), new Vector2(0.209f, 0.078f), new Vector2(0.165f, 0.021f), new Vector2(0.137f, -0.06f), new Vector2(0.068f, -0.085f) })
                wing.Tube(MeshKit.Path(9, new Vector3(0.008f, 0.002f, 0.009f), new Vector3(tip.x * 0.56f, 0.003f, tip.y * 0.7f),
                    new Vector3(tip.x, 0.001f, tip.y)), 0.0023f, 6, 0.0009f);
            return (body.Mesh("Butterfly thorax and antennae"), wing.Mesh("Swallowtail wing and veins"));
        }

        public static Mesh[] Bracer()
        {
            var housing = new MeshKit(); housing.Box(new Vector3(0, 0, 18), new Vector3(500, 640, 24), 12);
            var cuff = new MeshKit();
            cuff.Box(new Vector3(0, 0, 83), new Vector3(370, 470, 119), 28);
            cuff.Place(Matrix4x4.TRS(new Vector3(0, 0, 355), Quaternion.identity, new Vector3(1.15f, 1, 1)), () =>
            {
                foreach (float band in new[] { -168f, 168f })
                    cuff.Lathe(new[] { 209f, band - 34, 231f, band - 34, 239f, band - 24, 239f, band + 24,
                        231f, band + 34, 209f, band + 34, 209f, band - 34 }, 32);
            });
            return new[] { housing.Mesh("Wrist screen housing"), cuff.Mesh("Forearm bracer") };
        }

        public static Mesh MoonEngine()
        {
            var casing = new MeshKit();
            casing.Lathe(new[] { 0f, 0f, 3f, 0f, 3.5f, 1f, 2.6f, 6f, 3.2f, 9f, 2.4f, 9f, 1.9f, 6f, 0f, 1f }, 64);
            for (int i = 0; i < 7; i++)
                casing.Place(Matrix4x4.Translate(Vector3.up * (1.6f + i * 0.68f)), () => casing.Torus(3.03f - i * 0.075f, 0.14f, 48, 8));
            for (int i = 0; i < 14; i++)
            {
                float a = i * Mathf.PI / 7;
                var radial = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                casing.Rod(radial * 2.52f + Vector3.up * 5.9f, radial * 3.12f + Vector3.up * 8.85f, 0.26f, 8, 0.13f);
            }
            return casing.Mesh("Moon drive casing");
        }
        public static (Mesh mesh, Vector3 centre)[] Split(Mesh source)
        {
            var vertices = source.vertices; var normals = source.normals; var colours = source.colors32; var uv = source.uv; var triangles = source.triangles;
            var groups = Enumerable.Range(0, 8).Select(_ => new List<int>()).ToArray();
            Vector3 centreOfMesh = source.bounds.center;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 at = (vertices[triangles[i]] + vertices[triangles[i + 1]] + vertices[triangles[i + 2]]) / 3 - centreOfMesh;
                int group = (at.x > 0 ? 1 : 0) + (at.y > 0 ? 2 : 0) + (at.z > 0 ? 4 : 0);
                groups[group].Add(triangles[i]); groups[group].Add(triangles[i + 1]); groups[group].Add(triangles[i + 2]);
            }
            return groups.Where(group => group.Count > 0).Select(group =>
            {
                var indices = group.Distinct().ToArray();
                var map = indices.Select((old, index) => (old, index)).ToDictionary(pair => pair.old, pair => pair.index);
                var points = indices.Select(index => vertices[index]).ToArray();
                Vector3 centre = points.Aggregate(Vector3.zero, (sum, point) => sum + point) / points.Length;
                var mesh = new Mesh { name = source.name + " wreck section", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.vertices = points.Select(point => point - centre).ToArray();
                if (normals.Length == vertices.Length) mesh.normals = indices.Select(index => normals[index]).ToArray();
                if (colours.Length == vertices.Length) mesh.colors32 = indices.Select(index => colours[index]).ToArray();
                if (uv.Length == vertices.Length) mesh.uv = indices.Select(index => uv[index]).ToArray();
                mesh.triangles = group.Select(index => map[index]).ToArray(); mesh.RecalculateBounds();
                return (mesh, centre);
            }).ToArray();
        }
    }
}
