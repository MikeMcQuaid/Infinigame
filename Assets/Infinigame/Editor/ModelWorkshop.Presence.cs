using UnityEngine;
using static Infinigame.Models;
using Infinigame.Core;
using static Infinigame.MeshKit;

namespace Infinigame
{
    public static partial class ModelWorkshop
    {
        // Continuous shells first, overlapping carapace and muscle attachments second.
        static void Carapace(MeshKit shell, MeshKit veins, Vector3 centre, Vector3 size, int segments, int facets)
        {
            shell.colour = new Color(0.38f, 0.3f, 0.29f);
            shell.Ball(centre, Vector3.Scale(size, new Vector3(0.82f, 0.85f, 0.96f)), facets);
            shell.colour = Color.white;
            for (int i = 0; i < segments; i++)
            {
                float t = i / (float)(segments - 1), z = Mathf.Lerp(-size.z * 0.8f, size.z * 0.8f, t);
                float width = Mathf.Sqrt(Mathf.Max(0.12f, 1 - Mathf.Pow(z / size.z, 2)));
                Vector3 at = centre + new Vector3(0, Mathf.Sin(t * Mathf.PI) * size.y * 0.18f, z);
                shell.colour = new Color(0.34f, 0.22f, 0.34f);
                shell.Place(Move(at) * Turn(-12 + t * 24, 0, 0), () =>
                    shell.Ball(Vector3.zero, new Vector3(size.x * width, size.y * 0.95f, size.z * 1.55f / segments), facets));
                foreach (float side in new[] { -1f, 1f })
                {
                    shell.colour = Color.Lerp(new Color(0.76f, 0.68f, 0.71f), new Color(0.99f, 0.92f, 0.78f), t);
                    Scute(shell, at + new Vector3(0, size.y * 0.88f, size.z * 0.29f),
                        at + new Vector3(side * size.x * width * 1.19f, size.y * 0.18f, -size.z * 0.45f),
                        new Vector3(-1, side * 0.36f, 0), size.x * 0.40f, size.y * 0.39f, Mathf.Clamp(facets / 3, 4, 10));
                }
                shell.colour = Color.white;
                foreach (float side in new[] { -1f, 1f })
                    veins.Tube(Path(12, at + new Vector3(0, size.y * 0.82f, 0), at + new Vector3(side * size.x * width * 0.8f, size.y * 0.6f, 0.03f),
                        at + new Vector3(side * size.x * width, -size.y * 0.25f, 0)), size.y * 0.022f, 10);
            }
        }

        static void SensoryCrown(MeshKit shell, MeshKit eyes, Vector3 basePoint, float spread, float height, int count, int sides)
        {
            var previous = shell.colour;
            for (int i = 0; i < count; i++)
            {
                float angle = (i + 0.5f) * 2 * Mathf.PI / count;
                Vector3 outwards = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
                Vector3 root = basePoint + outwards * spread * 0.24f;
                Vector3 tip = basePoint + outwards * spread + Vector3.up * (height * (0.90f + 0.1f * Mathf.Cos(angle + 1)));
                float radius = height * 0.108f;
                shell.colour = new Color(0.79f, 0.61f, 0.56f);
                shell.Tube(Path(sides / 2, root, root + Vector3.up * height * 0.46f, tip - outwards * spread * 0.22f - Vector3.up * radius, tip),
                    radius * 1.25f, sides, radius * 0.38f);
                // A scalloped hood protects each lens while leaving the iris visible.
                Vector3 tangent = Vector3.Cross(Vector3.up, outwards);
                for (int petal = -1; petal <= 1; petal++)
                {
                    Vector3 lip = tip - outwards * radius * 0.25f + tangent * (petal * radius * 0.76f);
                    Scute(shell, lip - Vector3.up * radius, lip + Vector3.up * radius * 1.39f + outwards * radius * 0.36f,
                        tangent, radius * 0.43f, radius * 0.23f, Mathf.Min(6, sides));
                }
                shell.colour = new Color(0.42f, 0.35f, 0.47f);
                shell.Ball(tip, new Vector3(radius, radius * 1.39f, radius), sides);
                Vector3 iris = tip + outwards * radius * 0.55f + Vector3.up * radius * 0.30f;
                eyes.Ball(iris, Vector3.one * radius * 0.76f, sides);
                shell.colour = new Color(0.10f, 0.10f, 0.18f);
                shell.Ball(iris + outwards * radius * 0.58f, Vector3.one * radius * 0.39f, sides);
            }
            shell.colour = previous;
        }

