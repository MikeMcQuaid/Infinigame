using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;

namespace Infinigame.Editor
{
    static class VisorWorkshop
    {
        public static Mesh WeaponMark(int shape)
        {
            var points = new List<Vector3>();
            var indices = new List<int>();
            void Line(Vector2 a, Vector2 b)
            {
                Vector2 normal = new Vector2(b.y - a.y, a.x - b.x).normalized * 1.9f;
                int i = points.Count;
                points.Add(a - normal); points.Add(a + normal);
                points.Add(b + normal); points.Add(b - normal);
                indices.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            void Stroke(params int[] xy)
            {
                for (int i = 2; i < xy.Length; i += 2)
                    Line(new Vector2(xy[i-2], xy[i-1]), new Vector2(xy[i], xy[i+1]));
            }
            switch (shape)
            {
                case 0: // Gauntlet cuff and articulated knuckles.
                    Stroke(18,10, 51,8, 69,18, 74,32, 66,42, 46,46, 21,40, 16,28, 18,10);
                    Stroke(31,12, 32,35, 52,40, 66,35); Stroke(46,17, 62,20, 68,28);
                    break;
                case 1: // Focusing lens under the gauntlet.
                    Stroke(9,12, 47,8, 70,18, 93,21, 94,34, 66,39, 40,44, 14,37, 9,12);
                    Stroke(63,19, 62,33, 76,34, 82,22); Stroke(99,27, 159,27);
                    break;
                case 2: // Twin backpack launch cells.
                    Stroke(24,6, 16,34, 28,44, 79,42, 91,31, 79,7, 24,6);
                    Stroke(23,32, 36,25, 50,33, 36,40, 23,32); Stroke(53,32, 66,25, 80,33, 66,39, 53,32);
                    break;
                case 3: // Rail gauntlet, with its two exposed rails.
                    Stroke(9,12, 45,7, 63,16, 128,18, 148,23, 60,23, 54,31, 137,31, 125,37, 52,39, 18,42, 9,12);
                    break;
                case 4: // The radial backpack emitter.
                    Stroke(23,7, 84,7, 93,15, 84,25, 71,34, 51,38, 32,32, 21,21, 23,7);
                    for (int i = -2; i <= 2; i++) Stroke(55+i*9,33-math.abs(i)*3, 55+i*21,50-math.abs(i)*5);
                    break;
                case 5: // Paired pulse cannon barrels.
                    Stroke(9,12, 46,8, 68,17, 130,17, 144,22, 58,24, 59,29, 145,30, 136,36, 66,36, 40,43, 9,36, 9,12);
                    break;
                case 6: // Seeker body, tail fins and tracking head.
                    Stroke(9,6, 36,17, 103,17, 131,23, 145,28, 123,35, 37,36, 9,47, 16,31, 9,6);
                    Stroke(103,18, 99,26, 104,35); Stroke(22,25, 48,26);
                    break;
                case 7: // Wing lens and converging waveguides.
                    Stroke(12,10, 45,8, 78,18, 85,26, 78,37, 42,43, 13,40, 24,27, 12,10);
                    Stroke(31,15, 65,23, 65,31, 29,37); Stroke(88,23, 149,26, 88,31);
                    break;
                case 8: // Long ship rails with a bank of coils.
                    Stroke(7,15, 33,9, 70,15, 156,15, 164,21, 71,25, 71,29, 163,31, 149,37, 62,37, 31,42, 7,34, 7,15);
                    for (int i = 23; i < 63; i += 9) Stroke(i,16, i-4,34);
                    break;
                case 9: // Broad torpedo casing and armoured nose.
                    Stroke(13,10, 34,14, 58,7, 105,10, 129,18, 146,27, 129,37, 103,44, 56,45, 34,38, 13,43, 17,27, 13,10);
                    Stroke(58,9, 53,26, 57,44); Stroke(104,12, 114,26, 104,42);
                    break;
            }
            var mesh = new Mesh { name = "Weapon outline " + shape };
            mesh.SetVertices(points);
            mesh.SetTriangles(indices, 0);
            return mesh;
        }

        // A white arrowhead pointing right.
        public static Texture2D Pointer()
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = new Color(1f, 1f, 1f, math.saturate(0.5f * (size - x - 0.5f) - math.abs(y + 0.5f - size / 2f) + 0.5f));
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        // A white rounded square whose corners survive stretching, filled or as a line this thick.
        public static Texture2D Rounded(float line, int size = 32, float radius = 7f)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float2 corner = math.abs(new float2(x, y) + 0.5f - size / 2f) - (size / 2f - radius);
                // how far inside the shape this pixel is: from the rounded corner, or from the nearest straight edge
                float inside = math.min(radius - math.length(math.max(corner, 0f)), math.cmin(size / 2f - math.abs(new float2(x, y) + 0.5f - size / 2f)));
                pixels[y * size + x] = new Color(1f, 1f, 1f, math.saturate(line > 0f ? line - math.abs(inside - line) + 0.5f : inside + 0.5f));
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        public static Texture2D Touch()
        {
            var texture = new Texture2D(64, 64);
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) texture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(31.5f - Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)))));
            texture.Apply();
            return texture;
        }
    }
}
