using System;
using System.Linq;
using Unity.Mathematics;

namespace Infinigame.Core
{
    // What a mission can leave the player with.
    public enum Gear
    {
        Suit, Thrusters, Beams, Stealth, Capacitor, Ship, Orbiter, Shield, Missiles, Plating, Lance, Mender, Rail, Armour, Bulkheads, Nukes, GateParts, BackMissiles,
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
        public const int Buried = 0, Stranger = 1, Tower = 5, Citadel = 14, Won = 15;

        public static readonly Mission[] Missions =
        {
            new Mission
            {
                name = "Buried", world = "Earth", task = "CLICK TO DIG YOURSELF OUT", wins = new Gear[0],
                brief = "Weight above you. No memory, only the sound of a machine calling through the soil.",
            },
            new Mission
            {
                name = "The recovery machine", world = "Earth", task = "A ROBOT IS CALLING  ·  FIND ITS WORKSHOP", wins = new[] { Gear.Suit },
                brief = "I am a recovery robot. I found you after the crash. Let me wake your armour. There are things in your recovery record I cannot yet explain.",
            },
            new Mission
            {
                name = "The outpost", world = "Earth", task = "SILENCE THE OUTPOST MIND", guards = FoeKind.Drone, count = 4, wins = new[] { Gear.Thrusters },
                brief = "A controlling mind lives inside that outpost. Its shield will stop anything fired from outside. Go through the door, break it or find the terminal. Its thruster assembly belongs on your suit.",
            },
            new Mission
            {
                name = "The depot", world = "Earth", task = "SILENCE THE DEPOT MIND", guards = FoeKind.Drone, count = 6, wins = new[] { Gear.Beams, Gear.Stealth },
                brief = "The depot holds gauntlet emitters and a cloak. Your suit has three slots and six units of power. Take a style to the workshop, not every piece at once.",
            },
            new Mission
            {
                name = "The hangar", world = "Earth", task = "TAKE THE HANGAR  ·  YOUR SHIP IS INSIDE", guards = FoeKind.Drone, count = 8, wins = new[] { Gear.Capacitor, Gear.BackMissiles, Gear.Ship },
                brief = "That ship was yours. I knew its call sign before I found the wreck. The mind in the hangar has its power cell and your shoulder missiles.",
            },
            new Mission
            {
                name = "The gun tower", world = "Earth", task = "ENTER THE TOWER AND SILENCE ITS MIND", guards = FoeKind.Raider, count = 3, wins = new[] { Gear.Orbiter },
                brief = "The orbital drive is inside the tower. The ship can carry you there, but its guns cannot cross the shield. Your suit can.",
            },
            new Mission
            {
                name = "Luna", world = "Luna", task = "SILENCE THE MIND ON LUNA", guards = FoeKind.Raider, count = 4, wins = new[] { Gear.Shield },
                brief = "The moon holds a shield for the ship. Your suit already survives vacuum and radiation, but its thrusters cannot carry you between worlds.",
            },
            new Mission
            {
                name = "Mars", world = "Mars", task = "SILENCE THE MIND ON MARS", guards = FoeKind.Raider, count = 4, wins = new[] { Gear.Missiles },
                brief = "They are not separate raiders. Every hull is a body of the same presence. They copy the weapons they capture. Take the seekers and expect them to learn.",
            },
            new Mission
            {
                name = "Venus", world = "Venus", task = "SILENCE THE MIND ON VENUS", guards = FoeKind.Raider, count = 5, wins = new[] { Gear.Plating },
                brief = "You can walk safely in this heat. The ship needs the plating kept inside this base. I recognise the design of your armour. I helped build it.",
            },
            new Mission
            {
                name = "Mercury", world = "Mercury", task = "SILENCE THE MIND ON MERCURY", guards = FoeKind.Raider, count = 5, wins = new[] { Gear.Lance },
                brief = "The lance burns continuously while you hold the trigger. Do not mistake a quiet doorway for a safe one. Their bodies listen as well as watch.",
            },
            new Mission
            {
                name = "Jupiter", world = "Ganymede", task = "SILENCE THE MIND ON GANYMEDE", guards = FoeKind.Raider, count = 6, wins = new[] { Gear.Mender },
                brief = "This mind keeps a repair system for the ship. The robots can copy it at a workshop. Feed them ore, ice and salvage and they will build more of themselves.",
            },
            new Mission
            {
                name = "Saturn", world = "Titan", task = "SILENCE THE MIND ON TITAN", guards = FoeKind.Raider, count = 6, wins = new[] { Gear.Rail, Gear.Armour },
                brief = "The rail gauntlet pierces one body and the next behind it. Its long recharge leaves you open. Heavy plate draws from the same reserve as your beams and thrusters.",
            },
            new Mission
            {
                name = "Uranus", world = "Titania", task = "SILENCE THE MIND ON TITANIA", guards = FoeKind.Raider, count = 7, wins = new[] { Gear.Bulkheads },
                brief = "Bulkheads for the ship. The presence keeps spreading while we work. Defend the colonies: their robots will have to build something much larger than either of us.",
            },
            new Mission
            {
                name = "Neptune", world = "Triton", task = "SILENCE THE MIND ON TRITON", guards = FoeKind.Raider, count = 8, wins = new[] { Gear.Nukes },
                brief = "These torpedoes break ships and ground. They still cannot cross a base shield. The final mind lies too deep for even these. We will need a moon.",
            },
            new Mission
            {
                name = "The citadel", world = "", task = "ENTER THE CITADEL BY SHIP  ·  DESTROY THE SHIELD CORE  ·  ESCAPE", guards = FoeKind.Base, count = 1, wins = new[] { Gear.GateParts },
                brief = "Fly into the citadel through its open tunnel, follow the turns and destroy the shield core. Then get out. I helped create you to end this presence. The radiation is its prison, and the gate we are repairing could release it.",
            },
        };

