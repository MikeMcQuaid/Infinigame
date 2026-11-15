using System;
using System.Collections.Generic;
using UnityEngine;
using static Infinigame.MeshKit;

namespace Infinigame
{
    public static partial class Models
    {
        static readonly Dictionary<string, Transform> masters = new Dictionary<string, Transform>();
        static Material alloy, mechanism, rubber, optics, ceramic;
        static int Curves => LodSettings.Tier == 0 ? 28 : 48;
        static Material Alloy => alloy != null ? alloy : alloy = Paint(new Color(0.62f, 0.68f, 0.72f), 0.72f, 0.43f);
        static Material Mechanism => mechanism != null ? mechanism : mechanism = Paint(new Color(0.14f, 0.18f, 0.2f), 0.8f, 0.38f);
        static Material Rubber => rubber != null ? rubber : rubber = Paint(new Color(0.035f, 0.045f, 0.05f), 0.05f, 0.25f, Surface.Seal);
        static Material Optics => optics != null ? optics : optics = Shapes.Material("Glow", new Color(0.15f, 2.4f, 3.5f));
        static Material Ceramic => ceramic != null ? ceramic : ceramic = Paint(new Color(0.22f, 0.32f, 0.38f), 0.15f, 0.36f, Surface.Ceramic);

        static Transform Machine(string name, Action<Transform> build)
        {
            string key = name + Curves;
            if (!masters.TryGetValue(key, out var master) || master == null)
            {
                master = new GameObject(name).transform;
                build(master);
                master.gameObject.SetActive(false);
                master.gameObject.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(master.gameObject);
                masters[key] = master;
            }
            var copy = UnityEngine.Object.Instantiate(master);
            copy.name = name;
            copy.gameObject.hideFlags = HideFlags.None;
            copy.gameObject.SetActive(true);
            return copy;
        }

        // Flat faces and narrow rolled edges read as armour rather than inflated padding.
        static void Plate(MeshKit mesh, Vector3 centre, Vector3 size, int sides)
        {
            float[] widths = { 0, 0.88f, 0.97f, 1, 1, 0.97f, 0.88f, 0 }, heights = { -1, -1, -0.97f, -0.8f, 0.8f, 0.97f, 1, 1 };
            var rings = new Vector3[widths.Length][];
            for (int i = 0; i < rings.Length; i++)
                rings[i] = Array.ConvertAll(Oval(sides, size.z * 0.5f * widths[i], size.x * 0.5f * widths[i], 6), p => centre + new Vector3(p.y, heights[i] * size.y * 0.5f, p.x));
            mesh.Skin(rings);
        }

        static void Bearing(MeshKit frame, Vector3 at, float size, bool sideways = true)
        {
            frame.Place(Move(at) * Turn(sideways ? 0 : 90, 0, sideways ? 90 : 0), () =>
            {
                frame.Lathe(new[] { 0f, -size * 0.35f, size * 0.8f, -size * 0.35f, size, -size * 0.2f, size, size * 0.2f, size * 0.8f, size * 0.35f, 0f, size * 0.35f }, Curves);
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.PI / 4;
                    frame.Rod(new Vector3(Mathf.Cos(a) * size * 0.7f, -size * 0.38f, Mathf.Sin(a) * size * 0.7f),
                        new Vector3(Mathf.Cos(a) * size * 0.7f, size * 0.38f, Mathf.Sin(a) * size * 0.7f), size * 0.07f, 8);
                }
            });
        }

        static void Piston(MeshKit frame, MeshKit shell, Vector3 a, Vector3 b, float radius)
        {
            Vector3 mid = Vector3.Lerp(a, b, 0.55f);
            frame.Rod(a, b, radius * 0.55f, Curves);
            shell.Rod(a, mid, radius, Curves, radius * 0.86f);
            frame.Ball(a, radius * 1.2f, Curves);
            frame.Ball(b, radius * 0.85f, Curves);
        }

