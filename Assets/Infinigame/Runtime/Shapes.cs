using System.Collections.Generic;
using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Infinigame
{
    // Shared model parts and materials, with geometry for the seeded worlds.
    public static class Shapes
    {
        // The top of a body's air and the height of its cloud deck, as multiples of its radius. The air
        // clears the highest peaks. Broken cloud sits below the summits so they stand clear of it; an
        // unbroken shroud covers everything.
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

        static Material flame;
        internal static Transform Flame(Transform parent, Vector3 socket, Vector3 direction, float length, float radius)
        {
            if (flame == null) flame = new Material(Resources.Load<Shader>("ThrusterFlame")) { enableInstancing = true };
            var plume = Part(parent, "Thruster flame", Orb(1, 16), flame, socket + direction * (length * 0.5f), new Vector3(radius, length * 0.5f, radius), false).transform;
            plume.localRotation = Quaternion.FromToRotation(Vector3.up, direction);
            return plume;
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

        internal static Mesh Orb(float radius, int quads) => Models.Parts("Orb " + radius.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " " + quads)[0];

        public static Mesh Box() => Models.Parts("Box")[0];

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
        public static Texture2D Needles() => Resources.Load<Texture2D>("Surfaces/Needles");
        public static Texture2D Blades() => Resources.Load<Texture2D>("Surfaces/Blades");
        public static (Mesh trunk, Mesh needles) Conifer() { var m = Models.Parts("Conifer"); return (m[0], m[1]); }
        public static Mesh Spire() => Models.Parts("Spire")[0];

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
        public static Mesh Tuft() => Models.Parts("Tuft")[0];
        public static Mesh Rock(int quads = 0) => Models.Parts("Rock " + (quads == 0 ? LodSettings.Tier == 0 ? 6 : 12 : quads))[0];

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

        public static Mesh Seaweed() => Models.Parts("Seaweed")[0];
        public static Mesh Grazer() => Models.Parts("Grazer")[0];
        public static (Mesh body, Mesh wing) Bird() { var m = Models.Parts("Bird"); return (m[0], m[1]); }
        public static Mesh Fish() => Models.Parts("Fish")[0];

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

        public static bool MatteCombat => Application.isMobilePlatform || DeviceControls.VR || Application.platform == RuntimePlatform.WindowsPlayer;
        public static Material Carapace(Color colour, float size, Color eyes = default)
        {
            if (MatteCombat)
            {
                if (eyes == default && Application.platform == RuntimePlatform.WindowsPlayer && !DeviceControls.VR)
                    return new Material(Resources.Load<Shader>("DiffuseShell")) { color = colour, mainTexture = Textures.Albedo(Surface.Chitin), enableInstancing = true };
                var matte = eyes == default ? Solid(Surface.Chitin, colour) : Material("Glow", eyes);
                if (matte.HasProperty("_Smoothness")) matte.SetFloat("_Smoothness", 0);
                if (matte.HasProperty("_Metallic")) matte.SetFloat("_Metallic", 0);
                matte.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                matte.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                return matte;
            }
            var shell = Hull(colour, size, eyes);
            shell.SetFloat("_Organic", 1);
            return shell;
        }

        public static Material BroodTissue()
        {
            Color colour = new Color(0.71f, 0.39f, 0.49f);
            if (Application.platform == RuntimePlatform.WindowsPlayer && !DeviceControls.VR) return Carapace(colour, 2);
            var skin = Solid(Surface.Chitin, colour, true);
            skin.SetFloat("_Smoothness", 0.18f);
            skin.SetFloat("_BumpScale", 0.72f);
            skin.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            skin.enableInstancing = true;
            return skin;
        }
        public static Material PresenceShell(float size) => Carapace(new Color(0.83f, 0.76f, 0.70f), size);
        public static Material PresenceOptics(float size) => Carapace(Color.black, size, new Color(0.62f, 0.32f, 0.78f));

        public static Material Cutout(Texture2D texture)
        {
            var material = new Material(Resources.Load<Shader>("Foliage")) { enableInstancing = true };
            material.SetTexture("_BaseMap", texture);
            return material;
        }

        public static Color SunColour(BodyDescriptor body) => Color.HSVToRGB(Mathf.Lerp(0.04f, 0.16f, body.hue), 0.45f, 1f);
    }
}
