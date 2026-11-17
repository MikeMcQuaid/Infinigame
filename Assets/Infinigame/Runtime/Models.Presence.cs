using UnityEngine;
using Infinigame.Core;
using static Infinigame.MeshKit;

namespace Infinigame
{
    public static partial class Models
    {
        // Continuous shells first, overlapping carapace and muscle attachments second.
        static void Carapace(MeshKit shell, MeshKit veins, Vector3 centre, Vector3 size, int segments, bool plated = false, int facets = 0)
        {
            if (facets == 0) facets = Curves;
            shell.colour = new Color(0.38f, 0.3f, 0.29f);
            shell.Ball(centre, Vector3.Scale(size, new Vector3(0.82f, 0.85f, 0.96f)), facets);
            shell.colour = Color.white;
            for (int i = 0; i < segments; i++)
            {
                float t = i / (float)(segments - 1), z = Mathf.Lerp(-size.z * 0.8f, size.z * 0.8f, t);
                float width = Mathf.Sqrt(Mathf.Max(0.12f, 1 - Mathf.Pow(z / size.z, 2)));
                Vector3 at = centre + new Vector3(0, Mathf.Sin(t * Mathf.PI) * size.y * 0.18f, z);
                if (plated) shell.colour = new Color(0.24f, 0.08f, 0.29f);
                shell.Place(Move(at) * Turn(-12 + t * 24, 0, 0), () =>
                    shell.Ball(Vector3.zero, new Vector3(size.x * width, size.y * 0.95f, size.z * 1.55f / segments), facets));
                if (plated)
                {
                    foreach (float side in new[] { -1f, 1f })
                    {
                        shell.colour = Color.Lerp(new Color(0.25f, 0.105f, 0.47f), new Color(0.64f, 0.29f, 0.82f), t);
                        Scute(shell, at + new Vector3(0, size.y * 0.88f, size.z * 0.29f),
                            at + new Vector3(side * size.x * width * 1.19f, size.y * 0.18f, -size.z * 0.45f),
                            new Vector3(-1, side * 0.36f, 0), size.x * 0.40f, size.y * 0.39f, Mathf.Clamp(facets / 3, 4, 10));
                    }
                }
                else for (int fold = -2; fold <= 2; fold++)
                {
                    shell.colour = new Color(0.7f, 0.75f, 0.76f);
                    var ridge = new Vector3[facets];
                    for (int j = 0; j < ridge.Length; j++)
                    {
                        float a = 0.12f + j * (Mathf.PI - 0.24f) / (ridge.Length - 1);
                        ridge[j] = at + new Vector3(Mathf.Cos(a) * size.x * width, Mathf.Sin(a) * size.y * 0.97f,
                            size.z / segments * (fold * 0.17f + 0.12f * Mathf.Sin(a * 5 + i)));
                    }
                    shell.Tube(ridge, size.y * 0.035f, 10, size.y * 0.012f);
                }
                shell.colour = Color.white;
                foreach (float side in new[] { -1f, 1f })
                    veins.Tube(Path(12, at + new Vector3(0, size.y * 0.82f, 0), at + new Vector3(side * size.x * width * 0.8f, size.y * 0.6f, 0.03f),
                        at + new Vector3(side * size.x * width, -size.y * 0.25f, 0)), size.y * 0.022f, 10);
            }
        }

