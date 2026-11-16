using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

namespace Infinigame.Core
{
    // Grazers, birds and fish live on a world with seas. Drifters are the sky-life of gas giants:
    // vast gas-bags trailing tendrils through the cloud tops. Striders are the tall, slow walkers of
    // a cold desert with thin air.
    public enum Creature { Grazer, Bird, Fish, Drifter, Strider, Gull, Swift, ReefFish, Ray, Butterfly, Kelp }

    // A herd, flock or school, and the spot it keeps to.
    public struct Troop
    {
        public Creature kind;
        public float3 centre; // direction from the body's centre
        // Distance from the body's centre: the ground under a herd, a flock's cruising height, a school's depth.
        public float level;
        public int count;
        public uint seed;
        // How far its members roam, as a share of the usual: less where steep ground hems a herd in.
        public float spread;
    }

    // The animals of every world that has any. Where each troop lives is a pure function of the world,
    // and where each member is a pure function of time, so nothing is stored and everyone sees the same herd.
    public static class Wildlife
    {
        public static bool IsBird(Creature kind) => kind == Creature.Bird || kind == Creature.Gull || kind == Creature.Swift;
        public static bool IsFish(Creature kind) => kind == Creature.Fish || kind == Creature.ReefFish || kind == Creature.Ray;
        // The grid troops are dealt onto, in metres: at most one troop of a kind per cell.
        public static float CellSize(Creature kind) => kind == Creature.Butterfly || kind == Creature.Kelp ? 30f : IsFish(kind) ? 60f : IsBird(kind) ? 220f : kind == Creature.Drifter ? 1800f : kind == Creature.Strider ? 260f : 130f;

        // Whether a kind of animal lives on a world at all.
        public static bool Lives(BodyDescriptor body, Creature kind) => kind == Creature.Drifter ? body.kind == BodyKind.Gas
            : kind == Creature.Strider ? body.kind == BodyKind.Rock && body.SeaRadius == 0f && body.air > 0f && body.air < 1f
            : body.SeaRadius > 0f;

        // Herds graze gentle lowland, flocks wheel over land and coast, schools keep to open water,
        // drifters hang in a giant's cloud tops and striders cross level desert.
        public static bool Find(BodyDescriptor body, Creature kind, NodeId cell, out Troop troop)
        {
            troop = default;
            if (!Lives(body, kind)) return false;
            var rnd = new Random(math.hash(new float4(body.terrain.offset, cell.GetHashCode() + 7919 * (int)kind)) | 1u);
            float3 centre = CubeSphere.Direction(cell.face, cell.UvMin + rnd.NextFloat2(0.25f, 0.75f) * cell.UvSize);
            float height = body.GroundRadius(centre) - body.radius, chance = rnd.NextFloat();
            if (kind == Creature.Drifter)
            {
                if (chance > 0.45f) return false;
                troop = new Troop { kind = kind, centre = centre, count = rnd.NextInt(3, 8), seed = rnd.NextUInt(), spread = 1f, level = body.radius + rnd.NextFloat(60f, 500f) };
                return true;
            }
            if (kind == Creature.Butterfly || kind == Creature.Kelp)
            {
                if (kind == Creature.Butterfly ? height < 2 || height > 45 || chance > 0.65f : height > -3 || chance > 0.75f) return false;
                troop = new Troop { kind = kind, centre = centre, count = rnd.NextInt(3, 8), seed = rnd.NextUInt(), spread = 1,
                    level = body.GroundRadius(centre) + (kind == Creature.Butterfly ? 1.5f : 0) };
                return true;
            }
            bool walker = kind == Creature.Grazer || kind == Creature.Strider;
            if (kind == Creature.Grazer
                    ? height < 6f || height > 0.35f * body.terrain.mountainAmp || math.dot(body.GroundNormal(centre), centre) < 0.96f || chance > 0.5f
                    : kind == Creature.Strider ? math.dot(body.GroundNormal(centre), centre) < 0.97f || chance > 0.3f
                    : IsBird(kind) ? height < -8f || chance > 0.6f : height > -6f || chance > 0.6f)
                return false;
            // a school needs deep water all round it, not just under its middle
            Compass(centre, out float3 east, out float3 north);
            for (int side = 0; IsFish(kind) && side < 4; side++)
                if (body.GroundRadius(math.normalize(centre + (side < 2 ? east : north) * ((side % 2 * 2 - 1) * 8f / body.radius))) > body.SeaRadius - 3f)
                    return false;
            // a herd keeps to ground about as level as where it stands, so cliffs and gullies pen it in
            float spread = 1f;
            for (int side = 0; walker && side < 6; side++)
            {
                float3 outward = east * math.cos(side * math.PI / 3f) + north * math.sin(side * math.PI / 3f);
                while (spread > 0.2f && math.abs(body.GroundRadius(math.normalize(centre + outward * (14f * spread / body.radius))) - body.radius - height) > 3f * spread) spread *= 0.5f;
            }
            troop = new Troop
            {
                kind = kind, centre = centre, count = kind == Creature.Strider ? rnd.NextInt(2, 6) : rnd.NextInt(4, 10), seed = rnd.NextUInt(), spread = spread,
                level = walker ? body.radius + height
                    : IsBird(kind) ? math.max(body.radius + height, body.SeaRadius) + rnd.NextFloat(25f, 70f)
                    : body.SeaRadius - rnd.NextFloat(1.5f, math.min(4f, -height - 2f)),
            };
            return true;
        }

