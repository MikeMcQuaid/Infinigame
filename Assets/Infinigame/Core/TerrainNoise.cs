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
        // No volcano on the body stands taller than this. Its volcanoes are listed in BodyDescriptor.
        public float volcanoHeight;

        public float MinHeight => -(seaDepth + riverDepth + hillAmp + craterDepth * 1.6f);
        // a volcano smooths away the ranges it stands on, so it is one or the other that is highest
        public float MaxHeight => plains + hillAmp + math.max(mountainAmp, volcanoHeight) + craterDepth * 0.9f;
    }

    // A volcano: a cone about a direction from the centre, this many metres from summit to foot
    // and this tall, with a caldera sunk in its top.
    [Serializable]
    public struct Volcano
    {
        public float3 direction;
        public float radius, height;
        // The caldera's radius as a share of the cone's; how much of the height is a cliff round the
        // foot; how the flanks steepen towards the summit (1 is a straight slope); and how much lava
        // it still holds, 0 to 1: a glowing lake in the caldera, and fresh dark flows down the flanks.
        public float caldera, scarp, steepening, lava;
    }

    // What a blast leaves of the ground it lands on: soil, sand and dust char, bare rock melts and
    // runs, ice thaws to a pool and the sea boils. A pad is ground a landing bomb has levelled.
    public enum Scar { Char, Lava, Melt, Steam, Pad }

    // The mark a blast has left on a body.
    public struct Blast
    {
        // a direction from the centre and a radius in metres, as in BodyDescriptor.craters
        public float4 crater;
        public Scar scar;
        // when, in seconds on the clock of whoever dug it: molten rock cools from then
        public float born;
        // metres above the body's radius: where a pad lies, or the surface of the meltwater in a
        // thawed crater, which stands this far out from its middle as a share of its radius
        public float level, pool;
        // the way the ground falls away from it, as long as the slope is steep: molten rock runs that way
        public float3 downhill;
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
                    // ranges rise close to the coast, and the power sharpens ridges into peaks
                    relief = p.mountainAmp * math.smoothstep(0.05f, 0.35f, continent)
                        * math.smoothstep(-0.1f, 0.4f, noise.snoise(dir * (p.mountainFreq * 0.35f) + p.offset.zxy));
                    e += relief * math.pow(Ridged(dir * p.mountainFreq + p.offset.zxy, 5), 1.25f);
                }
            }

            if (p.riverDepth > 0f)
            {
                // Rivers follow the zero line of a low-frequency noise. A broad valley that widens with the
                // relief it cuts through keeps the banks walkable, and a narrow channel holds the water.
                float line = math.abs(noise.snoise(dir * p.riverFreq - p.offset));
                float bank = 0.4f * p.riverDepth;
                if (e > bank) e = math.lerp(e, bank, 1f - math.smoothstep(0f, math.min(0.5f, 2f * p.riverWidth + p.riverBank * (p.plains + relief)), line));
                e = math.min(e, math.lerp(e, -p.riverDepth, 1f - math.smoothstep(0.1f * p.riverWidth, 1.5f * p.riverWidth, line)));
            }

            if (p.craterDepth > 0f)
                e += Craters(dir * p.craterFreq + p.offset, p.craterDepth)
                    + Craters(dir * (p.craterFreq * 2.7f) - p.offset, p.craterDepth * 0.4f)
                    + Craters(dir * (p.craterFreq * 6.5f) + p.offset.yzx, p.craterDepth * 0.15f);

            return e;
        }

        // What a volcano makes of the ground at a direction on a body of this radius: metres of cone
        // in x, and in y how far (0 to 1) it has smoothed away the land it stands on. across is how
        // far out from the summit the direction lies, 1 at the foot.
        public static float2 Cone(float3 dir, in Volcano volcano, float radius, out float across)
        {
            across = math.distance(dir, volcano.direction) * radius / volcano.radius;
            if (across >= 1.15f) return 0f;
            // no volcano is round: its foot wanders in and out
            across *= 1f + 0.12f * noise.snoise(dir * (1.5f * radius / volcano.radius) + 7f * volcano.direction);
            if (across >= 1f) return 0f;
            float flank = math.pow((1f - math.max(across, volcano.caldera)) / (1f - volcano.caldera), volcano.steepening);
            // a flat floor inside steep walls
            float sunk = 0.22f * (1f - math.smoothstep(0.55f * volcano.caldera, volcano.caldera, across));
            return new float2(volcano.height * ((1f - volcano.scarp) * flank + volcano.scarp * (1f - math.smoothstep(0.86f, 1f, across)) - sunk),
                1f - math.smoothstep(0.7f, 1f, across));
        }

        // What lava has made of a volcano's surface, this far out from its summit: in x the lake of
        // it glowing on the caldera's floor, in y the flows it has left down the flanks, each 0 to 1.
        public static float2 Flows(float3 dir, in Volcano volcano, float across)
        {
            // the flows run out from the summit in broad fingers, each as far as its bearing takes it
            float3 bearing = math.normalizesafe(dir - volcano.direction * math.dot(dir, volcano.direction));
            float reach = 0.3f + 0.65f * math.smoothstep(-0.4f, 0.6f,
                0.8f * noise.snoise(bearing * 3f + 13f * volcano.direction) + 0.2f * noise.snoise(bearing * 6f - 5f * volcano.direction));
            return new float2(volcano.lava * (1f - math.smoothstep(0.42f * volcano.caldera, 0.55f * volcano.caldera, across)),
                (0.5f + 0.5f * volcano.lava) * (1f - math.smoothstep(reach - 0.25f, reach, across)));
        }

        // How far beyond its radius a pad blends into the ground round it, in metres.
        public static float Apron(float size) => 0.5f * size + 3f;

        // How far from its centre a blast shows, in metres: molten rock and meltwater run furthest.
        public static float Reach(in Blast blast) =>
            blast.scar == Scar.Pad ? blast.crater.w + Apron(blast.crater.w) : (blast.scar == Scar.Char || blast.scar == Scar.Steam ? 1.8f : 3.6f) * blast.crater.w;

        // What one blast has made of the height of the ground at a direction on a body of this
        // radius, after those before it. Its crater is added to height: a bowl nearly half as deep
        // as it is wide, with a low rim thrown up round it. pool rises to the surface of any
        // meltwater standing in it. flat gathers the level a pad was flattened to (x) and how far
        // the ground has been brought to it (y, 0 to 1). The ground is then
        // lerp(max(height, pool), flat.x, flat.y): water fills the holes, and pads lie over everything.
        public static void Shape(float3 dir, in Blast blast, float radius, ref float height, ref float pool, ref float2 flat)
        {
            float metres = math.distance(dir, blast.crater.xyz) * radius, size = blast.crater.w, across = metres / size;
            if (blast.scar == Scar.Pad)
            {
                float share = 1f - math.smoothstep(size, size + Apron(size), metres);
                if (share <= 0f) return;
                // as if each pad in turn had been laid over the last
                float both = flat.y + share - flat.y * share;
                flat = new float2((flat.x * flat.y * (1f - share) + blast.level * share) / both, both);
                return;
            }
            if (across >= 1.4f) return;
            float bowl = math.square(1f - math.square(math.min(across, 1f)));
            height += size * (0.08f * math.smoothstep(0.7f, 1f, across) * (1f - math.smoothstep(1f, 1.4f, across)) - 0.45f * bowl);
            if (blast.scar == Scar.Melt && across < blast.pool) pool = math.max(pool, blast.level);
        }

        // Rock struck at this time is stone cold. Rock that ran this far down the order has always
        // lain where it is: a volcano's old flows, which anything molten now may yet run over.
        public const float Cold = -1e6f, Ran = 0.3f;

        // What one blast has left on the ground at a direction, for the patch meshes to carry to
        // the shader: soot in paint.x; molten rock in paint.y (the nearer 1, the sooner it ran
        // there) and paint.w (how much of it there is), with when it was struck in born; and in
        // paint.z the wet that meltwater leaves, up to a half, over which it is standing water.
        // spread is the gap between the points being painted, in metres: a mark smaller than that
        // is painted as wide and as much fainter, so that from far off it neither vanishes nor swells.
        public static void Paint(float3 dir, in Blast blast, float radius, float spread, ref float4 paint, ref float born)
        {
            if (blast.scar == Scar.Steam || blast.scar == Scar.Pad) return;
            float3 offset = (dir - blast.crater.xyz) * radius;
            float wide = math.max(blast.crater.w, 0.75f * spread), faint = math.square(blast.crater.w / wide), across = math.length(offset) / wide;
            if (blast.scar == Scar.Char)
            {
                paint.x = math.max(paint.x, faint * (1f - math.smoothstep(1f, 1.8f, across)));
                return;
            }
            // Molten rock fills the bowl at once and spills a little over its rim. From there it
            // creeps downhill in a tongue that narrows to its tip, the further the steeper the
            // ground. Meltwater runs the same way.
            float slope = math.length(blast.downhill), run = math.clamp(8f * slope, 1.3f, 3.5f);
            if (across > run + 2f * spread / wide) return;
            float order = 1f - math.smoothstep(1f, 1.3f, across), along = math.dot(offset, blast.downhill) / (math.max(slope, 1e-4f) * wide * run);
            if (along > 0f && along < 1f)
                order = math.max(order, (1f - 0.8f * along) * (1f - math.smoothstep(0.4f, 1f,
                    math.length(offset - blast.downhill * (along * wide * run / math.max(slope, 1e-4f))) / (wide * (0.9f - 0.45f * along)))));
            if (blast.scar == Scar.Melt)
            {
                paint.z = math.max(paint.z, 0.49f * faint * math.smoothstep(0.15f, 0.4f, order));
                return;
            }
            // the newest rock to have run here is the hot one, unless it only brushed past older
            if (order >= 0.5f || (paint.y < 0.5f && order >= paint.y)) born = math.max(born, blast.born);
            paint.y = math.max(paint.y, order);
            paint.w = math.max(paint.w, faint * math.smoothstep(0f, 0.15f, order));
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

        // A bowl with a raised rim and a central peak around every cellular-noise feature point.
        static float Craters(float3 q, float depth)
        {
            float r = noise.cellular(q).x / 0.45f;
            return depth * (math.smoothstep(0f, 1f, r) - 1f + 0.3f * math.exp(-16f * (r - 1f) * (r - 1f)) + 0.3f * math.exp(-20f * r * r));
        }
    }
}
