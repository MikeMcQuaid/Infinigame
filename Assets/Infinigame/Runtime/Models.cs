using System;
using System.Collections.Generic;
using UnityEngine;
using static Infinigame.MeshKit;

namespace Infinigame
{
    // The game's models, each built from code with MeshKit: the player's ship and its cockpit, the
    // hermit's camp, the raiders with their drones, missiles and citadel and the life of stranger worlds.
    public static partial class Models
    {
        static Material Paint(Color colour, float metallic = 0f, float smoothness = 0.3f, Surface surface = Surface.Metal)
        {
            var material = Shapes.Material("Lit", colour);
            material.SetTexture("_BaseMap", Textures.Albedo(surface));
            material.SetTextureScale("_BaseMap", Vector2.one * 2);
            material.SetTexture("_BumpMap", Textures.Normal(surface));
            material.SetTextureScale("_BumpMap", Vector2.one * 2);
            material.SetFloat("_BumpScale", 0.3f);
            material.EnableKeyword("_NORMALMAP");
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            return material;
        }

        static Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        // A stuffed cushion: a rounded slab, fullest round its middle.
        static void Pillow(MeshKit kit, Vector3 centre, Vector3 size, int sides = 48)
        {
            float[] widths = { 0f, 0.78f, 0.95f, 1f, 1f, 0.95f, 0.78f, 0f }, heights = { -1f, -1f, -0.7f, -0.3f, 0.3f, 0.7f, 1f, 1f };
            var rings = new Vector3[widths.Length][];
            for (int i = 0; i < rings.Length; i++)
                rings[i] = Array.ConvertAll(Oval(sides, size.z / 2f * widths[i], size.x / 2f * widths[i], 4.5f), p => centre + new Vector3(p.y, heights[i] * size.y / 2f, p.x));
            kit.Skin(rings);
        }

        // A band of rectangular section round the y axis.
        static void Band(MeshKit kit, float inner, float outer, float bottom, float top, int sides) =>
            kit.Lathe(new[] { inner, bottom, outer, bottom, outer, top, inner, top }, sides);

        // The ship's fuselage, from its tail to its nose, as (where, value) keys along z: half its
        // width, its top and bottom, the height of its widest part and the angle up from there at
        // which the canopy's sill runs.
        const float Tail = -3.7f, Nose = 4f, Sill = 0.08f, Upper = 2.3f, Lower = 2.8f;
        static readonly float[] BodyWidth = { -3.7f, 0.13f, -3f, 0.3f, -2f, 0.56f, -1f, 0.74f, 0f, 0.8f, 1.2f, 0.8f, 2f, 0.74f, 2.6f, 0.66f, 3.3f, 0.52f, 4f, 0.4f };
        static readonly float[] BodyTop = { -3.7f, 0.42f, -3f, 0.56f, -2f, 0.7f, -1f, 0.86f, -0.2f, 1.02f, 0.3f, 1.14f, 1.1f, 1.37f, 1.8f, 1.12f, 2.3f, 0.68f, 2.7f, 0.64f, 3.2f, 0.59f, 4f, 0.5f };
        static readonly float[] BodyBottom = { -3.7f, 0.14f, -3f, -0.12f, -2f, -0.36f, -1f, -0.46f, 1.5f, -0.46f, 2.3f, -0.42f, 3.2f, -0.32f, 4f, -0.24f };
        static readonly float[] BodyWaist = { -3.7f, 0.28f, -2f, 0.1f, 0f, 0.05f, 2.3f, 0.05f, 3.2f, 0.08f, 4f, 0.12f };
        static readonly float[] BodySill = { -3.7f, 30f, 0.3f, 30f, 1.1f, 24f, 1.65f, 29f, 2.3f, 46f, 3f, 50f, 4f, 50f };

        static float Along(float z) => Mathf.InverseLerp(Tail, Nose, z);

        // A point on the fuselage: how far round it, from 0 at the right-hand waist over the crown
        // at a quarter and under the keel at three quarters, and how far along from tail to nose.
        // The sections are squarish ovals, fuller below the waist than above it.
        static Vector3 Fuselage(float round, float along)
        {
            float z = Mathf.Lerp(Tail, Nose, along), waist = Key(z, BodyWaist), sill = Key(z, BodySill) * Mathf.Deg2Rad;
            // the nose closes like the end of an egg
            float closing = Mathf.Clamp01((z - 3.2f) / (Nose - 3.2f)), open = Mathf.Sqrt(1f - closing * closing);
            round = Mathf.Repeat(round, 1f);
            // the sill is a fixed share of the way round wherever it runs, so panels can meet along it
            float angle = round < Sill ? sill * round / Sill
                : round < 0.5f - Sill ? Mathf.Lerp(sill, Mathf.PI - sill, (round - Sill) / (0.5f - 2f * Sill))
                : round < 0.5f ? Mathf.PI - sill * (0.5f - round) / Sill
                : 2f * Mathf.PI * round;
            float c = Mathf.Cos(angle), s = Mathf.Sin(angle), power = s > 0f ? Upper : Lower;
            float reach = s > 0f ? Key(z, BodyTop) - waist : Key(z, BodyBottom) - waist;
            return new Vector3(Key(z, BodyWidth) * open * Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 2f / power), waist + reach * open * Mathf.Pow(Mathf.Abs(s), 2f / power), z);
        }