        static void CladLimb(Transform root, float length, float width, bool foot = false)
        {
            MeshKit shell = new MeshKit(), frame = new MeshKit(), soft = new MeshKit();
            Bearing(frame, Vector3.zero, width * 0.62f);
            soft.Rod(new Vector3(0, -0.02f, 0), new Vector3(0, -length, 0), width * 0.46f, Curves, width * 0.32f);
            Vector3 Cuff(float u, float v)
            {
                float a = u * Mathf.PI * 2, swell = 0.76f + 0.24f * Mathf.Sin(v * Mathf.PI);
                return new Vector3(Mathf.Cos(a) * width * 0.75f * swell, -length * v,
                    Mathf.Sin(a) * width * 0.58f * swell + 0.02f * Mathf.Sin(v * Mathf.PI));
            }
            Panels(Cuff, new[] { 0f, 0.24f, 0.5f, 0.75f, 1f }, new[] { 0.13f, 0.6f, 0.91f },
                0.012f, 0.009f, 0.018f, (i, j) => i == 2 ? null : shell, frame);
            for (int i = 0; i < 4; i++)
            {
                float y = -length * (0.3f + i * 0.065f);
                frame.Box(new Vector3(0, y, width * 0.58f + 0.02f), new Vector3(width * 0.58f, 0.012f, 0.018f), 0.004f);
            }
            foreach (float side in new[] { -1f, 1f })
            {
                Piston(frame, shell, new Vector3(side * width * 0.48f, -0.05f, -width * 0.35f), new Vector3(side * width * 0.33f, -length * 0.91f, -width * 0.35f), width * 0.1f);
                soft.Tube(Path(10, new Vector3(side * width * 0.55f, 0, -0.03f), new Vector3(side * width * 0.66f, -length * 0.5f, -0.04f), new Vector3(side * width * 0.4f, -length, 0)), width * 0.05f, 12);
            }
            if (foot)
            {
                shell.Ball(new Vector3(0, -length + 0.05f, 0.025f), new Vector3(width * 0.77f, 0.09f, 0.13f), Curves);
                for (int toe = -1; toe <= 1; toe++)
                    shell.Place(Move(toe * width * 0.48f, -length + 0.04f, 0.16f) * Turn(-12, toe * -8, 0), () =>
                        Pillow(shell, Vector3.zero, new Vector3(width * 0.43f, 0.11f, 0.21f), Curves));
                for (int i = 0; i < 5; i++) soft.Box(new Vector3(0, -length - 0.025f, -0.05f + i * 0.065f), new Vector3(width * 1.45f, 0.035f, 0.025f), 0.008f);
            }
            Add(root, "Armour", shell, Alloy);
            Add(root, "Bearings and actuators", frame, Mechanism);
            Add(root, "Pressure seals", soft, Rubber);
        }

        public static Transform Gauntlet() => Machine("Gauntlet", root =>
        {
            MeshKit shell = new MeshKit(), frame = new MeshKit(), seal = new MeshKit(), glow = new MeshKit();
            Plate(shell, new Vector3(0, 0, -0.12f), new Vector3(0.19f, 0.16f, 0.29f), Curves);
            Plate(shell, new Vector3(0, 0, 0.07f), new Vector3(0.19f, 0.12f, 0.17f), Curves);
            Bearing(frame, new Vector3(0, 0, -0.015f), 0.075f);
            for (int finger = 0; finger < 4; finger++)
            {
                float x = -0.0675f + finger * 0.045f;
                for (int joint = 0; joint < 3; joint++)
                {
                    Vector3 at = new Vector3(x, -joint * 0.022f, 0.15f + joint * 0.035f);
                    Plate(shell, at, new Vector3(0.039f, 0.055f, 0.053f), Curves / 2);
                    frame.Rod(at + Vector3.left * 0.024f, at + Vector3.right * 0.024f, 0.011f, 12);
                }
                seal.Tube(Path(12, new Vector3(x, 0.062f, -0.25f), new Vector3(x, 0.083f, -0.05f), new Vector3(x, 0.07f, 0.11f)), 0.009f, 12);
                for (int rib = 0; rib < 7; rib++) frame.Box(new Vector3(x, 0.081f, -0.22f + rib * 0.023f), new Vector3(0.019f, 0.013f, 0.007f), 0.003f);
            }
            Plate(shell, new Vector3(-0.11f, -0.025f, 0.065f), new Vector3(0.07f, 0.075f, 0.13f), Curves);
            shell.Place(Move(0, 0.091f, -0.12f), () => shell.Lathe(new[] { 0.035f, 0, 0.035f, 0.018f, 0.027f, 0.025f, 0.018f, 0.025f, 0.018f, 0 }, Curves));
            glow.Ball(new Vector3(0, 0.108f, -0.12f), new Vector3(0.018f, 0.007f, 0.018f), Curves);
            for (int i = 0; i < 3; i++) glow.Box(new Vector3(0.102f, 0.025f, -0.14f + 0.04f * i), new Vector3(0.005f, 0.018f, 0.022f), 0.002f);
            Add(root, "Armour", shell, Alloy);
            Add(root, "Finger joints and heat sinks", frame, Mechanism);
            Add(root, "Tendons", seal, Rubber);
            Add(root, "Emitter", glow, Optics, false);
        });

