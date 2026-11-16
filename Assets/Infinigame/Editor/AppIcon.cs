using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Infinigame.Editor
{
    // The same opaque cartoon badge supplies every platform's icon sizes.
    public static class AppIcon
    {
        public static void Generate()
        {
            const int size = 1024;
            var texture = new Texture2D(size, size, TextureFormat.RGB24, false);
            var pixels = new Color[size * size];
            Color ink = new Color(0.035f, 0.055f, 0.11f), shell = new Color(0.77f, 0.86f, 0.89f), shade = new Color(0.32f, 0.47f, 0.59f), cyan = new Color(0.25f, 0.92f, 0.95f);
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                var point = new Vector2(x, y);
                Color colour = Color.Lerp(new Color(0.12f, 0.1f, 0.26f), new Color(0.34f, 0.23f, 0.5f), y / 1024f);
                void Round(float cx, float cy, float width, float height, float radius, Color paint)
                {
                    Vector2 q = new Vector2(Mathf.Abs(x - cx) - width / 2 + radius, Mathf.Abs(y - cy) - height / 2 + radius);
                    float distance = new Vector2(Mathf.Max(q.x, 0), Mathf.Max(q.y, 0)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0) - radius;
                    colour = Color.Lerp(colour, paint, Mathf.Clamp01(0.5f - distance));
                }
                void Circle(float cx, float cy, float radius, Color paint) => Round(cx, cy, radius * 2, radius * 2, radius, paint);
                Circle(183, 817, 92, new Color(0.18f, 0.65f, 0.69f));
                Circle(161, 838, 71, new Color(0.3f, 0.82f, 0.8f));
                foreach (var star in new[] { new Vector2(828, 826), new Vector2(867, 662), new Vector2(149, 572), new Vector2(300, 900) })
                {
                    float r = Vector2.Distance(point, star);
                    if (r < 8) colour = Color.Lerp(colour, shell, Mathf.Clamp01(8 - r));
                }
                Round(512, 164, 670, 224, 100, ink);
                Round(512, 180, 620, 182, 78, shade);
                Round(512, 197, 295, 174, 45, shell);
                Round(512, 185, 220, 45, 20, ink);
                Round(512, 185, 160, 15, 7, cyan);
                Round(512, 792, 38, 130, 16, ink);
                Circle(512, 853, 37, ink); Circle(512, 853, 22, new Color(1, 0.61f, 0.25f));
                Round(226, 505, 147, 251, 60, ink); Round(798, 505, 147, 251, 60, ink);
                Round(224, 522, 99, 189, 38, shade); Round(800, 522, 99, 189, 38, shade);
                Round(512, 511, 581, 538, 150, ink);
                Round(512, 527, 535, 487, 130, shell);
                Round(512, 626, 469, 104, 45, new Color(0.91f, 0.96f, 0.95f));
                Round(512, 500, 477, 235, 79, ink);
                Round(512, 509, 429, 189, 58, new Color(0.055f, 0.18f, 0.25f));
                Round(404, 517, 119, 98, 35, cyan); Round(620, 517, 119, 98, 35, cyan);
                Round(389, 535, 68, 25, 10, Color.white); Round(605, 535, 68, 25, 10, Color.white);
                Round(512, 333, 257, 111, 33, ink); Round(512, 340, 205, 62, 18, shade);
                for (int vent = -1; vent <= 1; vent++) Round(512 + vent * 48, 340, 18, 43, 8, ink);
                pixels[y * size + x] = colour;
            }
            texture.SetPixels(pixels); texture.Apply();
            const string path = "Assets/Infinigame/Icon.png";
            File.WriteAllBytes(path, texture.EncodeToPNG()); Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path); importer.maxTextureSize = 1024; importer.alphaSource = TextureImporterAlphaSource.None; importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            foreach (var kind in PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.iOS))
            {
                var icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.iOS, kind);
                foreach (var slot in icons) slot.SetTexture(icon);
                PlayerSettings.SetPlatformIcons(NamedBuildTarget.iOS, kind, icons);
            }
            AssetDatabase.SaveAssets();
        }
    }
}
