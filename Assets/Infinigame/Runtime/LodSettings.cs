using UnityEngine;

namespace Infinigame
{
    // Detail budget for one Unity quality level. Each platform has a default level in
    // Project Settings > Quality, and the level can be changed while playing.
    public readonly struct LodSettings
    {
        public readonly int quads, buildsPerFrame, waterQuads;
        public readonly float splitFactor, finestQuad, scatter;

        LodSettings(int quads, float splitFactor, float finestQuad, int buildsPerFrame, int waterQuads, float scatter)
        {
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
            new LodSettings(16, 1.5f, 1.5f, 2, 40, 0.3f),
            new LodSettings(24, 2f, 1f, 3, 64, 0.6f),
            new LodSettings(32, 2.5f, 0.75f, 6, 96, 1f),
        };

        public static int Tier => Mathf.Clamp(QualitySettings.GetQualityLevel(), 0, Tiers.Length - 1);
        public static LodSettings Current => Tiers[Tier];
    }
}
