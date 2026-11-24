using System;
using System.Linq;
using Unity.Mathematics;

namespace Infinigame.Core
{
    // What a mission can leave the player with.
    public enum Gear
    {
        Suit, Thrusters, Beams, Stealth, Capacitor, Ship, Orbiter, Shield, Missiles, Plating, Lance, Mender, Rail, Armour, Bulkheads, Nukes, GateParts, BackMissiles, Nova, ShipRail,
    }

    // One step of the game: where it is, who holds it and what is there to be won.
    public struct Mission
    {
        // its name, the world it is on, what the player is told to do and what the hermit has to say of it
        public string name, world, task, brief;
        // who has to be killed there, and how many of them
        public FoeKind guards;
        public int count;
        public Gear[] wins;
    }

    // Stable gear bit positions keep equipment compatible with older saves.
    public static class Campaign
    {
        public const int Buried = 0, Stranger = 1, FirstBurrow = 2, SecondBurrow = 3, ThirdBurrow = 4, Recovery = 5, Tower = 6, Citadel = 9, Won = 10;

        public static int Minds(int stage) => stage == SecondBurrow ? 2 : stage == ThirdBurrow || stage == Citadel ? 3 : 1;

        public static int OuterDefences(int stage) => stage < Tower || stage >= Citadel ? 0 : 2 * (stage - Tower + 1);

        public static int MigrateStage(int stage, int version = 1)
        {
            if (version < 2) stage = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 8, 8, 9, 9, 10, 11, 12 }[math.clamp(stage, 0, 15)];
            if (version < 3) stage = stage == 4 ? Recovery : stage >= 5 ? stage + 1 : stage;
            return version < 5 && stage >= Citadel ? math.max(Citadel, stage - 3) : stage;
        }

        public static Mission[] Missions { get; internal set; } = Array.Empty<Mission>();

        // Everything won by the missions before this one, as a set of bits.
        public static int Kit(int stage) => Missions.Take(stage).SelectMany(mission => mission.wins).Aggregate(1 << (int)Gear.Suit, (kit, gear) => kit | 1 << (int)gear);

        public static bool Has(int kit, Gear gear) => (kit & 1 << (int)gear) != 0;

        public static string Name(Gear gear) => gear switch
        {
            Gear.Suit => "POWERED ARMOUR", Gear.Thrusters => "SUIT THRUSTERS", Gear.Beams => "GAUNTLET BEAMS", Gear.Stealth => "A CLOAK", Gear.Capacitor => "A CAPACITOR",
            Gear.Orbiter => "AN ORBITAL DRIVE", Gear.Shield => "A RADIATION SHIELD", Gear.Missiles => "SEEKER MISSILES", Gear.Plating => "HEAT PLATING", Gear.Mender => "A HULL MENDER",
            Gear.Rail => "A RAIL GAUNTLET", Gear.Armour => "HEAVY PLATE", Gear.Bulkheads => "BULKHEADS", Gear.Nukes => "NUCLEAR TORPEDOES", Gear.GateParts => "MOON ENGINE PLANS",
            Gear.Lance => "TWIN BEAMS", Gear.Nova => "HEMISPHERE LASERS", Gear.ShipRail => "SHIP RAILGUN", Gear.Ship => "YOUR SHIP", Gear.BackMissiles => "BACK MISSILES", _ => gear.ToString().ToUpperInvariant(),
        };

        // What a mission wins, for the news of it: "A, B AND C".
        public static string Spoils(int stage)
        {
            var names = Missions[stage].wins.Select(Name).ToArray();
            return names.Length < 2 ? string.Concat(names) : $"{string.Join(", ", names.Take(names.Length - 1))} AND {names[names.Length - 1]}";
        }

        public static int FinalMoon(SolarSystemDescriptor system)
        {
            int moon = Array.FindIndex(system.bodies, body => body.name == "Phobos");
            return moon >= 0 ? moon : Enumerable.Range(1, system.bodies.Length - 1).Last(i => system.bodies[i].kind == BodyKind.Rock
                && system.bodies[i].orbit.parent > 0 && system.bodies[system.bodies[i].orbit.parent].kind == BodyKind.Rock && system.bodies[i].orbit.parent != SolarSystemDescriptor.Home);
        }

