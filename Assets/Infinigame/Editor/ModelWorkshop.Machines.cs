using System;
using UnityEngine;
using static Infinigame.MeshKit;

namespace Infinigame
{
    public static partial class ModelWorkshop
    {
        static Material alloy, mechanism, rubber, optics, ceramic, titanium;
        static int Curves => LodSettings.Tier == 0 ? 28 : 48;
        static Material Titanium => titanium != null ? titanium : titanium = Paint(new Color(0.60f, 0.64f, 0.69f), 0.46f, 0.38f);
        static Material Alloy => alloy != null ? alloy : alloy = Paint(new Color(0.62f, 0.68f, 0.72f), 0.72f, 0.43f);
        static Material Mechanism => mechanism != null ? mechanism : mechanism = Paint(new Color(0.14f, 0.18f, 0.2f), 0.8f, 0.38f);
        static Material Rubber => rubber != null ? rubber : rubber = Paint(new Color(0.035f, 0.045f, 0.05f), 0.05f, 0.25f, Surface.Seal);
        static Material Optics => optics != null ? optics : optics = Shapes.Material("Glow", new Color(0.15f, 2.4f, 3.5f));
        static Material Ceramic => ceramic != null ? ceramic : ceramic = Paint(new Color(0.22f, 0.32f, 0.38f), 0.15f, 0.36f, Surface.Ceramic);

        static Transform Machine(string name, Action<Transform> build)
        {
            var model = new GameObject(name).transform;
            build(model);
            return model;
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
            int sides = radius < 0.05f ? 16 : Curves;
            frame.Rod(a, b, radius * 0.55f, sides);
            shell.Rod(a, mid, radius, sides, radius * 0.86f);
            frame.Ball(a, radius * 1.2f, sides);
            frame.Ball(b, radius * 0.85f, sides);
        }

