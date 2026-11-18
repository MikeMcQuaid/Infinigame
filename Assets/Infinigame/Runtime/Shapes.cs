using System.Collections.Generic;
using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Infinigame
{
    // Every mesh, model and material in the game is made here from code. Textures.cs paints the surfaces.
    public static class Shapes
    {
        // The top of a body's air and the height of its cloud deck, as multiples of its radius. The air
        // clears the highest peaks. Broken cloud sits below the summits so they stand clear of it; an
        // unbroken shroud covers everything.
        static Mesh seaweed;
        public static Mesh Seaweed()
        {
            if (seaweed != null) return seaweed;
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
            return seaweed = mesh.Mesh("Seaweed fronds");
        }

        public static float AirTop(BodyDescriptor body) => 1f + math.max(0.12f, 1.45f * body.terrain.MaxHeight / body.radius);
        public static float CloudDeck(BodyDescriptor body) => 1f + (body.cloud < 1f ? 0.55f : 1.15f) * body.terrain.MaxHeight / body.radius;

        static readonly Dictionary<Color, Material> ribbons = new Dictionary<Color, Material>();
        public static Material Beam(Color tint)
        {
            if (!ribbons.TryGetValue(tint, out var ribbon) || ribbon == null)
                ribbons[tint] = ribbon = new Material(Resources.Load<Shader>("EnergyRibbon")) { color = tint };
            return ribbon;
        }

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

        public static Color Colour(float3 colour) => new Color(colour.x, colour.y, colour.z);

        // Sea colour, from the turquoise shallows (depth 0) to the dark open water (depth 1).
        public static Color SeaColour(float depth) =>
            Color.HSVToRGB(Mathf.Lerp(0.48f, 0.6f, depth), Mathf.Lerp(0.6f, 0.85f, depth), Mathf.Lerp(0.75f, 0.28f, depth));

        // The colour a great depth of a body's air scatters sunlight to: its sky at the horizon, and its
        // haze. Pale in Noise.hlsl is the same sum.
        public static Color Haze(BodyDescriptor body)
        {
            Color sky = Colour(body.sky).linear;
            float brightest = Mathf.Max(sky.maxColorComponent, 1e-3f);
            float Pale(float colour) => Mathf.Lerp(Mathf.Pow(colour / brightest, 0.25f), 1f, 0.15f);
            return new Color(Pale(sky.r), Pale(sky.g), Pale(sky.b)).gamma;
        }

        // Tells the shader that draws a body's surface about its air, which veils it from space.
        public static void Air(Material surface, BodyDescriptor body)
        {
            surface.SetColor("_Sky", new Color(body.sky.x, body.sky.y, body.sky.z, body.air));
            surface.SetFloat("_AirRadius", body.radius * AirTop(body));
        }

        // What a body's surface is made of. Ground is four painted layers blended by the elevation and
        // steepness that PatchMeshJob writes into the mesh UVs; a giant is nothing but banded cloud.
        public static Material Ground(BodyDescriptor body)
        {
            if (body.kind == BodyKind.Gas)
            {
                var gas = new Material(Resources.Load<Shader>("Gas"));
                gas.SetColor("_Colour", Colour(body.ground));
                gas.SetFloat("_Banding", body.cloud);
                gas.SetVector("_Seed", (Vector3)body.terrain.offset);
                Air(gas, body);
                return gas;
            }
            var terrain = body.terrain;
            float low = terrain.MinHeight, span = terrain.MaxHeight + terrain.polarCap - low, peaks = terrain.mountainAmp;
            float Elevation(float metres) => (metres - low) / span;
            bool living = body.SeaRadius > 0f;
            var material = new Material(Resources.Load<Shader>("Terrain"));
            // beach, lowland, cliff and summit. A dry body is dust and bare rock, with ice only where a polar cap lifts it.
            var layers = living ? new[] { Surface.Sand, Surface.Grass, Surface.Rock, Surface.Snow } : new[] { Surface.Dust, Surface.Dust, Surface.Rock, Surface.Snow };
            for (int i = 0; i < layers.Length; i++)
            {
                material.SetTexture("_Albedo" + i, Textures.Albedo(layers[i]));
                material.SetTexture("_Normal" + i, Textures.Normal(layers[i]));
            }
            // the elevations where sand gives way to grass, grass to rock and rock to snow, then sea level
            material.SetVector("_Bands", living
                ? new Vector4(Elevation(1.6f), Elevation(0.45f * peaks + 12f), Elevation(0.7f * peaks + 16f), Elevation(0f))
                : new Vector4(-1f, 2f, terrain.polarCap > 0f ? Elevation(0.9f * terrain.MaxHeight) : 2f, Elevation(0f)));
            // how broadly each gives way, and how many metres the whole range of elevation stands for
            material.SetVector("_Blend", new Vector4(0.8f / span, (0.1f * peaks + 4f) / span, (0.05f * peaks + 4f) / span, span));
            // dry ground is grey stone stained by whatever the world is made of
            material.SetFloat("_Saturation", living ? 1f : 0.15f);
            material.SetColor("_Tint", living ? Color.white : Colour(body.ground) * 1.7f);
            material.SetColor("_Low", living ? new Color(0.45f, 0.6f, 0.7f) : Color.white);
            material.SetFloat("_Shore", living ? 1f : 0f);
            Air(material, body);
            return material;
        }

        // Paints soft-edged strokes on a transparent square: needles on a twig, or blades of grass.
        // strokes yields (from, to, width in pixels at each end, colour) with positions in 0..1.
        // Where strokes cross, the one painted first shows.
        static Texture2D Strokes(Color background, IEnumerable<(float2 from, float2 to, float2 width, Color colour)> strokes)
        {
            // widths are for the full size: a phone gets the picture at half that
            int size = LodSettings.Tier == 0 ? 512 : 1024;
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
            texture.Apply(true, true);
            return texture;
        }

        // painted once: it takes a moment, and every system's firs and grasses are the same
        static Texture2D bough, tuft;

        // A conifer bough from above: a twig up the middle and side shoots that fork again, all thick with needles.
        public static Texture2D Needles()
        {
            if (bough != null) return bough;
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
            return bough = Strokes(new Color(0.1f, 0.24f, 0.09f), needles);
        }

        // A tuft of grass: blades fanning up from the bottom edge, each bowed over, tapering and paler towards its tip.
        public static Texture2D Blades()
        {
            if (tuft != null) return tuft;
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
            return tuft = Strokes(new Color(0.22f, 0.36f, 0.1f), strokes);
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
        public static Texture2D RingProfile(BodyDescriptor body)
        {
            const int size = 512;
            var pixels = new Color[size];
            for (int i = 0; i < size; i++)
                pixels[i] = Color.white * Rings.Density(body, math.lerp(body.ringInner, body.ringOuter, (i + 0.5f) / size));
            var texture = new Texture2D(size, 1, TextureFormat.R8, false, true) { wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        // Three cards standing round a triangle, each leaning out over its own side, so that a tuft has
        // breadth seen from above as well as from the side. About a metre across, lit like the ground.
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
            var mesh = Sphere(0.5f, quads > 0 ? quads : LodSettings.Tier == 0 ? 6 : 12);
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
        public static Mesh Ring(float inner, float outer, int segments)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float angle = 2f * Mathf.PI * i / segments;
                var around = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                if (i < segments)
                {
                    int a = vertices.Count;
                    triangles.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
                }
                vertices.Add(around * inner);
                vertices.Add(around * outer);
                uvs.Add(new Vector2(0f, i));
                uvs.Add(new Vector2(1f, i));
            }
            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh grazer, birdBody, birdWing, fish;

        public static Mesh Grazer()
        {
            if (grazer != null) return grazer;
            int sides = LodSettings.Tier == 0 ? 24 : 40;
            var body = new MeshKit { crease = 70 };
            body.Ball(new Vector3(0, 0.83f, 0), new Vector3(0.31f, 0.34f, 0.65f), sides);
            body.Ball(new Vector3(0, 1, 0.4f), new Vector3(0.27f, 0.38f, 0.32f), sides);
            body.Tube(MeshKit.Path(24, new Vector3(0, 0.95f, 0.45f), new Vector3(0, 1.25f, 0.63f), new Vector3(0, 1.3f, 0.85f)), 0.2f, sides, 0.14f);
            body.Ball(new Vector3(0, 1.27f, 0.91f), new Vector3(0.17f, 0.18f, 0.29f), sides);
            body.Ball(new Vector3(0, 1.19f, 1.11f), new Vector3(0.15f, 0.09f, 0.14f), sides);
            foreach (float side in new[] { -1f, 1f })
            {
                body.Ball(new Vector3(side * 0.15f, 1.32f, 0.98f), new Vector3(0.025f, 0.035f, 0.035f), 24);
                body.Tube(MeshKit.Path(24, new Vector3(side * 0.13f, 1.37f, 0.77f), new Vector3(side * 0.24f, 1.66f, 0.65f), new Vector3(side * 0.2f, 1.72f, 0.85f)), 0.055f, 24, 0.004f);
                body.Ball(new Vector3(side * 0.25f, 1.34f, 0.74f), new Vector3(0.15f, 0.035f, 0.075f), sides);
                foreach (float end in new[] { -1f, 1f })
                {
                    Vector3 hip = new Vector3(side * 0.22f, 0.79f, end * 0.42f), knee = new Vector3(side * 0.23f, 0.4f, end * 0.34f), hoof = new Vector3(side * 0.22f, 0.06f, end * 0.43f);
                    body.Ball(hip, new Vector3(0.13f, 0.22f, 0.19f), sides);
                    body.Tube(MeshKit.Path(24, hip, knee, hoof), 0.11f, sides, 0.055f);
                    body.Ball(knee, 0.085f, 24);
                    body.Box(hoof + Vector3.forward * 0.025f, new Vector3(0.12f, 0.11f, 0.19f), 0.025f);
                }
            }
            body.Tube(MeshKit.Path(24, new Vector3(0, 0.99f, -0.57f), new Vector3(0.08f, 0.73f, -0.78f), new Vector3(0.13f, 0.6f, -0.72f)), 0.045f, 20, 0.015f);
            return grazer = body.Mesh("Grazer anatomy");
        }

        public static (Mesh body, Mesh wing) Bird()
        {
            if (birdBody != null) return (birdBody, birdWing);
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
            return (birdBody = body.Mesh("Bird anatomy"), birdWing = wing.Mesh("Layered flight feathers"));
        }

        public static Mesh Fish()
        {
            if (fish != null) return fish;
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
            return fish = body.Mesh("Fish anatomy and fin rays");
        }

        // A screen that shows text, for the cockpit's instruments. It faces -z, towards the pilot.
        public static TextMesh Readout(Transform parent, Vector3 position, float height, TextAnchor anchor, Color colour)
        {
            var readout = new GameObject("Readout").AddComponent<TextMesh>();
            readout.transform.SetParent(parent, false);
            readout.transform.localPosition = position;
            readout.font = Type;
            readout.GetComponent<MeshRenderer>().sharedMaterial = Type.material;
            readout.fontSize = 96;
            readout.characterSize = height * 10f / 96f;
            readout.anchor = anchor;
            readout.color = colour;
            return readout;
        }

        static Font type;
        // The game's typeface: the system's own, so nothing is shipped.
        public static Font Type => type != null ? type : type = Font.CreateDynamicFontFromOSFont(new[] { "Avenir Next", "Segoe UI", "Helvetica Neue", "Arial" }, 32);

        // Materials for scattered things: bark, stone and anything cut out of a texture's alpha. With
        // relief, the surface's bumps catch the light too, on a mesh that has tangents to turn them by.
        public static Material Solid(Surface surface, Color tint, bool relief = false)
        {
            var material = Material(relief ? "Relief" : "Props", tint);
            material.mainTexture = Textures.Albedo(surface);
            if (relief) material.SetTexture("_BumpMap", Textures.Normal(surface));
            return material;
        }

        // The armour of a craft drawn with Instances, each copy with how battered it is: holes with
        // scorched, glowing edges eat it away as that rises. The holes are sized by the craft's size
        // in metres. Given a glow, it is the craft's lights instead, eaten by the same holes.
        public static Material Hull(Color colour, float size, Color glow = default)
        {
            var hull = new Material(Resources.Load<Shader>("Hull")) { color = colour, enableInstancing = true };
            hull.SetFloat("_Size", size);
            hull.SetTexture("_BaseMap", Textures.Albedo(Surface.Chitin));
            hull.SetFloat("_Metallic", 0.12f);
            hull.SetColor("_Glow", glow);
            return hull;
        }

        public static Material SentryShell()
        {
            var material = Hull(new Color(0.81f, 0.77f, 0.84f), 1.2f);
            material.SetFloat("_Organic", 1);
            return material;
        }

        public static Material Cutout(Texture2D texture)
        {
            var material = new Material(Resources.Load<Shader>("Foliage")) { enableInstancing = true };
            material.SetTexture("_BaseMap", texture);
            return material;
        }

        public static Color SunColour(BodyDescriptor body) => Color.HSVToRGB(Mathf.Lerp(0.04f, 0.16f, body.hue), 0.45f, 1f);
    }
}
