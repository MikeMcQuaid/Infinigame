using System;
using System.Collections.Generic;
using System.Linq;
using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace Infinigame
{
    // The fighting. Combat in the core says how foes move, what bolts hit and what it costs; this
    // decides when foes turn up, fires the player's weapons, blows holes in the ground, has
    // Effects show what each strike did and draws foes, battered as they are, and bolts. Raiders come for a ship in flight and drones for someone on
    // foot, more of both the deeper the run has gone. The raiders' base in the asteroid belt
    // shoots at whatever comes near and sends its own raiders out.
    public class Battle : MonoBehaviour
    {
        struct Burst
        {
            public float3 position;
            public float size, age;
        }

        [NonSerialized] public Game game;
        // Tests switch the ambushes off and place foes themselves.
        [NonSerialized] public bool quiet;

        public readonly List<Foe> Foes = new List<Foe>();
        public readonly List<Bolt> Bolts = new List<Bolt>();
        // What is left of the raiders' base in this system. Nothing, once it has been destroyed.
        public float Base { get; private set; }
        // When the player last fired.
        public float Fired { get; private set; } = float.MinValue;
        // What is left in the magazine of the weapon in hand, and whether a fresh one is going in.
        public int Rounds => rounds[game.Player.Arm];
        public bool Reloading => reload > 0f;

        readonly List<Burst> bursts = new List<Burst>();
        readonly List<Strike> struck = new List<Strike>();
        readonly Instances raiders = new Instances(), drones = new Instances(), flashes = new Instances(), missiles = new Instances();
        readonly Instances[] bolts = { new Instances(), new Instances() };
        Mesh raider, raiderLights, drone, droneEye, ball, missile;
        Material raiderHull, raiderGlow, droneHull, droneGlow, ours, theirs, fire, casing;
        Random rnd = new Random(1);
        float trigger, raid, pack, ashore, launch;
        // charged: a landing charge has gone down for the landing in hand
        bool left, charged;
        // each firearm's magazine, the one being changed and how long that has left, and how far fast firing has opened up the aim
        readonly int[] rounds = Combat.Arms.Select(weapon => weapon.clip).ToArray();
        int reloading;
        float reload, wild;

        void Awake()
        {
            (raider, raiderLights) = Models.Raider();
            (drone, droneEye) = Models.Drone();
            missile = Models.Missile();
            ball = Shapes.Sphere(0.5f, 8);
            // iron dark enough to menace and pale enough to pick out against the dark of space, lit from
            // within, and holed and burning where it has been shot, in holes sized to the craft
            var iron = new Color(0.36f, 0.3f, 0.28f);
            var menace = new Color(9f, 0.9f, 0.15f);
            (raiderHull, raiderGlow) = (Shapes.Hull(iron, 10f), Shapes.Hull(Color.black, 10f, menace));
            (droneHull, droneGlow) = (Shapes.Hull(iron, 1.2f), Shapes.Hull(Color.black, 1.2f, menace));
            casing = Shapes.Material("Props", new Color(0.3f, 0.28f, 0.27f));
            // bolts and blasts are balls of fire with soft edges
            Material Fire(Color colour)
            {
                var flash = new Material(Resources.Load<Shader>("Flash")) { enableInstancing = true };
                flash.SetColor("_Colour", colour);
                return flash;
            }
            ours = Fire(new Color(2.5f, 8f, 12f));
            theirs = Fire(new Color(14f, 1f, 0.25f));
            fire = Fire(new Color(12f, 6f, 2f));
        }

        // A new system, or a new run: nothing follows you there, its base stands whole, and there is a moment's peace.
        public void Reset()
        {
            Foes.Clear();
            Bolts.Clear();
            bursts.Clear();
            game.Effects.Clear();
            rnd = new Random(math.hash(new uint2(game.Seed, (uint)game.Depth)) | 1u);
            raid = 20f;
            pack = 35f;
            Base = game.Stage > Campaign.Citadel ? 0f : Combat.Health(FoeKind.Base, game.Threat);
            for (int i = 0; i < rounds.Length; i++) rounds[i] = Combat.Arms[i].clip;
            reload = wild = 0f;
        }

        // Foes and bolts are placed relative to the body the player is nearest, so they move with the player when that changes.
        public void Rebase(int from, int to)
        {
            var system = game.System;
            Bolts.Clear();
            game.Effects.Clear();
            for (int i = Foes.Count - 1; i >= 0; i--)
            {
                var foe = Foes[i];
                // drones belong to the ground they hover over, and the base to its own place
                if (foe.kind != FoeKind.Raider)
                {
                    Foes.RemoveAt(i);
                    continue;
                }
                system.Descriptor.Rebase(from, to, system.Clock, ref foe.position, ref foe.velocity);
                foe.forward = math.rotate(system.Descriptor.Turn(from, to, system.Clock), foe.forward);
                Foes[i] = foe;
            }
        }

        public void Add(FoeKind kind, float3 position)
        {
            var body = game.System.ReferenceBody;
            Foes.Add(Combat.Spawn(kind, position, game.Player.Position - position, game.Threat, rnd.NextUInt(), 0.6f * body.radius));
        }

        // One frame of fighting, while the world is running.
        public void Tick(Controls c)
        {
            var player = game.Player;
            var system = game.System;
            var body = system.ReferenceBody;
            float frame = Mathf.Clamp(Time.deltaTime, 1e-4f, 0.1f);
            bool flying = player.flying, aloft = flying && !player.ship.landed;
            float3 up = math.normalize(player.Position), self = flying ? player.ship.position : player.walker.position + up * 1.2f;

            // the base is there, with whatever is left of it, whenever the player is in its space
            bool there = body.kind == BodyKind.Station && Base > 0f;
            int standing = Foes.FindIndex(foe => foe.kind == FoeKind.Base);
            if (there && standing < 0)
            {
                Add(FoeKind.Base, float3.zero);
                var foe = Foes[Foes.Count - 1];
                foe.health = Base;
                Foes[Foes.Count - 1] = foe;
                launch = 6f;
                game.Notify(player.Has(Gear.Nukes) ? "THE RAIDERS' CITADEL  ·  TORPEDOES WILL BREAK IT" : "THE RAIDERS' CITADEL  ·  NOTHING YOU CARRY WILL MARK IT");
                game.Sounds.Alarm();
            }

            // the player's weapons: the ship's in flight, what is carried on foot
            trigger -= frame;
            var weapon = player.Weapon;
            int held = player.Arm;
            bool firearm = !flying && player.Armed;
            // a firearm's aim settles when it is given a moment, and its magazine is changed when it
            // runs dry or the player chooses: taking up another weapon abandons the change
            wild = Mathf.MoveTowards(wild, 0f, 0.05f * frame);
            if (reload > 0f && (!firearm || reloading != held)) reload = 0f;
            if (reload > 0f && (reload -= frame) <= 0f) rounds[held] = weapon.clip;
            if (firearm && reload <= 0f && rounds[held] < weapon.clip && (c.reload || rounds[held] == 0))
            {
                (reload, reloading) = (weapon.reload, held);
                // the click of the empty chamber that started it, then the fresh magazine going in
                if (rounds[held] == 0) game.Sounds.DryFire();
                game.Sounds.Reload();
            }
            if (c.fire && trigger <= 0f && player.Armed && (aloft || (firearm && reload <= 0f && rounds[held] > 0)) && !player.Autopilot)
            {
                var view = Camera.main.transform;
                float3 forward = view.forward, eye = (float3)view.position + system.Anchor;
                left = !left;
                int fired = Bolts.Count;
                if (flying)
                {
                    forward = math.rotate(player.ship.rotation, new float3(0, 0, 1));
                    Combat.Fire(weapon, player.ship.position + math.rotate(player.ship.rotation, new float3(left ? -1.7f : 1.7f, -0.3f, 2.5f)), forward, player.ship.velocity, ref rnd, Bolts);
                }
                else
                {
                    // from the hip, to meet the line of sight a way off: wilder fired fast, on the move or in the air
                    float3 muzzle = eye + (float3)(view.right * 0.25f - view.up * 0.22f);
                    float loose = wild + (player.walker.grounded ? 0.006f * math.saturate(math.length(player.walker.velocity) / Tuning.WalkSpeed) : 0.03f);
                    Combat.Fire(weapon, muzzle, math.normalize(eye + forward * 150f - muzzle), player.walker.velocity, ref rnd, Bolts, loose);
                    rounds[held]--;
                    wild += weapon.bloom;
                    player.Kick(weapon.kick);
                }
                trigger = weapon.period;
                Fired = Time.time;
                game.Sounds.Shot(weapon.voice);
            }

            // a ship setting down on ground too rough to sit on drops a charge ahead of itself to level it
            if (aloft && player.landing && !charged && Ship.Rough(body, player.ship.position))
            {
                Bolts.Add(new Bolt { position = player.ship.position - up * 2f, velocity = player.ship.velocity - up * 90f, life = 8f, blast = Ship.Pad, level = true });
                game.Notify("LANDING CHARGE AWAY");
                game.Sounds.Shot(1, 0.6f);
            }
            charged = aloft && player.landing && (charged || Bolts.Exists(bolt => bolt.level));

            if (!quiet) Ambush(frame, aloft, self, up, there);

            // foes and bolts move in steps of at most 1/60 s, like the player
            int steps = Mathf.Max(1, Mathf.CeilToInt(frame * 60f));
            float dt = frame / steps, hurt = 0f;
            ashore = flying ? 0f : ashore + frame;
            for (int step = 0; step < steps; step++)
            {
                for (int i = 0; i < Foes.Count; i++)
                {
                    var foe = Foes[i];
                    // raiders and the base have no quarrel with someone on foot, unless that someone carries a launcher
                    // to answer them with: otherwise raiders circle high, and in time leave
                    bool hunting = foe.kind == FoeKind.Drone || flying || player.Has(Gear.Launcher);
                    if (Combat.Hunt(ref foe, body, hunting ? self : self + up * 400f, player.Velocity, game.Threat, dt, out var shot) && hunting)
                    {
                        Bolts.Add(shot);
                        if (step == 0) game.Sounds.Shot(foe.kind == FoeKind.Raider ? 6 : foe.kind == FoeKind.Drone ? 7 : 8, 0.5f * math.saturate(1.2f - math.distance(foe.position, self) / 1500f));
                    }
                    Foes[i] = foe;
                }
                hurt += Combat.Step(Bolts, Foes, body, self, player.Velocity, flying ? Combat.ShipRadius : Combat.PersonRadius, dt, struck);
            }
            if (hurt > 0f) player.Hurt(hurt, !flying ? $"Gunned down by drones on {body.name}" : there ? "Shot to pieces by the raiders' citadel" : $"Shot down over {body.name}");

            // every strike shows: sparks and a chunk off whatever hull it hit, a fireball or a cloud of
            // steam where something big came down, a flash where anything else did, and a crater
            var effects = game.Effects;
            foreach (var strike in struck)
            {
                float3 where = math.normalizesafe(strike.at, up);
                // a ship's guns, its own or a raider's, tear far bigger holes than anything carried
                float hole = strike.blast * (player.flying ? 2.5f : 1f);
                bool sea = strike.ground && body.SeaRadius > 0f && body.GroundRadius(where) < body.SeaRadius;
                // a nuclear blast spares nothing near it, whoever fired it
                if (strike.blast >= Combat.Nuclear)
                {
                    effects.Mushroom(strike.at, where, 1.6f * strike.blast);
                    game.Sounds.Nuke();
                    player.Hurt(200f * math.saturate(1f - math.distance(strike.at, self) / (4f * strike.blast)), "Caught in the blast of a nuclear torpedo");
                }
                else if (strike.foe >= 0)
                {
                    var foe = Foes[strike.foe];
                    effects.Hit(strike.at, math.normalizesafe(strike.at - foe.position, -where), foe.kind == FoeKind.Base ? 20f : foe.size, foe.velocity);
                }
                else if (sea && strike.blast >= 1.2f) effects.Steam(strike.at, where, hole);
                else if (strike.ground && strike.blast >= 5f) effects.Explode(strike.at, float3.zero, 0.8f * strike.blast);
                else bursts.Add(new Burst { position = strike.at, size = math.max(1.5f, 1.6f * strike.blast) });
                if (!strike.ground) continue;
                game.Sounds.Thud(math.saturate(strike.blast / 6f) * math.saturate(1.2f - math.distance(strike.at, self) / 400f));
                // nothing is dug by small arms, or where the hermit is camped
                bool camp = system.Reference == SolarSystemDescriptor.Home && math.distance(strike.at, game.Hermit.Position) < 40f;
                if (strike.level) game.Level(strike.at);
                else if (strike.blast >= 1.2f && !camp) game.Dig(strike.at, hole);
            }
            struck.Clear();

            for (int i = Foes.Count - 1; i >= 0; i--)
            {
                var foe = Foes[i];
                float away = math.distance(foe.position, self);
                if (foe.kind == FoeKind.Base) Base = math.max(foe.health, 0f);
                if (foe.health <= 0f)
                {
                    game.Effects.Explode(foe.position, foe.velocity, foe.size);
                    game.Sounds.Boom(math.saturate(1f - away / 2500f), foe.kind != FoeKind.Drone);
                    game.Award(foe.kind);
                }
                else if (foe.kind == FoeKind.Base ? there : !(foe.kind == FoeKind.Raider ? away > 6000f || (ashore > 25f && !player.Has(Gear.Launcher)) : away > 350f)) continue;
                Foes.RemoveAt(i);
            }
            for (int i = bursts.Count - 1; i >= 0; i--)
            {
                var burst = bursts[i];
                burst.age += frame;
                bursts[i] = burst;
                if (burst.age > 0.5f) bursts.RemoveAt(i);
            }
        }

        // Decides when the next foes arrive.
        void Ambush(float frame, bool aloft, float3 self, float3 up, bool atBase)
        {
            var player = game.Player;
            var system = game.System;
            var body = system.ReferenceBody;
            int raiders = 0, drones = 0;
            foreach (var foe in Foes)
                if (foe.kind == FoeKind.Raider) raiders++;
                else if (foe.kind == FoeKind.Drone) drones++;
            // foes come sooner the further the game has gone
            float keen = 1f + 0.15f * (game.Threat - 1);

            // the base sends its raiders out from its own hull while a ship is in reach
            if (atBase && aloft && raiders < math.min(2 + game.Depth, 6) && math.length(self) < 2400f && (launch -= frame) <= 0f)
            {
                launch = 14f / keen;
                Add(FoeKind.Raider, rnd.NextFloat3Direction() * (0.7f * body.radius));
                return;
            }

            // elsewhere raiders jump a ship that lingers in flight, once it is a ship that can go anywhere
            if (!atBase && aloft && player.Has(Gear.Orbiter) && !player.Autopilot && raiders == 0 && (raid -= frame) <= 0f)
            {
                raid = rnd.NextFloat(45f, 75f) / keen;
                float3 ahead = math.rotate(player.ship.rotation, new float3(0, 0, 1));
                for (int i = 0, wing = math.min(1 + game.Depth / 2, 4); i < wing; i++)
                {
                    float3 from = self + math.normalize(ahead * 0.6f + rnd.NextFloat3Direction()) * rnd.NextFloat(1100f, 1500f);
                    // never out of the ground
                    float3 over = math.normalize(from);
                    Add(FoeKind.Raider, over * math.max(math.length(from), Ship.Surface(body, over) + 150f));
                }
                game.Notify("RAIDERS INBOUND");
                game.Sounds.Alarm();
            }

            // drones find someone on foot, though never at the hermit's fire
            bool exposed = !player.flying && player.Has(Gear.Pistol) && body.kind == BodyKind.Rock && !player.walker.swimming
                && !(system.Reference == SolarSystemDescriptor.Home && math.distance(self, game.Hermit.Position) < 70f);
            if (exposed && drones < math.min(1 + game.Depth, 5) && (pack -= frame) <= 0f)
            {
                pack = rnd.NextFloat(25f, 40f) / keen;
                float3 along = player.walker.forward, across = math.cross(up, along);
                for (int i = 0, count = math.min(1 + game.Depth / 2, 3); i < count; i++)
                {
                    float bearing = rnd.NextFloat(-1.6f, 1.6f);
                    float3 over = math.normalize(self + (along * math.cos(bearing) + across * math.sin(bearing)) * rnd.NextFloat(50f, 70f));
                    Add(FoeKind.Drone, over * (Ship.Surface(body, over) + 2f));
                }
                game.Notify("DRONES CLOSING");
                game.Sounds.Alarm();
            }
        }

        // Drawn every frame, paused or not.
        void Update()
        {
            float3 anchor = game.System.Anchor;
            float beat = Time.time;
            foreach (var list in new[] { raiders, drones, flashes, missiles, bolts[0], bolts[1] }) list.Clear();
            foreach (var foe in Foes)
            {
                float3 up = math.normalizesafe(foe.position, new float3(0, 1, 0));
                // the more it has been hit, the more of it is holes
                float battered = 1f - foe.health / Combat.Health(foe.kind, game.Threat);
                if (foe.kind == FoeKind.Raider) raiders.Add(Matrix4x4.TRS(foe.position - anchor, Quaternion.LookRotation(foe.forward, up), Vector3.one), battered, foe.seed);
                // a drone bobs as it hovers
                else if (foe.kind == FoeKind.Drone)
                    drones.Add(Matrix4x4.TRS(foe.position - anchor + up * (0.12f * math.sin(beat * 3f + foe.seed % 7u)), Quaternion.LookRotation(foe.forward, up), Vector3.one), battered, foe.seed);
            }
            foreach (var bolt in Bolts)
            {
                var facing = Quaternion.LookRotation(bolt.velocity);
                if (bolt.homing > 0f)
                {
                    // a missile is a thing, with its motor burning behind it
                    missiles.Add(Matrix4x4.TRS(bolt.position - anchor, facing, Vector3.one * 2.5f));
                    flashes.Add(Matrix4x4.TRS(bolt.position - anchor - (float3)(facing * Vector3.forward * 2.6f), facing, new Vector3(0.7f, 0.7f, 3f)));
                    continue;
                }
                // fast bolts are streaks, slow ones are balls of fire you can step aside from
                float speed = math.length(bolt.velocity), width = speed < 100f ? 0.4f : speed < 300f ? 0.12f : speed > 2000f ? 0.7f : 0.4f;
                bolts[bolt.hostile ? 1 : 0].Add(Matrix4x4.TRS(bolt.position - anchor, facing, new Vector3(width, width, math.clamp(speed * 0.02f, 0.5f, 60f))));
            }
            foreach (var burst in bursts)
                flashes.Add(Matrix4x4.TRS(burst.position - anchor, Quaternion.identity, Vector3.one * (burst.size * math.sin(math.PI * math.sqrt(burst.age / 0.5f)))));

            var bounds = new Bounds(Vector3.zero, Vector3.one * 20000f);
            raiders.Draw(raider, raiderHull, true, bounds);
            raiders.Draw(raiderLights, raiderGlow, false, bounds);
            drones.Draw(drone, droneHull, true, bounds);
            drones.Draw(droneEye, droneGlow, false, bounds);
            missiles.Draw(missile, casing, false, bounds);
            bolts[0].Draw(ball, ours, false, bounds);
            bolts[1].Draw(ball, theirs, false, bounds);
            flashes.Draw(ball, fire, false, bounds);
        }
    }
}
