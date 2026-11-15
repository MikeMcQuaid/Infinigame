using UnityEngine;

namespace Infinigame
{
    public static partial class Models
    {
        static Texture2D machined, machinedNormal;
        static Texture2D Machined
        {
            get
            {
                if (machined != null) return machined;
                const int size = 512;
                var pixels = new Color32[size * size];
                var random = new Unity.Mathematics.Random(5729);
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float grain = Mathf.PerlinNoise(x * 0.09f, y * 0.7f);
                    float wear = Mathf.PerlinNoise(x * 0.035f, y * 0.035f);
                    float scratch = y % 71 == 0 && x % 117 < 28 ? 0.13f : 0;
                    byte value = (byte)Mathf.Clamp(214 + grain * 22 + wear * 14 + random.NextFloat(-3, 3) + scratch * 255, 0, 255);
                    pixels[y * size + x] = new Color32(value, value, value, (byte)(130 + wear * 80));
                }
                machined = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Generated machined finish", wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
                var normals = new Color[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (pixels[y * size + (x + 1) % size].r - pixels[y * size + (x + size - 1) % size].r) / 255f;
                    float dy = (pixels[((y + 1) % size) * size + x].r - pixels[((y + size - 1) % size) * size + x].r) / 255f;
                    Vector3 normal = new Vector3(-dx * 2, -dy * 2, 1).normalized;
                    normals[y * size + x] = new Color(normal.x * 0.5f + 0.5f, normal.y * 0.5f + 0.5f, normal.z * 0.5f + 0.5f, 1);
                }
                machinedNormal = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { name = "Generated metal relief", wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
                machinedNormal.SetPixels(normals);
                machinedNormal.Apply(true, true);
                machined.SetPixels32(pixels);
                machined.Apply(true, true);
                return machined;
            }
        }

        public static Transform RoomPanel(Vector3 half, bool door)
        {
            var root = new GameObject(door ? "Door" : "Room").transform;
            var shell = new MeshKit();
            shell.Box(Vector3.zero, half * 2, Mathf.Min(0.2f, Mathf.Min(half.x, Mathf.Min(half.y, half.z)) * 0.3f));
            var ribs = new MeshKit();
            var panels = new MeshKit();
            bool along = half.x < half.z;
            float width = along ? half.z : half.x;
            if (half.y > 1)
                for (float at = -width + 1; at < width; at += 2.5f)
                {
                    Vector3 Point(float y, float bulge) => along ? new Vector3(half.x + bulge, y, at) : new Vector3(at, y, half.z + bulge);
                    foreach (float side in new[] { -1f, 1f })
                    {
                        Vector3 Flip(Vector3 p) => along ? new Vector3(p.x * side, p.y, p.z) : new Vector3(p.x, p.y, p.z * side);
                        Vector3 atPlate = Flip(Point(0, 0.035f));
                        Vector3 size = along ? new Vector3(0.12f, half.y * 1.65f, 2.05f) : new Vector3(2.05f, half.y * 1.65f, 0.12f);
                        panels.Box(atPlate, size, 0.055f);
                        for (int vent = 0; vent < 5; vent++)
                        {
                            Vector3 atVent = Flip(Point(-half.y * 0.55f + vent * 0.09f, 0.11f));
                            ribs.Box(atVent, along ? new Vector3(0.05f, 0.035f, 0.85f) : new Vector3(0.85f, 0.035f, 0.05f), 0.015f);
                        }
                        ribs.Tube(MeshKit.Path(20, Flip(Point(-half.y, 0)), Flip(Point(-half.y * 0.35f, 0.22f)), Flip(Point(half.y * 0.5f, 0.12f)), Flip(Point(half.y, 0))), 0.11f, 16, 0.045f);
                    }
                }
            Add(root, "Structural shell", shell, Paint(new Color(0.32f, 0.29f, 0.26f), 0.2f, 0.45f));
            Add(root, "Inset service panels", panels, Paint(new Color(0.38f, 0.4f, 0.4f), 0.55f, 0.32f));
            Add(root, "Grown ribs", ribs, Paint(new Color(0.4f, 0.24f, 0.16f), 0.05f, 0.55f));
            return root;
        }
    }
}