        // Half the fuselage's width at a height, for fitting things inside it and against it.
        static float BodyHalf(float z, float y)
        {
            float waist = Key(z, BodyWaist), power = y > waist ? Upper : Lower;
            float reach = y > waist ? Key(z, BodyTop) - waist : waist - Key(z, BodyBottom);
            return Key(z, BodyWidth) * Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Pow(Mathf.Abs(y - waist) / reach, power)), 1f / power);
        }

        // The fuselage's underside at a distance out from the middle, as a height below zero given back positive.
        static float BodyHalfDown(float z, float x)
        {
            float waist = Key(z, BodyWaist), reach = waist - Key(z, BodyBottom);
            return reach * Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Pow(x / Key(z, BodyWidth), Lower)), 1f / Lower) - waist;
        }

        // A point on an aerofoil: how far behind its leading edge and how far above its chord line.
        // Round goes from the trailing edge over the top to the leading edge at a half, and back underneath.
        static Vector2 Aerofoil(float round, float chord, float thickness)
        {
            float angle = 2f * Mathf.PI * round, s = 0.5f * (1f + Mathf.Cos(angle));
            float half = 5f * (0.2969f * Mathf.Sqrt(s) - 0.126f * s - 0.3516f * s * s + 0.2843f * s * s * s - 0.1036f * s * s * s * s) + 0.004f * s;
            return new Vector2(s * chord, (round % 1f < 0.5f ? half : -half) * thickness * chord);
        }

        // The right wing from root to tip: swept, tapered and lifting a little towards the tip.
        static Vector3 Wing(float round, float along)
        {
            Vector2 p = Aerofoil(round, Mathf.Lerp(2.7f, 1f, along), Mathf.Lerp(0.115f, 0.09f, along));
            return new Vector3(Mathf.Lerp(0.55f, 3.55f, along), -0.08f + 0.1f * along + p.y, Mathf.Lerp(0.75f, -1.3f, along) - p.x);
        }

        // The fin that turns up from the wing's tip.
        static Vector3 Winglet(float round, float along)
        {
            Vector2 p = Aerofoil(round, Mathf.Lerp(1f, 0.4f, along), 0.09f);
            // a tight bend out of the wing, then straight up and out
            const float bend = 0.3f, radius = 0.16f, cant = 78f * Mathf.Deg2Rad;
            float turn = cant * Mathf.Min(along / bend, 1f), run = 0.6f * Mathf.Max(along - bend, 0f) / (1f - bend);
            float x = 3.55f + radius * Mathf.Sin(turn) + run * Mathf.Cos(cant), y = 0.02f + radius * (1f - Mathf.Cos(turn)) + run * Mathf.Sin(cant);
            return new Vector3(x - Mathf.Sin(turn) * p.y, y + Mathf.Cos(turn) * p.y, Mathf.Lerp(-1.3f, -1.8f, along) - p.x);
        }

        // The tail fin, from where it leaves the fuselage to its tip.
        static Vector3 Fin(float round, float along)
        {
            Vector2 p = Aerofoil(round, Mathf.Lerp(1.9f, 0.7f, along), Mathf.Lerp(0.07f, 0.06f, along));
            return new Vector3(-p.y, Mathf.Lerp(0.45f, 1.95f, along), Mathf.Lerp(-1.45f, -2.95f, along) - p.x);
        }

        // The right engine's cowling, from where its nozzle begins to just behind its intake lip.
        const float EngineX = 1.3f, EngineY = 0.02f;
        static readonly float[] EngineRadius = { -3f, 0.335f, -2.2f, 0.4f, -1f, 0.43f, 0f, 0.415f, 0.42f, 0.385f };

        static Vector3 Cowling(float round, float along)
        {
            float z = Mathf.Lerp(-3f, 0.42f, along), radius = Key(z, EngineRadius), angle = 2f * Mathf.PI * round;
            return new Vector3(EngineX + radius * Mathf.Cos(angle), EngineY + 0.96f * radius * Mathf.Sin(angle), z);
        }

        // The player's ship: a small working exploration craft. Root named "Ship", forward is +z and
        // up is +y, with its skids at -Ship.Clearance. "Exterior" is everything seen from outside and
        // "Cockpit" is what the seated pilot sees and carries the Cockpit that works its instruments,
        // with "SpeedScreen", "RangeScreen" and "HeatScreen" at the middle of its three screens
        // and "Copilot" at the base of the passenger's seat.
        public static Transform Ship()
        {
            var root = new GameObject("Ship").transform;
            Transform exterior = Child(root, "Exterior"), cockpit = Child(root, "Cockpit");

            // One mesh to a material. The nose and the cabin's fittings are kept apart from the rest
            // because both views show them.
            var hull = new MeshKit();
            MeshKit Kit() => new MeshKit(hull);
            MeshKit plate = Kit(), accent = Kit(), dark = Kit(), steel = Kit(), scorched = Kit(), hazard = Kit(), glass = Kit(), jet = Kit(), red = Kit(), green = Kit(), white = Kit();
            MeshKit noseHull = Kit(), nosePlate = Kit(), noseDark = Kit();
            MeshKit trim = Kit(), fittings = Kit(), leather = Kit(), screens = Kit(), amber = Kit(), marks = Kit();

            Vector3[] Ring(int points, float z)
            {
                var ring = new Vector3[points];
                for (int i = 0; i < points; i++) ring[i] = Fuselage(i / (float)points, Along(z));
                return ring;
            }
            Vector3 Out(Sheet sheet, float round, float along) => Lifted(sheet, 1f)(round, along) - sheet(round, along);

            // The fuselage is skinned in panels with real gaps. The canopy's opening is left out, a
            // stripe runs under each sill and a few panels are bare hatches.
            float[] rounds = { 0f, 0.03f, 0.05f, Sill, 0.165f, 0.25f, 0.335f, 0.5f - Sill, 0.45f, 0.47f, 0.5f, 0.5625f, 0.625f, 0.6875f, 0.75f, 0.8125f, 0.875f, 0.9375f, 1f };
            float[] stations = Array.ConvertAll(new[] { Tail, -3.15f, -2.5f, -1.8f, -1.1f, -0.4f, 0.3f, 0.95f, 1.65f, 2.3f, 2.7f, 3.15f, 3.55f, 3.97f }, Along);
            var hatches = new HashSet<int> { 404, 504, 1303, 1403, 1306, 1406, 1102, 1602, 410, 510, 1111, 1611, 1205, 1505 };
            MeshKit Cladding(int i, int j, bool front)
            {
                if (j >= 9 != front || i >= 3 && i <= 6 && j >= 6 && j <= 8) return null;
                if (j == 12) return noseDark;
                if ((i == 1 || i == 8) && j >= 1 && j <= 8) return accent;
                return hatches.Contains(i * 100 + j) ? front ? nosePlate : plate : front ? noseHull : hull;
            }
            Panels(Fuselage, rounds, stations, 0.05f, 0.012f, 0.009f, (i, j) => Cladding(i, j, false), dark);
            Panels(Fuselage, rounds, stations, 0.05f, 0.012f, 0.009f, (i, j) => Cladding(i, j, true), noseDark);
            // each hatch is held down by a screw at every corner
            foreach (int hatch in hatches)
                for (int corner = 0; corner < 4; corner++)
                {
                    int i = hatch / 100, j = hatch % 100;
                    float round = Mathf.Lerp(rounds[i], rounds[i + 1], corner % 2 == 0 ? 0.12f : 0.88f), along = Mathf.Lerp(stations[j], stations[j + 1], corner < 2 ? 0.1f : 0.9f);
                    Vector3 at = Fuselage(round, along);
                    hull.Place(Aim(at, at + Out(Fuselage, round, along), Vector3.forward) * Turn(90f, 0f, 0f), () => (j >= 9 ? noseDark : dark).Lathe(new[] { 0.011f, 0f, 0.011f, 0.003f, 0.005f, 0.006f, 0f, 0.006f }, 6));
                }
            var tip = new List<Vector3[]>();
            foreach (float z in new[] { 3.97f, 3.98f, 3.99f, 3.997f, Nose }) tip.Add(Ring(48, z));
            noseDark.Skin(tip);
            dark.Skin(new[] { Ring(32, Tail) }, true, true);
            Panels(Fuselage, new[] { Sill, 0.25f, 0.5f - Sill }, new[] { Along(0.3f), Along(0.95f), Along(1.65f), Along(2.3f) }, 0.06f, 0f, 0f, (i, j) => glass);

            // The canopy's frame stands proud of the glass outside, and again inside where the pilot sees it.
            void Frames(MeshKit kit, float lift)
            {
                var raised = Lifted(Fuselage, lift);
                void Bar(float from, float to, float back, float front) =>
                    Panels(raised, new[] { from, to }, new[] { Along(back), Along(front) }, 0.05f, 0.003f, lift > 0f ? lift + 0.015f : lift, (i, j) => kit, kit);
                Bar(Sill - 0.006f, Sill + 0.012f, 0.27f, 2.33f);
                Bar(0.5f - Sill - 0.012f, 0.5f - Sill + 0.006f, 0.27f, 2.33f);
                foreach (float z in new[] { 0.3f, 0.95f, 1.65f, 2.3f }) Bar(Sill + 0.012f, 0.5f - Sill - 0.012f, z - 0.03f, z + 0.03f);
                Bar(0.243f, 0.257f, 0.33f, 0.92f);
            }
            Frames(dark, 0.012f);
            trim.flip = true;
            Frames(trim, -0.03f);
            // the cabin's walls below the sills, its floor and the bulkheads at either end
            Panels(Lifted(Fuselage, -0.03f), new[] { 0.5f - Sill, 0.5f, 0.625f, 0.75f, 0.875f, 1f, 1f + Sill }, new[] { Along(0.12f), Along(2.36f) }, 0.08f, 0f, 0f, (i, j) => trim);
            trim.flip = false;
            trim.Skin(new[] { Ring(32, 0.12f) }, true, false, true);
            trim.Skin(new[] { Ring(32, 2.36f) }, true, true);
            for (int i = 0; i < 12; i++)
            {
                float back = Mathf.Lerp(0.12f, 2.36f, i / 12f), front = Mathf.Lerp(0.12f, 2.36f, (i + 1) / 12f), floor = -0.12f;
                float a = BodyHalf(back, floor) - 0.02f, b = BodyHalf(front, floor) - 0.02f;
                trim.Quad(new Vector3(-a, floor, back), new Vector3(-b, floor, front), new Vector3(b, floor, front), new Vector3(a, floor, back));
            }

            // Thruster ports sit in cups sunk into the skin.
            void Port(Sheet sheet, float round, float along, MeshKit cup)
            {
                Vector3 at = sheet(round, along);
                hull.Place(Aim(at, at + Out(sheet, round, along), Vector3.forward) * Turn(90f, 0f, 0f) * Size(0.045f), () =>
                {
                    cup.Lathe(new[] { 1.3f, -0.1f, 1.3f, 0.12f, 1f, 0.2f, 0.75f, -0.5f, 0f, -0.5f }, 14);
                    steel.Lathe(new[] { 0.3f, -0.5f, 0.55f, 0.05f, 0.5f, 0.05f, 0.25f, -0.45f }, 10);
                });
            }
            foreach (float round in new[] { 0.04f, 0.2f, 0.3f, 0.46f, 0.68f, 0.82f })
            {
                Port(Fuselage, round, Along(3.35f), noseDark);
                Port(Fuselage, round, Along(-3.3f), dark);
            }

            // The tail fin, with its rudder and a painted cap.
            Panels(Fin, new[] { 0f, 0.16f, 0.5f, 0.84f, 1f }, new[] { 0f, 0.35f, 0.7f, 0.9f, 1f }, 0.05f, 0.011f, 0.008f, (i, j) => j == 3 ? accent : i == 0 || i == 3 ? plate : hull, dark);
            var finTop = new Vector3[40];
            for (int i = 0; i < finTop.Length; i++) finTop[i] = Fin(i / (float)finTop.Length, 1f);
            accent.Skin(new[] { finTop }, true, false, true);

            // Aerials, probes and the radiator behind the cabin.
            dark.Place(Move(0f, Key(-0.75f, BodyTop) - 0.02f, -0.75f) * Turn(0f, 90f, 0f), () => dark.Plate(Outline(-0.13f, 0f, 0.13f, 0f, 0.19f, 0.24f, 0.08f, 0.24f), 0.022f, 0.011f, 0.03f));
            dark.Place(Move(0f, Key(1.2f, BodyBottom) + 0.02f, 1.2f) * Turn(0f, 90f, 180f), () => dark.Plate(Outline(-0.1f, 0f, 0.1f, 0f, 0.15f, 0.18f, 0.06f, 0.18f), 0.02f, 0.01f, 0.025f));
            steel.Rod(new Vector3(0f, 0.7f, -1.9f), new Vector3(0f, 1.5f, -2.2f), 0.009f, 6, 0.004f);
            plate.Ball(new Vector3(0f, Key(-0.25f, BodyTop) - 0.01f, -0.25f), new Vector3(0.15f, 0.06f, 0.22f), 20);
            steel.Rod(new Vector3(0f, 0.12f, 3.9f), new Vector3(0f, 0.12f, 4.32f), 0.016f, 8, 0.007f);
            for (int i = 0; i < 10; i++)
            {
                float z = -1.55f + 0.07f * i, top = Key(z, BodyTop);
                dark.Place(Move(0f, top + 0.004f, z) * Turn(-35f, 0f, 0f), () => dark.Box(Vector3.zero, new Vector3(0.44f, 0.05f, 0.012f)));
            }
            plate.Place(Move(0f, Key(-1.23f, BodyTop) - 0.012f, -1.23f) * Turn(-9f, 0f, 0f), () =>
            {
                foreach (float side in new[] { -1f, 1f }) plate.Box(new Vector3(side * 0.24f, 0.02f, 0f), new Vector3(0.03f, 0.05f, 0.76f), 0.008f);
                foreach (float end in new[] { -1f, 1f }) plate.Box(new Vector3(0f, 0.02f, end * 0.38f), new Vector3(0.5f, 0.05f, 0.03f), 0.008f);
            });

            // Lights: white at the tail and under the nose, red beacons on the fin and the belly.
            white.Ball(new Vector3(0f, 0.28f, Tail - 0.01f), 0.04f, 10);
            red.Ball(new Vector3(0f, 1.955f, -3.3f), 0.035f, 10);
            red.Ball(new Vector3(0f, Key(-0.6f, BodyBottom), -0.6f), 0.04f, 10);

            hull.Mirror(() =>
            {
                // The wing: bare leading edges, grey control surfaces and a painted tip, then the winglet.
                Panels(Wing, new[] { 0f, 0.17f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.83f, 1f }, new[] { 0f, 0.1f, 0.19f, 0.31f, 0.5f, 0.68f, 0.86f, 1f }, 0.05f, 0.011f, 0.008f,
                    (i, j) => j == 6 ? accent : i == 3 || i == 4 ? steel : (i == 0 || i == 7) && j != 2 ? plate : hull, dark);
                Panels(Winglet, new[] { 0f, 0.25f, 0.5f, 0.75f, 1f }, new[] { 0f, 0.3f, 0.75f, 1f }, 0.04f, 0.01f, 0.007f, (i, j) => j == 2 ? accent : i == 1 || i == 2 ? steel : hull, dark);
                var wingletTop = new Vector3[32];
                for (int i = 0; i < wingletTop.Length; i++) wingletTop[i] = Winglet(i / (float)wingletTop.Length, 1f);
                accent.Skin(new[] { wingletTop }, true, false, true);
                // wicks that bleed static off the trailing edge
                foreach (float along in new[] { 0.55f, 0.7f, 0.85f, 0.97f })
                    steel.Rod(Wing(0f, along), Wing(0f, along) + new Vector3(0.02f, 0f, -0.16f), 0.005f, 5, 0.002f);
                foreach (float round in new[] { 0.25f, 0.75f }) Port(Wing, round, 0.93f, dark);

                // The engine: a panelled cowling with a striped collar behind its bare intake lip.
                var eighths = new float[9];
                for (int i = 0; i < 9; i++) eighths[i] = (i + 0.5f) / 8f;
                var sixteenths = new float[17];
                for (int i = 0; i < 17; i++) sixteenths[i] = i / 16f;
                Panels(Cowling, eighths, new[] { 0f, 0.175f, 0.38f, 0.585f, 0.79f, 0.936f }, 0.05f, 0.011f, 0.008f, (i, j) => j == 4 ? accent : (i + 3 * j) % 7 == 2 ? plate : hull, dark);
                Panels(Cowling, sixteenths, new[] { 0.936f, 1f }, 0.05f, 0.008f, 0.007f, (i, j) => i % 2 == 0 ? hazard : dark, dark);
                for (int i = 0; i < 5; i++)
                {
                    float z = -1.35f - 0.2f * i;
                    plate.Place(Move(EngineX, EngineY + 0.96f * Key(z, EngineRadius) - 0.012f, z) * Turn(-14f, 0f, 0f), () => plate.Box(Vector3.zero, new Vector3(0.24f, 0.03f, 0.13f), 0.008f));
                }
                hull.Place(Move(EngineX, EngineY, 0f) * Turn(90f, 0f, 0f), () =>
                {
                    // the lip, and the fan and its spinner just behind it: the wing passes through the cowling further back
                    steel.Lathe(new[] { 0.385f, 0.42f, 0.378f, 0.5f, 0.362f, 0.56f, 0.335f, 0.595f, 0.31f, 0.6f, 0.288f, 0.575f, 0.275f, 0.5f, 0.272f, 0.44f }, 48);
                    dark.Lathe(new[] { 0.3f, 0.44f, 0f, 0.44f }, 24);
                    steel.Lathe(new[] { 0.11f, 0.44f, 0.1f, 0.5f, 0.07f, 0.57f, 0.035f, 0.62f, 0f, 0.645f }, 24);
                    for (int i = 0; i < 18; i++)
                        hull.Place(Turn(0f, 20f * i, 0f), () => dark.Blade(new[] { new Vector3(0.09f, 0.478f, 0f), new Vector3(0.19f, 0.478f, 0f), new Vector3(0.27f, 0.478f, 0f) },
                            new Vector3(0f, 0.55f, 0.83f), t => 0.03f + 0.012f * t, t => 0.03f + 0.012f * t, t => 0.01f));

                    // the nozzle: overlapping petals round a scorched liner, a flame holder and the fire behind it
                    Band(scorched, 0.3f, 0.345f, -3.07f, -2.99f, 48);
                    for (int i = 0; i < 14; i++)
                    {
                        var rings = new Vector3[5][];
                        for (int step = 0; step < 5; step++)
                        {
                            float height = Mathf.Lerp(-3.46f, -3.04f, step / 4f), radius = Mathf.Lerp(0.268f, 0.338f, Mathf.Pow(step / 4f, 0.7f)) - (i % 2 == 0 ? 0f : 0.011f);
                            rings[step] = new Vector3[8];
                            for (int k = 0; k < 4; k++)
                            {
                                float angle = (i + (k / 3f - 0.5f) * 1.14f) * 2f * Mathf.PI / 14f;
                                var around = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                                rings[step][k] = around * radius + Vector3.up * height;
                                rings[step][7 - k] = around * (radius - 0.01f) + Vector3.up * height;
                            }
                        }
                        scorched.Skin(rings, true, true, true);
                    }
                    scorched.Lathe(new[] { 0.3f, -2.7f, 0.28f, -3.2f, 0.262f, -3.44f }, 32);
                    scorched.Lathe(new[] { 0f, -3.22f, 0.06f, -3.06f, 0.115f, -2.86f, 0.13f, -2.75f }, 20);
                    scorched.Place(Move(0f, -2.96f, 0f), () =>
                    {
                        scorched.Torus(0.19f, 0.014f, 28, 6);
                        for (int i = 0; i < 8; i++) scorched.Place(Turn(0f, 45f * i, 0f), () => scorched.Box(new Vector3(0.2f, 0f, 0f), new Vector3(0.2f, 0.03f, 0.012f)));
                    });
                    jet.Lathe(new[] { 0f, -2.74f, 0.3f, -2.74f }, 24);
                    jet.Lathe(new[] { 0.3f, -2.74f, 0.297f, -2.9f }, 24);
                });
                // a pressure tank strapped down between the fuselage and the cowling
                hull.Place(Move(0.775f, 0.17f, -1.75f) * Turn(90f, 0f, 0f), () =>
                {
                    steel.Lathe(new[] { 0f, -0.42f, 0.055f, -0.4f, 0.092f, -0.33f, 0.1f, -0.24f, 0.1f, 0.24f, 0.092f, 0.33f, 0.055f, 0.4f, 0f, 0.42f }, 16);
                    foreach (float strap in new[] { -0.2f, 0.2f }) Band(dark, 0.098f, 0.108f, strap - 0.02f, strap + 0.02f, 16);
                    dark.Lathe(new[] { 0.025f, 0.4f, 0.025f, 0.46f, 0f, 0.46f }, 8);
                });
                dark.Tube(Path(5, new Vector3(0.775f, 0.17f, -1.3f), new Vector3(0.8f, 0.3f, -1.15f), new Vector3(0.95f, 0.36f, -1.1f), new Vector3(EngineX - 0.25f, EngineY + 0.32f, -1.05f)), 0.014f, 6);
                // fairings over the flap tracks, and a fence on the wing
                foreach (float along in new[] { 0.42f, 0.72f })
                    hull.Place(Move(Wing(0.85f, along) + new Vector3(0f, -0.03f, 0.05f)) * Turn(90f, 0f, 0f), () => plate.Lathe(new[] { 0f, -0.42f, 0.035f, -0.3f, 0.055f, 0f, 0.045f, 0.22f, 0f, 0.36f }, 10));
                Vector3 fenceFront = Wing(0.41f, 0.5f), fenceBack = Wing(0.2f, 0.5f);
                hull.Place(Aim(fenceBack, fenceFront, Vector3.up), () => steel.Box(new Vector3(0f, 0.02f, Vector3.Distance(fenceBack, fenceFront) / 2f), new Vector3(0.012f, 0.1f, Vector3.Distance(fenceBack, fenceFront)), 0.004f));
                // struts and a fuel line from the fuselage to the cowling, left in the open
                foreach (float z in new[] { -1.25f, -2.3f })
                {
                    steel.Rod(new Vector3(BodyHalf(z, 0.42f) - 0.03f, 0.42f, z), new Vector3(EngineX - 0.26f, EngineY + 0.3f, z - 0.1f), 0.022f, 8);
                    dark.Ball(new Vector3(EngineX - 0.27f, EngineY + 0.3f, z - 0.1f), 0.04f, 8);
                }
                dark.Tube(Path(6, new Vector3(BodyHalf(-1.7f, 0.3f) - 0.03f, 0.3f, -1.7f), new Vector3(0.85f, 0.36f, -1.75f), new Vector3(EngineX - 0.3f, EngineY + 0.26f, -1.8f)), 0.016f, 6);

                // The cannon: a fairing against the cowling and a barrel in a finned jacket ending in a brake.
                hull.Place(Move(1.7f, -0.3f, 0f) * Turn(90f, 0f, 0f), () =>
                {
                    hull.Lathe(new[] { 0f, -1.35f, 0.05f, -1.2f, 0.1f, -0.8f, 0.115f, -0.2f, 0.115f, 0.7f, 0.1f, 1.25f, 0.08f, 1.5f, 0.045f, 1.5f }, 20);
                    steel.Lathe(new[] { 0.04f, 1.45f, 0.04f, 2.31f }, 14);
                    for (int i = 0; i < 8; i++) hull.Place(Turn(0f, 45f * i, 0f), () => dark.Box(new Vector3(0f, 1.92f, 0.052f), new Vector3(0.006f, 0.6f, 0.03f)));
                    foreach (float height in new[] { 1.6f, 1.92f, 2.22f }) Band(dark, 0.04f, 0.068f, height - 0.015f, height + 0.015f, 16);
                    dark.Lathe(new[] { 0.04f, 2.3f, 0.058f, 2.32f, 0.058f, 2.5f, 0.03f, 2.5f, 0.03f, 2.3f }, 16);
                });

                // The skid: a tube turned up at the front on two sprung legs, each with a brace.
                float ground = -Core.Ship.Clearance;
                steel.Tube(Path(6, new Vector3(0.9f, ground + 0.05f, -1.35f), new Vector3(0.9f, ground + 0.05f, 0.2f), new Vector3(0.9f, ground + 0.05f, 1.5f), new Vector3(0.9f, ground + 0.12f, 1.78f), new Vector3(0.9f, ground + 0.24f, 1.92f)), 0.045f, 10);
                foreach (float z in new[] { 1.15f, -0.9f })
                {
                    Vector3 top = new Vector3(BodyHalf(z, -0.3f) - 0.04f, -0.3f, z + 0.05f), foot = new Vector3(0.9f, ground + 0.06f, z), knee = Vector3.Lerp(top, foot, 0.55f);
                    dark.Rod(top, knee, 0.05f, 12);
                    steel.Rod(knee, foot, 0.03f, 12);
                    dark.Ball(top, 0.075f, 10);
                    dark.Box(foot, new Vector3(0.13f, 0.1f, 0.16f), 0.02f);
                    dark.Box(new Vector3(0.9f, ground + 0.01f, z), new Vector3(0.13f, 0.02f, 0.42f), 0.006f);
                    steel.Rod(new Vector3(BodyHalf(z - 0.45f, -0.36f) - 0.03f, -0.36f, z - 0.45f), Vector3.Lerp(top, foot, 0.8f), 0.016f, 6);
                    dark.Tube(Path(5, top + new Vector3(0.05f, 0.02f, 0.03f), knee + new Vector3(0.07f, 0f, 0.04f), foot + new Vector3(0.03f, 0.08f, 0.05f)), 0.008f, 5);
                }

                // A pipe run under the belly, a grab handle and a step by the cabin and a landing light.
                var run = new Vector3[16];
                for (int i = 0; i < run.Length; i++)
                {
                    float z = Mathf.Lerp(-2.1f, 1.9f, i / (run.Length - 1f));
                    run[i] = new Vector3(0.2f, -BodyHalfDown(z, 0.2f) - 0.012f, z);
                }
                steel.Tube(run, 0.02f, 8);
                for (int i = 1; i < run.Length; i += 3) dark.Box(run[i], new Vector3(0.07f, 0.06f, 0.05f), 0.008f);
                foreach (float z in new[] { 0.75f, 1.25f })
                {
                    float side = BodyHalf(z, 0.42f);
                    steel.Tube(Path(4, new Vector3(side - 0.01f, 0.42f, z - 0.1f), new Vector3(side + 0.05f, 0.42f, z - 0.07f), new Vector3(side + 0.05f, 0.42f, z + 0.07f), new Vector3(side - 0.01f, 0.42f, z + 0.1f)), 0.011f, 6);
                }
                dark.Box(new Vector3(BodyHalf(1f, -0.2f) + 0.04f, -0.2f, 1f), new Vector3(0.16f, 0.025f, 0.3f), 0.008f);
                white.Ball(new Vector3(0.16f, Key(2.9f, BodyBottom) + 0.015f, 2.9f), new Vector3(0.05f, 0.03f, 0.05f), 10);
            });

            // Port is red and starboard green, on the winglets' leading edges.
            Vector3 lamp = Winglet(0.5f, 0.9f) + new Vector3(0f, 0f, 0.01f);
            green.Ball(lamp, 0.035f, 10);
            red.Ball(new Vector3(-lamp.x, lamp.y, lamp.z), 0.035f, 10);

            // The cabin. Cockpit moves and lights everything on its dash, so each part that does
            // either is a mesh of its own under "Cockpit", named for Cockpit to find; the rest is
            // one mesh to a material, as outside. Across the dash run the weapons' lamps, the
            // throttle, a screen for speed, a ball that shows the horizon and screens for range
            // and heat, with the dials beneath.
            Material Glow(float r, float g, float b) => Shapes.Material("Glow", new Color(r, g, b));
            Material moulded = Paint(new Color(0.075f, 0.08f, 0.09f), 0f, 0.3f), switchgear = Paint(new Color(0.32f, 0.33f, 0.35f), 0.4f, 0.45f), hide = Paint(new Color(0.3f, 0.19f, 0.12f), 0f, 0.35f);
            Material warm = Glow(3f, 1.4f, 0.2f), unlit = Shapes.Material("Glow", Cockpit.Dark);
            Mesh Shape(string name, Action<MeshKit> build)
            {
                var kit = new MeshKit();
                build(kit);
                return kit.Mesh(name);
            }
            Transform Moving(Transform parent, string name, Vector3 at, Mesh mesh, Material material) => Shapes.Part(parent, name, mesh, material, at, Vector3.one, false).transform;
            TextMesh Letter(Transform parent, string text, Vector3 at, float height, float bright = 0.7f)
            {
                var readout = Shapes.Readout(parent, at, height, TextAnchor.MiddleCenter, Cockpit.Ink * bright);
                readout.text = text;
                return readout;
            }
            // A lamp is a tile that Cockpit lights behind its caption.
            Transform Lamp(string caption, Vector3 at, Mesh tile, float height)
            {
                var light = Moving(cockpit, caption, at, tile, unlit);
                Letter(light, caption, new Vector3(0f, 0f, -0.003f), height);
                return light;
            }

            trim.Place(Move(0f, 0f, 1.975f), () => trim.Plate(Outline(-0.63f, 0.34f, 0.63f, 0.34f, 0.63f, 0.58f, 0.57f, 0.66f, 0.5f, 0.69f, -0.5f, 0.69f, -0.57f, 0.66f, -0.63f, 0.58f), 0.06f, 0.006f));
            // each screen has an empty transform at its middle, carrying its figure and the legend under it
            (string name, float x, string legend)[] readouts = { ("SpeedScreen", -0.185f, "SPEED  M/S"), ("RangeScreen", 0.185f, ""), ("HeatScreen", 0.4f, "OUTSIDE  °C") };
            foreach (var (name, x, legend) in readouts)
            {
                const float y = 0.605f;
                screens.Quad(new Vector3(x - 0.097f, y - 0.043f, 1.944f), new Vector3(x - 0.097f, y + 0.043f, 1.944f), new Vector3(x + 0.097f, y + 0.043f, 1.944f), new Vector3(x + 0.097f, y - 0.043f, 1.944f));
                foreach (float side in new[] { -1f, 1f })
                {
                    fittings.Box(new Vector3(x, y + side * 0.046f, 1.94f), new Vector3(0.206f, 0.006f, 0.01f), 0.002f);
                    fittings.Box(new Vector3(x + side * 0.1f, y, 1.94f), new Vector3(0.006f, 0.092f, 0.01f), 0.002f);
                }
                var screen = Child(cockpit, name);
                screen.localPosition = new Vector3(x, y, 1.94f);
                Letter(screen, "", new Vector3(0f, 0.009f, -0.004f), 0.04f, 1f);
                Letter(screen, legend, new Vector3(0f, -0.029f, -0.004f), 0.011f).name = "Legend";
            }

            // Fittings are built standing on y, then laid on whatever face they belong to.
            void On(Vector3 at, Vector3 normal, Action build) => hull.Place(Aim(at, at + normal, Mathf.Abs(normal.y) > 0.9f ? Vector3.forward : Vector3.up) * Turn(90f, 0f, 0f), build);
            void Vent(Vector3 at, Vector3 normal, float width, float height) => On(at, normal, () =>
            {
                foreach (float side in new[] { -1f, 1f })
                {
                    fittings.Box(new Vector3(side * width / 2f, 0.004f, 0f), new Vector3(0.008f, 0.012f, height + 0.008f), 0.002f);
                    fittings.Box(new Vector3(0f, 0.004f, side * height / 2f), new Vector3(width + 0.008f, 0.012f, 0.008f), 0.002f);
                }
                for (int i = 0; i < 6; i++)
                    hull.Place(Move(0f, 0.004f, (i - 2.5f) / 6f * height) * Turn(35f, 0f, 0f), () => trim.Box(Vector3.zero, new Vector3(width, 0.002f, height / 6f)));
            });

            // A gauge is a dark face in a bezel under its name. A dial has a mark for each of its
            // figures from seven o'clock round to five, an amber band over the share of that sweep
            // that spells trouble and a needle for Cockpit to turn; `turned` swings the whole scale round.
            var face = Vector3.back;
            const float dial = 0.04f, sweep = Cockpit.Sweep;
            Vector3 Gauge(string name, float x, Action marked)
            {
                var centre = new Vector3(x, 0.49f, 1.945f);
                On(centre, face, () =>
                {
                    fittings.Lathe(new[] { dial * 1.15f, 0f, dial * 1.15f, 0.008f, dial * 1.06f, 0.011f, dial, 0.005f }, 24);
                    screens.Lathe(new[] { dial, 0.005f, 0f, 0.005f }, 24);
                    marked();
                });
                Letter(cockpit, name, centre + new Vector3(0f, dial + 0.016f, -0.002f), 0.011f);
                // what moves over the face rides just clear of its marks
                return centre + face * 0.007f;
            }
            Mesh needle = Shape("Needle", kit => kit.Box(new Vector3(0f, 0.3f * dial, 0f), new Vector3(0.003f, 0.9f * dial, 0.0012f)));
            void Dial(string name, float x, float turned, float cautionFrom, float cautionTo, params string[] figures)
            {
                float Mark(int i) => turned + Mathf.Lerp(-sweep, sweep, i / (figures.Length - 1f));
                var mount = Child(cockpit, name);
                mount.localPosition = Gauge(name, x, () =>
                {
                    for (int i = 0; i < figures.Length; i++)
                        hull.Place(Turn(0f, Mark(i), 0f), () => marks.Box(new Vector3(0f, 0.0056f, -dial * 0.84f), new Vector3(0.002f, 0.001f, dial * 0.16f)));
                    if (cautionTo > cautionFrom)
                        hull.Place(Turn(0f, 180f + turned + Mathf.Lerp(-sweep, sweep, cautionFrom), 0f), () => amber.Lathe(new[] { dial * 0.99f, 0.0054f, dial * 0.93f, 0.0054f }, 24, null, 2f * sweep * (cautionTo - cautionFrom)));
                    fittings.Lathe(new[] { dial * 0.12f, 0.005f, dial * 0.12f, 0.009f, 0f, 0.009f }, 8);
                });
                mount.localRotation = Quaternion.Euler(0f, 0f, -turned);
                Moving(mount, "Needle", Vector3.zero, needle, warm);
                for (int i = 0; i < figures.Length; i++)
                    if (figures[i] != "") Letter(cockpit, figures[i], mount.localPosition + Quaternion.Euler(0f, 0f, -Mark(i)) * Vector3.up * (0.56f * dial) + Vector3.forward * 0.0005f, 0.2f * dial);
            }
            Dial("THRUST", -0.235f, 0f, 0.5f, 1f, "0", "", "100", "", "200");
            Dial("ALTITUDE", 0.135f, 0f, 0f, 0f, "1", "", "100", "", "10K", "", "1M");
            // climbing swings its needle up from nine o'clock and sinking swings it down
            Dial("CLIMB", 0.235f, -90f, 0f, 0f, "", "100", "10", "0", "10", "100", "");
            Dial("HULL", 0.35f, 0f, 0f, 0.25f, "0", "", "50", "", "100");
            Dial("TEMP", 0.45f, 0f, 2f / 3f, 1f, "0", "", "", "", "", "MAX", "");
            // a scope whose spot shows which way the pilot has the thrusters pushing, across and up
            var thrusters = Child(cockpit, "Thrusters");
            thrusters.localPosition = Gauge("THRUSTERS", -0.135f, () =>
            {
                marks.Box(new Vector3(0f, 0.0056f, 0f), new Vector3(1.7f * dial, 0.001f, 0.0012f));
                marks.Box(new Vector3(0f, 0.0056f, 0f), new Vector3(0.0012f, 0.001f, 1.7f * dial));
            });
            thrusters.localScale = Vector3.one * (0.6f * dial);
            Moving(thrusters, "Spot", Vector3.zero, Shape("Spot", kit => kit.Ball(Vector3.zero, new Vector3(0.2f, 0.2f, 0.05f), 10)), Glow(0.4f, 2.2f, 3f));

            // The ball that shows the horizon sits half sunk in the dash and faces the pilot's eye,
            // which Player puts above it: sky over ground, with bars to count the pitch by.
            const float globe = 0.056f;
            Vector3 ball = new Vector3(0f, 0.592f, 1.973f), eye = new Vector3(0f, 0.95f, 1.2f);
            var attitude = Child(cockpit, "Attitude");
            attitude.localPosition = ball;
            attitude.localRotation = Quaternion.Euler(Mathf.Atan2(eye.y - ball.y, ball.z - eye.z) * Mathf.Rad2Deg, 0f, 0f);
            float[] Dome(float from, float to)
            {
                var profile = new float[18];
                for (int i = 0; i < 9; i++)
                {
                    float angle = Mathf.Lerp(from, to, i / 8f) * Mathf.Deg2Rad;
                    profile[2 * i] = globe * Mathf.Cos(angle);
                    profile[2 * i + 1] = globe * Mathf.Sin(angle);
                }
                return profile;
            }
            var horizon = Moving(attitude, "Horizon", Vector3.zero, Shape("Sky", kit => kit.Lathe(Dome(0f, 90f), 32)), Glow(0.12f, 0.45f, 0.95f));
            Moving(horizon, "Ground", Vector3.zero, Shape("Ground", kit => kit.Lathe(Dome(-90f, 0f), 32)), Glow(0.4f, 0.22f, 0.08f));
            Moving(horizon, "Lines", Vector3.zero, Shape("Lines", kit =>
            {
                kit.Lathe(new[] { globe + 0.0004f, -0.0012f, globe + 0.0004f, 0.0012f }, 32);
                foreach (int pitch in new[] { -60, -30, -20, -10, 10, 20, 30, 60 })
                    kit.Place(Turn(pitch, 0f, 0f), () => kit.Box(new Vector3(0f, 0f, -globe), new Vector3(pitch % 20 == 0 ? 0.026f : 0.013f, 0.0014f, 0.001f)));
            }), Glow(1.5f, 1.6f, 1.7f));
            On(new Vector3(ball.x, ball.y, 1.945f), face, () => fittings.Lathe(new[] { 0.062f, 0f, 0.062f, 0.012f, 0.056f, 0.016f, 0.0475f, 0.01f, 0.0475f, 0f }, 32));
            // the ship itself is a pair of wings fixed over the ball
            hull.Place(Move(ball) * Matrix4x4.Rotate(attitude.localRotation), () =>
            {
                foreach (float side in new[] { -1f, 1f }) amber.Box(new Vector3(side * 0.024f, 0f, -globe - 0.003f), new Vector3(0.02f, 0.003f, 0.001f));
                amber.Box(new Vector3(0f, 0f, -globe - 0.003f), new Vector3(0.004f, 0.004f, 0.001f));
            });

            // The throttle is a lever through a slot in the dash, hinged behind it: up for more. The
            // hinge is a little above the slot's middle, so that from the pilot's eye the handle rests there.
            var slot = new Vector3(-0.345f, 0.545f, 1.9445f);
            screens.Quad(slot + new Vector3(-0.006f, -0.052f, 0f), slot + new Vector3(-0.006f, 0.052f, 0f), slot + new Vector3(0.006f, 0.052f, 0f), slot + new Vector3(0.006f, -0.052f, 0f));
            Moving(cockpit, "Throttle", new Vector3(slot.x, slot.y + 0.012f, 2.005f), Shape("Throttle", kit =>
            {
                kit.Box(new Vector3(0f, 0f, -0.04f), new Vector3(0.005f, 0.008f, 0.08f));
                kit.Place(Move(0f, 0f, -0.085f) * Turn(0f, 0f, 90f), () => kit.Lathe(new[] { 0f, -0.024f, 0.011f, -0.02f, 0.011f, 0.02f, 0f, 0.024f }, 12));
            }), switchgear);
            Letter(cockpit, "THROTTLE", slot + new Vector3(0f, 0.096f, -0.002f), 0.011f);
            Letter(cockpit, "BOOST", slot + new Vector3(0f, 0.066f, -0.002f), 0.008f);
            Letter(cockpit, "REVERSE", slot + new Vector3(0f, -0.066f, -0.002f), 0.008f);

            // The weapons: a lamp for each, and under them a selector that points at the one that is live.
            var selector = new Vector3(-0.45f, 0.492f, 1.945f);
            Mesh gunLamp = Shape("Lamp", kit => kit.Box(Vector3.zero, new Vector3(0.1f, 0.018f, 0.004f), 0.001f));
            for (int i = 0; i < Cockpit.Racks.Length; i++)
            {
                Lamp(Cockpit.Racks[i], new Vector3(selector.x, 0.637f - 0.021f * i, 1.943f), gunLamp, 0.011f);
                Letter(cockpit, $"{i + 1}", selector + Quaternion.Euler(0f, 0f, (1.5f - i) * Cockpit.Detent) * Vector3.up * 0.037f + face * 0.002f, 0.01f);
            }
            Moving(cockpit, "Selector", selector, Shape("Selector", kit => kit.Place(Turn(-90f, 0f, 0f), () =>
            {
                kit.Lathe(new[] { 0.024f, 0f, 0.024f, 0.004f, 0.016f, 0.006f, 0.016f, 0.018f, 0f, 0.018f }, 16);
                kit.Box(new Vector3(0f, 0.014f, 0.004f), new Vector3(0.007f, 0.016f, 0.042f), 0.002f);
            })), switchgear);

            foreach (float side in new[] { -1f, 1f })
            {
                Vent(new Vector3(side * 0.565f, 0.6f, 1.945f), face, 0.05f, 0.035f);
                foreach (float height in new[] { 0.355f, 0.675f })
                    On(new Vector3(side * (height > 0.5f ? 0.44f : 0.61f), height, 1.945f), face, () => fittings.Lathe(new[] { 0.005f, 0f, 0.005f, 0.002f, 0f, 0.003f }, 6));
            }

            // The hood over the dash, running forward to the foot of the windscreen as the coaming. It
            // is level across the screens and turns down at its ends.
            var hood = new List<Vector3[]>();
            for (int i = 0; i <= 28; i++)
            {
                float across = i / 14f - 1f, droop = Mathf.Pow(Mathf.Max(0f, Mathf.Abs(across) - 0.8f) / 0.2f, 2f);
                var ring = new Vector3[22];
                for (int k = 0; k < 11; k++)
                {
                    float t = k / 10f, z = Mathf.Lerp(1.88f, 2.34f - 0.2f * across * across, t), x = across * Mathf.Lerp(0.63f, 0.5f, t);
                    float top = Key(t, 0f, 0.7f, 0.15f, 0.722f, 0.4f, 0.725f, 1f, 0.66f) - droop * Mathf.Lerp(0.085f, 0.16f, t);
                    ring[k] = new Vector3(x, top, z);
                    ring[21 - k] = new Vector3(x, top - Mathf.Lerp(0.02f, 0.05f, t), z + (k == 0 ? 0.006f : 0f));
                }
                hood.Add(ring);
            }
            trim.crease = 60f;
            trim.Skin(hood, true, true, true);
            trim.crease = 40f;

            // A pedestal runs down from the dash between the pilot's knees, and the stick stands on
            // it, raked back to the hand: a boot, a column and a wrapped grip under a head that
            // carries the firing button.
            trim.Place(Turn(0f, -90f, 0f), () => trim.Plate(Outline(1.5f, -0.12f, 1.5f, 0.17f, 1.74f, 0.34f, 1.98f, 0.34f, 1.98f, -0.12f), 0.24f, 0.012f));
            var rake = Turn(-14f, 0f, 0f);
            void Column(MeshKit kit) => kit.Place(rake, () =>
            {
                kit.Lathe(new[] { 0.04f, 0f, 0.036f, 0.02f, 0.02f, 0.05f, 0.013f, 0.06f }, 14);
                kit.Rod(new Vector3(0f, 0.05f, 0f), new Vector3(0f, 0.225f, 0f), 0.011f, 10);
                kit.Box(new Vector3(0f, 0.325f, 0.004f), new Vector3(0.05f, 0.036f, 0.046f), 0.01f);
            });
            void Grip(MeshKit kit) => kit.Place(rake, () => kit.Sweep(Path(6, new Vector3(0f, 0.215f, 0f), new Vector3(0f, 0.265f, 0.004f), new Vector3(0f, 0.31f, 0.004f)),
                i => Oval(12, 0.02f + 0.002f * Mathf.Cos(i * Mathf.PI / 2f), 0.023f + 0.002f * Mathf.Cos(i * Mathf.PI / 2f)), Vector3.forward));
            var pivot = new Vector3(0f, 0.34f, 1.78f);
            var stick = Moving(cockpit, "Stick", pivot, Shape("Stick", Column), moulded);
            Moving(stick, "Grip", Vector3.zero, Shape("Grip", Grip), hide);
            Moving(stick, "Trigger", rake.MultiplyPoint3x4(new Vector3(0.011f, 0.33f, -0.02f)), Shape("Trigger", kit => kit.Ball(Vector3.zero, 0.0065f, 10)), unlit);
            // from outside it is seen standing where the pilot left it
            dark.Place(Move(pivot), () =>
            {
                Column(dark);
                Grip(dark);
            });

            // A side console runs along the left wall to the dash, low enough to hide none of it.
            hull.Place(Size(-1f, 1f, 1f), () =>
            {
                var console = new List<Vector3[]>();
                for (int i = 0; i <= 12; i++)
                {
                    float z = Mathf.Lerp(0.5f, 1.96f, i / 12f), top = Mathf.Lerp(0.4f, 0.44f, i / 12f);
                    console.Add(new[] { new Vector3(0.44f, -0.12f, z), new Vector3(BodyHalf(z, -0.12f) - 0.02f, -0.12f, z), new Vector3(BodyHalf(z, top) - 0.02f, top, z), new Vector3(0.47f, top, z), new Vector3(0.44f, top - 0.03f, z) });
                }
                trim.Skin(console, true, true, true);
            });
            Vent(new Vector3(-0.439f, 0.3f, 1.75f), Vector3.right, 0.12f, 0.06f);

            // Along the right wall is a low cradle for the recovery machine: "Copilot"
            // is its base, far enough forward that whoever sits there shows at the edge of the view.
            var guest = new Vector3(0.47f, 0.43f, 2f);
            foreach (float side in new[] { -1f, 1f })
                trim.Rod(guest + new Vector3(side * 0.1f, -0.4f, 0), guest + new Vector3(side * 0.1f, -0.09f, 0), 0.025f, 12);
            Child(cockpit, "Copilot").localPosition = guest;
            trim.Box(guest + new Vector3(0f, -0.09f, 0f), new Vector3(0.3f, 0.06f, 0.3f), 0.02f);
            Pillow(leather, guest + new Vector3(0f, -0.035f, 0.01f), new Vector3(0.28f, 0.07f, 0.28f));

            // A panel under the canopy bow: a switch and its lamp for each thing the pilot sets, the
            // levers standing clear above it when on, and under those a lamp for each warning. Then
            // pedals and the seat with its harness.
            float bow = Key(1.65f, BodyTop) - 0.065f;
            trim.Box(new Vector3(0f, bow - 0.007f, 1.635f), new Vector3(0.3f, 0.054f, 0.07f), 0.008f);
            Mesh podLamp = Shape("Lamp", kit => kit.Box(Vector3.zero, new Vector3(0.042f, 0.016f, 0.004f), 0.001f));
            Mesh lever = Shape("Switch", kit => kit.Rod(Vector3.zero, new Vector3(0f, 0f, -0.028f), 0.0028f, 6, 0.0042f));
            string[] warnings = { "HEAT", "DAMAGE", "RAD", "LANDED", "BOOST", "GATE" }, modes = { "ASSIST", "AUTO", "LAND" };
            for (int i = 0; i < warnings.Length; i++) Lamp(warnings[i], new Vector3(0.047f * (i - 2.5f), bow - 0.02f, 1.598f), podLamp, 0.0085f);
            for (int i = 0; i < modes.Length; i++)
            {
                var mode = Lamp(modes[i], new Vector3(0.09f * (i - 1f) - 0.012f, bow + 0.004f, 1.598f), podLamp, 0.0085f);
                var hinge = new Vector3(0.034f, 0f, 0.002f);
                Moving(mode, "Switch", hinge, lever, switchgear);
                On(mode.localPosition + hinge, face, () => fittings.Lathe(new[] { 0.008f, 0f, 0.008f, 0.004f, 0.005f, 0.006f, 0f, 0.006f }, 6));
            }
            hull.Mirror(() =>
            {
                fittings.Place(Move(0.3f, 0.02f, 1.92f) * Turn(-25f, 0f, 0f), () => fittings.Box(Vector3.zero, new Vector3(0.1f, 0.14f, 0.012f), 0.004f));
                fittings.Rod(new Vector3(0.3f, -0.12f, 2.02f), new Vector3(0.3f, 0.02f, 1.925f), 0.01f, 6);
                // seat rails, and the straps over the pilot's shoulders
                fittings.Box(new Vector3(0.2f, -0.1f, 1f), new Vector3(0.03f, 0.04f, 0.8f), 0.006f);
                fittings.Place(Move(0.1f, 0.62f, 0.9f) * Turn(-12f, 0f, 0f), () => fittings.Box(Vector3.zero, new Vector3(0.045f, 0.72f, 0.006f)));
                fittings.Box(new Vector3(0.1f, 0.46f, 0.934f), new Vector3(0.055f, 0.04f, 0.012f), 0.003f);
            });
            trim.Box(new Vector3(0f, 0.02f, 0.98f), new Vector3(0.54f, 0.22f, 0.62f), 0.03f);
            trim.Place(Move(0f, 0.6f, 0.79f) * Turn(-12f, 0f, 0f), () => trim.Box(Vector3.zero, new Vector3(0.5f, 0.86f, 0.05f), 0.02f));
            Pillow(leather, new Vector3(0f, 0.18f, 1.02f), new Vector3(0.48f, 0.1f, 0.5f));
            leather.Place(Move(0f, 0.6f, 0.85f) * Turn(78f, 0f, 0f), () => Pillow(leather, Vector3.zero, new Vector3(0.44f, 0.09f, 0.7f)));
            leather.Place(Move(0f, 1.06f, 0.79f) * Turn(80f, 0f, 0f), () => Pillow(leather, Vector3.zero, new Vector3(0.26f, 0.09f, 0.2f)));

            foreach (var kit in new[] { hull, plate, accent, noseHull, nosePlate, hazard }) kit.tiling = 5f;
            Material shell = Paint(new Color(0.98f, 1f, 1.02f), 0.15f, 0.4f), grey = Paint(new Color(0.52f, 0.55f, 0.58f), 0.3f, 0.4f), gunmetal = Paint(new Color(0.1f, 0.105f, 0.12f), 0.5f, 0.35f);
            Material orange = Paint(new Color(1f, 0.36f, 0.1f), 0.1f, 0.4f), bare = Paint(new Color(0.62f, 0.64f, 0.66f), 0.85f, 0.6f), burnt = Paint(new Color(0.3f, 0.21f, 0.16f), 0.8f, 0.45f);
            Material yellow = Paint(new Color(1.2f, 0.9f, 0.1f), 0f, 0.35f), panes = Shapes.Material("Water", new Color(0.35f, 0.75f, 0.95f, 0.4f));

            void Add(Transform parent, string name, Mesh mesh, Material material, bool shadows = true)
            {
                if (mesh.vertexCount > 0) Shapes.Part(parent, name, mesh, material, Vector3.zero, Vector3.one, shadows);
            }
            Add(exterior, "Hull", hull.Mesh("Hull"), shell);
            Add(exterior, "Hatches", plate.Mesh("Hatches"), grey);
            Add(exterior, "Paint", accent.Mesh("Paint"), orange);
            Add(exterior, "Mechanism", dark.Mesh("Mechanism"), gunmetal);
            Add(exterior, "Metal", steel.Mesh("Metal"), bare);
            Add(exterior, "Nozzles", scorched.Mesh("Nozzles"), burnt);
            Add(exterior, "Stripes", hazard.Mesh("Stripes"), yellow);
            Add(exterior, "Canopy", glass.Mesh("Canopy"), panes, false);
            Add(exterior, "Exhaust", jet.Mesh("Exhaust"), Shapes.Material("Glow", new Color(2f, 5f, 8f)), false);
            Add(exterior, "Port", red.Mesh("Port"), Shapes.Material("Glow", new Color(6f, 0.3f, 0.2f)), false);
            Add(exterior, "Starboard", green.Mesh("Starboard"), Shapes.Material("Glow", new Color(0.3f, 6f, 0.8f)), false);
            Add(exterior, "Lamps", white.Mesh("Lamps"), Shapes.Material("Glow", new Color(5f, 5f, 4.5f)), false);
            // the nose and the cabin are in both views
            var shared = new (string name, Mesh mesh, Material material, bool shadows)[]
            {
                ("Nose", noseHull.Mesh("Nose"), shell, true), ("Nose hatches", nosePlate.Mesh("Nose hatches"), grey, true), ("Nose mechanism", noseDark.Mesh("Nose mechanism"), gunmetal, true),
                ("Cabin", trim.Mesh("Cabin"), moulded, true), ("Switchgear", fittings.Mesh("Switchgear"), switchgear, true), ("Seat", leather.Mesh("Seat"), hide, true),
                ("Screens", screens.Mesh("Screens"), Glow(0.02f, 0.09f, 0.11f), false), ("Warnings", amber.Mesh("Warnings"), warm, false), ("Markings", marks.Mesh("Markings"), Glow(0.5f, 0.8f, 0.9f), false),
            };
            foreach (var part in shared)
            {
                Add(exterior, part.name, part.mesh, part.material, part.shadows);
                Add(cockpit, part.name, part.mesh, part.material, part.shadows);
            }
            cockpit.gameObject.AddComponent<Cockpit>();
            return root;
        }

        // A chain hanging between two points: oval links, each turned a quarter from the last.
        static void Chain(MeshKit kit, Vector3 from, Vector3 to, float sag, float link, float wire, int sides = 4)
        {
            Vector3 At(float t) => Vector3.Lerp(from, to, t) + Vector3.down * (sag * 4f * t * (1f - t));
            float length = 0f;
            for (int i = 0; i < 16; i++) length += Vector3.Distance(At(i / 16f), At((i + 1) / 16f));
            int links = Mathf.Max(1, Mathf.RoundToInt(length / (link - 2.6f * wire)));
            var loop = new Vector3[13];
            for (int i = 0; i < 13; i++)
            {
                // two half circles joined by straights, lying in the link's own xz plane
                float angle = 2f * Mathf.PI * (i % 12) / 12f, across = link * 0.3f - wire;
                loop[i] = new Vector3(Mathf.Cos(angle) * across, 0f, Mathf.Sin(angle) * across + Mathf.Sign(Mathf.Sin(angle + 1e-4f)) * (link / 2f - across - wire));
            }
            for (int i = 0; i < links; i++)
            {
                Vector3 centre = At((i + 0.5f) / links), along = At((i + 0.6f) / links) - At((i + 0.4f) / links);
                kit.Place(Aim(centre, centre + along, Mathf.Abs(along.normalized.y) > 0.95f ? Vector3.forward : Vector3.up) * Turn(0f, 0f, i % 2 * 90f), () => kit.Tube(loop, wire, sides, -1f, false, true));
            }
        }

        // A horn: a round section that tapers to a point along a path, ringed with ridges.
        static void Horn(MeshKit kit, Vector3[] path, float radius, int sides = 8, float ridges = 0.1f) =>
            kit.Sweep(path, i =>
            {
                float t = i / (path.Length - 1f);
                return Oval(sides, radius * (1f - t) * (1f + ridges * Mathf.Cos(i * Mathf.PI)) + 0.002f * (1f - t));
            }, Vector3.up, true, false);

        // A five-pointed star of bars inside a ring, in the xy plane, one point straight down.
        static void Pentagram(MeshKit kit, float radius, float bar, bool ring = true)
        {
            Vector3 Point(int i) => new Vector3(Mathf.Cos((-90f + 72f * i) * Mathf.Deg2Rad), Mathf.Sin((-90f + 72f * i) * Mathf.Deg2Rad)) * radius;
            for (int i = 0; i < 5; i++) kit.Rod(Point(i), Point(i + 2), bar, 4);
            if (ring) kit.Place(Turn(90f, 0f, 0f), () => kit.Torus(radius, bar, 40, 4));
        }

        // An enemy fighter about 22 m long with its nose towards +z, inside a ball of radius 12. A
        // beaked skull for a prow, a spine and ribs caging a furnace, forward-raked blade wings that
        // read as an inverted star from ahead and a burning pentagram round its engines. The second
        // mesh is everything that glows.
        public static (Mesh hull, Mesh lights) Raider() => MobileBody(true);

        public static (Mesh shell, Mesh lights) Drone() => MobileBody(false);

        public static Mesh Missile()
        {
            var kit = new MeshKit();
            kit.Place(Turn(90f, 0f, 0f), () =>
            {
                kit.Lathe(new[] { 0f, -0.6f, 0.045f, -0.6f, 0.06f, -0.52f, 0.06f, 0.25f, 0.045f, 0.42f, 0.02f, 0.55f, 0f, 0.6f }, 10);
                Band(kit, 0.06f, 0.066f, -0.2f, -0.16f, 10);
            });
            for (int i = 0; i < 4; i++)
                kit.Place(Turn(0f, 0f, 45f + 90f * i) * Turn(0f, -90f, 0f), () =>
                {
                    kit.Plate(Outline(-0.58f, 0.05f, -0.3f, 0.05f, -0.46f, 0.2f, -0.58f, 0.2f), 0.012f, 0.006f, 0.02f);
                    kit.Plate(Outline(0.2f, 0.05f, 0.32f, 0.05f, 0.22f, 0.12f, 0.18f, 0.12f), 0.01f, 0.005f, 0.012f);
                });
            return kit.Mesh("Missile");
        }

        // The raiders' citadel: a cathedral of blackened iron in space, built to fit inside a ball of
        // the given radius. Root named "Station", its spires along y. A ten-sided keep with furnace
        // windows and five toothed docking maws stands in the middle of a great pentagram: a ribbed
        // ring carrying five towers, with a star of girders across it that burns along its length.
        // Chains hang from tower to tower and up to the spires.
        public static Transform Station(float radius)
        {
            var root = new GameObject("Station").transform;
            var iron = new MeshKit { crease = 30f, tiling = 6f / radius };
            MeshKit rust = new MeshKit(iron) { crease = 30f, tiling = 12f / radius }, fire = new MeshKit(iron), blood = new MeshKit(iron);
            // a point this far round the y axis in degrees, this far out and this high
            Vector3 Round(float angle, float reach, float height) => new Vector3(Mathf.Sin(angle * Mathf.Deg2Rad) * reach, height, Mathf.Cos(angle * Mathf.Deg2Rad) * reach);

            // A pointed window with fire behind iron bars, standing on its sill in the xy plane and looking along +z.
            void Lancet(float width, float height)
            {
                float half = width / 2f, spring = height * 0.7f;
                fire.Plate(Outline(-half, 0f, half, 0f, half, spring, half * 0.55f, height * 0.88f, 0f, height, -half * 0.55f, height * 0.88f, -half, spring), 0.5f);
                iron.Box(new Vector3(0f, height * 0.45f, 0.3f), new Vector3(width * 0.12f, height * 0.9f, 0.5f));
                iron.Box(new Vector3(0f, spring, 0.3f), new Vector3(width, width * 0.12f, 0.5f));
            }

            // everything is laid out in metres for a radius of 250
            iron.Place(Size(radius / 250f), () =>
            {
                // The keep, from the point of its lower spire to the point of its upper one, stepping in at each ledge.
                iron.Place(Turn(0f, 18f, 0f), () =>
                {
                    iron.Lathe(new[] { 0f, -238f, 4f, -210f, 8f, -170f, 12f, -170f, 12f, -160f, 15f, -125f, 20f, -125f, 20f, -112f, 24f, -78f, 31f, -78f, 31f, -70f, 32f, -30f, 32f, 30f, 31f, 70f,
                        25f, 70f, 24f, 76f, 21f, 108f, 17f, 108f, 16f, 114f, 12f, 152f, 9f, 152f, 8.5f, 158f, 5f, 200f, 0f, 240f }, 10);
                    foreach (float height in new[] { -30f, 8f, 40f }) Band(rust, 31.5f, 33.6f, height - 1.2f, height + 1.2f, 10);
                    Band(rust, 24f, 26.5f, 73f, 76f, 10);
                    Band(rust, 16f, 18.5f, 110f, 113f, 10);
                    Band(rust, 12f, 14f, -163f, -159f, 10);
                });
                for (int side = 0; side < 10; side++)
                {
                    float angle = 36f * side;
                    // a pilaster up each corner with a spike where it meets the ledges, then spikes up both spires
                    iron.Place(Turn(0f, angle + 18f, 0f), () =>
                    {
                        iron.Box(new Vector3(0f, -4f, 32.2f), new Vector3(2.6f, 150f, 2.6f), 0.6f);
                        rust.Spike(new Vector3(0f, 71f, 33f), new Vector3(0f, 80f, 45f), 1.5f);
                        rust.Spike(new Vector3(0f, -77f, 33f), new Vector3(0f, -88f, 44f), 1.5f);
                        for (int i = 0; i < 5; i++)
                        {
                            float up = 164f + 13f * i, down = -128f - 17f * i;
                            rust.Spike(new Vector3(0f, up, Key(up, 158f, 8.2f, 200f, 4.8f, 240f, 0f)), new Vector3(0f, up + 6f, Key(up, 158f, 8.2f, 200f, 4.8f, 240f, 0f) + 6f), 0.9f);
                            rust.Spike(new Vector3(0f, down, Key(down, -238f, 0f, -210f, 3.8f, -170f, 7.6f, -125f, 14.4f)), new Vector3(0f, down - 7f, Key(down, -238f, 0f, -210f, 3.8f, -170f, 7.6f, -125f, 14.4f) + 7f), 1f);
                        }
                    });

                    iron.Place(Turn(0f, angle, 0f), () =>
                    {
                        // three rows of tall windows on every face, and one on each tier above
                        foreach (float sill in new[] { -26f, 10f, 44f })
                        foreach (float across in new[] { -4.7f, 4.7f })
                            iron.Place(Move(across, sill, 30.5f), () => Lancet(5f, sill > 40f ? 22f : 27f));
                        iron.Place(Move(0f, 80f, 22.5f) * Turn(-5.4f, 0f, 0f), () => Lancet(4f, 21f));
                        iron.Place(Move(0f, 118f, 15f) * Turn(-6f, 0f, 0f), () => Lancet(3f, 17f));

                        if (side % 2 == 0)
                        {
                            // a face that looks at a point of the star: low windows, a flying buttress above and
                            // below out to the girder that crosses in front of it and a great horn off the top
                            foreach (float across in new[] { -4.7f, 4.7f }) iron.Place(Move(across, -64f, 30.5f), () => Lancet(4f, 20f));
                            foreach (float way in new[] { -1f, 1f })
                            {
                                iron.Sweep(Path(4, new Vector3(0f, way * 40f, 30f), new Vector3(0f, way * 35f, 42f), new Vector3(0f, way * 23f, 53f), new Vector3(0f, way * 7f, 58.7f)), i => Outline(-1.5f, -2f, 1.5f, -2f, 1.5f, 2f, -1.5f, 2f), Vector3.right);
                                rust.Spike(new Vector3(0f, way * 8f, 58.7f), new Vector3(0f, way * 36f, 58.7f), 2.2f);
                            }
                            Horn(rust, Path(5, new Vector3(0f, 108f, 17f), new Vector3(0f, 116f, 40f), new Vector3(0f, 150f, 62f), new Vector3(0f, 190f, 68f), new Vector3(0f, 222f, 60f)), 7f);
                            return;
                        }

                        // a docking maw: fire behind a pointed arch full of teeth, with a spiked tongue to land on
                        Horn(rust, Path(5, new Vector3(0f, -118f, 16f), new Vector3(0f, -128f, 36f), new Vector3(0f, -160f, 52f), new Vector3(0f, -195f, 50f), new Vector3(0f, -222f, 38f)), 6f);
                        for (int pipe = -2; pipe <= 2; pipe++)
                        {
                            rust.Rod(new Vector3(pipe * 2.3f, 68f, 27.5f), new Vector3(pipe * 2.3f, 96f - 5f * Mathf.Abs(pipe), 27.5f), 1f, 8);
                            fire.Rod(new Vector3(pipe * 2.3f, 95.9f - 5f * Mathf.Abs(pipe), 27.5f), new Vector3(pipe * 2.3f, 96.1f - 5f * Mathf.Abs(pipe), 27.5f), 0.7f, 8);
                        }
                        iron.Place(Move(0f, -50f, 30.4f), () =>
                        {
                            fire.Plate(Outline(-7f, -12f, 7f, -12f, 7f, 6f, 4f, 11f, 0f, 14f, -4f, 11f, -7f, 6f), 0.6f);
                            foreach (float way in new[] { -1f, 1f })
                            {
                                iron.Box(new Vector3(way * 8.6f, -1.5f, 4f), new Vector3(3.6f, 25f, 9f), 0.8f);
                                iron.Place(Move(way * 4.7f, 13.3f, 4f) * Turn(0f, 0f, -way * 38f), () => iron.Box(Vector3.zero, new Vector3(11.8f, 3.6f, 9f), 0.8f));
                                rust.Spike(new Vector3(way * 8.6f, 11f, 4f), new Vector3(way * 11f, 26f, 7f), 2f);
                                rust.Spike(new Vector3(way * 8f, -13.5f, 30f), new Vector3(way * 8f, -13.5f, 46f), 1.6f);
                                blood.Box(new Vector3(way * 5f, -12.2f, 17f), new Vector3(0.8f, 0.4f, 24f));
                            }
                            rust.Spike(new Vector3(0f, 16f, 4f), new Vector3(0f, 32f, 9f), 2.6f);
                            for (int tooth = -2; tooth <= 2; tooth++)
                            {
                                rust.Spike(new Vector3(tooth * 2.5f, 12f - 2.2f * Mathf.Abs(tooth), 5f), new Vector3(tooth * 2.5f, 3f - 1.5f * Mathf.Abs(tooth), 6.5f), 1.1f);
                                rust.Spike(new Vector3(tooth * 2.5f, -12f, 5f), new Vector3(tooth * 2.5f, -6.5f, 6.5f), 1.1f);
                            }
                            iron.Box(new Vector3(0f, -13.6f, 16f), new Vector3(17f, 2.6f, 30f), 0.8f);
                        });
                    });
                }

                // The ring: a six-sided tube with an iron rib every six degrees. Each rib grows a claw
                // above and below, curling in like a ribcage, and every other one a spike outwards.
                var circle = new Vector3[61];
                for (int i = 0; i <= 60; i++) circle[i] = Round(6f * (i % 60), 190f, 0f);
                iron.Sweep(circle, i => Oval(6, 6f, 7f), Vector3.up, false, false, true);
                for (int rib = 0; rib < 60; rib++)
                    iron.Place(Turn(0f, 6f * rib, 0f) * Move(0f, 0f, 190f), () =>
                    {
                        rust.Place(Turn(0f, 0f, 90f), () => Band(rust, 6.4f, 8.6f, -1.1f, 1.1f, 6));
                        foreach (float way in new[] { -1f, 1f })
                            Horn(rust, Path(3, new Vector3(0f, way * 8f, 0f), new Vector3(0f, way * 17f, 3f), new Vector3(0f, way * 23f, -3f), new Vector3(0f, way * 25f, -11f)), 1.7f, 5, 0.06f);
                        if (rib % 2 == 1) rust.Spike(new Vector3(0f, 0f, 8f), new Vector3(0f, 0f, rib % 12 == 7 ? 40f : 24f), 2.2f);
                        blood.Place(Move(0f, 0f, -190f) * Turn(0f, 3f, 0f) * Move(0f, 0f, 190f), () => blood.Box(new Vector3(0f, 0f, 5.75f), new Vector3(4.4f, 1.3f, 1.2f)));
                    });

                for (int point = 0; point < 5; point++)
                {
                    // One girder of the star: two booms laced with struts, round a conduit that burns from end to end.
                    Vector3 from = Round(72f * point, 190f, 0f), to = Round(72f * (point + 2), 190f, 0f), lift = Vector3.up * 7f;
                    iron.Rod(from + lift, to + lift, 2.2f, 6);
                    iron.Rod(from - lift, to - lift, 2.2f, 6);
                    blood.Rod(from, to, 1.5f, 6);
                    for (int bay = 0; bay < 24; bay++)
                        rust.Rod(Vector3.Lerp(from, to, bay / 24f) + (bay % 2 == 0 ? lift : -lift), Vector3.Lerp(from, to, (bay + 1) / 24f) + (bay % 2 == 0 ? -lift : lift), 0.9f, 4);
                    // iron knuckles along it, the largest where two girders cross
                    foreach (float along in new[] { 0.19f, 0.382f, 0.5f, 0.618f, 0.81f })
                    {
                        float size = along == 0.382f || along == 0.618f ? 1.5f : 1f;
                        iron.Place(Aim(Vector3.Lerp(from, to, along), to, Vector3.up) * Size(size), () =>
                        {
                            foreach (float way in new[] { -1f, 1f })
                            {
                                iron.Box(new Vector3(0f, way * 7f, 0f), new Vector3(6.5f, 6f, 5f), 1f);
                                rust.Spike(new Vector3(0f, way * 9f, 0f), new Vector3(0f, way * 24f, 0f), 1.7f);
                            }
                            iron.Box(new Vector3(-3.6f, 0f, 0f), new Vector3(1.2f, 14f, 3.4f), 0.4f);
                            iron.Box(new Vector3(3.6f, 0f, 0f), new Vector3(1.2f, 14f, 3.4f), 0.4f);
                        });
                    }

                    // The tower at this point of the star, crowned with horns.
                    iron.Place(Move(from) * Turn(0f, 72f * point, 0f), () =>
                    {
                        iron.Place(Turn(0f, 22.5f, 0f), () =>
                        {
                            iron.Lathe(new[] { 0f, -96f, 4f, -66f, 8f, -34f, 12.5f, -34f, 12.5f, -27f, 14f, 0f, 13f, 40f, 9.8f, 40f, 9.4f, 45f, 7f, 72f, 5f, 72f, 4.6f, 76f, 0f, 124f }, 8);
                            Band(rust, 13.5f, 16.5f, -5f, 5f, 8);
                            Band(rust, 9.8f, 11.4f, 40f, 42.5f, 8);
                        });
                        fire.Ball(new Vector3(0f, 124.5f, 0f), 1.5f, 8);
                        fire.Ball(new Vector3(0f, -96.5f, 0f), 1.5f, 8);
                        for (int side = 0; side < 8; side++)
                            iron.Place(Turn(0f, 45f * side, 0f), () =>
                            {
                                iron.Place(Move(0f, 7f, 12.6f) * Turn(-1.5f, 0f, 0f), () => Lancet(3.2f, 22f));
                                iron.Place(Move(0f, 48f, 8.4f) * Turn(-5f, 0f, 0f), () => Lancet(2.2f, 15f));
                                rust.Spike(Round(22.5f, 12.5f, -31f), Round(22.5f, 23f, -45f), 1.3f);
                                if (side % 2 == 1) Horn(rust, Path(5, new Vector3(0f, 40f, 9f), new Vector3(0f, 44f, 19f), new Vector3(0f, 62f, 24f), new Vector3(0f, 84f, 20f)), 3f, 6);
                            });
                    });

                    // Chains: from the tower up to the keep's spire and down to its lower one, and slung to the next tower.
                    Chain(rust, Round(72f * point, 187f, 100f), Round(72f * point, 5f, 200f), 26f, 10f, 1f);
                    Chain(rust, Round(72f * point, 187f, -78f), Round(72f * point, 8f, -170f), -22f, 10f, 1f);
                    Chain(rust, Round(72f * point + 4f, 189f, -28f), Round(72f * point + 68f, 189f, -28f), 55f, 10f, 1f);
                }
            });

            Shapes.Part(root, "Iron", iron.Mesh("Iron"), Paint(new Color(0.42f, 0.38f, 0.37f), 0.6f, 0.4f), Vector3.zero, Vector3.one);
            Shapes.Part(root, "Rust", rust.Mesh("Rust"), Paint(new Color(0.62f, 0.3f, 0.2f), 0.5f, 0.35f), Vector3.zero, Vector3.one);
            Shapes.Part(root, "Furnaces", fire.Mesh("Furnaces"), Shapes.Material("Glow", new Color(9f, 2.4f, 0.35f)), Vector3.zero, Vector3.one, false);
            Shapes.Part(root, "Pentagram", blood.Mesh("Pentagram"), Shapes.Material("Glow", new Color(8f, 0.4f, 0.1f)), Vector3.zero, Vector3.one, false);
            return root;
        }

        // A stone or a knot of wood: a ball with a lumpy skin.
        static void Lump(MeshKit kit, Vector3 centre, Vector3 radii, float seed, int sides = 32, float rough = 0.22f)
        {
            var rings = new Vector3[sides / 2 + 1][];
            for (int i = 0; i < rings.Length; i++)
            {
                float height = Mathf.PI * (i / (rings.Length - 1f) - 0.5f), wide = i == 0 || i == rings.Length - 1 ? 0f : Mathf.Cos(height);
                rings[i] = new Vector3[sides];
                for (int j = 0; j < sides; j++)
                {
                    float angle = 2f * Mathf.PI * j / sides;
                    var way = new Vector3(wide * Mathf.Sin(angle), Mathf.Sin(height), wide * Mathf.Cos(angle));
                    float swell = 1f + rough * Mathf.Sin(way.x * 3.1f + seed) * Mathf.Sin(way.y * 2.7f + 1.3f * seed) + rough * 0.5f * Mathf.Sin(way.z * 5.3f + 2.1f * seed + way.x * 2f);
                    rings[i][j] = centre + Vector3.Scale(radii, way) * swell;
                }
            }
            kit.Skin(rings);
        }

        // A hand, melted together from a palm, a wrist, four fingers and a thumb. It is built with
        // its fingers along y, its thumb towards +x and its palm facing +z, then set down at a wrist
        // with those three pointing where the caller says; that also decides whether it is a left
        // hand or a right. Each finger joint bends by `curl` degrees.
        static void Hand(MeshKit kit, Vector3 wrist, Vector3 thumbward, Vector3 fingers, Vector3 palm, float curl)
        {
            var bones = new List<(Vector3 from, Vector3 to, float radius)>();
            float[] lengths = { 0.07f, 0.08f, 0.074f, 0.058f };
            for (int finger = 0; finger < 4; finger++)
            {
                Vector3 joint = new Vector3(0.031f - 0.0205f * finger, 0.088f - 0.004f * Mathf.Abs(finger - 1), 0f);
                float bend = 0.35f * curl;
                foreach (float share in new[] { 0.46f, 0.3f, 0.24f })
                {
                    Vector3 next = joint + new Vector3(0f, Mathf.Cos(bend * Mathf.Deg2Rad), Mathf.Sin(bend * Mathf.Deg2Rad)) * (lengths[finger] * share);
                    bones.Add((joint, next, 0.0095f - 0.0005f * finger));
                    joint = next;
                    bend += curl;
                }
            }
            // the thumb comes round to meet the fingers as they close
            Vector3 ball = new Vector3(0.036f, 0.03f, 0.01f), knuckle = ball + new Vector3(0.6f, 0.7f, 0.25f + curl / 120f).normalized * 0.04f;
            bones.Add((ball, knuckle, 0.0115f));
            bones.Add((knuckle, knuckle + new Vector3(0.25f - curl / 100f, 0.8f, 0.3f + curl / 60f).normalized * 0.034f, 0.0098f));

            // squared up, so a rough aim does not skew the hand
            Vector3 along = fingers.normalized, face = (palm - along * Vector3.Dot(palm, along)).normalized, side = thumbward - along * Vector3.Dot(thumbward, along);
            var basis = Matrix4x4.identity;
            basis.SetColumn(0, (side - face * Vector3.Dot(side, face)).normalized);
            basis.SetColumn(1, along);
            basis.SetColumn(2, face);
            kit.Place(Matrix4x4.Translate(wrist) * basis, () => kit.Blob(p =>
            {
                float hand = Melt(Ellipsoid(p, new Vector3(0f, 0.045f, 0f), new Vector3(0.043f, 0.052f, 0.017f)), Capsule(p, new Vector3(0f, 0.01f, 0f), new Vector3(0f, -0.07f, 0f), 0.027f, 0.03f), 0.015f);
                foreach (var (from, to, radius) in bones) hand = Melt(hand, Capsule(p, from, to, radius, radius * 0.88f), 0.006f);
                return hand;
            }, new Vector3(-0.065f, -0.09f, -0.04f), new Vector3(0.1f, 0.185f, 0.115f), 0.0065f));
        }

        // A storm lantern: an iron cage round a flame, hanging from the point given.
        static void Lantern(MeshKit iron, MeshKit flame, Vector3 hook)
        {
            iron.Place(Move(hook + Vector3.down * 0.2f), () =>
            {
                iron.Lathe(new[] { 0f, 0f, 0.038f, 0f, 0.04f, 0.012f, 0.03f, 0.018f }, 10);
                iron.Lathe(new[] { 0.032f, 0.105f, 0.042f, 0.11f, 0.03f, 0.135f, 0.008f, 0.15f, 0f, 0.15f }, 10);
                for (int i = 0; i < 4; i++) iron.Place(Turn(0f, 45f + 90f * i, 0f), () => iron.Rod(new Vector3(0.031f, 0.015f, 0f), new Vector3(0.031f, 0.108f, 0f), 0.003f, 4));
                iron.Tube(Path(4, new Vector3(-0.03f, 0.12f, 0f), new Vector3(-0.022f, 0.185f, 0f), new Vector3(0.022f, 0.185f, 0f), new Vector3(0.03f, 0.12f, 0f)), 0.0025f, 4);
                iron.Rod(new Vector3(0f, 0.185f, 0f), new Vector3(0f, 0.2f, 0f), 0.002f, 4);
                flame.Lathe(new[] { 0f, 0.02f, 0.018f, 0.035f, 0.022f, 0.06f, 0.012f, 0.09f, 0f, 0.1f }, 8);
            });
        }

        static void Add(Transform root, string name, MeshKit mesh, Material material, bool shadows = true) =>
            Shapes.Part(root, name, mesh.Mesh(name), material, Vector3.zero, Vector3.one, shadows);

        public static Transform Robot() => Fabricator();

        public static Transform Camp()
        {
            var root = new GameObject("Robot shelter").transform;
            var robot = Robot(); robot.name = "Hermit"; robot.SetParent(root, false);
            var workshop = Building(Core.Building.Workshop); workshop.SetParent(root, false); workshop.localPosition = new Vector3(5, 0, -3);
            var fire = new GameObject("Fire").transform; fire.SetParent(root, false); fire.localPosition = new Vector3(-3, 0, 2);
            return root;
        }

        public static Transform Building(Core.Building kind)
        {
            var root = new GameObject(kind.ToString()).transform;
            MeshKit metal = new MeshKit(), glow = new MeshKit();
            metal.Box(new Vector3(0, 0.2f, 0), new Vector3(5, 0.4f, 5), 0.1f);
            if (kind == Core.Building.Radar)
            {
                metal.Rod(Vector3.zero, new Vector3(0, 9, 0), 0.3f, Curves);
                metal.Place(Move(new Vector3(0, 8.5f, 0)) * Turn(55, 0, 0), () =>
                {
                    metal.Lathe(new[] { 0f, 0f, 0.4f, 0.03f, 0.9f, 0.14f, 1.4f, 0.34f, 1.9f, 0.62f, 2.1f, 0.8f, 2.16f, 0.76f, 1.9f, 0.54f, 1.4f, 0.26f, 0.9f, 0.06f, 0f, -0.08f }, Curves * 2);
                    metal.Rod(Vector3.zero, Vector3.up * 1.6f, 0.06f, 24);
                    metal.Ball(Vector3.up * 1.6f, 0.16f, Curves);
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * Mathf.PI / 3;
                        metal.Rod(new Vector3(Mathf.Cos(a) * 2, 0.7f, Mathf.Sin(a) * 2), Vector3.up * 1.6f, 0.025f, 16);
                    }
                });
            }
            else if (kind == Core.Building.Mine || kind == Core.Building.Drill)
            {
                metal.Rod(new Vector3(0, 0.5f, 0), new Vector3(0, 5, 0), 0.6f, Curves, 0.2f);
                foreach (float x in new[] { -2f, 2f }) metal.Rod(new Vector3(x, 0, 0), new Vector3(0, 5, 0), 0.18f, 20);
                for (int i = 0; i < 8; i++)
                    metal.Place(Move(new Vector3(0, 0.5f + i * 0.47f, 0)), () => metal.Torus(0.48f - i * 0.025f, 0.06f, Curves, 16));
                foreach (float x in new[] { -1f, 1f })
                    metal.Place(Move(new Vector3(x, 1, -1.2f)), () => metal.Lathe(new[] { 0, -0.6f, 0.38f, -0.6f, 0.46f, -0.4f, 0.46f, 0.7f, 0.38f, 0.9f, 0, 0.9f }, Curves));
            }
            else if (kind == Core.Building.Turret)
            {
                metal.Rod(Vector3.zero, new Vector3(0, 2.5f, 0), 1, Curves, 0.7f);
                metal.Ball(new Vector3(0, 2.4f, 0), new Vector3(0.9f, 0.65f, 0.8f), Curves);
                foreach (float side in new[] { -1f, 1f })
                {
                    metal.Rod(new Vector3(side * 0.5f, 2.6f, 0.2f), new Vector3(side * 0.5f, 2.6f, 2.7f), 0.16f, Curves, 0.11f);
                    for (int i = 0; i < 7; i++)
                        metal.Place(Move(new Vector3(side * 0.5f, 2.6f, 0.5f + i * 0.3f)) * Turn(90, 0, 0), () => metal.Torus(0.16f, 0.035f, Curves, 16));
                    glow.Ball(new Vector3(side * 0.5f, 2.6f, 2.7f), new Vector3(0.08f, 0.08f, 0.03f), 24);
                }
            }
            else
            {
                metal.Box(new Vector3(-1.8f, 1.5f, 0), new Vector3(1, 3, 4), 0.1f);
                metal.Box(new Vector3(1.8f, 1.5f, 0), new Vector3(1, 3, 4), 0.1f);
                metal.Box(new Vector3(0, 3, 0), new Vector3(5, 0.4f, 4), 0.1f);
                if (kind == Core.Building.Workshop)
                {
                    var suit = Traveller(); suit.name = "Powered armour"; suit.SetParent(root, false); suit.localPosition = Vector3.up * 0.45f;
                }
            }
            MeshKit plates = new MeshKit(), cables = new MeshKit();
            foreach (float side in new[] { -1f, 1f })
            {
                for (int i = 0; i < 6; i++)
                {
                    float z = -1.7f + i * 0.65f;
                    Pillow(plates, new Vector3(side * 1.9f, 1.45f, z), new Vector3(0.9f, 2.4f, 0.58f), Curves);
                    metal.Rod(new Vector3(side * 2.4f, 0.4f, z), new Vector3(side * 2.4f, 2.7f, z), 0.055f, 16);
                    for (int j = 0; j < 5; j++)
                        cables.Box(new Vector3(side * 2.37f, 0.9f + j * 0.12f, z), new Vector3(0.06f, 0.04f, 0.38f), 0.015f);
                }
                for (int j = 0; j < 3; j++)
                    cables.Tube(Path(24, new Vector3(side * 2.2f, 0.5f, -2), new Vector3(side * 2.6f, 1.5f, -2.4f), new Vector3(side * 2.2f, 3 + j * 0.09f, -1), new Vector3(0, 3 + j * 0.09f, 0)), 0.045f, 12);
                Piston(metal, plates, new Vector3(side * 2.7f, 0.2f, -1.5f), new Vector3(side * 2.1f, 2.9f, -1.5f), 0.13f);
            }
            Add(root, "Service panels", plates, Alloy);
            Add(root, "Conduits and vents", cables, Rubber);
            glow.Box(new Vector3(0, 0.45f, 2.5f), new Vector3(4, 0.08f, 0.08f));
            Add(root, "Iron", metal, Paint(new Color(0.25f, 0.3f, 0.35f), 0.7f, 0.4f));
            Add(root, "Power", glow, Shapes.Material("Glow", new Color(0.2f, 2, 3)), false);
            return root;
        }

        public static Transform Gate()
        {
            var root = new GameObject("Gate").transform;
            MeshKit frame = new MeshKit(), power = new MeshKit();
            var rim = new Vector3[65];
            for (int i = 0; i < rim.Length; i++) rim[i] = new Vector3(Mathf.Cos(i * Mathf.PI / 32), Mathf.Sin(i * Mathf.PI / 32), 0) * 80;
            frame.Tube(rim, 8, 12);
            power.Tube(rim, 2, 10);
            Add(root, "Ring", frame, Paint(new Color(0.3f, 0.36f, 0.42f), 0.85f, 0.4f));
            Add(root, "Conduit", power, Shapes.Material("Glow", new Color(0.3f, 3, 5)), false);
            return root;
        }

        // A gas giant's sky-life, 40 m from the top of its float at y = 20 to the ends of its
        // tendrils: a bell of eight gas cells with its organs showing through, two ruffled frills
        // under the rim, five twisting feeding arms and a curtain of long tendrils. One mesh, with
        // the thin membranes built on both sides, for a translucent glowing material.
        public static Mesh Drifter()
        {
            var kit = new MeshKit { crease = 180f };
            // a thin skin turned about y, seen from inside as well as out
            void Membrane(float[] profile, int sides, Func<float, int, float> lobes)
            {
                int rings = profile.Length / 2;
                var back = new float[profile.Length];
                for (int i = 0; i < rings; i++)
                {
                    back[2 * i] = profile[2 * (rings - 1 - i)];
                    back[2 * i + 1] = profile[2 * (rings - 1 - i) + 1];
                }
                kit.Lathe(profile, sides, lobes);
                kit.Lathe(back, sides, (around, ring) => lobes(around, rings - 1 - ring));
            }
            kit.Lathe(new[] { 7.7f, 12f, 8f, 13.2f, 7.5f, 15.4f, 5.8f, 17.8f, 3.2f, 19.4f, 0f, 20f }, Curves * 2, (around, ring) => 1f + 0.08f * Mathf.Cos(8f * around) * (1f - ring / 5f));
            kit.Lathe(new[] { 0f, 19.2f, 3f, 18.6f, 5.3f, 17.1f, 6.9f, 15f, 7.3f, 13f, 7.2f, 12.1f, 7.7f, 12f }, Curves * 2, (around, ring) => 1f + 0.08f * Mathf.Cos(8f * around) * Mathf.Min(1f, ring / 5f));
            Membrane(new[] { 7.8f, 8.4f, 7.2f, 9.6f, 7.9f, 11f, 7.6f, 12.1f }, 56, (around, ring) => 1f + 0.035f * (3 - ring) * Mathf.Sin(14f * around + 0.8f * ring));
            Membrane(new[] { 4.6f, 6.2f, 4.1f, 8.4f, 4.7f, 10.5f, 4.3f, 12.6f }, 40, (around, ring) => 1f + 0.06f * (3 - ring) * Mathf.Sin(10f * around + 1.1f * ring));
            kit.Ball(new Vector3(0f, 16f, 0f), new Vector3(1.7f, 2.2f, 1.7f), Curves);
            for (int i = 0; i < 5; i++)
            {
                float angle = 2f * Mathf.PI * i / 5f;
                var around = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                kit.Ball(around * 3.1f + Vector3.up * 15.2f, new Vector3(1.1f, 1.7f, 1.1f), Curves);
                // a feeding arm: a ribbon that twists as it trails
                var arm = new Vector3[26];
                for (int k = 0; k < arm.Length; k++)
                {
                    float t = k / (arm.Length - 1f), swirl = angle + 2.4f * t;
                    arm[k] = new Vector3(Mathf.Sin(swirl), 0f, Mathf.Cos(swirl)) * (1.3f + 1.8f * t) + Vector3.up * (13f - (20f + 2.5f * i) * t);
                }
                kit.Sweep(arm, k =>
                {
                    float t = k / (arm.Length - 1f), width = 1.1f * Mathf.Pow(1f - t, 0.6f) + 0.08f, twist = 0.55f * k;
                    return Array.ConvertAll(Oval(20, width, 0.13f), p => new Vector2(p.x * Mathf.Cos(twist) - p.y * Mathf.Sin(twist), p.x * Mathf.Sin(twist) + p.y * Mathf.Cos(twist)));
                }, Vector3.forward, false);
            }
            for (int i = 0; i < 14; i++)
            {
                float angle = 2f * Mathf.PI * (i + 0.5f) / 14f, length = 24f + 8f * (i * 5 % 4) / 3f;
                var tendril = new Vector3[22];
                for (int k = 0; k < tendril.Length; k++)
                {
                    float t = k / (tendril.Length - 1f), sway = angle + 0.5f * Mathf.Sin(5f * t + i);
                    tendril[k] = new Vector3(Mathf.Sin(sway), 0f, Mathf.Cos(sway)) * (7.5f - 2.5f * t + 0.9f * Mathf.Sin(7f * t + 2f * i) * t) + Vector3.up * (12.2f - length * t);
                }
                kit.Tube(tendril, 0.14f, 16, 0.02f, false);
            }
            return kit.Mesh("Drifter");
        }

        // A dry world's grazer, about 6 m high, standing on the origin and facing +z: a small pod
        // carried on three long stilts that knee above it, a sail of spines along its back, a trunk
        // let down to the ground to feed and a long drooping stalk of sense organs. The second mesh
        // is what glows: the stalk's lamps, the tips of the sail and a row along its belly.
        public static (Mesh body, Mesh lights) Strider()
        {
            var body = new MeshKit { crease = 180f };
            var lights = new MeshKit(body) { crease = 180f };
            body.Ball(new Vector3(0f, 4.55f, 0f), new Vector3(0.55f, 0.46f, 0.95f), Curves);
            body.Ball(new Vector3(0f, 4.2f, -0.45f), new Vector3(0.38f, 0.42f, 0.5f), Curves);
            for (int i = 0; i < 3; i++)
            {
                // one stilt forward and two behind, each with two knees and a spread foot
                float angle = (120f * i) * Mathf.Deg2Rad;
                var outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                Vector3 hip = outward * 0.45f + Vector3.up * 4.6f, knee = outward * 1.7f + Vector3.up * 5.75f, hock = outward * 2.5f + Vector3.up * 2.7f, foot = outward * 1.95f;
                body.Tube(Path(10, hip, knee, hock, foot + Vector3.up * 0.08f), 0.1f, 24, 0.045f);
                body.Ball(knee, 0.16f, Curves);
                body.Ball(hock, 0.13f, Curves);
                for (int toe = -1; toe <= 1; toe++)
                    body.Spike(foot + Vector3.up * 0.1f, foot + Quaternion.Euler(0f, 50f * toe, 0f) * outward * 0.5f, 0.06f, 16);
            }

            // the sail: spines raked back along the spine, webbed between
            var spines = new Vector3[9][];
            for (int i = 0; i < 9; i++)
            {
                float along = i / 8f, z = Mathf.Lerp(0.6f, -0.85f, along), tall = 0.5f + 1.05f * Mathf.Sin(Mathf.PI * (0.15f + 0.8f * along));
                Vector3 root = new Vector3(0f, 4.95f - 0.25f * along * along, z), tip = root + new Vector3(0f, tall, -0.35f - 0.5f * along);
                body.Spike(root, tip, 0.035f, 5);
                lights.Ball(tip, 0.045f, 6);
                spines[i] = new Vector3[5];
                for (int k = 0; k < 5; k++) spines[i][k] = Vector3.Lerp(root, tip, k / 4f * 0.92f);
            }
            var web = new List<Vector3[]>();
            for (int i = 0; i < 8; i++)
            {
                web.Add(spines[i]);
                // the web sags between spines, most of all at its free edge
                var mid = new Vector3[5];
                for (int k = 0; k < 5; k++) mid[k] = Vector3.Lerp(spines[i][k], spines[i + 1][k], 0.5f) + Vector3.down * (0.03f * k * k);
                web.Add(mid);
            }
            web.Add(spines[8]);
            body.Skin(web, false);
            body.flip = true;
            body.Skin(web, false);
            body.flip = false;

            // the feeding trunk, flared where it meets the ground
            var trunk = Path(8, new Vector3(0f, 4.2f, 0.5f), new Vector3(0f, 3f, 0.95f), new Vector3(0f, 1.4f, 0.7f), new Vector3(0f, 0.3f, 1f));
            body.Sweep(trunk, i => Oval(24, i > trunk.Length - 4 ? 0.07f + 0.03f * (i - trunk.Length + 4) : Mathf.Lerp(0.13f, 0.07f, i / (trunk.Length - 1f))), Vector3.forward);
            // the stalk: up and forward, then drooping under the weight of its lamps
            var stalk = Path(7, new Vector3(0f, 4.8f, 0.75f), new Vector3(0f, 5.6f, 1.5f), new Vector3(0f, 5.95f, 2.4f), new Vector3(0f, 5.6f, 3.15f), new Vector3(0f, 5.05f, 3.4f));
            body.Tube(stalk, 0.12f, 24, 0.04f);
            for (int i = 0; i < 3; i++)
                lights.Ball(stalk[stalk.Length - 1] + new Vector3(0.14f * (i - 1), -0.1f - 0.05f * (i % 2), 0.02f * i), 0.13f - 0.02f * i, 10);
            for (int i = 0; i < 5; i++) lights.Ball(stalk[6 + 5 * i] + Vector3.down * 0.1f, 0.05f, 6);
            for (int i = 0; i < 4; i++)
                foreach (float side in new[] { -1f, 1f })
                    lights.Ball(new Vector3(side * 0.36f, 4.3f, 0.55f - 0.32f * i), 0.06f, 6);
            return (body.Mesh("Strider"), lights.Mesh("Strider lights"));
        }

        // Mineral growths for airless and desert worlds: three clusters of six-sided spires, each
        // about 4 m tall with its base at the origin. One is a tall spire in a crown of leaning
        // shards, one a fan of blades, one a stand of flat-topped columns. Every face is flat.
        public static Mesh[] Crystals()
        {
            var meshes = new Mesh[3];
            for (int cluster = 0; cluster < 3; cluster++)
            {
                var kit = new MeshKit { crease = 0f };
                var random = new System.Random(cluster + 11);
                float Roll(float low, float high) => Mathf.Lerp(low, high, (float)random.NextDouble());
                // a spire with a pointed end, or a blunt one when `point` is small
                void Spire(Vector3 foot, Vector3 lean, float length, float radius, float point = 0.22f)
                {
                    Vector3 along = lean.normalized, shoulder = foot + along * (length * (1f - point));
                    kit.Sweep(new[] { foot - along * 0.2f, shoulder, foot + along * length }, i => Oval(6, i == 0 ? radius * 1.12f : i == 1 ? radius : radius * (point < 0.1f ? 0.7f : 0f)), Vector3.forward, false, point < 0.1f);
                }
                if (cluster == 0)
                {
                    Spire(Vector3.zero, new Vector3(0.05f, 1f, 0.03f), 4f, 0.42f);
                    for (int i = 0; i < 30; i++)
                    {
                        float angle = Roll(0f, 2f * Mathf.PI), tilt = Roll(0.25f, 1.1f), size = Roll(0.4f, 1f);
                        var outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                        Spire(outward * Roll(0.15f, 0.55f), Vector3.up + outward * tilt, (2.6f - 1.5f * tilt) * size + 0.5f, 0.1f + 0.2f * size * (1.2f - tilt * 0.6f));
                    }
                }
                else if (cluster == 1)
                {
                    for (int i = 0; i < 17; i++)
                    {
                        float spread = (i - 8f) / 8f, size = 1f - 0.45f * Mathf.Abs(spread) + Roll(-0.1f, 0.1f);
                        Spire(new Vector3(spread * 0.5f, 0f, Roll(-0.12f, 0.12f)), new Vector3(spread * 0.95f, 1f, Roll(-0.15f, 0.15f)), 4f * size, 0.2f * size + 0.06f, 0.35f);
                    }
                    for (int i = 0; i < 14; i++)
                    {
                        float angle = Roll(0f, 2f * Mathf.PI);
                        var outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                        Spire(outward * Roll(0.3f, 0.8f), Vector3.up * 0.6f + outward, Roll(0.5f, 1.2f), Roll(0.08f, 0.16f));
                    }
                }
                else
                {
                    for (int i = 0; i < 30; i++)
                    {
                        float angle = i * 2.4f, reach = 0.19f * Mathf.Sqrt(i);
                        var foot = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * reach;
                        Spire(foot, new Vector3(foot.x * 0.08f, 1f, foot.z * 0.08f), Mathf.Max(0.4f, 4f - 2.9f * reach + Roll(-0.5f, 0.5f)), 0.19f, 0.04f);
                    }
                    for (int i = 0; i < 3; i++) Spire(new Vector3(Roll(-1f, 1f), 0.16f, Roll(0.9f, 1.3f)), new Vector3(Roll(-1f, 1f), 0.05f, Roll(-0.4f, 0.4f)), Roll(1f, 1.8f), 0.17f, 0.04f);
                }
                meshes[cluster] = kit.Mesh($"Crystals {cluster + 1}");
            }
            return meshes;
        }

        // A furnace world's vent life, about 3 m tall with its base at the origin: a clump of
        // leaning tubes built up in segments, each ending in a flared mouth that spreads a crown of
        // feathery gills.
        public static Mesh TubeGrove()
        {
            var kit = new MeshKit { crease = 180f };
            for (int tube = 0; tube < 7; tube++)
            {
                float angle = 2f * Mathf.PI * tube / 7f + 0.4f * Mathf.Sin(tube * 2.3f), height = 3f - 0.3f * (tube * 5 % 7), girth = 0.13f + 0.012f * (tube * 3 % 4);
                var outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                var path = Path(3, outward * 0.3f, outward * 0.42f + Vector3.up * height * 0.3f, outward * 0.7f + Vector3.up * height * 0.68f, outward * 0.78f + Vector3.up * height);
                int last = path.Length - 1;
                // each segment swells towards its top, and the mouth flares
                kit.Sweep(path, i => Oval(8, i == last ? girth * 1.55f : girth * (1f - 0.25f * i / last) * (1f + 0.16f * (i % 2))), Vector3.forward, true, false);
                // the throat, seen from above
                kit.flip = true;
                kit.Sweep(new[] { path[last], path[last] - Vector3.up * 0.3f }, i => Oval(8, i == 0 ? girth * 1.45f : girth * 0.3f), Vector3.forward, false, true);
                kit.flip = false;
                for (int gill = 0; gill < 7; gill++)
                {
                    float round = 2f * Mathf.PI * gill / 7f + tube;
                    Vector3 spread = new Vector3(Mathf.Sin(round), 0f, Mathf.Cos(round)), root = path[last] + spread * girth * 0.9f;
                    kit.Blade(Path(1, root, root + spread * 0.22f + Vector3.up * 0.3f, root + spread * 0.5f + Vector3.up * 0.34f, root + spread * 0.66f + Vector3.up * 0.16f), Vector3.Cross(spread, Vector3.up),
                        t => 0.09f * Mathf.Sin(Mathf.PI * (0.15f + 0.85f * t)), t => 0.09f * Mathf.Sin(Mathf.PI * (0.15f + 0.85f * t)), t => 0.03f * (1f - t) + 0.006f);
                }
            }
            return kit.Mesh("Tube grove");
        }
    }
}