        static (Mesh body, Mesh light) MobileBody(bool ship)
        {
            MeshKit shell = new MeshKit(), light = new MeshKit();
            Vector3 size = ship ? new Vector3(1.43f, 0.91f, 4.0f) : new Vector3(0.36f, 0.23f, 0.82f);
            int facets = Mathf.Min(Curves, 32);
            Carapace(shell, light, Vector3.zero, size, ship ? 9 : 6, facets);
            SensoryCrown(shell, light, new Vector3(0, size.y * 0.76f, size.z * (ship ? -0.10f : 0.18f)),
                size.x * 0.55f, size.y * (ship ? 1.25f : 1.85f), ship ? 5 : 3, facets / 2);
            foreach (float side in new[] { -1f, 1f })
            {
                foreach (float end in new[] { -1f, 1f })
                {
                    Vector3 root = new Vector3(side * size.x * 0.54f, 0, end * size.z * 0.17f);
                    Vector3 tip = new Vector3(side * size.x * (ship ? (end < 0 ? 2.15f : 1.72f) : 3.65f),
                        size.y * (end < 0 ? -0.28f : 0.18f), end * size.z * (ship ? 0.84f : 1.12f));
                    Vector3 mid = Vector3.Lerp(root, tip, 0.52f);
                    Vector3 edge = Vector3.Cross(Vector3.up, tip - root).normalized;
                    Vector3 outer = mid + edge * (side * end * size.x * 0.54f);
                    Vector3 inner = mid - edge * (side * end * size.x * 0.16f);
                    shell.colour = new Color(0.88f, 0.75f, 0.66f);
                    var arch = Path(8, root, outer + Vector3.up * size.y * 0.38f, tip);
                    shell.Tube(arch, size.y * (ship ? 0.49f : 0.32f), facets, size.y * 0.040f);
                    shell.colour = new Color(0.59f, 0.41f, 0.46f);
                    shell.Tube(Path(8, root - Vector3.up * size.y * 0.33f, inner - Vector3.up * size.y * 0.39f, tip),
                        size.y * 0.29f, facets / 2, size.y * 0.038f);
                    // Curved costae bridge the two spars around open breathing chambers.
                    for (int rib = 1; rib <= (ship ? 6 : 4); rib++)
                    {
                        float along = rib / (ship ? 7f : 5f);
                        Vector3 station = Vector3.Lerp(root, tip, along);
                        float archWidth = Mathf.Sin(along * Mathf.PI) * size.x;
                        shell.colour = Color.Lerp(new Color(0.51f, 0.35f, 0.41f), new Color(0.95f, 0.82f, 0.63f), along);
                        shell.Tube(Path(6, station + edge * (side * end * archWidth * 0.30f) + Vector3.up * size.y * 0.20f,
                            station + Vector3.up * size.y * 0.12f - Vector3.forward * size.z * 0.045f,
                            station - edge * (side * end * archWidth * 0.16f) - Vector3.up * size.y * 0.33f),
                            size.y * 0.065f, 8, size.y * 0.037f);
                    }
                    int cells = ship ? 4 : 2;
                    for (int i = 0; i < cells; i++)
                    {
                        float t = 0.27f + 0.46f * i / cells;
                        Vector3 socket = Vector3.Lerp(root, tip, t) - Vector3.up * size.y * 0.17f;
                        shell.Ball(socket, Vector3.one * (size.y * (ship ? 0.25f : 0.18f)), facets / 2);
                    }
                    shell.colour = new Color(0.85f, 0.77f, 0.70f);
                    Scute(shell, root, tip + (tip - root) * (ship ? 0.12f : 0.28f), edge, size.y * (ship ? 0.52f : 0.54f), size.y * 0.19f);
                }
                if (ship)
                {
                    var chamber = new Vector3(side * 1.14f, -0.30f, -1.92f);
                    Carapace(shell, light, chamber, new Vector3(0.61f, 0.55f, 1.95f), 6, facets);
                    Gills(shell, chamber + Vector3.right * side * 0.57f, side, 1.65f);
                }
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

        public static (Mesh body, Mesh light, Mesh leg) Sentry(MotileCaste caste = MotileCaste.Stalker, int detail = 0)
        {
            int facets = detail == 0 ? Curves : detail == 1 ? 16 : 8;
            int small = Mathf.Min(facets, 24), plate = Mathf.Clamp(facets / 3, 4, 10);
            MeshKit body = new MeshKit(), light = new MeshKit(), leg = new MeshKit();
            bool breacher = caste == MotileCaste.Breacher, spitter = caste == MotileCaste.Spitter;
            Carapace(body, light, new Vector3(0, 0.12f, 0), breacher ? new Vector3(0.88f, 0.5f, 0.85f)
                : spitter ? new Vector3(0.48f, 0.45f, 0.85f) : new Vector3(0.57f, 0.3f, 0.8f), 9, facets);
            SensoryCrown(body, light, new Vector3(0, spitter ? 0.52f : breacher ? 0.49f : 0.31f, spitter ? -0.34f : 0.42f),
                spitter ? 0.56f : breacher ? 0.52f : 0.41f, spitter ? 0.98f : breacher ? 0.48f : 0.77f,
                spitter ? 2 : breacher ? 4 : 3, small);
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 cheek = new Vector3(side * (breacher ? 0.56f : 0.31f), 0.10f, 0.83f);
                // Radial folds face forwards so the membrane and its scalloped rim read head-on.
                Vector3 Fan(float spoke, float radius)
                {
                    float angle = (spoke * 158 - 74) * Mathf.Deg2Rad;
                    float rib = Mathf.Cos(spoke * 20 * Mathf.PI);
                    return cheek + new Vector3(side * (0.12f + ((breacher ? 0.24f : spitter ? 0.30f : 0.43f) + 0.016f * rib) * Mathf.Cos(angle)) * radius,
                        Mathf.Sin(angle) * (breacher ? 0.27f : spitter ? 0.55f : 0.44f) * radius, radius * (-0.19f + rib * 0.026f) + 0.13f * Mathf.Sin(radius * Mathf.PI));
                }
                int spokes = detail == 2 ? 20 : 40;
                for (int j = 0; j < spokes; j++)
                {
                    float a = j / (float)spokes, b = (j + 1f) / spokes;
                    body.colour = Color.Lerp(new Color(0.37f, 0.19f, 0.34f), new Color(0.77f, 0.53f, 0.57f), 0.5f + 0.5f * Mathf.Cos((a + b) * 10 * Mathf.PI));
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
                body.Tube(Path(small / 2, elbow, elbow + new Vector3(-side * 0.14f, 0.02f, 0.18f),
                    claw + new Vector3(side * 0.04f, 0.12f, 0.15f)), breacher ? 0.11f : 0.065f, small, 0.008f);
                // Pleated mouth tissue stretches between the cheek and the hooked mandible.
                for (int fold = 0; fold < (detail == 2 ? 2 : 5); fold++)
                {
                    float t = fold / (detail == 2 ? 2f : 5f);
                    body.colour = Color.Lerp(new Color(0.30f, 0.13f, 0.23f), new Color(0.80f, 0.49f, 0.45f), t);
                    Vector3 lip = Vector3.Lerp(jaw, elbow, t);
                    body.Tube(Path(5, cheek - Vector3.up * 0.10f, lip + Vector3.down * (0.16f - t * 0.08f),
                        lip + Vector3.forward * 0.04f), 0.034f, 7, 0.012f);
                }
                body.colour = new Color(0.98f, 0.78f, 0.49f);
                for (int tooth = 0; tooth < 5; tooth++)
                {
                    Vector3 at = Vector3.Lerp(elbow, claw, tooth / 5f);
                    body.Tube(Path(8, at, at + new Vector3(-side * 0.08f, 0.03f, 0.05f), at + new Vector3(-side * 0.14f, 0, 0.08f)), 0.035f, 12, 0.004f);
                }
            }
            body.colour = new Color(0.56f, 0.43f, 0.48f);
            if (breacher)
            {
                for (int i = -2; i <= 2; i++)
                {
                    float a = i * 0.39f;
                    body.colour = Color.Lerp(new Color(0.63f, 0.55f, 0.58f), new Color(0.98f, 0.87f, 0.65f), 1 - Mathf.Abs(i) / 3f);
                    Vector3 from = new Vector3(Mathf.Sin(a) * 0.63f, 0.42f, 0.28f), tip = new Vector3(Mathf.Sin(a) * 1.28f, 0.77f - Mathf.Abs(i) * 0.08f, 1.15f);
                    Scute(body, from, tip, Vector3.right, 0.29f, 0.22f, plate);
                }
                for (int i = 0; i < 4; i++)
                    body.Ball(new Vector3(0, 0.58f, -0.65f + i * 0.22f), new Vector3(0.27f, 0.18f, 0.18f), facets);
            }
            if (spitter)
            {
                foreach (float side in new[] { -1f, 1f })
                {
                    Carapace(body, light, new Vector3(side * 0.32f, 0.6f, -0.42f), new Vector3(0.32f, 0.48f, 0.5f), 6, facets);
                    var neck = Path(28, new Vector3(side * 0.25f, 0.55f, -0.4f), new Vector3(side * 0.3f, 1.15f, 0.2f), new Vector3(side * 0.2f, 0.95f, 0.9f));
                    body.Tube(neck, 0.13f, facets, 0.18f);
                    body.Place(Move(neck[neck.Length - 1]) * Turn(90, 0, 0), () => body.Lathe(new[] { 0.12f, -0.06f, 0.19f, -0.06f, 0.2f, 0, 0.14f, 0.04f, 0.12f, 0.04f }, facets));
                    light.Ball(neck[neck.Length - 1], new Vector3(0.11f, 0.11f, 0.035f), facets);
                }
            }
            Vector3 hip = Vector3.zero, knee = new Vector3(0.51f, 0.08f, -0.14f), ankle = new Vector3(0.75f, -0.55f, 0), foot = new Vector3(0.83f, -0.90f, 0.15f);
            leg.colour = new Color(0.43f, 0.28f, 0.39f);
            leg.Tube(Path(16, hip, knee + Vector3.up * 0.08f, ankle), breacher ? 0.17f : 0.12f, 24, 0.048f);
            leg.colour = new Color(0.73f, 0.58f, 0.60f);
            leg.Ball(knee, new Vector3(0.15f, 0.12f, 0.13f), small);
            Scute(leg, hip, knee + new Vector3(0.11f, 0.07f, -0.08f), Vector3.back, 0.16f, 0.10f, plate);
            Scute(leg, knee + Vector3.up * 0.08f, ankle, Vector3.forward, 0.135f, 0.09f, plate);
            leg.colour = new Color(1, 0.88f, 0.66f);
            leg.Tube(Path(8, ankle, Vector3.Lerp(ankle, foot, 0.5f), foot + Vector3.up * 0.06f), 0.078f, small, 0.052f);
            foreach (float toe in new[] { -1f, 1f })
                leg.Tube(Path(3, foot + Vector3.up * 0.09f, foot + new Vector3(toe * 0.045f, 0.04f, 0.04f), foot + new Vector3(toe * 0.063f, 0, 0.11f)),
                    breacher ? 0.061f : 0.041f, small, 0.012f);
            return (body.Mesh(caste + " carapace"), light.Mesh(caste + " senses"), leg.Mesh(caste + " limb"));
        }

        public static (Mesh body, Mesh light) Mind()
        {
            MeshKit body = new MeshKit(), light = new MeshKit();
            // Corrugated neural lobes are skinned continuously, then crossed by branching folds.
            for (int i = 0; i < 7; i++)
            {
                float a = i * 2.399963f;
                Vector3 lobe = new Vector3(Mathf.Cos(a) * 0.64f, 0.16f + i * 0.084f, Mathf.Sin(a) * 0.58f);
                body.colour = Color.Lerp(new Color(0.67f, 0.47f, 0.53f), new Color(0.91f, 0.75f, 0.66f), i / 6f);
                Vector3 Surface(float u, float v)
                {
                    float angle = u * Mathf.PI * 2, latitude = (v - 0.5f) * Mathf.PI;
                    float wide = Mathf.Cos(latitude), ripple = 1 + 0.08f * Mathf.Sin(angle * 11 + Mathf.Sin(latitude * 7)) * wide;
                    return lobe + new Vector3(Mathf.Cos(angle) * wide * 0.92f, Mathf.Sin(latitude) * 0.69f, Mathf.Sin(angle) * wide * 0.80f) * ripple;
                }
                int sides = Curves * 2;
                var rings = new Vector3[sides / 2 + 1][];
                for (int j = 0; j < rings.Length; j++)
                {
                    rings[j] = new Vector3[sides];
                    for (int k = 0; k < sides; k++) rings[j][k] = Surface(1f - k / (float)sides, j / (float)(rings.Length - 1));
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
                body.Tube(stem, 0.36f, Curves, 0.025f);
                for (int branch = 0; branch < 3; branch++)
                {
                    Vector3 at = stem[12 + branch * 5], tangent = new Vector3(-root.z, 0, root.x) * (branch % 2 == 0 ? 1 : -1);
                    body.Tube(Path(16, at, at + tangent * 0.45f + Vector3.down * 0.2f, root * (2 + branch * 0.2f) + tangent * 0.8f + Vector3.down * 1.35f), 0.065f, 14, 0.008f);
                }
            }
            SensoryCrown(body, light, new Vector3(0, 0.72f, 0), 1.18f, 1.08f, 7, Mathf.Min(Curves, 24));
            return (body.Mesh("Rooted mind"), light.Mesh("Neural folds"));
        }

        // A floor root spreads from the neural folds, then branches up the inner face of the wall.
        public static Mesh BurrowRoot()
        {
            var tissue = new MeshKit { tiling = 1.4f };
            var trunk = Path(22, new Vector3(0, 0.09f, 0), new Vector3(0.22f, 0.04f, 1.3f),
                new Vector3(-0.30f, 0.06f, 3.3f), new Vector3(0.08f, 0.22f, 4));
            tissue.Tube(trunk, 0.23f, Curves, 0.09f);
            for (int i = 0; i < 5; i++)
            {
                float side = i % 2 == 0 ? -1 : 1;
                Vector3 fork = trunk[4 + i * 3];
                tissue.Tube(Path(12, fork, fork + new Vector3(side * 0.37f, -0.02f, 0.24f),
                    fork + new Vector3(side * 0.48f, 0.03f, 0.51f), fork + new Vector3(side * 0.84f, 0.02f, 0.82f)), 0.074f, 8, 0.012f);
            }
            for (int i = -1; i <= 1; i++)
                tissue.Tube(Path(16, trunk[21], new Vector3(i * 0.18f, 0.45f, 4.1f), new Vector3(i * 0.31f, 0.92f, 4),
                    new Vector3(i * 0.64f, 1.5f - 0.21f * Mathf.Abs(i), 4.24f)), 0.09f, 10, 0.014f);
            return tissue.Mesh("Branching neural root");
        }
        public static Transform Nest() => Machine("Presence nest", root =>
        {
            var (body, nerves) = Mind();
            Shapes.Part(root, "Root network", body, Shapes.BroodTissue(), new Vector3(0, 1.3f, 0), Vector3.one);
            Shapes.Part(root, "Neural folds", nerves, Shapes.PresenceOptics(1.2f), new Vector3(0, 1.3f, 0), Vector3.one);
        });

        // The flat inner face is the ceiling of the flight passage. All growth is above it.
        public static Transform CitadelRoof(bool crown) => Machine(crown ? "Citadel sensory chamber" : "Citadel vault", root =>
        {
            MeshKit shell = new MeshKit(), pores = new MeshKit(), chambers = new MeshKit();
            shell.colour = new Color(0.59f, 0.50f, 0.51f);
            shell.Box(Vector3.zero, new Vector3(58, 2, 58), 0.85f);
            shell.Lathe(new[] { 25f, 0.8f, 23f, 2.1f, 15f, 3.2f, 8f, 4.4f, 0f, 5f }, 40,
                (angle, ring) => 1 + 0.15f * Mathf.Cos(4 * angle));
            Carapace(shell, pores, Vector3.up * 5, new Vector3(10, crown ? 5 : 3, 13), 7, 20);
            for (int i = 0; i < 4; i++)
            {
                Vector3 axis = Quaternion.Euler(0, 45 + i * 90, 0) * Vector3.forward;
                Vector3 across = Vector3.Cross(Vector3.up, axis);
                shell.colour = new Color(0.93f, 0.82f, 0.69f);
                Scute(shell, axis * 3 + Vector3.up * 7.4f, axis * 35 + Vector3.up * 2, across, 6.7f, 2.7f);
                foreach (float side in new[] { -1f, 1f })
                {
                    Vector3 mid = axis * 18 + across * side * 7;
                    shell.colour = new Color(0.89f, 0.75f, 0.65f);
                    shell.Tube(Path(6, across * side * 4 + Vector3.up * 5, mid + Vector3.up * 7.7f, axis * 34 + Vector3.up), 3.1f, 14, 0.8f);
                    for (int bay = 0; bay < 4; bay++)
                    {
                        Vector3 jamb = axis * (9 + bay * 4) + across * side * (6 + Mathf.Sin(bay) * 1.7f);
                        shell.Tube(Path(4, jamb - axis * 2 + Vector3.up * 7, jamb + across * side * 2 + Vector3.up * 5,
                            jamb + across * side * 3 + axis * 2 + Vector3.up), 1.15f, 10, 0.65f);
                        chambers.Ball(jamb + Vector3.up * 3.7f, new Vector3(1.15f, 1.60f, 1.15f), 10);
                    }
                }
            }
            if (crown) SensoryCrown(shell, pores, Vector3.up * 9, 8.5f, 14, 5, 14);
            Add(root, "Vault and ribs", shell, Shapes.PresenceShell(48));
            Add(root, "Sensory pores", pores, Shapes.PresenceOptics(48), false);
            Add(root, "Incubation chambers", chambers, Shapes.Carapace(Color.black, 48, new Color(0.53f, 0.28f, 0.12f)), false);
        });
    }
}
