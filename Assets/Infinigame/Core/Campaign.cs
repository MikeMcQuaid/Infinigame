using System;
using System.Linq;
using Unity.Mathematics;

namespace Infinigame.Core
{
    // What a mission can leave the player with.
    public enum Gear
    {
        Pistol, Jetpack, Shotgun, Bike, Rifle, Ship, Orbiter, Shield, Missiles, Plating, Lance, Mender, Sniper, Armour, Bulkheads, Nukes, Warp, Launcher,
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

    // The game is a string of missions. The player begins buried, with nothing and no memory, and
    // everything they come to carry or fly is taken from the raiders: each mission is a place of
    // theirs to clear, and what it wins opens the way to the next, from a first pistol to the
    // nuclear torpedoes that are the only thing their citadel in the asteroid belt cannot shrug off.
    public static class Campaign
    {
        public const int Buried = 0, Stranger = 1, Tower = 5, Citadel = 14, Won = 15;

        public static readonly Mission[] Missions =
        {
            new Mission
            {
                name = "Buried", world = "Earth", task = "CLICK TO DIG YOURSELF OUT", wins = new Gear[0],
                brief = "Earth in your mouth, a weight on your chest, and no idea how you came to be under either.",
            },
            new Mission
            {
                name = "The stranger", world = "Earth", task = "SOMEONE HAS LIT A FIRE NEAR BY  ·  GO AND TALK TO THEM", wins = new[] { Gear.Pistol },
                brief = "Up out of the ground like a turnip, and not a thing in your head, I'd wager. Take this pistol. The raiders shot something down here days ago, and they shoot at whatever walks.",
            },
            new Mission
            {
                name = "The outpost", world = "Earth", task = "CLEAR THE RAIDERS' OUTPOST", guards = FoeKind.Drone, count = 4, wins = new[] { Gear.Jetpack },
                brief = "They have an outpost a short walk from here, with drones to mind it. They keep a lifting pack there that would suit you better than it suits them.",
            },
            new Mission
            {
                name = "The depot", world = "Earth", task = "CLEAR THE RAIDERS' DEPOT", guards = FoeKind.Drone, count = 6, wins = new[] { Gear.Shotgun, Gear.Bike },
                brief = "Further on is the depot they supply it from. There is a bike in there quicker than any legs, and a shotgun for whoever gets close enough to ask.",
            },
            new Mission
            {
                name = "The hangar", world = "Earth", task = "TAKE THE HANGAR  ·  YOUR SHIP IS INSIDE", guards = FoeKind.Drone, count = 8, wins = new[] { Gear.Rifle, Gear.Launcher, Gear.Ship },
                brief = "What they shot down was a ship, and I think it was yours. They dragged it to a hangar a long ride from here. Its guards carry rifles, and keep a launcher for whatever flies over. Help yourself to all three.",
            },
            new Mission
            {
                name = "The gun tower", world = "Earth", task = "SHOOT DOWN THE TOWER'S RAIDERS", guards = FoeKind.Raider, count = 3, wins = new[] { Gear.Orbiter },
                brief = "She flies, but they took the heart out of her engines: she will not climb out of the air. It went to their gun tower on the far side of the world. Go and take it back.",
            },
            new Mission
            {
                name = "Luna", world = "Luna", task = "CLEAR THE RAIDERS FROM LUNA", guards = FoeKind.Raider, count = 4, wins = new[] { Gear.Shield },
                brief = "Now you can reach the moon, and no further: out past it the sun's radiation would cook you in your seat. The raiders on Luna fly shielded. Take a shield from them.",
            },
            new Mission
            {
                name = "Mars", world = "Mars", task = "CLEAR THE RAIDERS FROM MARS", guards = FoeKind.Raider, count = 4, wins = new[] { Gear.Missiles },
                brief = "The whole system is open to you now, and they hold all of it. Start with Mars. They arm their ships there, and their missiles find their own way to a target.",
            },
            new Mission
            {
                name = "Venus", world = "Venus", task = "CLEAR THE RAIDERS FROM VENUS", guards = FoeKind.Raider, count = 5, wins = new[] { Gear.Plating },
                brief = "Venus next. Nothing lives under that cloud, but their ships there are plated against the heat, and plate is plate wherever it is shot at.",
            },
            new Mission
            {
                name = "Mercury", world = "Mercury", task = "CLEAR THE RAIDERS FROM MERCURY", guards = FoeKind.Raider, count = 5, wins = new[] { Gear.Lance },
                brief = "On Mercury they are building a lance: one shot of it goes through a ship like a nail. Better it were on yours.",
            },
            new Mission
            {
                name = "Jupiter", world = "Ganymede", task = "CLEAR THE RAIDERS FROM GANYMEDE", guards = FoeKind.Raider, count = 6, wins = new[] { Gear.Mender },
                brief = "Out past the belt, and past their citadel: leave that be, nothing you carry will scratch it. On Ganymede, by Jupiter, they have a thing that mends a hull as it flies.",
            },
            new Mission
            {
                name = "Saturn", world = "Titan", task = "CLEAR THE RAIDERS FROM TITAN", guards = FoeKind.Raider, count = 6, wins = new[] { Gear.Sniper, Gear.Armour },
                brief = "Titan, under Saturn's rings. Their marksmen train there, in armour. A long rifle and a hard shell would both sit well on you.",
            },
            new Mission
            {
                name = "Uranus", world = "Titania", task = "CLEAR THE RAIDERS FROM TITANIA", guards = FoeKind.Raider, count = 7, wins = new[] { Gear.Bulkheads },
                brief = "Titania, round the world that lies on its side. They brace their biggest ships there. Take the bracing: you will want it where you are going.",
            },
            new Mission
            {
                name = "Neptune", world = "Triton", task = "CLEAR THE RAIDERS FROM TRITON", guards = FoeKind.Raider, count = 8, wins = new[] { Gear.Nukes },
                brief = "Last and furthest, Triton. That is where they keep the torpedoes that crack worlds. Mind where you point them: they do to a planet what they do to anything else.",
            },
            new Mission
            {
                name = "The citadel", world = "", task = "DESTROY THE CITADEL  ·  ONLY NUCLEAR TORPEDOES WILL MARK IT", guards = FoeKind.Base, count = 1, wins = new[] { Gear.Warp },
                brief = "There is nothing left to take from them but the citadel itself, in the belt. Torpedoes, and plenty of them. Then perhaps you will remember who you are.",
            },
        };

