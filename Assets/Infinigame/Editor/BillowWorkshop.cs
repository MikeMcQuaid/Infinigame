using System;
using System.Collections.Generic;
using Infinigame.Core;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Random = Unity.Mathematics.Random;

namespace Infinigame
{
    static class BillowWorkshop
    {
        public static Texture2D Billows()
        {
            const int size = 512;
            var billows = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { filterMode = FilterMode.Trilinear };
            // straight into the texture's own memory, which begins with the full-size picture
            new BillowJob { size = size, pixels = billows.GetRawTextureData<Color32>() }.Schedule(size, 8).Complete();
            billows.Apply(true, false);
            return billows;
        }

        // Paints what every puff is cut from, which repeats: round heads crowding each other at
        // three sizes, as the tops of smoke and cloud do, with which way they slope beside them
        // and a finer field of wisps for a thinning puff to be eaten away by.
        // compiled before it first runs, even in the editor: it runs once, and slowly otherwise
        [BurstCompile(CompileSynchronously = true)]
        struct BillowJob : IJobParallelFor
        {
            public int size;
            [NativeDisableParallelForRestriction] public NativeArray<Color32> pixels;

            public void Execute(int y)
            {
                for (int x = 0; x < size; x++)
                {
                    float2 p = new float2(x + 0.5f, y + 0.5f) / size;
                    float height = Heads(p);
                    // a slope of sixteen heights in the width of the painting is as steep as a byte holds
                    float2 slope = (new float2(Heads(p + new float2(1f / size, 0f)), Heads(p + new float2(0f, 1f / size))) - height) * (size / 32f) + 0.5f;
                    // wisps are soft and of every size
                    float wisps = 0.5f + 0.55f * noise.pnoise(p * 4f, new float2(4f)) + 0.3f * noise.pnoise(p * 9f + 3.7f, new float2(9f)) + 0.15f * noise.pnoise(p * 19f + 1.3f, new float2(19f));
                    float4 packed = math.saturate(new float4(height, slope, wisps)) * 255f;
                    pixels[y * size + x] = new Color32((byte)packed.x, (byte)packed.y, (byte)packed.z, (byte)packed.w);
                }
            }

            // Bigger and smaller in broad patches, so that no two puffs are lumpy alike, and pushed
            // about before they are placed: nothing in smoke runs straight.
            static float Heads(float2 p)
            {
                p += 0.02f * new float2(noise.pnoise(p * 5f, new float2(5f)), noise.pnoise(p * 5f + 31.7f, new float2(5f)));
                return (0.55f * Cells(p, 5, 1) + 0.3f * Cells(p, 11, 2) + 0.15f * Cells(p, 23, 3)) * (0.775f + 0.225f * noise.pnoise(p * 3f, new float2(3f)));
            }

            // Round bumps packed edge to edge, one somewhere in each cell of a grid that repeats:
            // 1 at the top of a bump, falling to 0 in the creases between them, which are rounded off.
            static float Cells(float2 p, int cells, int seed)
            {
                p *= cells;
                int2 cell = (int2)math.floor(p);
                float crowd = 0f;
                for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    int2 other = cell + new int2(i, j), wrapped = (other % cells + cells) % cells;
                    crowd += math.exp(-14f * math.distancesq(p, other + Random.CreateFromIndex((uint)(wrapped.x + cells * (wrapped.y + cells * seed))).NextFloat2()));
                }
                return 1f - math.saturate(1.8f * -math.log(crowd) / 14f);
            }
        }
    }
}
