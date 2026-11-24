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
        [NonSerialized] public Game game;
        // Tests switch the ambushes off and place foes themselves.
        [NonSerialized] public bool quiet;

        public readonly List<Foe> Foes = new List<Foe>();
        public readonly List<Bolt> Bolts = new List<Bolt>();
        // What is left of the raiders' base in this system. Nothing, once it has been destroyed.
        public float Base => game.Stage > Campaign.Citadel ? 0 : Foes.Where(foe => foe.kind == FoeKind.Mind).Select(foe => foe.health).DefaultIfEmpty(Combat.Health(FoeKind.Mind, game.Threat)).First();
        // When the player last fired.
        public float Fired { get; private set; } = float.MinValue;

        readonly List<Strike> struck = new List<Strike>();
        readonly Instances raiders = new Instances(), drones = new Instances(), flashes = new Instances(), missiles = new Instances();
        readonly Instances[] bolts = { new Instances(), new Instances() };
        Mesh[] emplacement;
        readonly Instances emplacements = new Instances(), racks = new Instances(), acid = new Instances(), droplets = new Instances();
        Material bile, membrane;
        public Material MindTissue { get; private set; }
        Mesh raider, raiderLights, drone, droneEye, ball, missile, mind, mindLights;
        readonly Mesh[] sentry = new Mesh[9], sentryEye = new Mesh[9], leg = new Mesh[9];
        readonly Instances[] sentries = new Instances[9], legs = new Instances[9];
        Camera view;
        readonly Instances minds = new Instances();
        Material raiderHull, raiderGlow, broodShell, broodOptics, ours, theirs, fire, casing;
        Random rnd = new Random(1);
        float raid, pack, ashore, launch, railCharge, nextCarve, nextScorch, railUntil;
        LineRenderer railTwin, sightLine;
        Transform sightSpot;
        float sightRange, nextSightTrace, beamRange;
        Material beamLight, railLight;
        string previousWeapon;
        bool charging;
        public float RailCharge => railCharge;
        readonly float[] readyAt = new float[Combat.Guns.Length + Combat.Arms.Length];
        int beamSlot = -1;
        public float RecoveryPeriod => game.Player.Weapon.beam ? 1.2f : game.Player.Weapon.period;
        public bool Sweeping => game.Player.Weapon.sweep && Time.time < sweepEnds;
        public float Cooldown => math.max(0, readyAt[game.Player.WeaponSlot] - Time.time - (Sweeping ? sweepEnds - Time.time : 0));
        float nextSweepPulse;
        public void Rearm()
        {
            beamSlot = -1;
            Array.Clear(readyAt, 0, readyAt.Length);
            nextSweepPulse = railCharge = sweepEnds = sweepUntil = nextSightTrace = 0;
            charging = false;
        }
        float sweepEnds;
        int sweepIndex;
        readonly HashSet<uint> playerHits = new HashSet<uint>();
        public bool Charging => charging && railCharge > 0 && Cooldown <= 0;
        // charged: a landing charge has gone down for the landing in hand
        bool left, charged;
        LineRenderer ray;
        readonly List<Transform> shields = new List<Transform>();
        Material field;
        readonly List<LineRenderer> sweep = new List<LineRenderer>();
        float sweepUntil;
        public bool Beaming => ray != null && ray.enabled && game.Player.Weapon.beam;

        void Awake()
        {
            (raider, raiderLights) = Models.Raider();
            (drone, droneEye) = Models.Drone();
            view = Camera.main;
            for (int i = 0; i < sentry.Length; i++)
            {
                (sentry[i], sentryEye[i], leg[i]) = Models.Sentry((MotileCaste)(i % 3), i / 3);
                sentries[i] = new Instances();
                legs[i] = new Instances();
            }
            (mind, mindLights) = Models.Mind();
            MindTissue = Shapes.BroodTissue();
            emplacement = Models.SpineLauncher();
            missile = Models.Missile();
            ball = Shapes.Orb(0.5f, 8);
            ray = new GameObject("Weapon beam").AddComponent<LineRenderer>();
            ray.transform.SetParent(transform, false);
            ray.sharedMaterial = beamLight = Shapes.Beam(new Color(0.25f, 1.1f, 2.6f));
            railLight = Shapes.Beam(new Color(0.44f, 1.75f, 2.1f));
            ray.startWidth = ray.endWidth = 0.1f;
            ray.positionCount = 2;
            ray.enabled = false;
            railTwin = new GameObject("Twin rail trail").AddComponent<LineRenderer>();
            railTwin.transform.SetParent(transform, false); railTwin.sharedMaterial = ray.sharedMaterial;
            railTwin.startWidth = railTwin.endWidth = 0.08f; railTwin.positionCount = 2; railTwin.enabled = false;
            sightLine = Instantiate(railTwin, transform);
            sightLine.name = "Targeting laser";
            sightLine.sharedMaterial = Shapes.Beam(new Color(0.045f, 0.65f, 1));
            sightSpot = Shapes.Part(transform, "Aim impact", Shapes.Orb(1, 12), sightLine.sharedMaterial, Vector3.zero, Vector3.one, false).transform;
            sightSpot.gameObject.SetActive(false);
            sightLine.startWidth = 0.006f; sightLine.endWidth = 0.055f;
            (raiderHull, raiderGlow) = (Shapes.PresenceShell(10), Shapes.PresenceOptics(10));
            (broodShell, broodOptics) = (Shapes.PresenceShell(1.2f), Shapes.PresenceOptics(1.2f));
            bile = Shapes.Material("Glow", new Color(0.64f, 0.71f, 0.12f));
            membrane = Shapes.Solid(Surface.Chitin, new Color(0.31f, 0.09f, 0.39f));
            casing = Shapes.Material("Props", new Color(0.3f, 0.28f, 0.27f));
            // Keep projectile glow within the headset's exposure range.
            Material Fire(Color colour)
            {
                if (Shapes.MatteCombat) return Shapes.Material("Glow", colour / Mathf.Max(1, colour.maxColorComponent));
                var flash = new Material(Resources.Load<Shader>("Flash")) { enableInstancing = true };
                flash.SetColor("_Colour", colour);
                return flash;
            }
            ours = Fire(new Color(2.5f, 8f, 12f));
            theirs = Fire(new Color(1.8f, 0.21f, 1.55f));
            fire = Fire(new Color(12f, 6f, 2f));
        }

        void LateUpdate()
        {
            if (sightLine != null && (game.Paused || game.Dead || game.Player.SystemView || game.Hermit.Talking || game.ShowControls || game.Buried || game.Fall.active)) { sightLine.enabled = false; sightSpot.gameObject.SetActive(false); charging = false; railCharge = 0; }
            if (!game.Player.flying && (sightLine.enabled || Beaming))
            {
                var port = game.GetComponent<Arms>().Muzzle(game.Player.Weapon);
                if (sightLine.enabled)
                {
                    sightLine.SetPosition(0, port.position);
                    if (DeviceControls.VR)
                    {
                        sightLine.SetPosition(1, port.position + port.forward * sightRange);
                        if (sightSpot.gameObject.activeSelf) sightSpot.position = port.position + port.forward * (sightRange - 0.03f);
                    }
                }
                if (Beaming)
                {
                    ray.SetPosition(0, port.position);
                    if (DeviceControls.VR) ray.SetPosition(1, port.position + port.forward * beamRange);
                }
            }
        }



        // A new system, or a new run: nothing follows you there, its base stands whole, and there is a moment's peace.
        public void Reset()
        {
            game.Corpses.Clear();
            Foes.Clear(); playerHits.Clear(); Rearm();
            Bolts.Clear();
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
                if (foe.kind != FoeKind.Raider || foe.outerGuard)
                {
                    Foes.RemoveAt(i);
                    continue;
                }
                system.Descriptor.Rebase(from, to, system.Clock, ref foe.position, ref foe.velocity);
                foe.forward = math.rotate(system.Descriptor.Turn(from, to, system.Clock), foe.forward);
                Foes[i] = foe;
            }
        }

        public void Add(FoeKind kind, float3 position, MotileCaste caste = MotileCaste.Stalker)
        {
            var body = game.System.ReferenceBody;
            var foe = Combat.Spawn(kind, position, game.Player.Position - position, game.Threat, rnd.NextUInt(), 0.6f * body.radius, caste);
            if ((game.Stage > 5 || game.Presence.played > 900) && !game.Frontier.State(game.System.Reference, game.Frontier.Region(game.System.Reference, position)).mindDead && foe.seed % 3 == 0) foe.shield = 40;
            if (kind == FoeKind.Drone || kind == FoeKind.Sentry)
            {
                foe.emergence = 1;
                foe.cooldown = math.max(foe.cooldown, 1.3f);
            }
            Foes.Add(foe);
        }

        public float3 FriendlyFire(float3 from, float3 target, Weapon gun)
        {
            float3 direction = math.normalizesafe(target - from);
            if (gun.sweep)
            {
                gun.sweep = false; gun.beam = true;
                for (int i = 0; i < 16; i++) FriendlyFire(from, from + Combat.SweepDirection(i, 16, math.normalizesafe(from)) * 150, gun);
                return target;
            }
            if (!gun.beam && !gun.rail && !gun.sweep && !gun.melee)
            {
                int before = Bolts.Count; Combat.Fire(gun, from, direction, float3.zero, ref rnd, Bolts);
                for (int i = before; i < Bolts.Count; i++) { var bolt = Bolts[i]; bolt.robot = true; Bolts[i] = bolt; }
                return target;
            }
            float reach = math.distance(from, target);
            if (!game.Outpost.Visible(from, target))
            {
                float near = 0, far = reach;
                for (int i = 0; i < 12; i++) { float mid = (near + far) * 0.5f; if (game.Outpost.Visible(from, from + direction * mid)) near = mid; else far = mid; }
                reach = near;
                if (!game.Outpost.Shield.Blocks(from, direction * (reach + 0.02f), out _)) game.Outpost.Breach(from + direction * (reach + 0.02f), gun.damage);
            }
            int first = struck.Count;
            var end = Combat.Ray(Foes, game.System.ReferenceBody, from, direction, reach + 0.01f, gun.damage, gun.rail, game.Outpost.Shield, struck, robot: true);
            for (int i = first; i < struck.Count; i++) { var strike = struck[i]; strike.robot = true; struck[i] = strike; }
            return end;
        }
        float WallRange(float3 origin, float3 direction, float reach)
        {
            if (game.Outpost.Visible(origin, origin + direction * reach)) return reach;
            float near = 0, far = reach;
            for (int i = 0; i < 16; i++)
            {
                float mid = (near + far) * 0.5f;
                if (game.Outpost.Visible(origin, origin + direction * mid)) near = mid; else far = mid;
            }
            return near;
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

            var weapon = player.Weapon;
            if (previousWeapon != weapon.name) { charging = false; railCharge = railUntil = nextSightTrace = 0; }
            previousWeapon = weapon.name;
            ray.enabled = Time.time < railUntil;
            railTwin.enabled = Time.time < railUntil && flying;
            foreach (var beam in sweep)
            {
                beam.enabled = !flying && player.Weapon.sweep && Time.time < sweepUntil;
                if (beam.enabled) beam.SetPosition(0, player.BackpackPort - system.Anchor);
            }
            Vector3 port = Models.WingPort;
            if (flying && weapon.pellets == 1 && left) port.x = -port.x;
            var wrist = flying ? default : game.GetComponent<Arms>().Muzzle(weapon);
            float3 weaponPort = flying ? player.ship.position + math.rotate(player.ship.rotation, (float3)port) : (float3)wrist.position + system.Anchor;
            float3 muzzle = weapon.needs == Gear.BackMissiles ? player.BackpackPort : weaponPort;
            float3 forward = flying ? math.rotate(player.ship.rotation, new float3(0, 0, 1)) : (float3)view.transform.forward;
            bool wristSight = !flying && (weapon.rail || weapon.beam || weapon.needs == Gear.BackMissiles);
            float3 aimOrigin = DeviceControls.VR ? weaponPort : (float3)view.transform.position + system.Anchor;
            if (DeviceControls.VR && wristSight) forward = wrist.forward;
            bool aimMissile = DeviceControls.VR && weapon.needs == Gear.BackMissiles;
            sightLine.enabled = wristSight && !c.interfaceActive && player.Armed;
            bool showSpot = false;
            float3 aimedAt = default;
            if (flying && DeviceControls.VR && weapon.rail) aimOrigin = player.ship.position + math.rotate(player.ship.rotation, (float3)Models.PilotEye);
            if (sightLine.enabled || flying && DeviceControls.VR && weapon.rail && charging && !c.fire)
            {
                float reach = weapon.rail ? 2000 : weapon.beam ? 250 : 300;
                // The idle sight follows the hand every frame. Sample terrain again on every shot and release.
                if (c.fire || charging || Time.time >= nextSightTrace)
                {
                    float3 hit = Combat.Ray(Foes, body, aimOrigin, forward, reach, 0, false, game.Outpost.Shield, null);
                    sightRange = WallRange(aimOrigin, forward, math.distance(aimOrigin, hit));
                    nextSightTrace = Time.time + 0.05f;
                }
                float3 target = aimOrigin + forward * sightRange;
                showSpot = sightLine.enabled && sightRange < reach - 0.01f;
                if (showSpot)
                {
                    sightSpot.position = target - system.Anchor - forward * 0.03f;
                    sightSpot.localScale = Vector3.one * Mathf.Clamp(sightRange * 0.004f, 0.05f, 2);
                }
                sightLine.SetPosition(0, weaponPort - system.Anchor);
                sightLine.SetPosition(1, target - system.Anchor);
                aimedAt = target;
                if (!DeviceControls.VR || !wristSight || aimMissile)
                {
                    float3 launchOffset = weapon.needs == Gear.BackMissiles ? up * 0.6f : float3.zero;
                    forward = math.normalizesafe(target - muzzle - launchOffset, forward);
                }
            }
            sightSpot.gameObject.SetActive(showSpot);
            var fists = DeviceControls.VR && weapon.melee ? game.GetComponent<Arms>() : null;
            bool fireWeapon = !c.interfaceActive && (fists != null ? fists.MotionPunch : c.fire);
            int slot = player.WeaponSlot;
            if (beamSlot >= 0 && (beamSlot != slot || !fireWeapon || player.energy < weapon.energy * frame))
            {
                readyAt[beamSlot] = Time.time + 1.2f;
                beamSlot = -1;
            }
            bool ready = Time.time >= readyAt[slot];
            if (weapon.sweep && ready && fireWeapon && player.Armed && Suit.Spend(ref player.energy, weapon.energy))
            {
                sweepEnds = Time.time + 3;
                readyAt[slot] = sweepEnds + weapon.period;
                nextSweepPulse = Time.time;
            }
            if (weapon.sweep) fireWeapon = !c.interfaceActive && Sweeping && Time.time >= nextSweepPulse;
            if (c.interfaceActive) { charging = false; railCharge = 0; }
            if (weapon.rail && !c.interfaceActive)
            {
                fireWeapon = charging && (c.automaticFire && railCharge >= 1 || !c.fire && railCharge >= 0.35f);
                if (c.fire && ready) railCharge = math.min(1, railCharge + frame / math.max(0.1f, weapon.charge));
                weapon.damage *= math.lerp(0.35f, 1, railCharge);
                charging = c.fire;
                if (!c.fire && !fireWeapon) railCharge = 0;
            }
            else if (aimMissile && !c.interfaceActive)
            {
                fireWeapon = !c.fire && charging;
                charging = c.fire;
            }
            else { charging = false; railCharge = 0; }
            float damage = weapon.damage;
            for (int hand = 0; fireWeapon && hand < (fists != null ? 2 : 1); hand++)
            {
                if (fists != null && (fists.PunchHands & (1 << hand)) == 0) continue;
                if (!(ready || fists != null || weapon.sweep) || !player.Armed || flying && !aloft || player.Autopilot) break;
                if (!weapon.sweep && !Suit.Spend(ref player.energy, weapon.energy * (weapon.beam ? frame : 1))) break;
                weapon.damage = damage;
                float3 eye = (float3)view.transform.position + system.Anchor;
                if (player.ThirdPerson && !wristSight)
                {
                    float3 sight = Combat.Ray(Foes, body, eye, forward, 2000, 0, false, default, null);
                    if (!game.Outpost.Visible(eye, sight))
                    {
                        float near = 0, far = math.distance(eye, sight);
                        for (int i = 0; i < 14; i++)
                        {
                            float mid = (near + far) * 0.5f;
                            if (game.Outpost.Visible(eye, eye + forward * mid)) near = mid; else far = mid;
                        }
                        sight = eye + forward * near;
                    }
                    forward = math.normalizesafe(sight - muzzle, forward);
                }
                if (fists != null) { muzzle = fists.punchOrigins[hand]; forward = fists.punchDirections[hand]; }
                bool cockpitAssist = DeviceControls.VR && flying && (weapon.beam || weapon.rail || weapon.voice == 0);
                if ((DeviceControls.Touch && !DeviceControls.VR || cockpitAssist) && !weapon.sweep)
                {
                    bool Visible(float3 at)
                    {
                        if (!game.Outpost.Visible(muzzle, at) || game.Outpost.Shield.Blocks(muzzle, at - muzzle, out _)) return false;
                        for (int sample = 1; sample < 12; sample++)
                        {
                            float3 point = math.lerp(muzzle, at, sample / 12f);
                            if (math.length(point) < Combat.Surface(body, point)) return false;
                        }
                        return true;
                    }
                    int target = Combat.AssistedTarget(Foes, muzzle, forward, weapon.melee ? 4.5f : flying ? 1500 : 250, cockpitAssist ? 7 : flying ? 18 : 14, Visible);
                    if (target >= 0)
                    {
                        float3 at = Foes[target].position;
                        if (cockpitAssist)
                        {
                            forward = weapon.beam || weapon.rail ? math.normalizesafe(at - muzzle) : Combat.Intercept(at - muzzle, Foes[target].velocity - player.Velocity, weapon.speed);
                            aimedAt = at;
                        }
                        else forward = Combat.Turn(forward, math.normalizesafe(at - muzzle), math.radians(flying ? 12 : 10));
                    }
                }
                int learnedWeapon = flying ? 5 + Array.FindIndex(Combat.Guns, gun => gun.name == weapon.name) : player.Arm;
                float demolition = weapon.damage;
                if (flying || player.Powered)
                {
                    weapon.damage *= War.Resistance(game.Frontier.Local, learnedWeapon);
                    War.Learn(game.Frontier.Local, learnedWeapon, weapon.beam ? frame * 2 : 1);
                }
                if (weapon.sweep)
                {
                    float3 origin = player.BackpackPort;
                    Combat.Sweep(Foes, body, origin, weapon.damage, game.Outpost.Shield, game.Outpost.Visible, struck, game.Challenge);
                    sweepUntil = Time.time + 0.23f;
                    for (int i = 0; i < 6; i++)
                    {
                        if (sweep.Count <= i)
                        {
                            var beam = new GameObject("Hemisphere ray").AddComponent<LineRenderer>();
                            beam.transform.SetParent(transform, false); beam.sharedMaterial = beamLight;
                            beam.startWidth = beam.endWidth = 0.035f; beam.positionCount = 2; sweep.Add(beam);
                        }
                        float3 direction = math.rotate(quaternion.AxisAngle(up, Time.time * 2.4f), Combat.SweepDirection((sweepIndex + i) % 64, 64, up));
                        if (i % 2 == 1) direction = -direction;
                        if (i == 0) direction = math.normalizesafe(player.walker.forward - up * 0.3f);
                        float reach = Combat.SweepReach;
                        for (float distance = 2; distance <= Combat.SweepReach; distance += 2)
                            if (!game.Outpost.Visible(origin, origin + direction * distance))
                            {
                                reach = distance - 2;
                                if (!game.Outpost.Shield.Blocks(origin, direction * distance, out _)) game.Outpost.Breach(origin + direction * distance, weapon.damage);
                                break;
                            }
                        float3 end = Combat.Ray(Foes, body, origin, direction, reach, 0, false, game.Outpost.Shield, struck);
                        if (system.Bodies[system.Reference].TryGetComponent<Creatures>(out var animals)) animals.Strike(origin, end - origin);
                        sweep[i].enabled = true; sweep[i].SetPosition(0, origin - system.Anchor); sweep[i].SetPosition(1, end - system.Anchor);
                    }
                }
                else if (weapon.beam || weapon.rail || weapon.melee)
                {
                    float reach = weapon.melee ? fists != null ? fists.punchLengths[hand] : 4.5f : weapon.rail ? 2000 : flying ? 3000 : 250;
                    float fullReach = reach;
                    float3 end = muzzle + forward * reach;
                    bool grazedWall = false;
                    if (!game.Outpost.Visible(muzzle, end))
                    {
                        float near = 0, far = reach;
                        for (int i = 0; i < 14; i++)
                        {
                            float mid = (near + far) * 0.5f;
                            if (game.Outpost.Visible(muzzle, muzzle + forward * mid)) near = mid; else far = mid;
                        }
                        reach = near;
                        if (!game.Outpost.Shield.Blocks(muzzle, forward * (reach + 0.02f), out _))
                            game.Outpost.Breach(muzzle + forward * (reach + 0.01f), demolition * (weapon.beam ? frame : 1));
                    }
                    else if (fists != null) grazedWall = game.Outpost.PunchWall(muzzle, end, demolition);
                    if (game.Outpost.Shield.Blocks(muzzle, forward * reach, out var shieldStop)) reach = math.distance(muzzle, shieldStop);
                    if (system.Bodies[system.Reference].TryGetComponent<Scatter>(out var growth) && growth.Strike(muzzle, muzzle + forward * reach, demolition * (weapon.beam ? frame : 1), out float3 tree)) reach = math.distance(muzzle, tree);
                    game.Corpses.Strike(muzzle, forward * reach, weapon.damage);
                    int previousHits = struck.Count;
                    end = Combat.Ray(Foes, body, muzzle, forward, reach, weapon.damage * (weapon.beam ? frame : 1), weapon.rail, game.Outpost.Shield, struck, level: game.Challenge);
                    bool animal = system.Bodies[system.Reference].TryGetComponent<Creatures>(out var wildlife) && wildlife.Strike(muzzle, end - muzzle);
                    if (weapon.beam && game.Progression.ranks[0] >= 3)
                    {
                        int chained = 0;
                        for (int target = 0; target < Foes.Count && chained < 2; target++)
                        {
                            var foe = Foes[target];
                            if (foe.health <= 0 || foe.friendly || math.distance(end, foe.position) < foe.size + 0.2f || math.distance(end, foe.position) > 12 || !game.Outpost.Visible(end, foe.position)) continue;
                            Combat.Ray(Foes, body, end, math.normalizesafe(foe.position - end), 12, weapon.damage * frame * 0.3f, false, game.Outpost.Shield, struck, level: game.Challenge);
                            game.Effects.Crackle(foe.position); chained++;
                        }
                    }
                    if (weapon.rail || weapon.beam)
                    {
                        if (!flying && weapon.beam) beamRange = math.distance(muzzle, end);
                        if (weapon.rail) railUntil = Time.time + 0.10f;
                        ray.sharedMaterial = railTwin.sharedMaterial = weapon.rail ? railLight : beamLight;
                        ray.startWidth = railTwin.startWidth = weapon.rail ? 0.028f : flying ? 0.85f : 0.1f;
                        ray.endWidth = railTwin.endWidth = weapon.rail ? 0.012f : flying ? 1.65f : 0.1f;
                        if (flying)
                        {
                            float3 second = player.ship.position + math.rotate(player.ship.rotation, new float3(-port.x, port.y, port.z));
                            float3 otherDirection = DeviceControls.VR && (weapon.rail || weapon.beam && math.any(aimedAt != 0)) ? math.normalizesafe(aimedAt - second, forward) : forward;
                            float otherReach = WallRange(second, otherDirection, weapon.rail ? 2000 : 3000);
                            float3 other = Combat.Ray(Foes, body, second, otherDirection, otherReach, weapon.damage * (weapon.beam ? frame : 1), weapon.rail, game.Outpost.Shield, struck, level: game.Challenge);
                            railTwin.enabled = true; railTwin.SetPosition(0, second - system.Anchor); railTwin.SetPosition(1, other - system.Anchor);
                        }
                    }
                    if (weapon.melee)
                    {
                        bool robot = !game.Hermit.Aboard && Combat.Crosses(muzzle, forward * reach, game.Hermit.Position + math.normalizesafe(game.Hermit.Position), 1.2f);
                        if (robot) game.Hermit.Punched();
                        robot |= game.Frontier.Punch(muzzle, forward * reach);
                        if (fists != null)
                        {
                            if (struck.Count == previousHits && reach >= fullReach && !grazedWall && !robot && !animal && math.length(end) > body.GroundRadius(math.normalizesafe(end)) + 0.12f) continue;
                            fists.Contact(hand);
                        }
                        if (player.Powered)
                        {
                            if (struck.Count > previousHits && struck[previousHits].foe >= 0 && !struck[previousHits].ground)
                            {
                                player.energy = math.min(player.MaxEnergy, player.energy + 22);
                                player.health = math.min(player.MaxHealth, player.health + 8);
                            }
                            player.walker.velocity = Walker.MoveTowards(player.walker.velocity, forward * math.clamp(math.length(body.GravityAt(player.Position)) * 1.2f, 3, 11) + up * 1.5f, 12);
                            player.walker.grounded = false;
                            player.walker.position += up * 0.08f;
                            game.Effects.Hit(end, -forward, 2);
                        }
                        for (int i = 0; i < Foes.Count; i++)
                        {
                            var foe = Foes[i];
                            if (!foe.friendly && math.distance(foe.position, end) < foe.size + 1) { foe.velocity += forward * (player.Powered ? 45 : 8); Foes[i] = foe; }
                        }
                        if (math.length(end) < body.GroundRadius(math.normalizesafe(end)) + 1) game.Dig(end, player.Powered ? 3 : 1.4f);
                    }
                    else
                    {
                        ray.enabled = true;
                        ray.SetPosition(0, muzzle - system.Anchor);
                        ray.SetPosition(1, end - system.Anchor);
                        if (game.Outpost.Shield.Blocks(muzzle, forward * reach, out _)) game.Outpost.Flash();
                    }
                }
                else if (flying && weapon.pellets == 2)
                {
                    weapon.pellets = 1;
                    Combat.Fire(weapon, muzzle, forward, player.Velocity, ref rnd, Bolts);
                    Combat.Fire(weapon, player.ship.position + math.rotate(player.ship.rotation, new float3(-port.x, port.y, port.z)), forward, player.Velocity, ref rnd, Bolts);
                }
                else Combat.Fire(weapon, muzzle, forward, player.Velocity, ref rnd, Bolts, launchUp: weapon.needs == Gear.BackMissiles ? up : float3.zero);
                if (weapon.sweep) { sweepIndex += 6; nextSweepPulse = Time.time + 0.24f; }
                else readyAt[slot] = Time.time + (fists != null ? 0 : weapon.period);
                if (weapon.rail) railCharge = 0;
                if (flying && weapon.beam) beamSlot = slot;
                if (!weapon.beam) game.Sounds.Shot(weapon.voice);
                if (flying && weapon.pellets == 1) left = !left;
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
                    var front = game.Frontier.State(system.Reference, game.Frontier.Region(system.Reference, foe.position));
                    foe.clever = !front.mindDead && (game.Stage > 2 || game.Presence.played > 600);
                    if (front.mindDead) foe.shield = 0;
                    if (foe.patrol && front.factory) foe.scattered = false;
                    bool hunting = !foe.friendly && !foe.scattered;
                    float3 quarry = game.Frontier.Target(foe.position, self);
                    if (foe.outerGuard && foe.kind == FoeKind.Raider && math.distance(foe.position, game.Outpost.Position) > 220)
                        quarry = math.normalizesafe(game.Outpost.Place(new float3(0, 45, -70))) * (body.radius * Shapes.AirTop(body) + 45);
                    if (foe.emergence > 0 && !foe.stationary)
                    {
                        if (foe.guardian && body.kind == BodyKind.Rock)
                        {
                            float3 normal = math.normalizesafe(foe.position);
                            foe.position = normal * (body.GroundRadius(normal) + Combat.SentryHeight);
                        }
                        foe.emergence = math.max(0, foe.emergence - dt / 1.25f);
                        Foes[i] = foe;
                        continue;
                    }
                    foe.health -= body.LavaDamage(foe.position, foe.size, Time.time) * dt;
                    foe.recoil = math.max(0, foe.recoil - dt * 4);
                    foe.attack = math.max(0, foe.attack - dt * 5);
                    if (foe.kind == FoeKind.Sentry && (!foe.stationary || player.cloaked))
                    {
                        if (foe.friendly) hunting = false;
                        else hunting = Combat.Observe(ref foe, quarry, c.fire ? 1 : c.sprint ? 0.6f : 0.05f, player.cloaked ? 0.03f : math.max(0.15f, math.saturate(math.dot(up, math.normalizesafe(system.ToReference(0))))) ,
                            game.Outpost.Visible(foe.position, quarry), dt) == Alert.Alarmed;
                    }
                    if (foe.guardian && !foe.scattered)
                    {
                        var mind = Foes.Where(other => other.kind == FoeKind.Mind && other.health > 0).OrderBy(other => math.distancesq(other.position, foe.position)).FirstOrDefault();
                        bool wounded = mind.health > 0 && mind.health < mind.maximumHealth;
                        if (wounded) quarry = self;
                        hunting = foe.provoked || wounded || math.distance(foe.position, quarry) < (body.kind == BodyKind.Station ? 160 : 12);
                        if (hunting) foe.alert = Alert.Alarmed;
                        else if (step == 0 && body.kind == BodyKind.Rock)
                        {
                            float3 normal = math.normalizesafe(foe.position);
                            foe.position = normal * (body.GroundRadius(normal) + Combat.SentryHeight);
                        }
                    }
                    float3 before = foe.position;
                    if (hunting || foe.scattered)
                    {
                        bool fired = Combat.Hunt(ref foe, body, quarry, player.Velocity, game.Threat, dt, out var shot);
                        if (foe.kind == FoeKind.Raider && body.kind == BodyKind.Rock)
                        {
                            float floor = body.radius * Shapes.AirTop(body) + foe.size;
                            if (math.lengthsq(foe.position) < floor * floor)
                            {
                                float3 outward = math.normalizesafe(foe.position, up);
                                foe.position = outward * floor;
                                foe.velocity -= outward * math.min(0, math.dot(foe.velocity, outward));
                            }
                        }
                        if (foe.outerGuard && foe.kind == FoeKind.Raider) game.Outpost.Shield.Bounce(before, ref foe.position, ref foe.velocity, foe.size);
                        if (foe.kind == FoeKind.Sentry)
                        {
                            foe.position = game.Outpost.Resolve(before, foe.position, 0.9f, out bool blocked);
                            if (blocked && foe.clever && !foe.stationary)
                            {
                                float3 side = math.cross(up, math.normalizesafe(quarry - before)) * (foe.seed % 2 == 0 ? 1 : -1);
                                foe.position = game.Outpost.Resolve(before, before + side * dt * 5, 0.9f, out _);
                            }
                            if (!foe.stationary && foe.clever && foe.caste == MotileCaste.Breacher && !body.Dry(self) && (foe.burrowing >= 5 || math.distance(self, foe.position) > 15 && math.distance(self, foe.position) < 75))
                            {
                                foe.burrowing += dt;
                                if (foe.burrowing > 5 && foe.burrowing <= 5 + dt)
                                {
                                    game.Notify("SUBSURFACE MOVEMENT · KEEP MOVING");
                                    foe.burrowOrigin = foe.position;
                                    foe.burrowTarget = self - player.walker.forward * 7;
                                    game.Dig(foe.position, 1.5f);
                                }
                                if (foe.burrowing >= 5)
                                {
                                    fired = false;
                                    float progress = math.saturate((foe.burrowing - 5) / 2);
                                    float3 under = math.normalizesafe(math.lerp(foe.burrowOrigin, foe.burrowTarget, progress));
                                    foe.position = under * (body.GroundRadius(under) + 0.9f - 3 * math.sin(progress * math.PI));
                                }
                                if (foe.burrowing > 7)
                                {
                                    float3 emerge = math.normalizesafe(foe.burrowTarget);
                                    foe.position = emerge * (body.GroundRadius(emerge) + 0.9f);
                                    game.Dig(foe.position, 2); game.Effects.Hit(foe.position, emerge, 3);
                                    foe.burrowing = -12;
                                }
                            }
                        }
                        if (fired && hunting && game.Outpost.Visible(foe.position, quarry))
                        {
                            if (foe.kind == FoeKind.Raider && !front.mindDead)
                            {
                                var copied = Combat.CapturedWeapon(player.kit, foe.seed);
                                shot.velocity = math.normalizesafe(shot.velocity) * math.max(420, copied.speed);
                                shot.homing = copied.homing;
                                shot.blast = copied.blast;
                                shot.damage = math.max(shot.damage, copied.damage * 0.12f);
                                shot.life = math.max(3, copied.life);
                            }
                            foe.shield = 0; // A personal field must drop before its owner can fire out.
                            foe.attack = 1;
                            shot.damage *= game.Pressure;
                            Bolts.Add(shot);
                            if (foe.kind != FoeKind.Sentry || foe.stationary || foe.caste != MotileCaste.Stalker)
                                game.Sounds.Shot(foe.kind == FoeKind.Raider ? 6 : foe.kind == FoeKind.Base ? 8 : 7,
                                0.45f / (1 + math.lengthsq(foe.position - self) / 2500));
                        }
                    }
                    if (foe.patrol && !hunting && !foe.scattered)
                    {
                        float3 roaming = math.normalizesafe(Walker.Project(foe.forward, math.normalizesafe(foe.position)));
                        foe.position += roaming * dt * 2;
                        float3 over = math.normalizesafe(foe.position);
                        foe.position = over * (body.GroundRadius(over) + 0.9f);
                    }
                    if (foe.kind == FoeKind.Sentry && !game.Hermit.Aboard && system.Reference == SolarSystemDescriptor.Home)
                    {
                        float3 away = Walker.Project(foe.position - game.Hermit.Position, up);
                        float gap = math.length(away);
                        if (gap < 1.8f && gap > 0.01f) foe.position += away * ((1.8f - gap) / gap);
                    }
                    if (foe.kind == FoeKind.Sentry && !foe.stationary)
                    {
                        float travel = math.length(Walker.Project(foe.position - before, math.normalizesafe(before)));
                        foe.pace = math.lerp(foe.pace, math.saturate(travel / math.max(0.001f, dt * 2)), math.min(1, dt * 14));
                        foe.stride = math.frac(foe.stride + math.min(travel / 0.94f, dt * 4.5f));
                        float reach = Combat.SentryReach(foe.caste);
                        float coil = hunting && !foe.scattered && math.distancesq(foe.position, quarry) < (reach + 2) * (reach + 2)
                            ? math.saturate(1 - math.max(0, foe.cooldown) / 0.4f) : 0;
                        float previous = foe.windup;
                        foe.windup = math.lerp(previous, coil, math.min(1, dt * 16));
                        if (previous < 0.28f && foe.windup >= 0.28f)
                            game.Sounds.Encounter(foe.caste == MotileCaste.Breacher ? EncounterCue.Breacher :
                                foe.caste == MotileCaste.Spitter ? EncounterCue.Spitter : EncounterCue.Threat, foe.position);
                    }
                    Foes[i] = foe;
                }
                for (int i = Bolts.Count - 1; i >= 0; i--)
                {
                    var bolt = Bolts[i];
                    bool clear = game.Outpost.Visible(bolt.position, bolt.position + bolt.velocity * dt) && !game.Outpost.Shield.Blocks(bolt.position, bolt.velocity * dt, out _);
                    if (clear && system.Bodies[system.Reference].TryGetComponent<Creatures>(out var animals) && animals.Strike(bolt.position, bolt.velocity * dt)) { Bolts.RemoveAt(i); continue; }
                    if (clear && system.Bodies[system.Reference].TryGetComponent<Scatter>(out var trees) && trees.Strike(bolt.position, bolt.position + bolt.velocity * dt, bolt.damage, out float3 wood))
                    { game.Dig(wood, math.max(0.5f, bolt.blast)); game.Effects.Hit(wood, up, 1); Bolts.RemoveAt(i); continue; }
                    if (game.Outpost.Visible(bolt.position, bolt.position + bolt.velocity * dt)) continue;
                    float near = 0, far = dt;
                    for (int j = 0; j < 12; j++)
                    {
                        float mid = (near + far) * 0.5f;
                        if (game.Outpost.Visible(bolt.position, bolt.position + bolt.velocity * mid)) near = mid; else far = mid;
                    }
                    // A cannon round can cross the turret and its backing wall in the same physics step.
                    if (!bolt.hostile && bolt.homing == 0 && bolt.ejectTime <= 0 && Foes.Any(foe => !foe.friendly && foe.health > 0
                        && !(bolt.robot && foe.kind == FoeKind.Mind) && Combat.Crosses(bolt.position, bolt.velocity * near - foe.velocity * dt, foe.position, foe.size)))
                    {
                        bolt.velocity *= near / dt;
                        Bolts[i] = bolt;
                        continue;
                    }
                    if (game.Outpost.Shield.Blocks(bolt.position, bolt.velocity * far, out _)) game.Outpost.Flash();
                    else
                    {
                        float3 at = bolt.position + bolt.velocity * far;
                        game.Outpost.Breach(at, bolt.damage);
                        game.Effects.Hit(at, -math.normalizesafe(bolt.velocity), math.max(1, bolt.blast), float3.zero);
                    }
                    Bolts.RemoveAt(i);
                }
                hurt += Combat.Step(Bolts, Foes, body, self, player.Velocity, flying ? Combat.ShipRadius : Combat.PersonRadius, dt, struck, game.Outpost.Shield, game.Outpost.Visible, game.Challenge);
            }
            if (hurt > 0f) player.Hurt(hurt, !flying ? $"Gunned down by Skimmers on {body.name}" : there ? "Shot to pieces by the Presence citadel" : $"Shot down over {body.name}");

            // every strike shows: sparks and a chunk off whatever hull it hit, a fireball or a cloud of
            // steam where something big came down, a flash where anything else did, and a crater
            var effects = game.Effects;
            foreach (var strike in struck)
            {
                if (strike.shield) { game.Outpost.Flash(); continue; }
                game.Corpses.Strike(strike.at, float3.zero, strike.blast * 20, math.max(0.7f, strike.blast));
                float3 where = math.normalizesafe(strike.at, up);
                // a ship's guns, its own or a raider's, tear far bigger holes than anything carried
                if (strike.blast > 0 && system.Bodies[system.Reference].TryGetComponent<Creatures>(out var animals)) animals.Strike(strike.at, float3.zero, strike.blast);
                float hole = strike.blast * (player.flying ? 2.5f : 1f);
                bool sea = strike.ground && body.SeaRadius > 0f && body.GroundRadius(where) < body.SeaRadius;
                // a nuclear blast spares nothing near it, whoever fired it
                if (!strike.robot && !strike.hostile)
                    for (int i = 0; i < Foes.Count; i++)
                        if (i == strike.foe || strike.blast > 0 && math.distance(Foes[i].position, strike.at) < strike.blast && game.Outpost.Visible(strike.at, Foes[i].position)) playerHits.Add(Foes[i].seed);
                if (strike.blast >= Combat.Nuclear)
                {
                    effects.Mushroom(strike.at, where, 1.6f * strike.blast);
                    game.Sounds.Nuke();
                    if (strike.hostile) player.Hurt(game.Outpost.Shield.Blocks(strike.at, self - strike.at, out _) ? 0 : Combat.NuclearDamage(body, strike.at, self, strike.blast), "Caught in the blast of a nuclear torpedo");
                }
                else if (strike.foe >= 0)
                {
                    var foe = Foes[strike.foe];
                    if (foe.kind == FoeKind.Sentry && !foe.stationary) game.Sounds.Encounter(EncounterCue.ShellHit, strike.at);
                    effects.Flesh(strike.at, math.normalizesafe(strike.at - foe.position, -where), math.min(foe.size, 4), foe.velocity);
                }
                else if (sea && strike.blast >= 1.2f) effects.Steam(strike.at, where, hole);
                else if (strike.ground && strike.blast >= 5f) effects.Explode(strike.at, float3.zero, 0.8f * strike.blast);
                else if (strike.blast > 0.21f || !weapon.beam && !weapon.sweep || Time.time >= nextScorch)
                {
                    effects.Impact(strike.at, where, math.max(flying && weapon.beam ? 3.5f : 1, strike.blast), weapon.beam && !strike.hostile && !strike.robot);
                    nextScorch = Time.time + 0.12f;
                }
                if (!strike.ground) continue;
                game.Sounds.Thud(math.saturate(strike.blast / 6f) * math.saturate(1.2f - math.distance(strike.at, self) / 400f));
                if (strike.level) game.Level(strike.at);
                else if (strike.blast >= 1.2f || Time.time >= nextCarve)
                {
                    game.Dig(strike.at, math.max(flying && weapon.beam ? 4.5f : 1.4f, hole));
                    if (strike.blast < 1.2f) nextCarve = Time.time + 0.12f;
                }
            }
            struck.Clear();

            for (int i = Foes.Count - 1; i >= 0; i--)
            {
                var foe = Foes[i];
                float away = math.distance(foe.position, self);
                if (foe.health <= 0f)
                {
                    var corpse = Models.Wreck(foe.stationary ? "Spine turret" : foe.kind == FoeKind.Raider ? "Harrower" : foe.kind == FoeKind.Sentry ? foe.caste.ToString() : foe.kind == FoeKind.Mind ? "Mind" : "Skimmer");
                    game.Corpses.Add(corpse, foe.kind == FoeKind.Raider ? raiderHull : broodShell, foe.position, Quaternion.LookRotation(foe.forward, up), foe.velocity, foe.kind == FoeKind.Raider ? 0.8f : foe.kind == FoeKind.Mind ? foe.size * 0.2f : 0.25f, foe.kind == FoeKind.Mind ? foe.size / 2 : 1, foe.kind == FoeKind.Mind, math.min(9, foe.size * 1.4f));
                    if (foe.stationary) body.foundations.RemoveAll(foundation => math.distance(foundation.xyz, foe.position) < 5);
                    bool credited = playerHits.Remove(foe.seed);
                    var front = game.Frontier.State(system.Reference, game.Frontier.Region(system.Reference, foe.position));
                    if (credited) War.Kill(front, foe.kind == FoeKind.Raider);
                    else if (foe.kind == FoeKind.Raider) front.alienShips = math.max(0, front.alienShips - 1);
                    else front.aliens = math.max(0, front.aliens - 1);
                    if (credited && weapon.rail) { player.health = math.min(player.MaxHealth, player.health + 8); player.hull = math.min(player.MaxHull, player.hull + 8); }
                    if (foe.kind == FoeKind.Sentry && !foe.stationary) game.Sounds.Encounter(EncounterCue.Collapse, foe.position);
                    else game.Sounds.Boom(math.saturate(1f - away / 2500f), foe.kind != FoeKind.Drone);
                    game.Award(foe.kind, credited);
                }
                else if (foe.outerGuard || foe.guardian || foe.kind == FoeKind.Mind || foe.kind == FoeKind.Sentry || (foe.kind == FoeKind.Base ? there : !(foe.kind == FoeKind.Raider ? away > 6000f || (ashore > 25f && !player.Has(Gear.BackMissiles)) : away > 350f))) continue;
                Foes.RemoveAt(i);
            }

        }

        // Decides when the next foes arrive.
        void Ambush(float frame, bool aloft, float3 self, float3 up, bool atBase)
        {
            var player = game.Player;
            var system = game.System;
            var body = system.ReferenceBody;
            if (game.Stage <= 2 || game.Frontier.Local.alienShips <= 0) return;
            int raiders = 0, drones = 0;
            foreach (var foe in Foes)
                if (foe.kind == FoeKind.Raider) raiders++;
                else if (foe.kind == FoeKind.Drone) drones++;
            // foes come sooner the further the game has gone
            float keen = (1f + 0.15f * (game.Threat - 1)) * game.Pressure;

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
                float3 ahead = view.transform.forward;
                for (int i = 0, wing = math.min(1 + game.Depth / 2, 4); i < wing; i++)
                {
                    float3 from = self + math.normalize(-ahead + rnd.NextFloat3Direction() * 0.46f) * rnd.NextFloat(1100f, 1500f);
                    // never out of the ground
                    float3 over = math.normalize(from);
                    Add(FoeKind.Raider, over * math.max(math.length(from), body.kind == BodyKind.Rock ? body.radius * Shapes.AirTop(body) + 75 : Ship.Surface(body, over) + 150f));
                }
                game.Notify("HARROWERS INBOUND");
                game.Hermit.Remark(game.Wording("incoming"));
                game.Sounds.Alarm();
            }

            // drones find someone on foot, though never at the hermit's fire
            bool exposed = game.Frontier.Local.aliens > 0 && !player.flying && player.Has(Gear.Suit) && body.kind == BodyKind.Rock && !player.walker.swimming
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
            int shieldCount = 0;
            emplacements.Clear(); racks.Clear(); acid.Clear(); droplets.Clear();
            foreach (var list in new[] { raiders, drones, flashes, missiles, minds, bolts[0], bolts[1] }) list.Clear();
            foreach (var list in sentries) list.Clear();
            foreach (var list in legs) list.Clear();
            float focalPixels = view.scaledPixelHeight * view.projectionMatrix.m11 * 0.5f;
            float3 eye = (float3)view.transform.position + anchor;
            for (int index = 0; index < Foes.Count; index++)
            {
                var foe = Foes[index];
                if (game.Player.InSpace && foe.kind != FoeKind.Raider && foe.kind != FoeKind.Base && game.System.ReferenceBody.kind != BodyKind.Station) continue;
                float3 up = game.System.ReferenceBody.kind == BodyKind.Station ? new float3(0, 1, 0) : math.normalizesafe(foe.position, new float3(0, 1, 0));
                float3 shown = foe.position - anchor - up * (foe.stationary ? 0 : math.smoothstep(0, 1, foe.emergence) * (foe.kind == FoeKind.Drone ? 4f : foe.size + 2));
                if (foe.shield > 0)
                {
                    if (field == null) field = new Material(Resources.Load<Shader>("Shield"));
                    if (shieldCount == shields.Count) shields.Add(Shapes.Part(transform, "Personal field", ball, field, Vector3.zero, Vector3.one, false).transform);
                    var shell = shields[shieldCount++]; shell.gameObject.SetActive(true); shell.position = shown; shell.localScale = Vector3.one * (foe.size * 2.5f);
                }
                // the more it has been hit, the more of it is holes
                float battered = 1f - foe.health / math.max(1, foe.maximumHealth);
                if (foe.stationary)
                {
                    var baseRotation = Quaternion.FromToRotation(Vector3.up, up);
                    emplacements.Add(Matrix4x4.TRS(shown - up, baseRotation, Vector3.one), battered, foe.seed);
                    Vector3 rackUp = math.abs(math.dot(foe.forward, up)) < 0.999f ? (Vector3)up : baseRotation * Vector3.forward;
                    racks.Add(Matrix4x4.TRS(shown, Quaternion.LookRotation(foe.forward, rackUp), Vector3.one), battered, foe.seed);
                }
                else if (foe.kind == FoeKind.Raider) raiders.Add(Matrix4x4.TRS(shown, Quaternion.LookRotation(foe.forward, up), Vector3.one), battered, foe.seed);
                // a drone bobs as it hovers
                else if (foe.kind == FoeKind.Drone)
                    drones.Add(Matrix4x4.TRS(shown + up * (0.12f * math.sin(beat * 3f + foe.seed % 7u)), Quaternion.LookRotation(foe.forward, up), Vector3.one), battered, foe.seed);
                else if (foe.kind == FoeKind.Mind)
                    minds.Add(Matrix4x4.TRS(shown, Quaternion.LookRotation(foe.forward, up), Vector3.one * (foe.size / 2)), battered, foe.seed);
                else if (foe.kind == FoeKind.Sentry)
                {
                    Quaternion facing = Quaternion.LookRotation(math.normalizesafe(Walker.Project(foe.forward, up), new float3(0, 0, 1)), up);
                    float range = math.distance(foe.position, eye);
                    float pixels = 2 * foe.size * focalPixels / math.max(1, range);
                    int detail = LodSettings.SentryDetail(pixels, foe.detail);
                    if (foe.detail != detail) { foe.detail = detail; Foes[index] = foe; }
                    int batch = detail * 3 + (int)foe.caste;
                    float cycle = foe.stride + (foe.seed % 37u) / 37f;
                    Vector3 sway = new Vector3(0, -foe.windup * 0.12f + math.sin(cycle * math.PI * 4) * 0.035f * foe.pace, foe.attack * 0.18f - foe.windup * 0.08f);
                    sentries[batch].Add(Matrix4x4.TRS(shown + (float3)(facing * sway),
                        facing * Quaternion.Euler(foe.windup * 12 - foe.attack * 18 + foe.recoil * 24, 0, math.sin(foe.seed) * foe.recoil * 18),
                        new Vector3(1 + foe.recoil * 0.08f, 1 - foe.recoil * 0.12f, 1)), battered, foe.seed);
                    for (int i = 0; i < Models.LimbCount(foe.caste); i++)
                    {
                        int pairs = Models.LimbCount(foe.caste) / 2;
                        bool left = i < pairs;
                        Vector3 attachment = Models.LimbRoot(foe.caste, i);
                        float2 foot = GroundStride.Foot(cycle + ((i % pairs + (left ? 0 : 1)) % 2) * 0.5f) * foe.pace;
                        legs[batch].Add(Matrix4x4.TRS(shown + (float3)(facing * attachment),
                            facing * Quaternion.Euler(-foot.x * 34, 0, (left ? -1 : 1) * foot.y * 11) * Quaternion.Euler(0, left ? 180 : 0, 0), Vector3.one), battered, foe.seed);
                    }
                }
            }
            for (int i = shieldCount; i < shields.Count; i++) shields[i].gameObject.SetActive(false);
            foreach (var bolt in Bolts)
            {
                var facing = Quaternion.LookRotation(bolt.velocity);
                if (bolt.spit)
                {
                    acid.Add(Matrix4x4.TRS(bolt.position - anchor, facing, new Vector3(0.52f, 0.48f, 0.75f)));
                    for (int i = 0; i < 4; i++)
                    {
                        float twist = bolt.life * 11 + i * 2.4f;
                        var offset = new Vector3(math.cos(twist) * 0.16f, math.sin(twist) * 0.16f, -0.20f * i);
                        droplets.Add(Matrix4x4.TRS(bolt.position - anchor + (float3)(facing * offset), facing, Vector3.one * (0.31f - 0.055f * i)));
                    }
                    continue;
                }
                if (bolt.homing > 0f)
                {
                    // a missile is a thing, with its motor burning behind it
                    float size = math.lengthsq(bolt.cruiseVelocity) > 0 ? 0.32f : 2.5f;
                    missiles.Add(Matrix4x4.TRS(bolt.position - anchor, facing, Vector3.one * size));
                    flashes.Add(Matrix4x4.TRS(bolt.position - anchor - (float3)(facing * Vector3.forward * size), facing, new Vector3(0.28f, 0.28f, 1.2f) * size));
                    continue;
                }
                // fast bolts are streaks, slow ones are balls of fire you can step aside from
                float speed = math.length(bolt.velocity), width = speed < 100f ? 0.4f : speed < 300f ? 0.12f : speed > 2000f ? 0.7f : 0.4f;
                bolts[bolt.hostile ? 1 : 0].Add(Matrix4x4.TRS(bolt.position - anchor, facing, new Vector3(width, width, math.clamp(speed * 0.02f, 0.5f, 60f))));
            }
            var bounds = new Bounds(Vector3.zero, Vector3.one * 20000f);
            raiders.Draw(raider, raiderHull, true, bounds);
            raiders.Draw(raiderLights, raiderGlow, false, bounds);
            drones.Draw(drone, broodShell, true, bounds);
            drones.Draw(droneEye, broodOptics, false, bounds);
            for (int i = 0; i < sentries.Length; i++)
            {
                sentries[i].Draw(sentry[i], broodShell, true, bounds);
                sentries[i].Draw(sentryEye[i], broodOptics, false, bounds);
                legs[i].Draw(leg[i], broodShell, true, bounds);
            }
            minds.Draw(mind, MindTissue, true, bounds);
            minds.Draw(mindLights, broodOptics, false, bounds);
            missiles.Draw(missile, casing, false, bounds);
            acid.Draw(ball, membrane, false, bounds); droplets.Draw(ball, bile, false, bounds);
            bolts[0].Draw(ball, ours, false, bounds);
            bolts[1].Draw(ball, theirs, false, bounds);
            emplacements.Draw(emplacement[0], broodShell, true, bounds);
            racks.Draw(emplacement[1], broodShell, true, bounds);
            racks.Draw(emplacement[2], broodOptics, false, bounds);
            flashes.Draw(ball, fire, false, bounds);
        }
    }
}