        static (Mesh body, Mesh light) MobileBody(bool ship)
        {
            MeshKit shell = new MeshKit(), light = new MeshKit();
            Vector3 size = ship ? new Vector3(2.3f, 0.95f, 4.2f) : new Vector3(0.65f, 0.35f, 0.75f);
            Carapace(shell, light, Vector3.zero, size, ship ? 12 : 7);
            // A vessel carries ribbed propulsion chambers; scouts have a broad sensory fan.
            if (ship)
            {
                foreach (float side in new[] { -1f, 1f })
                {
                    Carapace(shell, light, new Vector3(side * 2.25f, -0.15f, -1.5f), new Vector3(0.7f, 0.65f, 2.4f), 9);
                    for (int rib = 0; rib < 12; rib++)
                    {
                        float z = -3.5f + rib * 0.38f;
                        shell.Tube(Path(20, new Vector3(side * 0.7f, 0.35f, z), new Vector3(side * 2, 1.05f, z - 0.4f), new Vector3(side * 2.8f, 0.1f, z - 0.7f)), 0.11f, 20, 0.025f);
                    }
                    Gills(shell, new Vector3(side * 2.5f, 0.1f, -1), side, 2);
                }
            }
            else
            {
                for (int i = -3; i <= 3; i++)
                {
                    float a = i * 0.28f;
                    Vector3 root = new Vector3(i * 0.13f, 0.12f, 0.48f), tip = new Vector3(Mathf.Sin(a) * 1.3f, 0.22f + 0.15f * Mathf.Cos(a), 0.9f + Mathf.Cos(a) * 0.22f);
                    shell.Tube(Path(18, root, Vector3.Lerp(root, tip, 0.5f) + Vector3.up * 0.18f, tip), 0.095f, 24, 0.035f);
                    shell.Ball(tip, new Vector3(0.08f, 0.1f, 0.12f), Curves);
                    light.Ball(tip + Vector3.forward * 0.095f, new Vector3(0.045f, 0.055f, 0.03f), 24);
                }
            }
            foreach (float side in new[] { -1f, 1f })
            {
                for (int i = 0; i < (ship ? 5 : 3); i++)
                {
                    float z = size.z * (0.5f - i * 0.3f);
                    Vector3 a = new Vector3(side * size.x * 0.6f, 0, z), b = new Vector3(side * size.x * 1.65f, -size.y * 0.35f, z - size.z * 0.3f), c = new Vector3(side * size.x * 1.25f, -size.y * 0.7f, z - size.z * 0.8f);
                    shell.Tube(Path(18, a, b, c), size.y * 0.25f, Curves, size.y * 0.025f);
                    light.Tube(Path(18, a + Vector3.up * size.y * 0.12f, b, c), size.y * 0.035f, 10, size.y * 0.009f);
                    shell.Ball(a, size.y * 0.3f, Curves);
                }
                Vector3 throat = new Vector3(side * size.x * 0.5f, 0, -size.z * 0.9f);
                shell.Place(Move(throat) * Turn(90, 0, 0), () =>
                    shell.Lathe(new[] { size.y * 0.32f, -0.2f, size.y * 0.42f, 0f, size.y * 0.3f, 0.25f, size.y * 0.18f, 0.3f }, Curves));
                light.Ball(throat, new Vector3(size.y * 0.27f, size.y * 0.27f, 0.08f), Curves);
                for (int i = 0; i < 3; i++)
                    light.Ball(new Vector3(side * size.x * (0.3f + i * 0.18f), size.y * 0.3f, size.z * (0.91f - i * 0.06f)), size.y * (0.18f - i * 0.03f), Curves);
            }
            return (shell.Mesh(ship ? "Presence vessel" : "Presence flier"), light.Mesh("Sensory organs"));
        }

        static void Gills(MeshKit shell, Vector3 at, float side, float scale)
        {
            shell.colour = new Color(0.35f, 0.25f, 0.25f);
            for (int i = 0; i < 12; i++)
            {
                float z = (i - 5.5f) * 0.055f;
                shell.Tube(Path(12, at + new Vector3(-side * 0.08f, 0.2f, z) * scale,
                    at + new Vector3(side * 0.07f, 0, z) * scale, at + new Vector3(0, -0.15f, z) * scale), 0.018f * scale, 12, 0.008f * scale);
            }
            shell.colour = Color.white;
        }

        public static int LimbCount(MotileCaste caste) => caste == MotileCaste.Breacher ? 8 : caste == MotileCaste.Spitter ? 4 : 6;

        public static Vector3 LimbRoot(MotileCaste caste, int index)
        {
            int pairs = LimbCount(caste) / 2;
            return new Vector3((index < pairs ? -1 : 1) * (caste == MotileCaste.Breacher ? 0.62f : 0.4f), 0,
                (index % pairs / (float)(pairs - 1) - 0.5f) * (caste == MotileCaste.Spitter ? 0.7f : 0.9f));
        }

