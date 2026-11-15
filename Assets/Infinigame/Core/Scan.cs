using System.Collections.Generic;
using Unity.Mathematics;

namespace Infinigame.Core
{
    // Something a scanner can single out: it stands on its foot (a point relative to the body's
    // centre), this tall and this far across.
    public struct Scannable
    {
        public string name;
        public float3 foot;
        public float height, girth;
    }

    // What the player's scanner is pointing at, and the plain facts about a place that a language
    // model needs to write about it.
    public static class Scan
    {
        public const float Range = 80f;

        // The nearest thing the ray passes through, or -1.
        public static int Pick(float3 eye, float3 ray, IReadOnlyList<Scannable> things)
        {
            int best = -1;
            float nearest = Range;
            for (int i = 0; i < things.Count; i++)
            {
                var thing = things[i];
                float3 up = math.normalize(thing.foot), to = thing.foot - eye;
                // the point up its height that comes closest to the ray
                float lean = math.dot(up, ray), across = 1f - lean * lean;
                to += up * (across < 1e-4f ? 0f : math.clamp((lean * math.dot(to, ray) - math.dot(to, up)) / across, 0f, thing.height));
                float along = math.dot(to, ray);
                if (along <= 0f || along >= nearest || math.lengthsq(to - ray * along) > thing.girth * thing.girth) continue;
                nearest = along;
                best = i;
            }
            return best;
        }

        // Where the ray first meets the ground or the sea.
        public static bool Ground(BodyDescriptor body, float3 eye, float3 ray, out float3 hit)
        {
            for (float along = 1f; along < Range; along += 1.5f)
            {
                hit = eye + ray * along;
                float3 up = math.normalize(hit);
                if (math.length(hit) < math.max(body.GroundRadius(up), body.SeaRadius)) return true;
            }
            hit = default;
            return false;
        }

        // The body a ray from a point in the reference body's frame is aimed at, or -1. Far-off
        // worlds are small targets, so anything within a degree or so counts, and of those the one
        // aimed at most squarely wins: a moon is not lost against the planet behind it.
        public static int Body(SolarSystemDescriptor system, int reference, double time, float3 eye, float3 ray)
        {
            int best = -1;
            float squarest = float.MaxValue, nearest = float.MaxValue;
            for (int i = 0; i < system.bodies.Length; i++)
            {
                float3 to = system.ToLocal(reference, time, system.PositionAt(i, time)) - eye;
                float along = math.dot(to, ray), wide = math.max(0f, math.length(to - ray * along) - system.bodies[i].radius) / math.max(along, 1e-3f);
                if (along <= 0f || wide > 0.02f || wide > squarest || (wide == squarest && along >= nearest)) continue;
                (squarest, nearest, best) = (wide, along, i);
            }
            return best;
        }

        // What kind of ground a spot is, in a word or two. natural is what it was made as, whatever
        // has been blasted out of it since: BodyDescriptor.Blasted goes by that.
        public static string Terrain(BodyDescriptor body, float3 direction, bool natural = false)
        {
            float height = body.GroundRadius(direction, natural) - body.radius, level = math.dot(body.GroundNormal(direction, natural), direction);
            float peaks = body.terrain.mountainAmp, pole = math.smoothstep(0.75f, 0.95f, math.abs(direction.y));
            if (body.kind == BodyKind.Gas) return "cloud tops";
            if (body.kind == BodyKind.Station) return "blackened iron hull";
            if (body.SeaRadius > 0f)
                return height < 0f ? "sea" : height < 1.6f ? "beach sand"
                    : level > 0.75f && height + body.terrain.polarCap * pole > 0.7f * peaks + 16f ? "snow"
                    : level < 0.8f ? "cliff rock" : height > 0.45f * peaks + 12f ? "mountain rock" : "meadow grass";
            if (height + body.terrain.polarCap * pole > 0.9f * body.terrain.MaxHeight && body.terrain.polarCap > 0f) return "polar ice";
            foreach (var volcano in body.volcanoes)
                if (TerrainNoise.Cone(direction, volcano, body.radius, out float across).y > 0f)
                    return TerrainNoise.Flows(direction, volcano, across).x > 0.5f ? "lava lake" : across < volcano.caldera ? "caldera floor" : "volcano flank";
            return level < 0.8f ? "bare rock face" : body.terrain.craterDepth > 0.05f * body.radius && height < -0.3f * body.terrain.craterDepth ? "crater floor dust" : "dust plain";
        }

        // A body in a sentence: what it is, how big, how heavy, how warm and how long its day is.
        public static string Describe(BodyDescriptor body)
        {
            string text = $"{body.name}: {body.summary}. Radius {body.radius:0} m";
            if (body.kind == BodyKind.Station) return text + ".";
            text += $", surface gravity {body.surfaceGravity:0.0} m/s2, about {Climate.Celsius(body.heat):0} degrees Celsius {(body.kind == BodyKind.Gas ? "at the cloud tops" : "at the surface")}";
            if (body.spinPeriod != 0) text += $", a day of {Span(body.spinPeriod)}{(body.spinPeriod < 0 ? ", turning backwards" : "")}";
            if (body.orbit.parent >= 0) text += $", {(body.orbit.parent == 0 ? "a year" : "a month")} of {Span(body.orbit.period)}";
            return text + ".";
        }

        // A length of time as the player would sit through it: minutes, or hours once there are a hundred of those.
        public static string Span(double seconds) => math.abs(seconds) < 6000 ? $"{math.abs(seconds) / 60:0} min" : $"{math.abs(seconds) / 3600:#,0} h";

        // A spot on a body in a sentence: latitude, height, the lie of the land, the hour and the weather.
        public static string Place(SolarSystemDescriptor system, int index, float3 position, double time)
        {
            var body = system.bodies[index];
            float3 up = math.normalize(position), toSun = math.normalize(system.ToLocal(index, time, system.PositionAt(0, time)));
            float latitude = math.degrees(math.asin(up.y)), light = math.dot(up, toSun), rain = Weather.Rain(body, up, time);
            string hour = light > 0.7f ? "around midday" : light > 0.15f ? "in daylight" : light > -0.1f ? "at twilight" : "at night";
            string sky = body.air == 0f ? "under a black airless sky" : rain > 0.5f ? "in heavy rain" : rain > 0.05f ? "in light rain"
                : body.cloud >= 1f ? "under unbroken cloud" : "under a clear sky";
            return $"on {body.name} at {math.abs(latitude):0} degrees {(latitude < 0f ? "south" : "north")}, " +
                $"{math.length(position) - math.max(body.radius, body.SeaRadius):0} m above {(body.SeaRadius > 0f ? "the sea" : "the mean surface")}, on {Terrain(body, up)}, {hour}, {sky}";
        }
    }
}
