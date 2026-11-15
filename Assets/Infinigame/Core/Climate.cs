using Unity.Mathematics;

namespace Infinigame.Core
{
    // How hot it is anywhere in the system, in kelvin, from real figures. The game's distances are
    // far too small to be to scale, so sunlight is worked out from how far the real world each
    // planet stands for is from the sun, with the space between filled in smoothly.
    public static class Climate
    {
        // The sun's surface temperature, and its radius in astronomical units.
        public const float Photosphere = 5772f, SunRadius = 0.00465f;

        // What a ship's hull and a suit can take before they start to fail.
        public const float HullLimit = 900f, SuitLimit = 420f;

        // How far from the sun, in astronomical units, a distance from the star's centre in metres stands for.
        public static float Au(SolarSystemDescriptor system, double distance)
        {
            // from the star's surface out through each planet's orbit, a straight line between neighbours on log paper
            double nearMetres = system.bodies[0].radius, nearAu = SunRadius, farMetres = nearMetres, farAu = nearAu;
            foreach (int planet in system.Planets)
            {
                (nearMetres, nearAu) = (farMetres, farAu);
                (farMetres, farAu) = (system.bodies[planet].orbit.radius, system.bodies[planet].au);
                if (distance < farMetres) break;
            }
            double along = math.log(math.max(distance, system.bodies[0].radius) / nearMetres) / math.log(farMetres / nearMetres);
            return (float)(nearAu * math.pow(farAu / nearAu, along));
        }

        // The temperature something small settles at in sunlight this far out: scorching beside the
        // star, about 5 degrees Celsius where the Earth is, bitter beyond the giants.
        public static float Sunlit(float au)
        {
            float size = math.min(1f, SunRadius / au);
            return Photosphere * math.pow((1f - math.sqrt(1f - size * size)) / 2f, 0.25f);
        }

        // How hot it is at a point given in a body's own frame.
        public static float At(SolarSystemDescriptor system, int index, float3 position, double time)
        {
            var body = system.bodies[index];
            float3 sun = system.ToLocal(index, time, system.PositionAt(0, time));
            float space = Sunlit(Au(system, math.distance(position, sun))), distance = math.length(position), extra = 0;
            for (int i = 1; i < system.bodies.Length; i++)
            {
                var star = system.bodies[i];
                if (star.kind != BodyKind.Sun) continue;
                float3 light = system.ToLocal(index, time, system.PositionAt(i, time));
                float warmth = Sunlit(Au(system, math.distance(position, light))) * math.sqrt(star.radius / system.bodies[0].radius);
                extra += math.pow(warmth, 4);
            }
            space = math.pow(math.pow(space, 4) + extra, 0.25f);
            if (body.kind == BodyKind.Sun || body.kind == BodyKind.Station || distance < 1e-3f) return space;

            float3 up = position / distance;
            if (body.kind == BodyKind.Gas)
            {
                // cold at the cloud tops, and hotter by the metre below them, as the real giants are:
                // a few hundred kelvin just under the clouds, thousands a little way down
                float depth = (body.radius - distance) / body.radius;
                return depth > 0f ? body.heat + 30000f * math.pow(depth, 0.7f) : math.lerp(body.heat, space, math.smoothstep(0f, 0.4f * body.radius, distance - body.radius));
            }

            float ground = Ship.Surface(body, up), height = distance - ground;
            // warmest a little after midday, coldest before dawn; air evens the two out, and its absence does the opposite
            float surface = body.heat + body.swing * (2f * math.smoothstep(-0.3f, 0.7f, math.dot(up, math.normalizesafe(sun))) - 1f);
            if (body.SeaRadius > 0f)
                // a living world is colder at its poles and on its mountain tops
                surface -= 32f * up.y * up.y + 55f * math.saturate((ground - body.radius) / body.terrain.MaxHeight);
            surface = math.pow(math.pow(math.max(surface, 0), 4) + extra, 0.25f);
            return math.lerp(surface, space, math.smoothstep(0f, 0.3f * body.radius, height));
        }

        public static float Celsius(float kelvin) => kelvin - 273.15f;
    }
}