        // Everything won by the missions before this one, as a set of bits.
        public static int Kit(int stage) => Missions.Take(stage).SelectMany(mission => mission.wins).Aggregate(0, (kit, gear) => kit | 1 << (int)gear);

        public static bool Has(int kit, Gear gear) => (kit & 1 << (int)gear) != 0;

        public static string Name(Gear gear) => gear switch
        {
            Gear.Orbiter => "AN ORBITAL DRIVE", Gear.Shield => "A RADIATION SHIELD", Gear.Missiles => "SEEKER MISSILES", Gear.Plating => "HEAT PLATING", Gear.Mender => "A HULL MENDER",
            Gear.Sniper => "A SNIPER RIFLE", Gear.Armour => "ARMOUR", Gear.Bulkheads => "BULKHEADS", Gear.Nukes => "NUCLEAR TORPEDOES", Gear.Warp => "A WARP DRIVE",
            Gear.Ship => "YOUR SHIP", Gear.Rifle => "A RIFLE", Gear.Launcher => "A MISSILE LAUNCHER", _ => $"A {gear.ToString().ToUpperInvariant()}",
        };

        // What a mission wins, for the news of it: "A, B AND C".
        public static string Spoils(int stage)
        {
            var names = Missions[stage].wins.Select(Name).ToArray();
            return names.Length < 2 ? string.Concat(names) : $"{string.Join(", ", names.Take(names.Length - 1))} AND {names[names.Length - 1]}";
        }

        // The body a mission is on. The citadel is its own.
        public static int World(SolarSystemDescriptor system, int stage) =>
            Array.FindIndex(system.bodies, body => stage == Citadel ? body.kind == BodyKind.Station : body.name == Missions[stage].world);

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
