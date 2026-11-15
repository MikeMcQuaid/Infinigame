using System.Collections.Generic;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

namespace Infinigame.Core
{
    public struct Rock
    {
        public float3 position;
        public float radius;
    }

    // A planet's rings are countless separate rocks, each on its own orbit, in a sheet a few metres
    // thick. From far off they blur into bands; close to, every one is a thing to fly round or into.
    // A star's ring is its asteroid belt: far bigger rocks, far further apart, in a far deeper sheet.
    //
    // Rocks come in classes, each half the size of the last and four times as many. A class cuts
    // the ring into bands by distance from the planet, and each band into cells round it. Which
    // rocks a cell holds is a pure function of the planet, so nothing is stored; a band turns as
    // one, at the speed of an orbit at its distance, so the inner ring laps the outer.
    public static class Rings
    {
        public const int Classes = 5, PerCell = 96;

        // The radius of the biggest rock in a class, and the width of that class's cells.
        public static float Size(BodyDescriptor body, int size) => body.ringRock / (1 << size);
        public static float Cell(BodyDescriptor body, int size) => 120f * Size(body, size);

        public static int Bands(BodyDescriptor body, int size) => (int)math.ceil((body.ringOuter - body.ringInner) / Cell(body, size));

        public static int Sectors(BodyDescriptor body, int size, int band) =>
            math.max(1, (int)math.round(2f * math.PI * (body.ringInner + (band + 0.5f) * Cell(body, size)) / Cell(body, size)));

        // How full the ring is this far from the planet, 0 to 1: broad bands, soft edges and one dark division.
        public static float Density(BodyDescriptor body, float distance)
        {
            float across = (distance - body.ringInner) / (body.ringOuter - body.ringInner);
            if (across <= 0f || across >= 1f) return 0f;
            return math.saturate(0.6f + 0.55f * noise.snoise(new float2(across * 5f, body.terrain.offset.x)))
                * math.smoothstep(0f, 0.05f, across) * (1f - math.smoothstep(0.92f, 1f, across))
                * (1f - 0.95f * (1f - math.smoothstep(0.012f, 0.03f, math.abs(across - 0.62f))));
        }

        // How far a band has turned about the planet's axis (+y) by a time, in the planet's own
        // turning frame, in radians. Rings circle the same way the planet spins, and a belt the way
        // the planets go round.
        public static float Turned(BodyDescriptor body, int size, int band, double time)
        {
            if (body.ringPeriod != 0) return (float)math.fmod(-2 * math.PI_DBL / body.ringPeriod * time, 2 * math.PI_DBL);
            double distance = body.ringInner + (band + 0.5) * Cell(body, size), sense = body.spinPeriod < 0 ? 1 : -1;
            double rate = sense * math.sqrt(body.surfaceGravity * body.radius * (double)body.radius / (distance * distance * distance))
                + (body.spinPeriod == 0 ? 0 : 2 * math.PI_DBL / body.spinPeriod);
            return (float)math.fmod(rate * time, 2 * math.PI_DBL);
        }

        // The rocks of one cell, where they were at time zero. Turn them by Turned for where they are now.
        public static void Rocks(BodyDescriptor body, int size, int band, int sector, List<Rock> into)
        {
            var rnd = new Random(math.hash(new float4(body.terrain.offset.x, size, band, sector)) | 1u);
            float cell = Cell(body, size), arc = 2f * math.PI / Sectors(body, size, band);
            for (int i = 0; i < PerCell; i++)
            {
                float distance = body.ringInner + (band + rnd.NextFloat()) * cell, angle = (sector + rnd.NextFloat()) * arc;
                float height = rnd.NextFloat(-body.ringThick, body.ringThick), radius = Size(body, size) * rnd.NextFloat(0.5f, 1f);
                if (rnd.NextFloat() < Density(body, distance) * body.ringDensity)
                    into.Add(new Rock { position = new float3(math.cos(angle) * distance, height, math.sin(angle) * distance), radius = radius });
            }
        }

        static float3 TurnY(float3 point, float angle)
        {
            math.sincos(angle, out float sine, out float cosine);
            return new float3(point.x * cosine + point.z * sine, point.y, point.z * cosine - point.x * sine);
        }

        // Where a rock that started at a point is by now.
        public static float3 Now(BodyDescriptor body, int size, int band, double time, float3 start) => TurnY(start, Turned(body, size, band, time));

        // The rock, if any, that a sphere in the planet's frame is touching. normal points from the
        // rock to the sphere and velocity is the rock's. Only rocks big enough to matter count.
        public static bool Hit(BodyDescriptor body, float3 position, float radius, double time, List<Rock> scratch, out float3 normal, out float3 velocity)
        {
            normal = velocity = default;
            float distance = math.length(position.xz);
            float margin = body.ringRock + radius;
            if (body.ringOuter == 0f || math.abs(position.y) > body.ringThick + margin || distance < body.ringInner - margin || distance > body.ringOuter + margin)
                return false;
            for (int size = 0; size < Classes; size++)
            {
                // gravel does a ship no harm
                if (Size(body, size) < 1.5f) break;
                float cell = Cell(body, size), reach = radius + Size(body, size);
                int bands = Bands(body, size);
                for (int band = math.max(0, (int)math.floor((distance - reach - body.ringInner) / cell)); band <= math.min(bands - 1, (int)math.floor((distance + reach - body.ringInner) / cell)); band++)
                {
                    float turned = Turned(body, size, band, time);
                    float3 start = TurnY(position, -turned);
                    int sectors = Sectors(body, size, band);
                    float arc = 2f * math.PI / sectors, angle = math.atan2(start.z, start.x), spread = reach / math.max(distance, 1f);
                    for (int sector = (int)math.floor((angle - spread) / arc); sector <= (int)math.floor((angle + spread) / arc); sector++)
                    {
                        scratch.Clear();
                        Rocks(body, size, band, ((sector % sectors) + sectors) % sectors, scratch);
                        foreach (var rock in scratch)
                        {
                            if (math.distancesq(rock.position, start) > math.square(rock.radius + radius)) continue;
                            float3 centre = TurnY(rock.position, turned);
                            normal = math.normalizesafe(position - centre, new float3(0, 1, 0));
                            // every rock in a band circles the axis at the band's rate
                            velocity = math.cross(new float3(0f, Turned(body, size, band, 1.0), 0f), centre);
                            return true;
                        }
                    }
                }
            }
            return false;
        }
    }
}