        public static (Mesh body, Mesh light, Mesh leg) Sentry(MotileCaste caste = MotileCaste.Stalker, int detail = 0)
        {
            int facets = detail == 0 ? Curves : detail == 1 ? 16 : 8;
            int small = Mathf.Min(facets, 24), plate = Mathf.Clamp(facets / 3, 4, 10);
            MeshKit body = new MeshKit(), light = new MeshKit(), leg = new MeshKit();
            bool breacher = caste == MotileCaste.Breacher, spitter = caste == MotileCaste.Spitter;
            Carapace(body, light, new Vector3(0, 0.12f, 0), breacher ? new Vector3(0.88f, 0.5f, 0.85f)
                : spitter ? new Vector3(0.48f, 0.45f, 0.85f) : new Vector3(0.57f, 0.3f, 0.8f), 9, true, facets);
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 cheek = new Vector3(side * (breacher ? 0.56f : 0.31f), 0.10f, 0.83f);
                // Radial folds face forwards so the membrane and its scalloped rim read head-on.
                Vector3 Fan(float spoke, float radius)
                {
                    float angle = (spoke * 158 - 74) * Mathf.Deg2Rad;
                    float rib = Mathf.Cos(spoke * 20 * Mathf.PI);
                    return cheek + new Vector3(side * (0.12f + (0.43f + 0.016f * rib) * Mathf.Cos(angle)) * radius,
                        Mathf.Sin(angle) * 0.44f * radius, radius * (-0.19f + rib * 0.026f) + 0.13f * Mathf.Sin(radius * Mathf.PI));
                }
                int spokes = detail == 2 ? 20 : 40;
                for (int j = 0; j < spokes; j++)
                {
                    float a = j / (float)spokes, b = (j + 1f) / spokes;
                    body.colour = Color.Lerp(new Color(0.23f, 0.08f, 0.29f), new Color(0.77f, 0.39f, 0.70f), 0.5f + 0.5f * Mathf.Cos((a + b) * 10 * Mathf.PI));
                    for (int k = 0; k < 4; k++)
                    {
                        float r = k / 4f, next = (k + 1) / 4f;
                        if (side > 0) body.Quad(Fan(a, r), Fan(a, next), Fan(b, next), Fan(b, r));
                        else body.Quad(Fan(b, r), Fan(b, next), Fan(a, next), Fan(a, r));
                    }
                }
                body.colour = new Color(0.98f, 0.78f, 0.49f);
                Vector3 jaw = new Vector3(side * 0.36f, 0.02f, 0.6f), elbow = new Vector3(side * 0.58f, -0.03f, 1.14f), claw = new Vector3(side * 0.30f, -0.38f, 1.22f);
                body.Tube(Path(small, jaw, elbow, claw), breacher ? 0.19f : 0.115f, facets, 0.004f);
                for (int tooth = 0; tooth < 5; tooth++)
                {
                    Vector3 at = Vector3.Lerp(elbow, claw, tooth / 5f);
                    body.Tube(Path(8, at, at + new Vector3(-side * 0.08f, 0.03f, 0.05f), at + new Vector3(-side * 0.14f, 0, 0.08f)), 0.035f, 12, 0.004f);
                }
            }
            body.colour = new Color(0.43f, 0.24f, 0.59f);
            if (breacher)
            {
                for (int i = -3; i <= 3; i++)
                {
                    float a = i * 0.25f;
                    Vector3 from = new Vector3(Mathf.Sin(a) * 0.65f, 0.3f, 0.55f), tip = new Vector3(Mathf.Sin(a) * 1.1f, 1.05f - Mathf.Abs(i) * 0.11f, 0.35f);
                    body.Tube(Path(24, from, Vector3.Lerp(from, tip, 0.65f) + Vector3.forward * 0.14f, tip), 0.19f, facets, 0.025f);
                }
                for (int i = 0; i < 4; i++)
                    body.Ball(new Vector3(0, 0.58f, -0.65f + i * 0.22f), new Vector3(0.27f, 0.18f, 0.18f), facets);
            }
            if (spitter)
            {
                foreach (float side in new[] { -1f, 1f })
                {
                    Carapace(body, light, new Vector3(side * 0.32f, 0.6f, -0.42f), new Vector3(0.32f, 0.48f, 0.5f), 6, true, facets);
                    var neck = Path(28, new Vector3(side * 0.25f, 0.55f, -0.4f), new Vector3(side * 0.3f, 1.15f, 0.2f), new Vector3(side * 0.2f, 0.95f, 0.9f));
                    body.Tube(neck, 0.13f, facets, 0.18f);
                    body.Place(Move(neck[neck.Length - 1]) * Turn(90, 0, 0), () => body.Lathe(new[] { 0.12f, -0.06f, 0.19f, -0.06f, 0.2f, 0, 0.14f, 0.04f, 0.12f, 0.04f }, facets));
                    light.Ball(neck[neck.Length - 1], new Vector3(0.11f, 0.11f, 0.035f), facets);
                }
            }
            for (int i = -1; i <= 1; i++)
            {
                Vector3 eye = new Vector3(i * 0.19f, spitter ? 0.55f : breacher ? 0.39f : 0.25f, 0.77f - Mathf.Abs(i) * 0.07f);
                body.colour = new Color(0.55f, 0.39f, 0.56f);
                body.Ball(eye, new Vector3(0.14f, 0.085f, 0.16f), small);
                light.Ball(eye + Vector3.forward * 0.14f, new Vector3(0.061f, 0.021f, 0.035f), Mathf.Min(small, 20));
            }
            Vector3 hip = Vector3.zero, knee = new Vector3(0.51f, 0.08f, -0.14f), ankle = new Vector3(0.75f, -0.55f, 0), foot = new Vector3(0.83f, -0.90f, 0.15f);
            leg.colour = new Color(0.43f, 0.28f, 0.39f);
            leg.Tube(Path(16, hip, knee + Vector3.up * 0.08f, ankle), breacher ? 0.17f : 0.12f, 24, 0.048f);
            leg.colour = new Color(0.43f, 0.21f, 0.65f);
            leg.Ball(knee, new Vector3(0.15f, 0.12f, 0.13f), small);
            Scute(leg, hip, knee + new Vector3(0.11f, 0.07f, -0.08f), Vector3.back, 0.16f, 0.10f, plate);
            Scute(leg, knee + Vector3.up * 0.08f, ankle, Vector3.forward, 0.135f, 0.09f, plate);
            leg.colour = new Color(1, 0.88f, 0.66f);
            leg.Tube(Path(12, ankle, Vector3.Lerp(ankle, foot, 0.5f) + Vector3.forward * 0.04f, foot), 0.07f, 20, 0.003f);
            return (body.Mesh(caste + " carapace"), light.Mesh(caste + " senses"), leg.Mesh(caste + " limb"));
        }