        // Everything won by the missions before this one, as a set of bits.
        public static int Kit(int stage) => Missions.Take(stage).SelectMany(mission => mission.wins).Aggregate(0, (kit, gear) => kit | 1 << (int)gear);

        public static bool Has(int kit, Gear gear) => (kit & 1 << (int)gear) != 0;

        public static string Name(Gear gear) => gear switch
        {
            Gear.Suit => "POWERED ARMOUR", Gear.Thrusters => "SUIT THRUSTERS", Gear.Beams => "GAUNTLET BEAMS", Gear.Stealth => "A CLOAK", Gear.Capacitor => "A CAPACITOR",
            Gear.Orbiter => "AN ORBITAL DRIVE", Gear.Shield => "A RADIATION SHIELD", Gear.Missiles => "SEEKER MISSILES", Gear.Plating => "HEAT PLATING", Gear.Mender => "A HULL MENDER",
            Gear.Rail => "A RAIL GAUNTLET", Gear.Armour => "HEAVY PLATE", Gear.Bulkheads => "BULKHEADS", Gear.Nukes => "NUCLEAR TORPEDOES", Gear.GateParts => "GATE PARTS",
            Gear.Ship => "YOUR SHIP", Gear.BackMissiles => "BACK MISSILES", _ => gear.ToString().ToUpperInvariant(),
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
            if (stage <= Tower) return SolarSystemDescriptor.Home;
            var worlds = Enumerable.Range(1, system.bodies.Length - 1).Where(i => system.bodies[i].kind == BodyKind.Rock && i != SolarSystemDescriptor.Home).ToArray();
            return worlds[(stage - Tower - 1) % worlds.Length];
        }

        // The spot on that body's ground where the mission is. On the home world they lead away
        // from where the player wakes, each further than the last and the last round the far side.
        public static float3 Site(SolarSystemDescriptor system, int stage)
        {
            int world = World(system, stage);
            var body = system.bodies[world];
            if (stage == Citadel) return new float3(0, 1, 0);
            if (world != SolarSystemDescriptor.Home) return body.FindLanding(system.seed + (uint)stage);
            float3 landing = body.FindLanding(system.seed + 1u);
            if (stage <= Stranger) return stage == Buried ? landing : body.FindCamp(landing);
            float metres = stage == 2 ? 280f : stage == 3 ? 800f : stage == 4 ? 1700f : 0.85f * math.PI * body.radius;
            float3 heading = math.rotate(quaternion.AxisAngle(landing, 2.1f * stage), math.normalize(math.cross(landing, new float3(0.2f, 0.9f, 0.4f))));
            return body.FindSite(math.rotate(quaternion.AxisAngle(math.cross(landing, heading), metres / body.radius), landing));
        }
    }
}
