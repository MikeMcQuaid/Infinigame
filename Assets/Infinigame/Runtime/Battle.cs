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
        public float Base => game.Stage > Campaign.Citadel ? 0 : Foes.Where(foe => foe.kind == FoeKind.Mind).Select(foe => foe.health).DefaultIfEmpty(Combat.Health(FoeKind.Mind, game.Threat)).First();
        // When the player last fired.
        public float Fired { get; private set; } = float.MinValue;

        readonly List<Burst> bursts = new List<Burst>();
        readonly List<Strike> struck = new List<Strike>();
        readonly Instances raiders = new Instances(), drones = new Instances(), flashes = new Instances(), missiles = new Instances();
        readonly Instances[] bolts = { new Instances(), new Instances() };
        Mesh raider, raiderLights, drone, droneEye, ball, missile, sentry, sentryEye, leg, mind, mindLights;
        readonly Instances sentries = new Instances(), legs = new Instances(), minds = new Instances();
        Material raiderHull, raiderGlow, droneHull, droneGlow, ours, theirs, fire, casing;
        Random rnd = new Random(1);
        float trigger, raid, pack, ashore, launch;
        // charged: a landing charge has gone down for the landing in hand
        bool left, charged;
        LineRenderer ray;

        void Awake()
        {
            (raider, raiderLights) = Models.Raider();
            (drone, droneEye) = Models.Drone();
            (sentry, sentryEye, leg) = Models.Sentry();
            (mind, mindLights) = Models.Mind();
            missile = Models.Missile();
            ball = Shapes.Sphere(0.5f, 8);
            ray = new GameObject("Weapon beam").AddComponent<LineRenderer>();
            ray.transform.SetParent(transform, false);
            ray.sharedMaterial = Shapes.Material("Glow", new Color(0.5f, 3, 5));
            ray.startWidth = ray.endWidth = 0.1f;
            ray.positionCount = 2;
            ray.enabled = false;
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
            if (ray != null) ray.enabled = false;
        }

        // Foes and bolts are placed relative to the body the player is nearest, so they move with the player when that changes.
        public void Rebase(int from, int to)
        {
            var system = game.System;
            game.Outpost.Leave(from);
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

            bool there = body.kind == BodyKind.Station && Base > 0;
            trigger -= frame;
            ray.enabled = false;
            var weapon = player.Weapon;
            if (c.fire && trigger <= 0 && player.Armed && (aloft || !flying) && !player.Autopilot
                && (flying || Suit.Spend(ref player.energy, weapon.energy * (weapon.beam ? frame : 1))))
            {
                var view = Camera.main.transform;
                float3 forward = view.forward, eye = (float3)view.position + system.Anchor;
                float3 muzzle = flying ? player.ship.position + math.rotate(player.ship.rotation, new float3(1.7f, -0.3f, 2.5f))
                    : eye + (float3)(view.right * 0.25f + view.up * (weapon.homing > 0 ? 0.2f : -0.22f));
                if (flying) forward = math.rotate(player.ship.rotation, new float3(0, 0, 1));
                else if (player.Hand(UnityEngine.XR.XRNode.RightHand, out var hand, out var aim))
                {
                    muzzle = (float3)(hand + aim * Vector3.forward * 0.12f) + system.Anchor;
                    forward = aim * Vector3.forward;
                }
                if (weapon.beam || weapon.rail || weapon.melee)
                {
                    float reach = weapon.melee ? 4.5f : weapon.rail ? 2000 : flying ? 3000 : 250;
                    float3 end = muzzle + forward * reach;
                    if (!game.Outpost.Visible(muzzle, end))
                    {
                        float near = 0, far = reach;
                        for (int i = 0; i < 14; i++)
                        {
                            float mid = (near + far) * 0.5f;
                            if (game.Outpost.Visible(muzzle, muzzle + forward * mid)) near = mid; else far = mid;
                        }
                        reach = near;
                        if (weapon.melee && game.Outpost.World == system.Reference) game.Outpost.Breach(muzzle + forward * (reach + 0.01f), weapon.damage);
                    }
                    end = Combat.Ray(Foes, body, muzzle, forward, reach, weapon.damage * (weapon.beam ? frame : 1), weapon.rail, game.Outpost.Shield, struck);
                    if (weapon.melee)
                    {
                        for (int i = 0; i < Foes.Count; i++)
                        {
                            var foe = Foes[i];
                            if (math.distance(foe.position, end) < foe.size + 1) { foe.velocity += forward * 45; Foes[i] = foe; }
                        }
                        if (math.length(end) < body.GroundRadius(math.normalizesafe(end)) + 1) game.Dig(end, 3);
                    }
                    else
                    {
                        ray.enabled = true;
                        ray.SetPosition(0, muzzle - system.Anchor);
                        ray.SetPosition(1, end - system.Anchor);
                        if (game.Outpost.Shield.Blocks(muzzle, forward * reach, out _)) game.Outpost.Flash();
                    }
                }
                else Combat.Fire(weapon, muzzle, forward, player.Velocity, ref rnd, Bolts);
                trigger = weapon.period;
                if (!weapon.beam || Time.time - Fired > 0.15f) game.Sounds.Shot(weapon.voice);
                Fired = Time.time;
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
                    bool hunting = !foe.friendly && !foe.scattered;
                    float3 quarry = self;
                    if (foe.kind == FoeKind.Sentry)
                    {
                        if (foe.friendly)
                        {
                            int mind = Foes.FindIndex(other => other.kind == FoeKind.Mind && other.health > 0);
                            if (mind >= 0 && game.Outpost.Visible(foe.position, Foes[mind].position))
                            {
                                var target = Foes[mind]; target.health -= 8 * dt; Foes[mind] = target;
                            }
                            hunting = false;
                        }
                        else hunting = Combat.Observe(ref foe, self, c.fire ? 1 : c.sprint ? 0.6f : 0.05f, player.cloaked ? 0.03f : math.max(0.15f, math.saturate(math.dot(up, math.normalizesafe(system.ToReference(0))))) ,
                            game.Outpost.Visible(foe.position, self), dt) == Alert.Alarmed;
                    }
                    float3 before = foe.position;
                    if (hunting || foe.scattered)
                    {
                        bool fired = Combat.Hunt(ref foe, body, quarry, player.Velocity, game.Threat, dt, out var shot);
                        if (foe.kind == FoeKind.Sentry) foe.position = game.Outpost.Resolve(before, foe.position, 0.9f, out _);
                        if (fired && hunting && game.Outpost.Visible(foe.position, self))
                        {
                            if (foe.kind == FoeKind.Raider)
                            {
                                var copied = Combat.CapturedWeapon(player.kit, foe.seed);
                                shot.velocity = math.normalizesafe(shot.velocity) * copied.speed;
                                shot.homing = copied.homing;
                                shot.blast = copied.blast;
                                shot.damage = math.max(shot.damage, copied.damage * 0.12f);
                                shot.life = copied.life;
                            }
                            Bolts.Add(shot);
                        }
                    }
                    Foes[i] = foe;
                }
                for (int i = Bolts.Count - 1; i >= 0; i--)
                    if (!game.Outpost.Visible(Bolts[i].position, Bolts[i].position + Bolts[i].velocity * dt)) Bolts.RemoveAt(i);
                hurt += Combat.Step(Bolts, Foes, body, self, player.Velocity, flying ? Combat.ShipRadius : Combat.PersonRadius, dt, struck, game.Outpost.Shield);
            }
            if (hurt > 0f) player.Hurt(hurt, !flying ? $"Gunned down by drones on {body.name}" : there ? "Shot to pieces by the raiders' citadel" : $"Shot down over {body.name}");

            // every strike shows: sparks and a chunk off whatever hull it hit, a fireball or a cloud of
            // steam where something big came down, a flash where anything else did, and a crater
            var effects = game.Effects;
            foreach (var strike in struck)
            {
                if (strike.shield) { game.Outpost.Flash(); continue; }
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
                if (foe.health <= 0f)
                {
                    game.Effects.Explode(foe.position, foe.velocity, foe.size);
                    game.Sounds.Boom(math.saturate(1f - away / 2500f), foe.kind != FoeKind.Drone);
                    game.Collect(new float3(0, 0, foe.kind == FoeKind.Raider ? 12 : 4));
                    game.Award(foe.kind);
                }
                else if (foe.kind == FoeKind.Mind || foe.kind == FoeKind.Sentry || (foe.kind == FoeKind.Base ? there : !(foe.kind == FoeKind.Raider ? away > 6000f || (ashore > 25f && !player.Has(Gear.BackMissiles)) : away > 350f))) continue;
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
                game.Hermit.Remark("Incoming bodies of the presence. Their ships are closing on us.");
                game.Sounds.Alarm();
            }

            // drones find someone on foot, though never at the hermit's fire
            bool exposed = !player.flying && player.Has(Gear.Suit) && body.kind == BodyKind.Rock && !player.walker.swimming
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
            foreach (var list in new[] { raiders, drones, flashes, missiles, sentries, legs, minds, bolts[0], bolts[1] }) list.Clear();
            foreach (var foe in Foes)
            {
                float3 up = math.normalizesafe(foe.position, new float3(0, 1, 0));
                // the more it has been hit, the more of it is holes
                float battered = 1f - foe.health / Combat.Health(foe.kind, game.Threat);
                if (foe.kind == FoeKind.Raider) raiders.Add(Matrix4x4.TRS(foe.position - anchor, Quaternion.LookRotation(foe.forward, up), Vector3.one), battered, foe.seed);
                // a drone bobs as it hovers
                else if (foe.kind == FoeKind.Drone)
                    drones.Add(Matrix4x4.TRS(foe.position - anchor + up * (0.12f * math.sin(beat * 3f + foe.seed % 7u)), Quaternion.LookRotation(foe.forward, up), Vector3.one), battered, foe.seed);
                else if (foe.kind == FoeKind.Mind)
                    minds.Add(Matrix4x4.TRS(foe.position - anchor, Quaternion.LookRotation(foe.forward, up), Vector3.one), battered, foe.seed);
                else if (foe.kind == FoeKind.Sentry)
                {
                    Quaternion facing = Quaternion.LookRotation(math.normalizesafe(Walker.Project(foe.forward, up), new float3(0, 0, 1)), up);
                    sentries.Add(Matrix4x4.TRS(foe.position - anchor, facing, Vector3.one), battered, foe.seed);
                    foreach (float side in new[] { -1f, 1f })
                        legs.Add(Matrix4x4.TRS(foe.position - anchor + (float3)(facing * new Vector3(side * 0.24f, -0.05f, 0)),
                            facing * Quaternion.Euler(side * Mathf.Sin(beat * 9) * math.min(math.length(foe.velocity) * 6, 35), 0, 0), Vector3.one), battered, foe.seed);
                }
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
            sentries.Draw(sentry, droneHull, true, bounds);
            sentries.Draw(sentryEye, droneGlow, false, bounds);
            legs.Draw(leg, droneHull, true, bounds);
            minds.Draw(mind, droneHull, true, bounds);
            minds.Draw(mindLights, droneGlow, false, bounds);
            missiles.Draw(missile, casing, false, bounds);
            bolts[0].Draw(ball, ours, false, bounds);
            bolts[1].Draw(ball, theirs, false, bounds);
            flashes.Draw(ball, fire, false, bounds);
        }
    }
}