        public static (Mesh body, Mesh light) Mind()
        {
            MeshKit body = new MeshKit(), light = new MeshKit();
            // Corrugated neural lobes are skinned continuously, then crossed by branching folds.
            for (int i = 0; i < 7; i++)
            {
                float a = i * 2.399963f;
                Vector3 lobe = new Vector3(Mathf.Cos(a) * 0.5f, 0.28f + i * 0.13f, Mathf.Sin(a) * 0.5f);
                Vector3 Surface(float u, float v)
                {
                    float angle = u * Mathf.PI * 2, latitude = (v - 0.5f) * Mathf.PI;
                    float wide = Mathf.Cos(latitude), ripple = 1 + 0.08f * Mathf.Sin(angle * 11 + Mathf.Sin(latitude * 7)) * wide;
                    return lobe + new Vector3(Mathf.Cos(angle) * wide * 0.73f, Mathf.Sin(latitude) * 0.48f, Mathf.Sin(angle) * wide * 0.62f) * ripple;
                }
                int sides = Curves * 2;
                var rings = new Vector3[sides / 2 + 1][];
                for (int j = 0; j < rings.Length; j++)
                {
                    rings[j] = new Vector3[sides];
                    for (int k = 0; k < sides; k++) rings[j][k] = Surface(k / (float)sides, j / (float)(rings.Length - 1));
                }
                body.Skin(rings);
                for (int fold = 0; fold < 6; fold++)
                {
                    var path = new Vector3[36];
                    for (int j = 0; j < path.Length; j++)
                    {
                        float v = 0.12f + j * 0.76f / (path.Length - 1);
                        path[j] = Surface(fold / 6f + 0.025f * Mathf.Sin(v * 24 + i), v);
                    }
                    body.Tube(path, 0.025f, 10, 0.012f);
                    if (fold % 2 == 0) light.Tube(path, 0.008f, 8);
                }
                Vector3 root = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var stem = Path(32, lobe, root * 1.1f + Vector3.down * 0.35f, root * 1.8f + Vector3.down * 1.3f, root * 2.6f + Vector3.down * 1.35f);
                body.Tube(stem, 0.2f, Curves, 0.02f);
                for (int branch = 0; branch < 3; branch++)
                {
                    Vector3 at = stem[12 + branch * 5], tangent = new Vector3(-root.z, 0, root.x) * (branch % 2 == 0 ? 1 : -1);
                    body.Tube(Path(16, at, at + tangent * 0.45f + Vector3.down * 0.2f, root * (2 + branch * 0.2f) + tangent * 0.8f + Vector3.down * 1.35f), 0.065f, 14, 0.008f);
                }
            }
            return (body.Mesh("Rooted mind"), light.Mesh("Neural folds"));
        }

        public static Transform Nest() => Machine("Presence nest", root =>
        {
            var (body, nerves) = Mind();
            Shapes.Part(root, "Root network", body, Shapes.Hull(new Color(0.38f, 0.24f, 0.19f), 2.5f), new Vector3(0, 1.3f, 0), Vector3.one);
            Shapes.Part(root, "Neural folds", nerves, Shapes.Material("Glow", new Color(2.5f, 0.35f, 0.08f)), new Vector3(0, 1.3f, 0), Vector3.one);
        });
    }
}
