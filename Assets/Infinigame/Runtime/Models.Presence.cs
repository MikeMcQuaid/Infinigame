using UnityEngine;
using static Infinigame.MeshKit;

namespace Infinigame
{
    public static partial class Models
    {
        // Continuous shells first, overlapping carapace and muscle attachments second.
        static void Carapace(MeshKit shell, MeshKit veins, Vector3 centre, Vector3 size, int segments)
        {
            shell.Ball(centre, Vector3.Scale(size, new Vector3(0.82f, 0.85f, 0.96f)), Curves);
            for (int i = 0; i < segments; i++)
            {
                float t = i / (float)(segments - 1), z = Mathf.Lerp(-size.z * 0.8f, size.z * 0.8f, t);
                float width = Mathf.Sqrt(Mathf.Max(0.12f, 1 - Mathf.Pow(z / size.z, 2)));
                Vector3 at = centre + new Vector3(0, Mathf.Sin(t * Mathf.PI) * size.y * 0.18f, z);
                shell.Place(Move(at) * Turn(-12 + t * 24, 0, 0), () =>
                    shell.Ball(Vector3.zero, new Vector3(size.x * width, size.y * 0.95f, size.z * 1.55f / segments), Curves));
                for (int fold = -2; fold <= 2; fold++)
                {
                    var ridge = new Vector3[Curves];
                    for (int j = 0; j < ridge.Length; j++)
                    {
                        float a = 0.12f + j * (Mathf.PI - 0.24f) / (ridge.Length - 1);
                        ridge[j] = at + new Vector3(Mathf.Cos(a) * size.x * width, Mathf.Sin(a) * size.y * 0.97f,
                            size.z / segments * (fold * 0.17f + 0.12f * Mathf.Sin(a * 5 + i)));
                    }
                    shell.Tube(ridge, size.y * 0.035f, 10, size.y * 0.012f);
                }
                foreach (float side in new[] { -1f, 1f })
                    veins.Tube(Path(12, at + new Vector3(0, size.y * 0.82f, 0), at + new Vector3(side * size.x * width * 0.8f, size.y * 0.6f, 0.03f),
                        at + new Vector3(side * size.x * width, -size.y * 0.25f, 0)), size.y * 0.055f, 10);
            }
        }

        static (Mesh body, Mesh light) MobileBody(bool ship)
        {
            MeshKit shell = new MeshKit(), light = new MeshKit();
            Vector3 size = ship ? new Vector3(2.3f, 0.95f, 4.2f) : new Vector3(0.65f, 0.35f, 0.75f);
            Carapace(shell, light, Vector3.zero, size, ship ? 12 : 7);
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

        public static (Mesh body, Mesh light, Mesh leg) Sentry()
        {
            MeshKit body = new MeshKit(), light = new MeshKit(), leg = new MeshKit();
            Carapace(body, light, new Vector3(0, 0.12f, 0), new Vector3(0.65f, 0.35f, 0.8f), 7);
            for (int i = 0; i < 3; i++)
            {
                Vector3 eye = new Vector3((i - 1) * 0.28f, 0.58f, 0.55f - Mathf.Abs(i - 1) * 0.1f);
                body.Tube(Path(16, new Vector3((i - 1) * 0.3f, 0.2f, 0.2f), eye - Vector3.forward * 0.15f, eye), 0.07f, 16, 0.04f);
                body.Ball(eye, new Vector3(0.14f, 0.12f, 0.18f), Curves);
                light.Ball(eye + Vector3.forward * 0.14f, new Vector3(0.09f, 0.07f, 0.05f), Curves);
            }
            Vector3 hip = Vector3.zero, knee = new Vector3(0.45f, -0.2f, 0), foot = new Vector3(0.65f, -0.88f, 0.12f);
            leg.Tube(Path(14, hip, knee, foot), 0.13f, Curves, 0.035f);
            leg.Ball(knee, new Vector3(0.18f, 0.14f, 0.16f), Curves);
            for (int i = -1; i <= 1; i++) leg.Tube(Path(8, foot, foot + new Vector3(0.09f, -0.015f, i * 0.1f), foot + new Vector3(0.2f, -0.02f, i * 0.16f)), 0.04f, 12, 0.005f);
            leg.Tube(Path(14, hip + Vector3.up * 0.05f, knee + new Vector3(0, 0.08f, 0.08f), foot), 0.035f, 12, 0.012f);
            return (body.Mesh("Sentry carapace"), light.Mesh("Sentry senses"), leg.Mesh("Sentry limb"));
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
