using Infinigame.Core;
using UnityEngine;

namespace Infinigame
{
    // Detail budget for one Unity quality level. Each platform has a default level in
    // Project Settings > Quality, and the level can be changed while playing.
    public readonly struct LodSettings
    {
        public readonly int quads, buildsPerFrame, waterQuads;
        public readonly float splitFactor, finestQuad, scatter;
        // The fewest pixels tall a plant, rock or animal can be and still be worth drawing.
        public readonly float pixels;

        LodSettings(int quads, float splitFactor, float finestQuad, int buildsPerFrame, int waterQuads, float scatter, float pixels)
        {
            this.pixels = pixels;
            this.quads = quads;
            this.splitFactor = splitFactor;
            this.finestQuad = finestQuad;
            this.buildsPerFrame = buildsPerFrame;
            this.waterQuads = waterQuads;
            this.scatter = scatter;
        }

        // Indexed by quality level: Mobile, VR, PC.
        static readonly LodSettings[] Tiers =
        {
            new LodSettings(16, 1.5f, 1.5f, 2, 40, 0.3f, 8f),
            new LodSettings(24, 2f, 1f, 3, 64, 0.6f, 5f),
            new LodSettings(64, 3.4f, 0.5f, 10, 160, 2f, 3f),
        };

        // Hysteresis prevents a moving camera from switching meshes every frame at a boundary.
        public static int SentryDetail(float pixels, int previous)
        {
            if (pixels >= (previous == 0 ? 130 : 165)) return 0;
            if (pixels >= (previous == 2 ? 46 : 34)) return 1;
            return 2;
        }

        public static int Tier => Mathf.Clamp(QualitySettings.GetQualityLevel(), 0, Tiers.Length - 1);
        public static float Adaptive = 1;
        public static LodSettings Current
        {
            get
            {
                var tier = Tiers[Tier];
                return new LodSettings(tier.quads, tier.splitFactor * Adaptive, tier.finestQuad, Mathf.Max(1, Mathf.RoundToInt(tier.buildsPerFrame * Adaptive)), tier.waterQuads, tier.scatter, tier.pixels);
            }
        }

        // How far off something this many metres tall is still worth drawing. The view widens with
        // speed, but ranges are worked out for the resting view so they do not breathe with it.
        public float Range(float height) => Detail.Range(height, pixels, Camera.main.pixelHeight, 70f);
    }
}