        // The body a mission is on. The citadel is its own.
        public static int World(SolarSystemDescriptor system, int stage)
        {
            if (stage == Citadel) return Array.FindIndex(system.bodies, body => body.kind == BodyKind.Station);
            if (!system.generated) return Array.FindIndex(system.bodies, body => body.name == Missions[stage].world);
            if (stage < Tower) return SolarSystemDescriptor.Home;
            if (stage == Tower) return Array.FindIndex(system.bodies, body => body.orbit.parent == SolarSystemDescriptor.Home);
            var worlds = Enumerable.Range(1, system.bodies.Length - 1).Where(i => system.bodies[i].kind == BodyKind.Rock && i != SolarSystemDescriptor.Home && i != World(system, Tower)).ToArray();
            return worlds[(stage - Tower - 1) % worlds.Length];
        }

        // The spot on that body's ground where the mission is. On the home world they lead away
        // from where the player wakes, each further than the last and the last round the far side.
        public static float3 Site(SolarSystemDescriptor system, int stage)
        {
            // Fix every site before a landing pad or weapon can alter the terrain.
            if (system.missionSites.Count == 0)
                for (int i = 0; i <= Citadel; i++) system.missionSites[i] = FindSite(system, i);
            return system.missionSites[stage];
        }

        static float3 FindSite(SolarSystemDescriptor system, int stage)
        {
            int world = World(system, stage);
            var body = system.bodies[world];
            if (stage == Citadel) return new float3(0, 1, 0);
            if (world != SolarSystemDescriptor.Home) return body.FindLanding(system.seed + (uint)stage);
            float3 landing = body.FindLanding(system.seed + 1u);
            if (stage == Buried) return landing;
            if (stage == Stranger)
            {
                float3 east = math.normalizesafe(math.cross(landing, new float3(0.2f, 0.9f, 0.4f)));
                for (int i = 0; i < 256; i++)
                {
                    float3 near = math.normalize(landing + math.rotate(quaternion.AxisAngle(landing, i * 2.399963f), east) * ((9 + i % 4 * 4) / body.radius));
                    if (body.GroundRadius(near) > body.SeaRadius + 3 && math.dot(body.GroundNormal(near), near) > 0.94f) return near;
                }
                return landing;
            }
            if (stage < Tower)
            {
                float3 previous = system.missionSites[stage - 1], origin = previous * body.GroundRadius(previous);
                float budget = stage <= SecondBurrow ? 300 : 800;
                float3 onward = math.normalizesafe(Walker.Project(previous - landing, previous), math.normalizesafe(math.cross(previous, new float3(0.2f, 0.9f, 0.4f))));
                float before = math.distance(previous, landing) * body.radius;
                for (int i = 0; i < 1800; i++)
                {
                    float bearing = i == 0 ? 0 : i < 600 ? math.sin(i * 2.399963f) * 1.3f : i * 2.399963f;
                    float distance = budget * (i == 0 ? 0.8f : 0.35f + 0.6f * ((i * 0.618034f) % 1));
                    float3 at = math.normalize(previous + math.rotate(quaternion.AxisAngle(previous, bearing), onward) * (distance / body.radius));
                    float height = body.GroundRadius(at);
                    if (height <= body.SeaRadius + 3 || math.dot(body.GroundNormal(at), at) < 0.95f
                        || math.distance(at * height, origin) > budget - 50 || i < 600 && math.distance(at, landing) * body.radius < before + 30) continue;
                    return at;
                }
                // A valid dry fallback stays within the travel budget even on an unusually steep coast.
                return previous;
            }
            float metres = 0.85f * math.PI * body.radius;
            float3 heading = math.rotate(quaternion.AxisAngle(landing, 2.1f * stage), math.normalize(math.cross(landing, new float3(0.2f, 0.9f, 0.4f))));
            return body.FindSite(math.rotate(quaternion.AxisAngle(math.cross(landing, heading), metres / body.radius), landing));
        }
    }
}
