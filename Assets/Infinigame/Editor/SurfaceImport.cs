using System.IO;
using UnityEditor;
using UnityEngine;

namespace Infinigame.Editor
{
    class SurfaceImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            const string root = "Assets/Infinigame/Resources/";
            bool visor = assetPath.StartsWith(root + "Visor/");
            if (!visor && !assetPath.StartsWith(root + "Surfaces/")) return;
            var importer = (TextureImporter)assetImporter;
            string name = Path.GetFileNameWithoutExtension(assetPath);
            bool foliage = name == "Needles" || name == "Blades";
            importer.isReadable = false;
            importer.mipmapEnabled = !visor;
            importer.wrapMode = visor || foliage ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            importer.filterMode = visor ? FilterMode.Bilinear : FilterMode.Trilinear;
            if (visor)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spriteBorder = Vector4.one * (name == "Panel" ? 19 : name == "Outline" ? 12 : name == "Dot" ? 6 : 0);
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                return;
            }
            bool normal = name.EndsWith(" relief");
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normal && name != "Billows";
            importer.alphaIsTransparency = foliage;
            importer.anisoLevel = foliage ? 4 : 8;
            foreach (var platform in new[] { "Standalone", "iPhone" })
            {
                var settings = importer.GetPlatformTextureSettings(platform);
                settings.overridden = true;
                settings.maxTextureSize = platform == "iPhone" ? (foliage ? 512 : 1024) : 2048;
                settings.format = platform == "iPhone" ? TextureImporterFormat.ASTC_6x6 : normal ? TextureImporterFormat.BC5 : TextureImporterFormat.BC7;
                importer.SetPlatformTextureSettings(settings);
            }
        }

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith("Assets/Infinigame/Resources/Effects/")) return;
            var importer = (AudioImporter)assetImporter;
            var settings = importer.defaultSampleSettings;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.9f;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = settings;
        }
    }
}