        public static Transform Traveller() => Machine("Traveller", root =>
        {
            var torso = Child(root, "Torso"); torso.localPosition = new Vector3(0, 1.36f, 0);
            MeshKit shell = new MeshKit(), frame = new MeshKit(), seal = new MeshKit(), glow = new MeshKit();
            seal.Ball(new Vector3(0, -0.08f, 0), new Vector3(0.25f, 0.39f, 0.16f), Curves);
            Vector3 Breastplate(float u, float v)
            {
                float a = u * Mathf.PI * 2, width = Mathf.Lerp(0.31f, 0.22f, v) + 0.018f * Mathf.Sin(v * Mathf.PI);
                return new Vector3(Mathf.Cos(a) * width, 0.27f - v * 0.49f,
                    Mathf.Sin(a) * (0.17f + 0.045f * Mathf.Sin(v * Mathf.PI)));
            }
            Panels(Breastplate, new[] { 0f, 0.22f, 0.25f, 0.28f, 0.5f, 0.75f, 1f }, new[] { 0f, 0.74f, 1f },
                0.012f, 0.008f, 0.018f, (i, j) => shell, frame);
            foreach (float side in new[] { -1f, 1f })
            {
                for (int i = 0; i < 4; i++)
                {
                    shell.Place(Move(side * 0.1f, -0.21f - i * 0.04f, 0.1f) * Turn(10, side * 12, side * 7), () =>
                        Pillow(shell, Vector3.zero, new Vector3(0.2f - i * 0.012f, 0.065f, 0.15f), Curves));
                    frame.Rod(new Vector3(side * 0.26f, -0.16f - i * 0.045f, -0.12f), new Vector3(side * 0.26f, -0.16f - i * 0.045f, 0.12f), 0.014f, 12);
                }
                Piston(frame, shell, new Vector3(side * 0.24f, -0.35f, -0.1f), new Vector3(side * 0.28f, 0.08f, -0.1f), 0.032f);
                glow.Box(new Vector3(side * 0.13f, 0.16f, 0.177f), new Vector3(0.05f, 0.012f, 0.008f), 0.003f);
                for (int i = 0; i < 3; i++) frame.Ball(new Vector3(side * 0.23f, 0.2f - i * 0.12f, 0.145f), 0.009f, 12);
            }
            Add(torso, "Armour", shell, Ceramic);
            Add(torso, "Spine and actuators", frame, Mechanism);
            Add(torso, "Pressure suit", seal, Rubber);
            Add(torso, "Power indicators", glow, Optics, false);
            var head = Child(torso, "Head"); head.localPosition = new Vector3(0, 0.43f, 0);
            shell = new MeshKit(); frame = new MeshKit(); glow = new MeshKit();
            shell.Ball(Vector3.zero, new Vector3(0.183f, 0.22f, 0.192f), Curves);
            for (int i = 0; i < 4; i++) frame.Place(Move(0, -0.19f - i * 0.013f, 0), () => frame.Torus(0.105f, 0.008f, Curves, 8));
            Pillow(frame, new Vector3(0, 0.01f, 0.17f), new Vector3(0.305f, 0.09f, 0.07f), Curves);
            Pillow(glow, new Vector3(0, 0.01f, 0.205f), new Vector3(0.21f, 0.018f, 0.015f), Curves);
            foreach (float side in new[] { -1f, 1f })
                shell.Place(Move(side * 0.125f, -0.07f, 0.12f) * Turn(15, side * -25, side * -12), () =>
                    Pillow(shell, Vector3.zero, new Vector3(0.095f, 0.17f, 0.08f), Curves));
            foreach (float side in new[] { -1f, 1f }) Bearing(frame, new Vector3(side * 0.18f, 0, 0), 0.06f);
            for (int i = -2; i <= 2; i++) frame.Tube(Path(12, new Vector3(i * 0.035f, -0.15f, 0.15f), new Vector3(i * 0.04f, -0.08f, 0.2f), new Vector3(i * 0.042f, -0.035f, 0.19f)), 0.009f, 10);
            Add(head, "Armour", shell, Alloy); Add(head, "Helmet fittings", frame, Mechanism); Add(head, "Visor", glow, Optics, false);
            foreach (float side in new[] { -1f, 1f })
            {
                string prefix = side < 0 ? "Left" : "Right";
                var arm = Child(torso, prefix + " arm"); arm.localPosition = new Vector3(side * 0.38f, 0.12f, 0);
                CladLimb(arm, 0.33f, 0.17f);
                var forearm = Child(arm, "Elbow"); forearm.localPosition = new Vector3(0, -0.34f, 0);
                CladLimb(forearm, 0.27f, 0.13f);
                var hand = Gauntlet(); hand.SetParent(forearm, false); hand.localPosition = new Vector3(0, -0.33f, 0.03f); hand.localRotation = Quaternion.Euler(90, 0, side < 0 ? 0 : 180); hand.localScale = Vector3.one * 0.82f;
                var thigh = Child(root, prefix + " leg"); thigh.localPosition = new Vector3(side * 0.18f, 0.96f, 0);
                CladLimb(thigh, 0.41f, 0.2f);
                var calf = Child(thigh, "Knee"); calf.localPosition = new Vector3(0, -0.43f, 0);
                CladLimb(calf, 0.49f, 0.17f, true);
            }
            var pack = Child(torso, "Thruster pack");
            shell = new MeshKit(); frame = new MeshKit(); glow = new MeshKit();
            Plate(shell, new Vector3(0, -0.02f, -0.22f), new Vector3(0.38f, 0.53f, 0.24f), Curves);
            foreach (float side in new[] { -1f, 1f })
            {
                shell.Place(Move(side * 0.18f, -0.3f, -0.27f), () => shell.Lathe(new[] { 0.08f, 0.42f, 0.11f, 0.3f, 0.11f, 0.08f, 0.14f, 0, 0.1f, 0, 0.075f, 0.2f }, Curves));
                glow.Ball(new Vector3(side * 0.18f, -0.295f, -0.27f), new Vector3(0.09f, 0.02f, 0.09f), Curves);
                for (int i = 0; i < 8; i++) frame.Place(Move(side * 0.18f, -0.19f + i * 0.025f, -0.27f), () => frame.Torus(0.113f, 0.009f, Curves, 8));
            }
            Add(pack, "Nozzles", shell, Alloy); Add(pack, "Cooling fins", frame, Mechanism); Add(pack, "Thruster light", glow, Optics, false);
        });

