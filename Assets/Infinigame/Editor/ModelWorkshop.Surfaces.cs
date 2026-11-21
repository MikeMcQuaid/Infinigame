using UnityEngine;
using static Infinigame.Models;

namespace Infinigame
{
    public static partial class ModelWorkshop
    {
        public static Mesh[] SpineLauncher()
        {
            var baseplate = new MeshKit(); var rack = new MeshKit(); var spines = new MeshKit();
            baseplate.Lathe(new[] { 0.08f, -0.08f, 0.94f, -0.05f, 1.03f, 0.18f, 0.84f, 0.35f, 0.57f, 0.43f, 0.46f, 0.83f }, 24, (angle, ring) => 1 + 0.07f * Mathf.Cos(angle * 6));
            for (int i = 0; i < 4; i++)
            {
                var rotation = Quaternion.Euler(0, 45 + i * 90, 0);
                baseplate.Place(Matrix4x4.Rotate(rotation), () =>
                {
                    baseplate.Tube(new[] { new Vector3(0, 0.37f, 0.5f), new Vector3(0, 0.47f, 1.07f), new Vector3(0, 0.07f, 1.48f) }, 0.25f, 8, 0.16f);
                    baseplate.Box(new Vector3(0, 0.04f, 1.46f), new Vector3(0.44f, 0.18f, 0.65f), 0.07f);
                    baseplate.Spike(new Vector3(0, 0.53f, 0.97f), new Vector3(0, 0.23f, 1.6f), 0.15f, 7);
                });
            }
            for (int side = -1; side <= 1; side += 2)
            {
                baseplate.Tube(new[] { Vector3.up * 0.35f, new Vector3(side * 0.67f, 0.53f, 0), new Vector3(side * 0.79f, 0.94f, 0) }, 0.2f, 12, 0.15f);
                rack.Rod(new Vector3(side * 0.54f, 0, 0), new Vector3(side * 0.87f, 0, 0), 0.29f, 16, 0.23f);
            }
            rack.Box(Vector3.back * 0.1f, new Vector3(1.54f, 0.37f, 1.62f), 0.14f);
            for (int i = -1; i <= 1; i++)
            {
                Vector3 offset = new Vector3(i * 0.59f, i == 0 ? 0 : 0.3f, 0);
                rack.Rod(offset + Vector3.back * 1.23f, offset + Vector3.forward * 0.69f, 0.23f, 16, 0.29f);
                rack.Tube(new[] { offset + Vector3.forward * 0.63f, offset + Vector3.forward * 1.36f }, 0.3f, 16, 0.32f, false);
                rack.Tube(new[] { offset + Vector3.forward * 1.36f, offset + Vector3.forward * 0.78f }, 0.26f, 16, 0.20f, false);
                for (int band = 0; band < 4; band++)
                {
                    var at = offset + Vector3.forward * (-0.8f + 0.71f * band);
                    rack.Place(Matrix4x4.TRS(at, Quaternion.Euler(90, 0, 0), Vector3.one), () => rack.Torus(0.3f, 0.044f, 16, 7));
                }
                rack.Spike(offset + Vector3.forward * 0.77f, offset + Vector3.forward * 1.5f, 0.15f, 9);
                spines.Rod(offset + Vector3.up * 0.26f + Vector3.back * 0.87f, offset + Vector3.up * 0.287f + Vector3.forward * 0.59f, 0.035f, 7);
                spines.Ball(offset + Vector3.forward * 1.44f, 0.042f, 9);
            }
            return new[] { baseplate.Mesh("Rooted spine bed"), rack.Mesh("Tracking spine rack"), spines.Mesh("Charged spines") };
        }

        public static Transform CitadelCarapace()
        {
            var root = new GameObject("Citadel carapace").transform;
            var skin = new MeshKit();
            var ribs = new MeshKit();
            const int rings = 38, sides = 64;
            Vector3 Hide(int ring, int side)
            {
                float t = ring / (float)rings, angle = side * Mathf.PI * 2 / sides;
                var radii = Infinigame.Core.Interior.MantleRadii(t, angle);
                return new Vector3(132 * Mathf.SmoothStep(0, 1, t * 3) + Mathf.Cos(angle) * radii.x,
                    Mathf.Sin(angle) * radii.y, -329 + 1225 * t);
            }
            for (int r = 0; r < rings; r++)
                for (int i = 0; i < sides; i++) skin.Quad(Hide(r,i), Hide(r,i+1), Hide(r+1,i+1), Hide(r+1,i));
            for (int i = 0; i < sides; i += 4)
            {
                var ridge = new Vector3[rings + 1];
                for (int r = 0; r <= rings; r++) ridge[r] = Hide(r, i);
                ribs.Tube(ridge, 4.2f, 9, 1.8f);
                for (int r = 5; r < rings - 3; r += 5)
                {
                    Vector3 spine = Hide(r, i);
                    Vector3 outward = new Vector3(Mathf.Cos(i * Mathf.PI * 2 / sides), Mathf.Sin(i * Mathf.PI * 2 / sides), 0.55f);
                    ribs.Rod(spine, spine + outward * (19 + 13 * Mathf.Sin(i + r)), 5.5f, 8, 0.4f);
                }
            }
            Add(root, "Fluted mantle", skin, Shapes.Solid(Surface.Chitin, new Color(0.29f, 0.14f, 0.22f), true));
            Add(root, "Dorsal ridges", ribs, Shapes.Solid(Surface.Chitin, new Color(0.45f, 0.24f, 0.29f), true));
            return root;
        }

        public static Transform RoomPanel(Vector3 half, bool door)
        {
            var root = new GameObject(door ? "Membrane" : "Living chamber").transform;
            var shell = new MeshKit();
            shell.Box(Vector3.zero, half * 2, Mathf.Min(1f, Mathf.Min(half.x, Mathf.Min(half.y, half.z)) * 0.3f));
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
                        ribs.Tube(MeshKit.Path(20, Flip(Point(-half.y, 0)), Flip(Point(-half.y * 0.35f, 0.22f)), Flip(Point(half.y * 0.5f, 0.12f)), Flip(Point(half.y, 0))), 0.65f, 16, 0.2f);
                    }
                }
            Add(root, "Living membrane", shell, Shapes.Solid(Surface.Chitin, new Color(0.28f, 0.08f, 0.23f), true));
            Add(root, "Chitin scales", panels, Shapes.Solid(Surface.Chitin, new Color(0.38f, 0.12f, 0.3f), true));
            Add(root, "Grown ribs", ribs, Shapes.Solid(Surface.Chitin, new Color(0.45f, 0.055f, 0.28f), true));
            return root;
        }
    }
}
