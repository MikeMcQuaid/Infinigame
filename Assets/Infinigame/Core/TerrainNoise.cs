using System;
using Unity.Mathematics;

namespace Infinigame.Core
{
    // Lengths are metres, frequencies are noise cycles across the unit sphere.
    [Serializable]
    public struct TerrainParams
    {
        public float3 offset;
        // Seas: zero depth means a dry body where everything counts as inland.
        public float seaDepth, seaBias, continentFreq, warp;
        public float plains, hillFreq, hillAmp;
        public float mountainFreq, mountainAmp;
        // riverWidth is the channel's half-width in noise units; riverBank widens the valley per metre it cuts through.
        public float riverFreq, riverWidth, riverBank, riverDepth;
        public float craterFreq, craterDepth;
        // Added to the colour elevation near the poles so they read as ice caps.
        public float polarCap;

        public float MinHeight => -(seaDepth + riverDepth + hillAmp + craterDepth * 1.5f);
        public float MaxHeight => plains + hillAmp + mountainAmp + craterDepth * 0.5f;
    }

    public static class TerrainNoise
    {
        // Height above the body's base radius for a unit direction. Zero is sea level.
        // Pure and seam-free: it only depends on the direction, never on the patch asking.
        public static float Height(float3 dir, in TerrainParams p)
        {
            float inland = 1f, continent = 1f, e = 0f, relief = 0f;
            if (p.seaDepth > 0f)
            {
                float3 w = dir * p.continentFreq + p.offset;
                w += p.warp * new float3(noise.snoise(w * 1.7f + 11f), noise.snoise(w * 1.7f + 23f), noise.snoise(w * 1.7f + 37f));
                continent = Fbm(w, 4) + p.seaBias;
                // smoothstep flattens both sides of the coast into beaches and shallows
                inland = math.smoothstep(0f, 0.3f, continent);
                e = continent > 0f ? p.plains * inland : -p.seaDepth * math.smoothstep(0f, 0.35f, -continent);
            }

            if (inland > 0f)
            {
                e += p.hillAmp * inland * Fbm(dir * p.hillFreq + p.offset.yzx, 4);
                if (p.mountainAmp > 0f)
                {
                    relief = p.mountainAmp * math.smoothstep(0.1f, 0.45f, continent)
                        * math.smoothstep(-0.1f, 0.4f, noise.snoise(dir * (p.mountainFreq * 0.35f) + p.offset.zxy));
                    e += relief * Ridged(dir * p.mountainFreq + p.offset.zxy, 4);
                }
            }

            if (p.riverDepth > 0f)
            {
                // Rivers follow the zero line of a low-frequency noise. A broad valley that widens with the
                // relief it cuts through keeps the banks walkable, and a narrow channel holds the water.
                float line = math.abs(noise.snoise(dir * p.riverFreq - p.offset));
                float bank = 0.4f * p.riverDepth;
                if (e > bank) e = math.lerp(e, bank, 1f - math.smoothstep(0f, 2f * p.riverWidth + p.riverBank * (p.plains + relief), line));
                e = math.min(e, math.lerp(e, -p.riverDepth, 1f - math.smoothstep(0.1f * p.riverWidth, 1.5f * p.riverWidth, line)));
            }

            if (p.craterDepth > 0f)
                e += Craters(dir * p.craterFreq + p.offset, p.craterDepth)
                    + Craters(dir * (p.craterFreq * 2.7f) - p.offset, p.craterDepth * 0.4f);

            return e;
        }

        static float Fbm(float3 q, int octaves)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++, amp *= 0.5f, q = q * 2.03f + 17.1f)
            {
                sum += amp * noise.snoise(q);
                norm += amp;
            }
            return sum / norm;
        }

        // Sharp ridges where the noise crosses zero, with detail concentrated on the ridges.
        static float Ridged(float3 q, int octaves)
        {
            float sum = 0f, amp = 0.5f, norm = 0f, weight = 1f;
            for (int i = 0; i < octaves; i++, amp *= 0.5f, q = q * 2.1f + 31.7f)
            {
                float n = 1f - math.abs(noise.snoise(q));
                n *= n * weight;
                weight = math.saturate(n * 2f);
                sum += amp * n;
                norm += amp;
            }
            return sum / norm;
        }

        // A bowl with a raised rim around every cellular-noise feature point.
        static float Craters(float3 q, float depth)
        {
            float r = noise.cellular(q).x / 0.45f;
            return depth * (math.smoothstep(0f, 1f, r) - 1f + 0.25f * math.exp(-16f * (r - 1f) * (r - 1f)));
        }
    }
}
