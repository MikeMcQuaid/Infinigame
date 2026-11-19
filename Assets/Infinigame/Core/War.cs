using System;
using Unity.Mathematics;

namespace Infinigame.Core
{
    [Serializable]
    public class Front
    {
        public int world, region, rank, depth, kills, shipKills;
        public float aliens, dogs, alienShips, ships;
        public bool factory, shipyard, established, mindDead;
        public float[] exposure = new float[10];
    }

    public enum EscortOrder { Follow, Attack, Patrol }
    [Serializable] public class EscortOrders
    {
        public int count = 2;
        public EscortOrder order;
    }

    // Populations represent the whole world; nearby units are a bounded sample of that war.
    public static class War
    {
        public static float Pressure(int deaths) => 1 / (1 + math.max(0, deaths) * 0.18f);
        public static float3 Follow(float3 position, float3 target, float3 leaderVelocity, float dt)
        {
            float gap = math.distance(position, target);
            return position + math.normalizesafe(target - position) * math.min(gap, math.max(120, math.length(leaderVelocity) + math.min(Autopilot.Speed, gap * 0.8f)) * dt);
        }
        public static Front Create(int world, int depth, int rank) => new Front
        {
            world = world, depth = depth, rank = rank,
            aliens = 90 + rank * 35 + (depth - 1) * 100,
            alienShips = 8 + rank * 3 + (depth - 1) * 12,
        };
        public static float Resistance(Front front, int weapon) => front.mindDead || front.exposure == null || weapon < 0 || weapon >= front.exposure.Length ? 1 : 1 / (1 + math.min(0.35f, front.exposure[weapon] * 0.005f));
        public static void Learn(Front front, int weapon, float amount)
        {
            if (front.mindDead) return;
            if (front.exposure == null || front.exposure.Length != 10) front.exposure = new float[10];
            front.exposure[weapon] = math.min(100, front.exposure[weapon] + amount);
        }
        public static int RequiredKills(Front front) => 4 + front.rank + (front.depth - 1) * 2;
        public static bool Controlled(Front front) => front.factory && front.mindDead && front.aliens <= math.max(5, front.dogs * 0.25f);
        public static bool CanSpread(int origin, int destination) => math.abs(origin - destination) <= 1;
        public static bool Build(Front front, bool ships)
        {
            if (ships ? front.shipyard || !Controlled(front) : front.factory || !front.mindDead && front.kills < RequiredKills(front)) return false;
            if (ships) { front.shipyard = true; front.ships = math.max(1, front.ships); }
            else { front.factory = true; front.dogs = math.max(8 + front.rank * 4, front.dogs); }
            return true;
        }
        public static void Kill(Front front, bool ship, int count = 1)
        {
            if (ship) { front.shipKills += count; if (front.alienShips > 0) front.alienShips = math.max(1, front.alienShips - count); }
            else { front.kills += count; if (front.aliens > 0) front.aliens = math.max(1, front.aliens - count); }
        }
        public static void Step(Front front, float seconds, float pressure = 1)
        {
            // Small fixed slices make losses and replication independent of frame rate.
            while (seconds > 0)
            {
                float dt = math.min(1, seconds); seconds -= dt;
                for (int weapon = 0; front.exposure != null && weapon < front.exposure.Length; weapon++) front.exposure[weapon] = math.max(0, front.exposure[weapon] - dt * 0.5f);
                float growth = (0.004f + front.rank * 0.0005f + (front.depth - 1) * 0.001f) * pressure;
                if (front.aliens > 0 || front.factory)
                {
                    float hostile = front.aliens, friendly = front.dogs;
                    // Underground clutches keep hatching after the local mind is gone.
                    float clutch = 3 + front.rank;
                    float hatching = math.max(clutch, hostile) * growth * (1 - hostile / 4000);
                    float hunted = math.min(math.max(0, hostile - clutch), friendly * (front.mindDead ? 0.5f : 0.18f));
                    front.aliens = math.max(1, hostile + dt * (hatching - hunted));
                    if (front.factory)
                    {
                        float pack = 64 + front.rank * 8;
                        float assembly = math.max(4, friendly) * (front.mindDead ? 0.13f : 0.09f) * math.saturate(1 - friendly / pack);
                        front.dogs = math.max(0, friendly + dt * (assembly - math.min(assembly * 0.65f, hostile * 0.0008f * pressure)));
                    }
                }
                if (front.alienShips > 0)
                {
                    float hostile = front.alienShips, friendly = front.ships;
                    front.alienShips = math.max(0, hostile + dt * (hostile * growth * (1 - hostile / 800) - friendly * 0.08f));
                    front.ships = math.max(0, friendly + dt * ((front.shipyard ? math.max(1, friendly) * 0.02f : 0) - hostile * 0.002f * pressure));
                    if (!front.shipyard && friendly <= 0) front.alienShips = math.max(1, front.alienShips);
                    else if (front.alienShips < 0.5f) front.alienShips = 0;
                }
                front.dogs = math.min(front.dogs, 4000);
                front.ships = math.min(front.ships, 800);
            }
        }
    }
}