        // Where one member is and which way it faces, relative to the body's centre. Everyone circles
        // the troop's spot: birds and fish round and round, grazers ambling to and fro along an arc.
        public static void Member(BodyDescriptor body, in Troop troop, int index, double time, out float3 position, out float3 forward)
        {
            var rnd = new Random(math.hash(new uint2(troop.seed, (uint)index)) | 1u);
            float3 up = troop.centre;
            Compass(up, out float3 east, out float3 north);
            float phase = rnd.NextFloat(2f * math.PI), ring, angle, heading = 1f, lift = 0f;
            switch (troop.kind)
            {
                case Creature.Grazer:
                case Creature.Strider:
                    // a strider's legs are long: it keeps further from its fellows
                    ring = rnd.NextFloat(3f, 14f) * troop.spread * (troop.kind == Creature.Strider ? 2.5f : 1f);
                    float amble = Turn(time, rnd.NextFloat(0.02f, 0.05f)) + phase * 3f;
                    angle = phase + 0.9f * math.sin(amble);
                    heading = math.cos(amble);
                    break;
                case Creature.Drifter:
                    // they ride the wind in a slow wide gyre, rising and sinking as they go
                    ring = rnd.NextFloat(80f, 420f);
                    angle = phase + Turn(time, 4f / ring);
                    lift = rnd.NextFloat(-60f, 60f) + 25f * math.sin(Turn(time, 0.05f) + phase);
                    break;
                case Creature.Bird:
                case Creature.Gull:
                case Creature.Swift:
                    ring = rnd.NextFloat(20f, 60f);
                    angle = phase + Turn(time, 9f / ring);
                    lift = rnd.NextFloat(-6f, 6f) + 2f * math.sin(Turn(time, 0.5f) + phase);
                    break;
                case Creature.Butterfly:
                    ring = rnd.NextFloat(1, 4); angle = phase + Turn(time, 0.8f); lift = math.sin(Turn(time, 2) + phase) * 0.5f; break;
                case Creature.Kelp:
                    ring = rnd.NextFloat(0.2f, 3); angle = phase; break;
                default:
                    ring = rnd.NextFloat(1.5f, 6f);
                    angle = phase + Turn(time, 1.2f / ring);
                    lift = rnd.NextFloat(-0.8f, 0.8f) + 0.3f * math.sin(Turn(time, 0.7f) + phase);
                    break;
            }

            float3 outward = east * math.cos(angle) + north * math.sin(angle), along = north * math.cos(angle) - east * math.sin(angle);
            float3 direction = math.normalize(up + outward * (ring / troop.level));
            // grazers walk the ground, birds clear it and fish keep between the seabed and the waves
            float ground = body.GroundRadius(direction);
            bool walks = troop.kind == Creature.Grazer || troop.kind == Creature.Strider || troop.kind == Creature.Kelp;
            position = direction * (walks ? ground
                : troop.kind == Creature.Drifter ? troop.level + lift
                : troop.kind == Creature.Butterfly ? ground + 1.2f + lift
                : IsBird(troop.kind) ? math.max(troop.level + lift, ground + 10f)
                : math.min(math.max(troop.level + lift, ground + 0.4f), body.SeaRadius - 0.5f));
            // a grazer turning round swings through facing outwards rather than flipping on the spot
            forward = math.normalize(Walker.Project(along * heading + outward * (walks ? 0.3f : 0f), direction));
        }

        // Two directions along the ground at right angles.
        static void Compass(float3 up, out float3 east, out float3 north)
        {
            east = math.normalize(math.cross(up, math.abs(up.y) < 0.9f ? new float3(0, 1, 0) : new float3(1, 0, 0)));
            north = math.cross(up, east);
        }

        // An angle growing at a steady rate, wrapped so it stays precise however long the game runs.
        static float Turn(double time, float radiansPerSecond) => (float)math.fmod(time * radiansPerSecond, 2 * math.PI_DBL);
    }
}