        static Transform Fabricator() => Machine("Robot", root =>
        {
            var torso = Child(root, "Torso"); torso.localPosition = new Vector3(0, 1.05f, 0);
            MeshKit shell = new MeshKit(), frame = new MeshKit(), seal = new MeshKit();
            seal.Ball(Vector3.zero, new Vector3(0.3f, 0.26f, 0.74f), Curves);
            Vector3 Back(float u, float v)
            {
                float a = u * Mathf.PI, width = 0.24f + 0.09f * Mathf.Sin(v * Mathf.PI);
                return new Vector3(Mathf.Cos(a) * width, Mathf.Sin(a) * 0.26f + 0.03f, Mathf.Lerp(-0.73f, 0.68f, v));
            }
            Panels(Back, new[] { 0f, 0.3f, 0.5f, 0.7f, 1f }, new[] { 0f, 0.15f, 0.31f, 0.47f, 0.63f, 0.8f, 1f },
                0.015f, 0.012f, 0.018f, (i, j) => shell, frame);
            for (int i = 0; i < 6; i++)
            {
                float z = -0.58f + i * 0.22f;
                Bearing(frame, new Vector3(0, -0.02f, z), 0.13f);
                foreach (float side in new[] { -1f, 1f })
                {
                    frame.Rod(new Vector3(side * 0.24f, 0.08f, z - 0.075f), new Vector3(side * 0.24f, 0.08f, z + 0.075f), 0.017f, 12);
                    for (int bolt = 0; bolt < 2; bolt++) frame.Ball(new Vector3(side * 0.258f, 0.17f, z - 0.08f + bolt * 0.16f), 0.013f, 12);
                }
            }
            for (int i = -2; i <= 2; i++) seal.Tube(Path(18, new Vector3(i * 0.055f, 0.18f, -0.7f), new Vector3(i * 0.06f, 0.31f, 0), new Vector3(i * 0.05f, 0.27f, 0.61f)), 0.014f, 12);
            Add(torso, "Overlapping carapace", shell, Alloy); Add(torso, "Spinal mechanisms", frame, Mechanism); Add(torso, "Cables", seal, Rubber);
            var neck = Child(torso, "Neck"); neck.localPosition = new Vector3(0, 0.12f, 0.58f);
            shell = new MeshKit(); frame = new MeshKit();
            foreach (float side in new[] { -1f, 1f })
                Piston(frame, shell, new Vector3(side * 0.11f, 0, -0.12f), new Vector3(side * 0.09f, 0.39f, 0.18f), 0.055f);
            for (int i = 0; i < 6; i++) Plate(shell, new Vector3(0, i * 0.064f, i * 0.032f), new Vector3(0.23f, 0.095f, 0.16f), Curves);
            Add(neck, "Neck armour", shell, Alloy); Add(neck, "Neck actuators", frame, Mechanism);
            var head = Child(neck, "Head"); head.localPosition = new Vector3(0, 0.41f, 0.22f);
            shell = new MeshKit(); frame = new MeshKit(); var glow = new MeshKit();
            Pillow(shell, new Vector3(0, 0.025f, -0.015f), new Vector3(0.38f, 0.21f, 0.43f), Curves);
            foreach (float side in new[] { -1f, 1f })
                shell.Place(Move(side * 0.15f, -0.025f, 0.1f) * Turn(0, side * 15, side * 15), () =>
                    Pillow(shell, Vector3.zero, new Vector3(0.1f, 0.15f, 0.29f), Curves));
            Pillow(frame, new Vector3(0, -0.035f, 0.22f), new Vector3(0.32f, 0.15f, 0.1f), Curves);
            for (int i = 0; i < 3; i++)
            {
                Vector3 at = new Vector3((i - 1) * 0.095f, i == 1 ? 0.02f : -0.03f, 0.285f);
                frame.Place(Move(at) * Turn(90, 0, 0), () => frame.Torus(i == 1 ? 0.051f : 0.035f, 0.012f, Curves, 10));
                glow.Ball(at, new Vector3(0.03f, 0.03f, 0.018f), Curves);
            }
            foreach (float side in new[] { -1f, 1f })
            {
                Bearing(frame, new Vector3(side * 0.2f, 0.01f, 0), 0.065f);
                frame.Tube(Path(10, new Vector3(side * 0.15f, 0.08f, -0.15f), new Vector3(side * 0.19f, 0.2f, -0.1f), new Vector3(side * 0.15f, 0.26f, 0)), 0.012f, 12);
            }
            Add(head, "Sensor housing", shell, Alloy); Add(head, "Optical mounts", frame, Mechanism); Add(head, "Three optics", glow, Optics, false);
            for (int i = 0; i < 4; i++)
            {
                float side = i % 2 == 0 ? -1 : 1;
                var upper = Child(root, "Leg " + i); upper.localPosition = new Vector3(side * 0.32f, 0.95f, i < 2 ? 0.46f : -0.5f);
                CladLimb(upper, 0.39f, 0.16f);
                var lower = Child(upper, "Knee"); lower.localPosition = new Vector3(0, -0.4f, 0.06f);
                CladLimb(lower, 0.51f, 0.12f, true);
            }
        });
    }
}
