using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Infinigame
{
    public enum Surface { Sand, Grass, Rock, Snow, Bark, Dust, Water, Lava, Metal, Ceramic, Seal, Chitin }

    // Every surface in the game is painted here from layered noise the first time it is wanted: a
    // colour, and a height field whose slopes become the normal map. The height also rides in the
    // colour's alpha, so that where two kinds of ground meet the shader can let the higher one show:
    // sand lies in the cracks of rock, not over it. Nothing is loaded from disk.
    // All of them tile, because every noise below repeats over a whole number of cells.
    public static class Textures
    {
        // How deep each surface's relief is next to the width of one tile.
        static readonly float[] Relief = { 0.022f, 0.06f, 0.09f, 0.035f, 0.16f, 0.04f, 0.05f, 0.07f, 0.0012f, 0.002f, 0.008f, 0.012f };
        static readonly (Texture2D albedo, Texture2D normal)[] painted = new (Texture2D, Texture2D)[Relief.Length];

        public static Texture2D Albedo(Surface surface) => Get(surface).albedo;
        public static Texture2D Normal(Surface surface) => Get(surface).normal;

        static (Texture2D albedo, Texture2D normal) Get(Surface surface)
        {
            ref var slot = ref painted[(int)surface];
            if (slot.albedo != null) return slot;
            // Shared mipmapped tiles keep close detail without allocating a map for every object.
            int size = LodSettings.Tier == 0 ? 1024 : 2048;
            Texture2D Blank(bool linear) => new Texture2D(size, size, TextureFormat.RGBA32, true, linear)
                { name = surface + (linear ? " relief" : " colour and height"), filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            slot = (Blank(false), Blank(true));
            var pixels = new NativeArray<float4>(size * size, Allocator.TempJob);
            new PaintJob { surface = surface, size = size, pixels = pixels }.Schedule(size, 8).Complete();
            // straight into the textures' own memory, which begins with the full-size picture
            new PackJob { size = size, bump = Relief[(int)surface] * size / 2f, pixels = pixels, colours = slot.albedo.GetRawTextureData<Color32>(), slopes = slot.normal.GetRawTextureData<Color32>() }
                .Schedule(size, 8).Complete();
            pixels.Dispose();
            slot.albedo.Apply(true, true);
            slot.normal.Apply(true, true);
            return slot;
        }

        // compiled before it first runs, even in the editor: it runs once, and slowly otherwise
        [BurstCompile(CompileSynchronously = true)]
        struct PaintJob : IJobParallelFor
        {
            public Surface surface;
            public int size;
            [NativeDisableParallelForRestriction] public NativeArray<float4> pixels;

            public void Execute(int y)
            {
                for (int x = 0; x < size; x++) pixels[y * size + x] = Paint(surface, new float2(x + 0.5f, y + 0.5f) / size);
            }
        }

        // Turns what was painted into the two textures: colour with height in alpha, and the height's slopes as a normal map.
        [BurstCompile(CompileSynchronously = true)]
        struct PackJob : IJobParallelFor
        {
            public int size;
            public float bump;
            [ReadOnly] public NativeArray<float4> pixels;
            [NativeDisableParallelForRestriction] public NativeArray<Color32> colours, slopes;

            public void Execute(int y)
            {
                for (int x = 0; x < size; x++)
                {
                    float3 normal = math.normalize(new float3((Height(x - 1, y) - Height(x + 1, y)) * bump, (Height(x, y - 1) - Height(x, y + 1)) * bump, 1f));
                    colours[y * size + x] = Bytes(pixels[y * size + x]);
                    slopes[y * size + x] = Bytes(new float4(normal * 0.5f + 0.5f, 1f));
                }
            }

            float Height(int x, int y) => pixels[(y + size) % size * size + (x + size) % size].w;

            static Color32 Bytes(float4 colour)
            {
                int4 bytes = (int4)(math.saturate(colour) * 255f + 0.5f);
                return new Color32((byte)bytes.x, (byte)bytes.y, (byte)bytes.z, (byte)bytes.w);
            }
        }

        // Colour in xyz and height in w, both 0 to 1, for a point on the unit tile.
        static float4 Paint(Surface surface, float2 uv)
        {
            switch (surface)
            {
                case Surface.Metal:
                {
                    float brush = noise.pnoise(uv * new float2(24, 700), new float2(24, 700));
                    float wear = Fbm(uv, 8, 3), pits = Fbm(uv, 240, 2);
                    float scratch = math.pow(math.saturate(noise.pnoise(uv * new float2(9, 320), new float2(9, 320)) + 0.35f), 8);
                    float value = 0.77f + brush * 0.045f + wear * 0.15f - scratch * 0.25f;
                    return new float4(new float3(value), math.saturate(0.5f + brush * 0.15f + pits * 0.13f - scratch));
                }
                case Surface.Ceramic:
                {
                    float grain = Fbm(uv, 180, 2), wear = Fbm(uv, 5, 3);
                    float3 cells = Cells(uv * 38, 38);
                    float chip = (1 - math.smoothstep(0.02f, 0.055f, cells.y - cells.x)) * math.smoothstep(0.6f, 0.8f, wear);
                    return new float4(new float3(0.86f + grain * 0.1f - chip * 0.35f), 0.5f + grain * 0.06f - chip * 0.2f);
                }
                case Surface.Seal:
                {
                    float weave = math.sin(uv.x * 256 * math.PI) * math.sin(uv.y * 256 * math.PI);
                    float grain = Fbm(uv, 64, 2);
                    return new float4(new float3(0.68f + grain * 0.2f + weave * 0.08f), 0.5f + weave * 0.16f);
                }
                case Surface.Chitin:
                {
                    float warp = Fbm(uv, 6, 3), pores = Fbm(uv, 160, 2);
                    float3 scales = Cells(uv * new float2(17, 27) + 0.55f * (warp - 0.5f), new float2(17, 27));
                    float seam = 1 - math.smoothstep(0.015f, 0.1f, scales.y - scales.x);
                    float striae = 0.5f + 0.5f * noise.pnoise(uv * new float2(12, 180), new float2(12, 180));
                    float3 colour = math.lerp(new float3(0.34f, 0.42f, 0.44f), new float3(0.96f, 0.83f, 0.61f), warp);
                    colour *= (0.75f + striae * 0.16f + pores * 0.15f) * (1 - seam * 0.42f);
                    return new float4(colour, 0.55f - seam * 0.28f + striae * 0.045f + pores * 0.04f);
                }
                case Surface.Sand:
                {
                    // Wind ripples a hand apart that fork, wander and die away, over soft hollows. The
                    // colour hardly changes: it is the relief that draws them, as on a real beach.
                    float wander = Fbm(uv, 3f, 3), hollows = Fbm(uv + 0.31f, 2f, 3), grain = Fbm(uv, 96f, 2);
                    float streaks = noise.pnoise(uv * new float2(4f, 31f) + 3f, new float2(4f, 31f));
                    // a gentle windward slope and a short steep lee
                    float along = math.frac(17f * uv.y + 1.8f * wander + 0.6f * streaks);
                    float ripple = math.smoothstep(0f, 0.72f, along) * (1f - math.smoothstep(0.72f, 1f, along)) * (0.3f + 0.7f * math.smoothstep(0.3f, 0.7f, Fbm(uv + 0.7f, 3f, 3)));
                    float2 speck = Random(math.floor(uv * 400f));
                    float3 stones = Cells(uv * 15f, 15f);
                    float pebble = stones.z > 0.955f ? math.saturate(1f - math.square(stones.x / (0.05f + 0.07f * math.frac(stones.z * 31f)))) : 0f;
                    float3 colour = math.lerp(new float3(0.72f, 0.62f, 0.46f), new float3(0.86f, 0.78f, 0.62f), 0.45f * grain + 0.35f * hollows + 0.2f * ripple);
                    // dark heavy grains and the odd bright one of quartz
                    colour *= speck.x > 0.965f ? 0.6f : speck.x < 0.04f ? 1.12f : 1f;
                    // small stones and bits of shell
                    colour = math.lerp(colour, math.lerp(new float3(0.4f, 0.38f, 0.36f), new float3(0.9f, 0.87f, 0.8f), math.frac(stones.z * 7f)), math.smoothstep(0f, 0.3f, pebble));
                    return new float4(colour, 0.2f + 0.25f * hollows + 0.3f * ripple + 0.08f * grain + 0.25f * math.sqrt(pebble));
                }
                case Surface.Dust:
                {
                    // grey regolith: soft mounds, grit, pits of every size with raised rims, and scattered stones
                    float grit = Fbm(uv, 64f, 3), mounds = Fbm(uv, 4f, 4);
                    float3 large = Cells(uv * 4f + 0.3f * (mounds - 0.5f), 4f), small = Cells(uv * 11f, 11f), stones = Cells(uv * 23f, 23f);
                    static float Pit(float3 cell, float share)
                    {
                        if (cell.z > share) return 0f;
                        float across = cell.x / (0.12f + 0.3f * math.frac(cell.z * 37f));
                        return math.smoothstep(0f, 1f, across) - 1f + 0.4f * math.exp(-math.square((across - 1f) / 0.18f));
                    }
                    float pit = Pit(large, 0.4f) + 0.5f * Pit(small, 0.3f);
                    float stone = stones.z > 0.9f ? math.sqrt(math.saturate(1f - math.square(stones.x / (0.1f + 0.22f * math.frac(stones.z * 17f))))) : 0f;
                    float shade = 0.5f + 0.3f * (0.6f * grit + 0.4f * mounds) + 0.08f * pit + math.smoothstep(0f, 0.4f, stone) * (0.1f - 0.3f * math.frac(stones.z * 5f));
                    return new float4(shade, shade, shade, math.saturate(0.3f + 0.35f * mounds + 0.12f * grit + 0.3f * pit + 0.3f * stone));
                }
                case Surface.Grass:
                {
                    // turf from above: blades of three sizes lying over one another in clumps, each its own
                    // green and paler towards its tip, a few of them dry, over the dark of the thatch beneath
                    float3 blade = Blades(uv * 48f, 48f, 0.12f);
                    float3 other = Blades(uv * 72f + 0.5f, 72f, 0.14f) * new float3(0.85f, 1f, 1f);
                    if (other.x > blade.x) blade = other;
                    other = Blades(uv * 108f + 0.25f, 108f, 0.16f) * new float3(0.7f, 1f, 1f);
                    if (other.x > blade.x) blade = other;
                    float lush = Fbm(uv, 4f, 3), thatch = Fbm(uv, 80f, 2);
                    float3 green = math.lerp(new float3(0.24f, 0.38f, 0.11f), new float3(0.46f, 0.6f, 0.2f), blade.y);
                    if (math.frac(blade.y * 17f) > 0.9f) green = new float3(0.6f, 0.55f, 0.3f);
                    float3 colour = math.lerp(math.lerp(new float3(0.12f, 0.15f, 0.05f), new float3(0.3f, 0.27f, 0.14f), thatch * thatch),
                        green * (0.6f + 0.55f * blade.z), math.smoothstep(0f, 0.15f, blade.x)) * (0.88f + 0.24f * lush);
                    return new float4(colour, blade.x);
                }
                case Surface.Rock:
                {
                    // broken stone: beds that split along joints into flat faces, each tilted and weathered its
                    // own way, then smaller blocks within them, chiselled all over
                    float2 warp = new float2(Fbm(uv, 4f, 3), Fbm(uv + 0.5f, 4f, 3)) - 0.5f, beds = new float2(3f, 6f), bits = new float2(8f, 13f);
                    float3 slabs = Facets(uv * beds + 0.35f * warp, beds), blocks = Facets(uv * bits + 0.6f * warp + 0.37f, bits), lichen = Cells(uv * 7f + warp, 7f);
                    float chisel = Ridged(uv, 12f, 3), grit = Fbm(uv, 110f, 2), stain = Fbm(uv + 0.2f, 3f, 4);
                    float joint = 1f - math.smoothstep(0f, 0.045f, slabs.y), crack = 1f - math.smoothstep(0f, 0.04f, blocks.y);
                    float height = math.saturate(0.5f * slabs.x + 0.24f * blocks.x + 0.15f * chisel + 0.05f * grit - 0.4f * joint - 0.15f * crack);
                    float3 colour = math.lerp(new float3(0.4f, 0.39f, 0.39f), new float3(0.62f, 0.56f, 0.47f), stain) * (0.85f + 0.25f * slabs.z) * (0.93f + 0.14f * blocks.z);
                    // faint beds of darker and paler stone, dirt in every crack and hollow
                    colour *= 0.95f + 0.08f * math.sin(2f * math.PI * (11f * uv.y + uv.x + 1.4f * warp.x));
                    colour *= (0.86f + 0.22f * height) * (1f - 0.35f * joint) * (1f - 0.2f * crack) * (0.92f + 0.16f * grit);
                    // grey-green lichen settles on a few faces
                    if (lichen.z > 0.72f) colour = math.lerp(colour, new float3(0.52f, 0.55f, 0.4f), 0.55f * (1f - math.smoothstep(0.12f, 0.34f, lichen.x + 0.2f * chisel)) * Fbm(uv, 40f, 2));
                    return new float4(colour, height);
                }
                case Surface.Snow:
                {
                    // wind-smoothed drifts under a faint crust, blue in the hollows, with ice crystals catching the light
                    float drifts = Fbm(uv, 4f, 4), crust = Fbm(uv, 44f, 2);
                    float3 crystals = Cells(uv * 80f, 80f);
                    float3 colour = math.lerp(new float3(0.76f, 0.84f, 0.95f), new float3(0.97f, 0.98f, 1f), math.smoothstep(0.25f, 0.75f, drifts) * (0.9f + 0.1f * crust));
                    return new float4(colour + (crystals.z > 0.9f && crystals.x < 0.15f ? 0.06f : 0f), 0.8f * drifts + 0.2f * crust);
                }
                case Surface.Water:
                {
                    // Height: swells running before one wind, their crests long and narrower than their troughs,
                    // and a chop over them. Colour: the lace of foam that lies where water meets the shore.
                    float bend = 0.3f * Fbm(uv, 3f, 3), chop = Fbm(uv, 16f, 4);
                    // so many crests across the tile each way: whole numbers, so that it repeats
                    static float Swell(float2 uv, float2 crests, float shift) =>
                        math.square(0.5f + 0.5f * math.sin(2f * math.PI * (math.dot(crests, uv) + shift))) / math.length(crests);
                    float height = Swell(uv, new float2(0f, 3f), bend) + Swell(uv, new float2(1f, 5f), bend + 0.37f) + Swell(uv, new float2(-2f, 7f), bend + 0.74f)
                        + Swell(uv, new float2(2f, 10f), bend + 0.11f) + Swell(uv, new float2(-3f, 13f), bend + 0.48f) + Swell(uv, new float2(4f, 17f), bend + 0.85f)
                        + 0.1f * noise.pnoise(uv * new float2(7f, 29f), new float2(7f, 29f));
                    float3 bubbles = Cells(uv * 14f + 1.5f * (chop - 0.5f), 14f), froth = Cells(uv * 37f, 37f);
                    float foam = math.saturate(1.2f * math.smoothstep(0.12f, 0f, bubbles.y - bubbles.x) + 0.4f * math.smoothstep(0.12f, 0f, froth.y - froth.x) + 1.2f * Fbm(uv, 6f, 3) - 0.6f);
                    return new float4(foam, foam, foam, math.saturate(0.75f * height + 0.2f * chop));
                }
                case Surface.Lava:
                {
                    // Rock that ran molten and set: a black crust broken into plates, each wrinkled into
                    // ropes the way it was flowing. The height falls to nothing in the cracks between
                    // them, which is where the shader lets the heat beneath show while there is any.
                    float2 warp = new float2(Fbm(uv, 4f, 3), Fbm(uv + 0.5f, 4f, 3)) - 0.5f;
                    float3 plates = Cells(uv * 6f + 0.7f * warp, 6f), chips = Cells(uv * 17f + warp, 17f);
                    float whole = math.smoothstep(0f, 0.13f, plates.y - plates.x) * math.smoothstep(-0.02f, 0.05f, chips.y - chips.x), grit = Fbm(uv, 90f, 2);
                    // so many ropes across the tile each way: whole numbers, so that it repeats
                    float2 lie = plates.z < 0.33f ? new float2(21f, 6f) : plates.z < 0.66f ? new float2(-8f, 19f) : new float2(15f, -16f);
                    float ropes = 0.5f + 0.5f * math.sin(2f * math.PI * (math.dot(lie, uv) + 2.5f * warp.x + 0.6f * Fbm(uv + 0.3f, 9f, 2)));
                    float height = math.saturate(whole * (0.5f + 0.2f * math.frac(plates.z * 7f) + 0.22f * ropes + 0.08f * grit));
                    float3 colour = math.lerp(new float3(0.035f, 0.03f, 0.03f), new float3(0.13f, 0.115f, 0.105f), ropes * (0.4f + 0.6f * grit))
                        * (0.75f + 0.5f * math.frac(plates.z * 13f)) * (0.35f + 0.65f * whole);
                    return new float4(colour, height);
                }
                default:
                {
                    // bark: long plates split by deep furrows, with fibres running up the trunk
                    float3 plates = Cells(new float2(uv.x * 14f, uv.y * 4f) + 0.6f * (Fbm(uv, 6f, 3) - 0.5f), new float2(14f, 4f));
                    float furrow = 1f - math.smoothstep(0.02f, 0.22f, plates.y - plates.x), fibres = 0.5f + 0.5f * noise.pnoise(uv * new float2(70f, 7f), new float2(70f, 7f));
                    float height = math.saturate(0.75f * (1f - furrow) + 0.25f * fibres);
                    float3 colour = math.lerp(new float3(0.11f, 0.075f, 0.05f), math.lerp(new float3(0.4f, 0.28f, 0.18f), new float3(0.42f, 0.38f, 0.33f), plates.z), height * height);
                    return new float4(colour, height);
                }
            }
        }

        // Two random numbers belonging to a cell of a grid.
        static float2 Random(float2 cell) => math.frac(math.sin(new float2(math.dot(cell, new float2(127.1f, 311.7f)), math.dot(cell, new float2(269.5f, 183.3f)))) * 43758.5453f);

        // Smooth noise summed over octaves, about 0 to 1, repeating every `period` cells.
        static float Fbm(float2 uv, float period, int octaves)
        {
            float sum = 0f, amplitude = 0.5f, total = 0f;
            for (int i = 0; i < octaves; i++, period *= 2f, amplitude *= 0.5f)
            {
                // whole-number offsets keep each octave different without breaking the repeat
                sum += amplitude * noise.pnoise(uv * period + 17f * i, period);
                total += amplitude;
            }
            return math.saturate(0.5f + 0.85f * sum / total);
        }

        // Sharp crests where the noise crosses zero.
        static float Ridged(float2 uv, float period, int octaves)
        {
            float sum = 0f, amplitude = 0.5f, total = 0f;
            for (int i = 0; i < octaves; i++, period *= 2f, amplitude *= 0.5f)
            {
                sum += amplitude * math.square(1f - math.abs(noise.pnoise(uv * period + 31f * i, period)));
                total += amplitude;
            }
            return sum / total;
        }

        // Blades of grass, one rooted in each cell of a grid that repeats every `period` cells (a
        // multiple of three). Each lies its own way, though neighbours lean alike as a clump does,
        // curves, tapers to its tip and reaches over the cells next to it. For the blade lying
        // uppermost over the point: how high it stands there in x (zero where there is none), a
        // random number belonging to it in y, and how far along it the point is in z.
        static float3 Blades(float2 p, float period, float width)
        {
            float2 cell = math.floor(p), within = p - cell;
            float3 best = 0f;
            for (int j = -2; j <= 2; j++)
            for (int i = -2; i <= 2; i++)
            {
                float2 id = cell + new float2(i, j);
                id -= period * math.floor(id / period);
                float2 random = Random(id), from = within - (new float2(i, j) + random);
                // no blade rooted that far off could reach
                if (math.lengthsq(from) > 3.6f) continue;
                float2 clump = Random(math.floor(id / 3f) + 71f);
                float angle = 2f * math.PI * clump.x + 2.2f * (random.y - 0.5f), length = 0.9f + 0.8f * math.frac(random.x * 13.7f);
                float2 direction = new float2(math.cos(angle), math.sin(angle));
                float along = math.dot(from, direction) / length;
                if (along < 0f || along > 1f) continue;
                // bowed to one side, most in the middle
                float aside = math.abs(math.dot(from, new float2(-direction.y, direction.x)) - 0.35f * (clump.y - 0.5f) * math.sin(math.PI * along)) / (width * (1f - along * along));
                if (aside >= 1f) continue;
                // the tip stands highest; the rounded back catches the light
                float height = (0.3f + 0.7f * along) * (1f - 0.5f * aside * aside) * (0.75f + 0.25f * random.y);
                if (height > best.x) best = new float3(height, math.frac(random.x * 7.31f + random.y * 3.17f), along);
            }
            return best;
        }

        // Cellular noise that repeats every `period` cells: the distance to the nearest feature point
        // in x, to the next nearest in y, and a random number belonging to the nearest in z.
        static float3 Cells(float2 p, float2 period)
        {
            float2 cell = math.floor(p), within = p - cell;
            float3 best = new float3(8f, 8f, 0f);
            for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
            {
                float2 id = cell + new float2(i, j);
                id -= period * math.floor(id / period);
                float2 point = Random(id);
                float distance = math.length(new float2(i, j) + point - within);
                if (distance < best.x) best = new float3(distance, best.x, math.frac(point.x * 7.31f + point.y * 3.17f));
                else if (distance < best.y) best.y = distance;
            }
            return best;
        }

        // The same cells as flat faces of broken stone, each tilted its own way and set at its own
        // depth: the height of the face over the point in x, how far the point is from the crack round
        // the face in y, and a random number belonging to the face in z.
        static float3 Facets(float2 p, float2 period)
        {
            float2 cell = math.floor(p), within = p - cell, to = 0f, random = 0f;
            float nearest = 8f, next = 8f;
            for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
            {
                float2 id = cell + new float2(i, j);
                id -= period * math.floor(id / period);
                float2 point = Random(id), offset = new float2(i, j) + point - within;
                float distance = math.length(offset);
                if (distance < nearest) (next, nearest, to, random) = (nearest, distance, offset, point);
                else if (distance < next) next = distance;
            }
            float level = math.frac(random.x * 7.31f + random.y * 3.17f);
            return new float3(math.saturate(0.2f + 0.6f * level + math.dot(Random(random * 91f) - 0.5f, to)), next - nearest, level);
        }
    }
}