        // One pressed sheet with five flat faces meeting at an off-centre ridge.
        static void FoldedPlate(MeshKit kit, Vector3 heel, Vector3 tip, Vector3 across, float width, float rise)
        {
            Vector3 along = tip - heel;
            across = Vector3.ProjectOnPlane(across, along).normalized * width;
            Vector3 fold = Vector3.Cross(along, across).normalized * rise;
            Vector3[] rim = { heel - across * 0.42f, heel + along * 0.32f - across, tip,
                heel + along * 0.37f + across, heel + across * 0.42f };
            Vector3 ridge = heel + along * 0.42f + fold;
            for (int i = 0; i < rim.Length; i++)
            {
                Vector3 a = rim[i], b = rim[(i + 1) % rim.Length];
                kit.Triangle(a, b, ridge);
                kit.Triangle(b, a, heel + along * 0.42f - fold * 0.32f);
            }
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
                0.018f, 0.012f, 0.018f, (i, j) => i >= 2 ? null : shell, frame);
            FoldedPlate(shell, new Vector3(0, -length * 0.10f, width * 0.54f), new Vector3(0, -length * 0.88f, width * 0.53f),
                Vector3.right, width * 0.85f, width * 0.42f);
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
            Plate(seal, new Vector3(0, 0, -0.12f), new Vector3(0.17f, 0.14f, 0.29f), Curves);
            Plate(frame, new Vector3(0, 0, 0.07f), new Vector3(0.17f, 0.10f, 0.17f), Curves);
            Bearing(frame, new Vector3(0, 0, -0.015f), 0.075f);
            for (int finger = 0; finger < 4; finger++)
            {
                float x = -0.0675f + finger * 0.045f;
                seal.Tube(Path(12, new Vector3(x, 0.062f, -0.25f), new Vector3(x, 0.083f, -0.05f), new Vector3(x, 0.07f, 0.11f)), 0.009f, 12);
                for (int rib = 0; rib < 7; rib++) frame.Box(new Vector3(x, 0.081f, -0.22f + rib * 0.023f), new Vector3(0.019f, 0.013f, 0.007f), 0.003f);
            }
            // Each phalanx pivots beneath its own overlapping armour plate.
            for (int finger = 0; finger < 5; finger++)
            {
                bool thumb = finger == 0;
                var link = Child(root, "Digit " + finger);
                link.localPosition = thumb ? new Vector3(-0.081f, -0.014f, 0.04f) : new Vector3(-0.0675f + (finger - 1) * 0.045f, 0, 0.128f);
                for (int joint = 0; joint < 3; joint++)
                {
                    float length = (thumb ? 0.047f : finger == 4 ? 0.041f : 0.052f) * (1f - joint * 0.15f);
                    float width = (thumb ? 0.024f : 0.021f) * (1f - joint * 0.09f);
                    link.localRotation = GauntletFingers.Bend(finger, joint, 0.78f);
                    var bone = new MeshKit();
                    bone.Rod(Vector3.left * width, Vector3.right * width, width * 0.59f, 12);
                    bone.Box(new Vector3(0, -width * 0.16f, length * 0.49f), new Vector3(width * 1.64f, width * 1.13f, length), width * 0.30f);
                    FoldedPlate(bone, new Vector3(0, width * 0.65f, -length * 0.14f), new Vector3(0, width * 0.41f, length * 0.96f),
                        Vector3.right, width, width * 0.34f);
                    Add(link, "Knuckle plate", bone, Titanium);
                    if (joint < 2)
                    {
                        link = Child(link, joint == 0 ? "Middle" : "Tip");
                        link.localPosition = Vector3.forward * length;
                    }
                }
            }
            shell.Place(Move(0, 0.091f, -0.12f), () => shell.Lathe(new[] { 0.035f, 0, 0.035f, 0.018f, 0.027f, 0.025f, 0.018f, 0.025f, 0.018f, 0 }, Curves));
            glow.Ball(new Vector3(0, 0.108f, -0.12f), new Vector3(0.018f, 0.007f, 0.018f), Curves);
            for (int i = 0; i < 3; i++) glow.Box(new Vector3(0.102f, 0.025f, -0.14f + 0.04f * i), new Vector3(0.005f, 0.018f, 0.022f), 0.002f);
            // Split rails leave the palm lens and its concentric seals visible between the knuckles.
            foreach (float side in new[] { -1f, 1f })
            {
                float x = side * 0.074f;
                FoldedPlate(shell, new Vector3(side * 0.052f, 0.069f, -0.25f), new Vector3(x, 0.056f, 0.15f),
                    new Vector3(1, -side * 0.54f, 0), 0.050f, 0.027f);
                frame.Rod(new Vector3(x, 0.103f, -0.18f), new Vector3(x, 0.105f, 0.14f), 0.012f, 12);
                glow.Rod(new Vector3(x, 0.114f, -0.15f), new Vector3(x, 0.116f, 0.125f), 0.004f, 8);
            }
            foreach (float side in new[] { -1f, 1f })
            {
                seal.Place(Move(side * 0.061f, -0.053f, 0.065f) * Turn(0, side * 15, 0), () =>
                    Pillow(seal, Vector3.zero, new Vector3(0.062f, 0.046f, 0.135f), Curves / 2));
                for (int rib = 0; rib < 7; rib++)
                {
                    float z = -0.16f + rib * 0.026f;
                    frame.Rod(new Vector3(side * 0.08f, -0.054f, z), new Vector3(side * 0.058f, -0.067f, z + 0.013f), 0.004f, 8);
                }
            }
            frame.Place(Move(0, -0.063f, 0.067f) * Turn(180, 0, 0), () =>
                frame.Lathe(new[] { 0.052f, 0, 0.055f, 0.009f, 0.047f, 0.017f, 0.034f, 0.017f, 0.032f, 0 }, 32));
            glow.Ball(new Vector3(0, -0.076f, 0.067f), new Vector3(0.034f, 0.009f, 0.034f), 20);
            for (int i = 0; i < 10; i++)
            {
                float angle = 36f * i;
                shell.Place(Move(0, 0, -0.23f) * Turn(0, 0, angle) * Move(0, 0.09f, 0), () =>
                    shell.Box(Vector3.zero, new Vector3(0.051f, 0.021f, 0.062f), 0.008f));
            }
            Add(root, "Armour", shell, Titanium);
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
                0.018f, 0.012f, 0.018f, (i, j) => j == 1 && (i == 0 || i == 3) ? null : shell, frame);
            var plates = new MeshKit();
            foreach (float side in new[] { -1f, 1f })
            {
                plates.Place(Move(side * 0.145f, 0.095f, 0.155f) * Turn(-10, side * 17, side * -16), () =>
                    Pillow(plates, Vector3.zero, new Vector3(0.266f, 0.32f, 0.115f), Curves));
                shell.Place(Move(side * 0.29f, 0.17f, -0.01f) * Turn(0, 0, side * -20), () =>
                    shell.Ball(Vector3.zero, new Vector3(0.14f, 0.14f, 0.18f), Curves));
                FoldedPlate(plates, new Vector3(side * 0.22f, 0.26f, 0.14f), new Vector3(side * 0.055f, -0.13f, 0.20f),
                    new Vector3(1, 0, -side * 0.38f), 0.14f, 0.075f);
                FoldedPlate(plates, new Vector3(side * 0.23f, 0.23f, -0.11f), new Vector3(side * 0.31f, -0.045f, 0.11f),
                    new Vector3(1, 0, -side * 1.3f), 0.105f, 0.055f);
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
            Add(torso, "Breastplates", plates, Titanium);
            Add(torso, "Spine and actuators", frame, Mechanism);
            Add(torso, "Pressure suit", seal, Rubber);
            Add(torso, "Power indicators", glow, Optics, false);
            var head = Child(torso, "Head"); head.localPosition = new Vector3(0, 0.43f, 0);
            shell = new MeshKit(); frame = new MeshKit(); glow = new MeshKit();
            shell.Ball(new Vector3(0, 0.012f, -0.025f), new Vector3(0.18f, 0.205f, 0.167f), Curves * 2);
            var glass = new MeshKit();
            glass.Ball(new Vector3(0, 0.015f, 0.101f), new Vector3(0.166f, 0.109f, 0.103f), Curves * 2);
            Add(head, "Pressure visor", glass, Paint(new Color(0.045f, 0.085f, 0.105f), 0.63f, 0.74f));
            for (int i = 0; i < 4; i++) frame.Place(Move(0, -0.19f - i * 0.013f, 0), () => frame.Torus(0.105f, 0.008f, Curves, 8));
            foreach (float side in new[] { -1f, 1f })
            {
                shell.Place(Move(side * 0.104f, -0.079f, 0.105f) * Turn(-18, side * 27, side * -20), () =>
                    shell.Ball(Vector3.zero, new Vector3(0.067f, 0.093f, 0.063f), Curves));
                glow.Tube(Path(18, new Vector3(side * 0.043f, 0.018f, 0.202f), new Vector3(side * 0.107f, 0.036f, 0.183f), new Vector3(side * 0.146f, 0.031f, 0.155f)), 0.0045f, 10);
            }
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
            var collar = new MeshKit();
            foreach (float side in new[] { -1f, 1f })
                for (int slat = 0; slat < 4; slat++)
                {
                    Vector3 throat = new Vector3(side * 0.09f, 0.21f - slat * 0.028f, 0.16f);
                    collar.Tube(Path(6, throat, throat + new Vector3(side * 0.09f, -0.014f, 0.043f),
                        throat + new Vector3(side * 0.16f, -0.03f, -0.005f)), 0.012f, 8, 0.007f);
                }
            Add(torso, "Collar heat exchangers", collar, Alloy);
            var pack = Child(torso, "Thruster pack");
            shell = new MeshKit(); frame = new MeshKit(); glow = new MeshKit();
            Plate(shell, new Vector3(0, -0.02f, -0.22f), new Vector3(0.38f, 0.53f, 0.24f), Curves);
            foreach (float side in new[] { -1f, 1f })
            {
                shell.Place(Move(side * 0.18f, -0.3f, -0.27f), () => shell.Lathe(new[] { 0.08f, 0.42f, 0.11f, 0.3f, 0.11f, 0.08f, 0.14f, 0, 0.1f, 0, 0.075f, 0.2f }, Curves));
                FoldedPlate(shell, new Vector3(side * 0.13f, -0.17f, -0.31f), new Vector3(side * 0.11f, 0.23f, -0.29f),
                    Vector3.right, 0.115f, 0.087f);
                glow.Ball(new Vector3(side * 0.18f, -0.295f, -0.27f), new Vector3(0.09f, 0.02f, 0.09f), Curves);
                for (int i = 0; i < 8; i++) frame.Place(Move(side * 0.18f, -0.19f + i * 0.025f, -0.27f), () => frame.Torus(0.113f, 0.009f, Curves, 8));
            }
            var tube = new MeshKit();
            tube.Lathe(new[] { 0.058f, -0.34f, 0.085f, -0.31f, 0.085f, -0.04f, 0.096f, -0.03f, 0.096f, 0f, 0.063f, 0f, 0.063f, -0.26f }, 20);
            var launcher = Child(pack, "Missile port");
            launcher.localPosition = new Vector3(0.15f, 0.25f, -0.24f);
            Add(launcher, "Launch tube", tube, Mechanism);
            Add(pack, "Nozzles", shell, Alloy); Add(pack, "Cooling fins", frame, Mechanism); Add(pack, "Thruster light", glow, Optics, false);
        });

        // A swept plate with a broad leading lip, crowned section and a trailing point.
        static void Scute(MeshKit kit, Vector3 nose, Vector3 tip, Vector3 across, float width, float crown, int samples = 10)
        {
            Vector3 along = (tip - nose).normalized;
            across = Vector3.ProjectOnPlane(across, along).normalized;
            Vector3 normal = Vector3.Cross(along, across);
            var rings = new Vector3[samples + 1][];
            for (int i = 0; i < rings.Length; i++)
            {
                float t = i / (float)samples;
                float taper = Mathf.Sin(Mathf.Pow(t, 0.62f) * Mathf.PI);
                Vector3 spine = Vector3.Lerp(nose, tip, t) + normal * (crown * taper);
                var ring = rings[i] = new Vector3[samples + 2];
                for (int j = 0; j < ring.Length; j++)
                {
                    float angle = j * 2 * Mathf.PI / ring.Length;
                    ring[j] = spine + taper * (across * (Mathf.Cos(angle) * width)
                        + normal * (Mathf.Sin(angle) * crown * 0.65f));
                }
            }
            kit.Skin(rings);
        }

        public static Transform Robot() => Machine("Robot", root =>
        {
            var torso = Child(root, "Torso"); torso.localPosition = new Vector3(0, 1.05f, 0);
            MeshKit shell = new MeshKit(), frame = new MeshKit(), seal = new MeshKit();
            seal.Ball(new Vector3(0, -0.015f, 0.42f), new Vector3(0.25f, 0.29f, 0.32f), Curves);
            seal.Ball(new Vector3(0, 0.015f, -0.51f), new Vector3(0.24f, 0.21f, 0.22f), Curves);
            frame.Rod(new Vector3(0, 0.08f, -0.61f), new Vector3(0, 0.12f, 0.62f), 0.085f, 12);
            for (int i = 0; i < 6; i++)
            {
                float z = -0.58f + i * 0.22f;
                frame.Place(Move(0, 0.085f, z) * Turn(90, 0, 0), () => frame.Torus(0.15f, 0.031f, 10, 8));
                foreach (float side in new[] { -1f, 1f })
                    frame.Rod(new Vector3(side * 0.15f, 0.08f, z), new Vector3(side * 0.10f, -0.075f, z + 0.22f), 0.024f, 10);
            }
            foreach (float side in new[] { -1f, 1f })
            {
                seal.Tube(Path(10, new Vector3(side * 0.15f, 0.02f, -0.58f), new Vector3(side * 0.16f, -0.18f, -0.10f),
                    new Vector3(side * 0.19f, -0.09f, 0.45f)), 0.032f, 10);
                frame.Rod(new Vector3(side * 0.14f, 0.09f, -0.47f), new Vector3(side * 0.062f, 0.39f, -0.26f), 0.041f, 10);
                for (int rib = 0; rib < 5; rib++)
                {
                    float z = -0.36f + rib * 0.145f;
                    Vector3 top = new Vector3(side * 0.105f, 0.18f, z);
                    Vector3 heel = new Vector3(side * 0.18f, -0.17f, z + 0.09f);
                    frame.Tube(Path(6, top, new Vector3(side * 0.26f, 0.14f, z + 0.02f), heel), 0.019f, 8, 0.011f);
                    shell.Rod(top - Vector3.forward * 0.03f, top + Vector3.forward * 0.03f, 0.032f, 8);
                }
                foreach (float z in new[] { -0.41f, 0.58f })
                {
                    FoldedPlate(shell, new Vector3(side * 0.08f, 0.22f, z), new Vector3(side * 0.32f, -0.22f, z - 0.26f),
                        new Vector3(-1, side * 0.8f, 0), 0.22f, 0.12f);
                    FoldedPlate(shell, new Vector3(side * 0.12f, 0.24f, z + 0.12f), new Vector3(side * 0.39f, -0.12f, z),
                        new Vector3(-1, side * 0.8f, 0.65f), 0.17f, 0.1f);
                }
            }
            Add(torso, "Overlapping carapace", shell, Titanium); Add(torso, "Spinal mechanisms", frame, Alloy); Add(torso, "Cables", seal, Rubber);
            var neck = Child(torso, "Neck"); neck.localPosition = new Vector3(0, 0.12f, 0.58f);
            shell = new MeshKit(); frame = new MeshKit();
            foreach (float side in new[] { -1f, 1f })
            {
                Piston(frame, shell, new Vector3(side * 0.11f, 0, -0.12f), new Vector3(side * 0.09f, 0.39f, 0.18f), 0.055f);
                frame.Tube(Path(6, new Vector3(side * 0.12f, 0.02f, 0.07f), new Vector3(side * 0.16f, 0.20f, 0.13f),
                    new Vector3(side * 0.13f, 0.39f, 0.15f)), 0.021f, 10);
            }
            frame.Tube(Path(10, Vector3.zero, new Vector3(0, 0.16f, 0.15f), new Vector3(0, 0.4f, 0.21f)), 0.055f, 12);
            Add(neck, "Neck armour", shell, Alloy); Add(neck, "Neck actuators", frame, Mechanism);
            var head = Child(neck, "Head"); head.localPosition = new Vector3(0, 0.41f, 0.22f);
            shell = new MeshKit(); frame = new MeshKit(); var glow = new MeshKit();
            Pillow(frame, new Vector3(0, 0.014f, -0.015f), new Vector3(0.33f, 0.17f, 0.40f), Curves);
            foreach (float side in new[] { -1f, 1f })
                FoldedPlate(shell, new Vector3(side * 0.15f, 0.035f, -0.12f), new Vector3(side * 0.12f, -0.10f, 0.28f),
                    new Vector3(0.5f, -side, 0), 0.09f, 0.05f);
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
            foreach (float side in new[] { -1f, 1f })
                FoldedPlate(shell, new Vector3(side * 0.09f, 0.10f, -0.24f), new Vector3(side * 0.115f, 0.060f, 0.31f),
                    new Vector3(1, -side * 0.40f, 0), 0.137f, 0.085f);
            Add(head, "Sensor housing", shell, Titanium); Add(head, "Optical mounts", frame, Mechanism); Add(head, "Three optics", glow, Optics, false);
            var barrel = new MeshKit();
            barrel.Place(Move(0, 0.47f, -0.17f) * Turn(90, 0, 0), () =>
                barrel.Lathe(new[] { 0f, -0.38f, 0.12f, -0.32f, 0.12f, -0.1f, 0.085f, -0.04f, 0.067f, 0.50f, 0.075f, 0.54f, 0.045f, 0.55f, 0.045f, 0.50f }, 28));
            for (int i = 0; i < 4; i++)
                barrel.Place(Move(0, 0.47f, -0.38f + i * 0.09f) * Turn(90, 0, 0), () => barrel.Torus(0.12f, 0.012f, 24, 8));
            Bearing(barrel, new Vector3(0, 0.33f, -0.27f), 0.13f);
            Add(torso, "Dorsal laser", barrel, Mechanism);
            for (int i = 0; i < 4; i++)
            {
                float side = i % 2 == 0 ? -1 : 1;
                var upper = Child(root, "Leg " + i); upper.localPosition = new Vector3(side * 0.32f, 0.84f, i < 2 ? 0.46f : -0.5f);
                CladLimb(upper, 0.39f, 0.16f);
                var lower = Child(upper, "Knee"); lower.localPosition = new Vector3(0, -0.4f, 0.06f);
                CladLimb(lower, 0.51f, 0.12f, true);
                upper.localRotation = Quaternion.Euler(i < 2 ? -16 : 34, 0, 0);
                lower.localRotation = Quaternion.Euler(i < 2 ? 42 : -58, 0, 0);
            }
        });
    }
}
