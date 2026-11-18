using System;
using System.Linq;
using System.Collections.Generic;
using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;

namespace Infinigame
{
    // A controlling mind behind a shield. The rooms use their own local frame, independent of terrain.
    public class Outpost : MonoBehaviour
    {
        [NonSerialized] public Game game;
        public int World { get; private set; } = -1;
        public float3 Position { get; private set; }
        public float3 Prize { get; private set; }
        public int Left { get; private set; }
        Ruin recovered;
        public bool Recovered => recovered != null && recovered.stage == game.Stage && recovered.detonates > 0;
        public bool SiteCleared => Recovered && recovered.destroyed;
        public float3 Exit => Place(new float3(0, 1, -34));
        public int OuterLeft => game.Battle.Foes.Count(foe => foe.outerGuard && foe.health > 0);
        public float Demolition => (float)game.Ruins.Where(ruin => ruin.detonates > 0 && !ruin.destroyed).Select(ruin => math.max(0, ruin.detonates - game.System.Clock)).DefaultIfEmpty(0).Min();
        bool MissionFactory => game.Frontier.State(World, World == SolarSystemDescriptor.Home ? stage - Campaign.FirstBurrow : 0).factory;
        public bool Fallen => World >= 0 && Left == 0;
        public bool Near => Fallen && !Recovered && game.Stage != Campaign.Citadel && World == game.System.Reference && !game.Player.flying && math.distance(game.Player.Position, Prize) < 6f;
        public string Errand => SiteCleared ? "BUILD A ROBODOG FACTORY IN THIS REGION" : Recovered ? "ESCAPE THE BURROW" : game.Stage == Campaign.Citadel ? "ESCAPE THROUGH THE TUNNEL ENTRANCE" : $"TAKE {Campaign.Spoils(game.Stage)}";
        public bool Entering => stage == Campaign.FirstBurrow && !Rooms.walls.Last().broken
            || !new Burrow { origin = Position, rotation = rotation }.Contains(game.Player.Position) || Local(game.Player.Position).z < -20;
        public string Instruction => Fallen ? Errand : stage == Campaign.Citadel ? game.Mission.task : OuterLeft > 0
            ? $"CLEAR THE LANDING ZONE · TURRETS: {game.Battle.Foes.Count(foe => foe.outerGuard && foe.stationary && foe.health > 0)} · SHIPS: {game.Battle.Foes.Count(foe => foe.outerGuard && !foe.stationary && foe.health > 0)}"
            : Entering ? "ENTER THE MARKED BURROW · DIG THROUGH BLOCKAGES"
            : $"FOLLOW THE TUNNEL · DESTROY {Left} {(Left == 1 ? "MIND" : "MINDS")}";
        public float3 Waypoint => OuterLeft > 0 ? game.Battle.Foes.Where(foe => foe.outerGuard && foe.health > 0).OrderBy(foe => math.distancesq(foe.position, game.Player.Position)).First().position
            : Entering ? Place(Rooms.entrance)
            : game.Battle.Foes.Where(foe => foe.kind == FoeKind.Mind && foe.health > 0).OrderBy(foe => math.distancesq(foe.position, game.Player.Position)).Select(foe => foe.position).DefaultIfEmpty(Core).First();
        public Interior Rooms { get; private set; }
        public bool Open { get; private set; }
        public Shield Shield => new Shield { centre = game.Stage == Campaign.Citadel && Rooms != null ? Core : Position, radius = game.Stage == Campaign.Citadel ? 22 : 45, active = World == game.System.Reference && !Recovered && (game.Stage == Campaign.Citadel ? !Fallen && (Left > 1 || game.Battle.Foes.Any(foe => foe.stationary && foe.health > 0)) : stage >= Campaign.Tower && stage < Campaign.Citadel) };
        public bool LandingLocked => World == game.System.Reference && stage >= Campaign.Tower && stage < Campaign.Citadel && !Recovered && (!spawned || OuterLeft > 0);

        public bool Conceals(float3 observer, float3 contact)
        {
            var shield = Shield;
            if (shield.active && math.distancesq(contact, shield.centre) < shield.radius * shield.radius &&
                math.distancesq(observer, shield.centre) >= shield.radius * shield.radius) return true;
            foreach (var burrow in game.System.ReferenceBody.burrows)
                if (burrow.InPassage(contact) && !burrow.InPassage(observer)) return true;
            return false;
        }

        public bool AtConsole => false;
        public float Escape { get; private set; }
        public float3 Core => Place(Rooms.core);
        sealed class Kept
        {
            public int stage, world;
            public float3 position;
            public quaternion rotation;
            public Interior rooms;
            public Ruin ruin;
            public Transform root;
            public List<Transform> walls;
        }
        readonly List<Kept> kept = new List<Kept>();
        SolarSystemDescriptor system;
        int stage = -1;
        quaternion rotation;
        Transform fort, crate, door, shell, prizeShield;
        Material shimmer;
        bool spawned, explainedShield;
        readonly List<Foe> waiting = new List<Foe>();
        float flash;
        readonly List<Transform> walls = new List<Transform>();
        public float3 Local(float3 point) => math.rotate(math.inverse(rotation), point - Position);
        public float3 Place(float3 point) => Position + math.rotate(rotation, point);

        public void Settle()
        {
            if (system != game.System.Descriptor)
            {
                if (fort != null) Destroy(fort.gameObject);
                foreach (var site in kept) if (site.root != null) Destroy(site.root.gameObject);
                kept.Clear();
                fort = null;
                system = game.System.Descriptor;
                for (int completed = 2; completed < math.min(game.Stage, Campaign.Citadel); completed++)
                {
                    if (completed == Campaign.Recovery) continue;
                    Build(completed);
                    Keep();
                }
            }
            else if (fort != null && stage < game.Stage) Keep();
            else if (fort != null) Destroy(fort.gameObject);
            recovered = null;
            spawned = Open = explainedShield = false;
            waiting.Clear();
            Left = Campaign.Minds(game.Stage);
            Escape = 0;
            walls.Clear();
            Rooms = null;
            World = -1;
            if (game.Stage >= Campaign.FirstBurrow && game.Stage <= Campaign.Citadel && game.Stage != Campaign.Recovery)
            {
                Build(game.Stage);
                recovered = game.Ruins.Find(item => item.stage == stage && item.detonates > 0);
                if (Recovered) { Left = 0; Open = true; Keep(); }
            }
        }

        void Keep()
        {
            if (stage == Campaign.Citadel) { Destroy(fort.gameObject); fort = null; return; }
            var ruin = game.Ruins.FirstOrDefault(item => item.stage == stage);
            if (ruin?.destroyed == true)
            {
                game.System.Descriptor.bodies[World].burrows.RemoveAll(burrow => math.distance(burrow.origin, Position) < 1);
                game.System.Descriptor.bodies[World].foundations.RemoveAll(item => math.distance(item.xyz, Place(Rooms.core)) < 80);
                game.System.Bodies[World].Dig(Place(Rooms.core), 30);
                Destroy(fort.gameObject); fort = null; return;
            }
            shell.gameObject.SetActive(false);
            crate.gameObject.SetActive(false);
            if (door != null) door.gameObject.SetActive(false);
            kept.Add(new Kept { stage = stage, world = World, position = Position, rotation = rotation, rooms = Rooms, ruin = ruin, root = fort, walls = new List<Transform>(walls) });
            fort = shell = crate = door = null;
        }

        void Build(int mission)
        {
            Open = false;
            stage = mission;
            World = Campaign.World(system, stage);
            walls.Clear();
            bool station = stage == Campaign.Citadel;
            var body = game.System.Descriptor.bodies[World];
            float3 site = Campaign.Site(game.System.Descriptor, stage);
            Position = station ? float3.zero : site * body.GroundRadius(site);
            rotation = station ? quaternion.identity : quaternion.LookRotationSafe(Walker.Project(new float3(0.2f, 0.9f, 0.4f), site), site);

            Rooms = station ? Interior.Citadel() : Interior.Burrow(stage == Campaign.FirstBurrow ? 0 : stage == Campaign.SecondBurrow ? 1 : 2);
            if (stage == 2)
                Rooms.walls = Rooms.walls.Concat(new[] { new Wall(new float3(0, 1.5f, -23), new float3(5, 3, 1)) { damage = 192 } }).ToArray();
            if (!station) game.System.Bodies[World].SealBurrow(new Burrow { origin = Position, rotation = rotation });
            if (!station)
                for (int i = 0; i < 16; i++)
                {
                    float z = -27 + i * 4;
                    game.System.Bodies[World].Tunnel(Place(new float3(0, Interior.TunnelFloor(z), z)), 7);
                }
            if (!station)
                for (int i = 0; i < Rooms.walls.Length; i++)
                {
                    var wall = Rooms.walls[i];
                    if (wall.centre.y - wall.half.y > Interior.TunnelFloor(wall.centre.z) + 1) continue;
                    for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++)
                    {
                        float3 up = math.normalizesafe(Place(wall.centre + new float3(x * wall.half.x, 0, z * wall.half.z)));
                        Rooms.SinkWall(i, Local(up * body.GroundRadius(up)).y - 4);
                    }
                }
            var damage = game.Ruins.FirstOrDefault(ruin => ruin.stage == stage);
            if (damage != null)
            {
                Open = damage.open;
                foreach (int index in damage.broken)
                    if (index >= 0 && index < Rooms.walls.Length) { var wall = Rooms.walls[index]; wall.broken = true; Rooms.walls[index] = wall; }
            }
            fort = new GameObject("Mind burrow").transform;
            fort.SetParent(game.System.Bodies[World].transform, false);
            fort.localPosition = Position;
            fort.localRotation = rotation;
            var lit = Shapes.Material("Glow", new Color(0.25f, 0.9f, 1.1f));
            foreach (var wall in Rooms.walls)
            {
                var part = Shapes.Part(fort, station ? "Living citadel wall" : "Grown tunnel rock", Shapes.Rock(6), Shapes.Solid(Surface.Chitin, new Color(0.48f, 0.19f, 0.4f), true), Vector3.zero, wall.half * 2.8f).transform;
                part.SetParent(fort, false);
                part.localPosition = wall.centre;
                part.gameObject.SetActive(!wall.broken && !(wall.door && (Open || stage < game.Stage)));
                walls.Add(part);
                if (wall.door) door = part;
                if (!station && wall.half.x < 3 && !wall.broken)
                {
                    float3 foot = Place(wall.centre - new float3(0, wall.half.y, 0));
                    if (!body.foundations.Any(item => math.distance(item.xyz, foot) < 1)) body.foundations.Add(new float4(foot, 1.5f));
                }
            }

            if (!station)
                for (int z = -15; z <= 33; z += 6)
                {
                    float3 foot = Place(new float3(0, Interior.TunnelFloor(z), z));
                    if (!body.foundations.Any(item => math.distance(item.xyz, foot) < 1)) body.foundations.Add(new float4(foot, 4));
                }
            for (int i = 0; i < (station ? Rooms.route.Length : 4); i++)
            {
                var lamp = new GameObject("Room light").AddComponent<Light>();
                lamp.transform.SetParent(fort, false);
                lamp.transform.localPosition = station ? (Vector3)Rooms.route[i] + Vector3.up * 14 : new Vector3(0, Interior.TunnelFloor(-12 + i * 14) + 4, -12 + i * 14);
                lamp.color = new Color(0.65f, 0.2f, 0.55f);
                lamp.range = station ? 70 : 18;
                lamp.intensity = station ? 0.65f : 0.8f;
            }
            shimmer = new Material(Resources.Load<Shader>("Shield"));
            shell = Shapes.Part(fort, "Shield", Shapes.Sphere(station ? 22 : 45, 24), shimmer, station ? (Vector3)Rooms.core : Vector3.zero, Vector3.one, false).transform;
            Prize = Place(station ? Rooms.entrance : Rooms.core + new float3(0, 0, 4));
            if (!station) { float3 up = math.normalizesafe(Prize); Prize = up * (body.GroundRadius(up) + 0.03f); }
            crate = Models.Equipment(stage == 2); crate.name = "Prize"; crate.SetParent(fort, false); crate.localPosition = Local(Prize);
            prizeShield = Shapes.Part(crate, "Locked equipment field", Shapes.Sphere(1.7f, 32), shimmer, Vector3.up * 0.8f, new Vector3(1, 0.8f, 1), false).transform;
            {
                var tissue = Shapes.Solid(Surface.Chitin, new Color(0.6f, 0.08f, 0.37f), true);
                for (int i = 0; i < (station ? Rooms.route.Length : 12); i++)
                {
                    float z = -20 + i * 4.5f;
                    var rib = Shapes.Part(fort, "Living tunnel tissue", Shapes.Sphere(1, 24), tissue,
                        station ? (Vector3)Rooms.route[i] + new Vector3(i % 2 == 0 ? -23 : 23, 0, 0) : new Vector3(i % 2 == 0 ? -4.2f : 4.2f, Interior.TunnelFloor(z) + 2.5f, z), station ? new Vector3(3, 25, 5) : new Vector3(0.7f, 2.8f, 1.1f)).transform;
                    rib.gameObject.AddComponent<LivingTissue>();
                }
            }
        }

        public void Leave(int world)
        {
            if (world != World) return;
            waiting.Clear();
            waiting.AddRange(game.Battle.Foes.Where(foe => foe.health > 0 && (foe.kind == FoeKind.Mind || foe.kind == FoeKind.Sentry || foe.outerGuard)));
            spawned = false;
        }
        public void Flash() => flash = 1f;
        public void Breach(float3 point, float damage)
        {
            void Hit(Interior rooms, float3 local, int mission, List<Transform> parts)
            {
                if (!rooms.Breach(local, damage)) return;
                var saved = game.Ruins.FirstOrDefault(ruin => ruin.stage == mission);
                if (saved == null) { saved = new Ruin { stage = mission }; game.Ruins.Add(saved); }
                saved.broken = Enumerable.Range(0, rooms.walls.Length).Where(i => rooms.walls[i].broken).ToList();
                foreach (int i in saved.broken) parts[i].gameObject.SetActive(false);
                game.Effects.Hit(point, math.normalizesafe(point), 2, float3.zero);
                game.Write();
            }
            if (fort != null && Rooms != null && World == game.System.Reference)
            {
                Hit(Rooms, Local(point), stage, walls);
                Open |= Rooms.walls.Any(wall => wall.door && wall.broken);
            }
            foreach (var site in kept)
                if (site.world == game.System.Reference) Hit(site.rooms, math.rotate(math.inverse(site.rotation), point - site.position), site.stage, site.walls);
        }
        public bool Use() => false;
        public float3 Resolve(float3 from, float3 to, float radius, out bool hit)
        {
            hit = false;
            if (fort != null && Rooms != null && World == game.System.Reference)
                to = Place(Rooms.Move(Local(from), math.rotate(math.inverse(rotation), to - from), radius, 1, Open, out hit, radius < 2 ? 0.4f : 0));
            foreach (var site in kept)
            {
                if (site.world != game.System.Reference) continue;
                var turn = math.inverse(site.rotation);
                float3 moved = site.rooms.Move(math.rotate(turn, from - site.position), math.rotate(turn, to - from), radius, 1, true, out bool blocked, radius < 2 ? 0.4f : 0);
                to = site.position + math.rotate(site.rotation, moved);
                hit |= blocked;
            }
            return to;
        }
        public bool Visible(float3 from, float3 to)
        {
            if (fort != null && Rooms != null && World == game.System.Reference && !Rooms.Visible(Local(from), Local(to), Open)) return false;
            foreach (var site in kept)
                if (site.world == game.System.Reference && !site.rooms.Visible(math.rotate(math.inverse(site.rotation), from - site.position), math.rotate(math.inverse(site.rotation), to - site.position), true)) return false;
            return true;
        }
        void Update()
        {
            if (shell == null) return;
            shell.gameObject.SetActive(Shield.active);
            if (prizeShield != null) prizeShield.gameObject.SetActive(!Fallen);
            flash = Mathf.MoveTowards(flash, 0, Time.deltaTime * 2);
            shimmer.SetFloat("_Hit", flash);
            for (int i = 0; i < walls.Count; i++) walls[i].gameObject.SetActive(!Rooms.walls[i].broken && !(Rooms.walls[i].door && Open));
        }
        public void ArmDemolition()
        {
            if (World < 0 || Recovered || stage == Campaign.Citadel) return;
            var ruin = game.Ruins.FirstOrDefault(item => item.stage == stage);
            if (ruin == null) { ruin = new Ruin { stage = stage }; game.Ruins.Add(ruin); }
            ruin.detonates = game.System.Clock + 20;
            recovered = ruin;
            Keep();
            game.Write();
            game.Hermit.Remark(stage == Campaign.FirstBurrow
                ? "Power core secured. Your fists, hand beam and first suit upgrade unlock after the blast. Kills and discoveries earn XP. Twenty seconds. Get out!"
                : "Recovered " + Campaign.Spoils(stage) + ". Demolition in twenty seconds. Get clear to bring the equipment online.");
        }
        public void Tick()
        {
            for (int index = kept.Count - 1; index >= 0; index--)
            {
                var site = kept[index];
                var ruin = site.ruin;
                if (ruin == null || ruin.destroyed || ruin.detonates <= 0) continue;
                double remaining = ruin.detonates - game.System.Clock;
                if (site.world == game.System.Reference && math.ceil(remaining) != math.ceil(remaining + Time.deltaTime)) game.Sounds.Alarm();
                if (remaining > 0) continue;
                float3 centre = site.position + math.rotate(site.rotation, site.rooms.core);
                ruin.destroyed = true;
                Destroy(site.root.gameObject); kept.RemoveAt(index);
                game.System.Descriptor.bodies[site.world].burrows.RemoveAll(burrow => math.distance(burrow.origin, site.position) < 1);
                game.System.Descriptor.bodies[site.world].foundations.RemoveAll(item => math.distance(item.xyz, centre) < 80);
                game.System.Bodies[site.world].Dig(centre, 30);
                if (site.world == game.System.Reference)
                {
                    game.Effects.Mushroom(centre, math.normalizesafe(centre), 40);
                    game.Sounds.Nuke();
                    game.Player.Hurt(100 * math.saturate(1 - math.distance(game.Player.Position, centre) / 30), "Inside the burrow demolition radius");
                    for (int i = 0; i < game.Battle.Foes.Count; i++)
                    {
                        var foe = game.Battle.Foes[i];
                        if (math.distance(foe.position, centre) < 35) { foe.health = 0; game.Battle.Foes[i] = foe; }
                    }
                }
                if (site.stage == game.Stage)
                {
                    game.FitRewards();
                    game.Hermit.Remark((site.stage == Campaign.FirstBurrow ? "Beam and powered fists online. Pick one improvement each time your XP reaches the next level. " : Campaign.Spoils(stage) + " online. ")
                        + (MissionFactory ? "Your factory already controls this site." : "Build a robodog factory in this region. Its patrols keep the brood from returning."));
                }
                else game.Hermit.Remark("The burrow is gone. " + game.Hermit.Briefing);
                game.Write();
            }
            if (Recovered)
            {
                if (SiteCleared && MissionFactory && !game.Dead) game.Advance();
                return;
            }
            if (World != game.System.Reference) return;
            if (Fallen)
            {
                crate.gameObject.SetActive(true);
                if (game.Stage == Campaign.Citadel)
                {
                    if (math.ceil(Escape) != math.ceil(Escape - Time.deltaTime)) game.Sounds.Alarm();
                    Escape -= Time.deltaTime;
                    if (Local(game.Player.Position).z < Rooms.entrance.z - 30)
                    {
                        game.Effects.Explode(Position, float3.zero, 200);
                        game.Advance();
                    }
                    else if (Escape <= 0) game.Player.Hurt(10000, "Caught inside the collapsing citadel");
                }
                return;
            }
            if (spawned && !explainedShield && stage >= Campaign.Tower && stage < Campaign.Citadel && OuterLeft == 0)
            {
                explainedShield = true;
                game.Hermit.Remark("The landing zone is clear. The field still repels ships and gunfire. Land outside it and walk through. Follow the descending tunnel and shoot or punch through any blockage. The violet mind is at the far end. Destroy it, then take the equipment behind it.");
                game.Notify("LAND OUTSIDE THE FIELD · ENTER ON FOOT");
            }
            for (int i = 0; i < game.Battle.Foes.Count; i++)
            {
                var foe = game.Battle.Foes[i];
                if (foe.kind == FoeKind.Mind)
                {
                    if (stage != Campaign.Citadel)
                    {
                        float3 up = math.normalizesafe(foe.position);
                        foe.position = up * (game.System.ReferenceBody.GroundRadius(up) + 1.5f);
                    }
                    foe.protectedCore = stage == Campaign.Citadel ? math.distance(foe.position, Core) < 30 && Shield.active : OuterLeft > 0; game.Battle.Foes[i] = foe;
                }
            }
            if (spawned || math.distance(game.Player.Position, Position) > (game.Stage == Campaign.Citadel ? 1800 : 600)) return;
            spawned = true;
            if (waiting.Count > 0)
            {
                game.Battle.Foes.AddRange(waiting);
                waiting.Clear();
                return;
            }
            if (stage == Campaign.Citadel)
            {
                for (int i = 0; i < 3; i++)
                {
                    float3 point = i == 2 ? Rooms.core : Rooms.route[i == 0 ? 5 : 10];
                    point.y = -16;
                    game.Battle.Add(FoeKind.Mind, Place(point));
                    var mind = game.Battle.Foes[^1]; mind.size = 6 + i * 2; mind.health = mind.maximumHealth *= 3 + i; game.Battle.Foes[^1] = mind;
                    for (int guard = 0; guard < 2; guard++)
                    {
                        game.Battle.Add(FoeKind.Sentry, Place(point + new float3(guard == 0 ? -12 : 12, -12, -12)), (MotileCaste)guard);
                        var sentry = game.Battle.Foes[^1]; sentry.guardian = true; game.Battle.Foes[^1] = sentry;
                    }
                }
                for (int i = 2; i < Rooms.route.Length - 1; i += 2)
                {
                    float3 point = Rooms.route[i] + new float3(i % 4 == 0 ? -17 : 17, -28, 0);
                    game.Battle.Add(FoeKind.Sentry, Place(point), MotileCaste.Breacher);
                    var turret = game.Battle.Foes[^1]; turret.stationary = true; game.Battle.Foes[^1] = turret;
                }
            }
            else
                for (int i = 0; i < Campaign.Minds(stage); i++)
                {
                    float z = i == Campaign.Minds(stage) - 1 ? 32 : Campaign.Minds(stage) == 2 ? 17 : i == 0 ? -2 : 20;
                    float3 at = Place(new float3(0, Interior.TunnelFloor(z) + 1, z));
                    float3 up = math.normalizesafe(at);
                    game.Battle.Add(FoeKind.Mind, up * (game.System.ReferenceBody.GroundRadius(up) + 1.5f));
                }
            if (stage >= Campaign.Tower && stage < Campaign.Citadel)
                for (int i = 0; i < Campaign.OuterDefences(stage); i++)
                {
                    float3 at = Place(new float3((i % 2 == 0 ? -1 : 1) * (18 + i * 7), i % 2 == 0 ? 100 : 1, -68));
                    float3 up = math.normalizesafe(at);
                    at = up * (game.System.ReferenceBody.GroundRadius(up) + (i % 2 == 0 ? 100 : 1));
                    game.Battle.Add(i % 2 == 0 ? FoeKind.Raider : FoeKind.Sentry, at, MotileCaste.Breacher);
                    var guard = game.Battle.Foes[^1]; guard.outerGuard = true; guard.stationary = i % 2 != 0;
                    game.Battle.Foes[^1] = guard;
                    if (guard.stationary) game.System.ReferenceBody.foundations.Add(new float4(guard.position, 4));
                }
            if (stage == Campaign.Citadel) return;
            int coreIndex = game.Battle.Foes.FindIndex(foe => foe.kind == FoeKind.Mind);
            var core = game.Battle.Foes[coreIndex]; core.protectedCore = OuterLeft > 0; game.Battle.Foes[coreIndex] = core;
            var minds = game.Battle.Foes.Where(foe => foe.kind == FoeKind.Mind).Select(foe => Local(foe.position)).ToArray();
            for (int i = 0; i < math.max(minds.Length * 2, game.Mission.count); i++)
            {
                // Two sentries watch the rooms; the remaining guards use the authored patrol kind.
                FoeKind kind = FoeKind.Sentry;
                float3 position = game.Stage == Campaign.Citadel
                    ? new float3(i < 3 ? 0 : 160, -29.1f, i < 3 ? -200 + 50 * i : 30 + 40 * (i - 3))
                    : kind == FoeKind.Raider ? new float3((i % 4 - 1.5f) * 35, 25, -90 - i / 4 * 30)
                    : kind == FoeKind.Drone ? new float3((i % 4 - 1.5f) * 12, 3, -18 - i / 4 * 8)
                    : new float3((i % 2 * 2 - 1) * 2, Interior.TunnelFloor(-16 + i * 44f / math.max(1, game.Mission.count - 1)) + 0.9f, -16 + i * 44f / math.max(1, game.Mission.count - 1));
                if (i < minds.Length * 2) position = minds[i / 2] + new float3(i % 2 == 0 ? -2.5f : 2.5f, 0.9f, -3);
                game.Battle.Add(kind, Place(position), Combat.Caste(game.Stage, i));
                var sentry = game.Battle.Foes[^1]; sentry.guardian = i < minds.Length * 2; game.Battle.Foes[^1] = sentry;
            }
        }
        public void Killed(FoeKind kind)
        {
            if (kind != FoeKind.Mind || World != game.System.Reference || Fallen) return;
            if (--Left > 0) { game.Notify($"MIND DESTROYED · {Left} MINDS REMAIN IN {(stage == Campaign.Citadel ? "THE CITADEL" : "THIS REGION")}"); return; }
            Open = true;
            Escape = 25;
            game.Frontier.State(World, World == SolarSystemDescriptor.Home ? stage - Campaign.FirstBurrow : 0).mindDead = true;
            game.Presence.Liberate(World);
            for (int i = 0; i < game.Battle.Foes.Count; i++)
            {
                var foe = game.Battle.Foes[i]; foe.scattered = true; foe.shield = 0; foe.clever = false; game.Battle.Foes[i] = foe;
            }
            game.Notify(game.Stage == Campaign.Citadel ? "CORE DESTROYED  ·  25 SECONDS TO ESCAPE" : "MIND SILENT · COORDINATION, COPIED WEAPONS AND SHIELDS OFFLINE");
            game.Hermit.Remark(game.Stage == Campaign.Citadel ? "That shield held the final mind safe. Get us out. We have twenty-five seconds." : "The mind is silent. This region has lost coordinated tactics, copied weapons and shields. " + (game.Frontier.Local.factory ? "Your factory is already built. Its robots can now rebuild losses and win this world." : game.FactoryUnlocked ? "Build a robodog factory at a clear surface site. Its robots will now control the alien population in this region." : "Take the equipment behind the mind. Escape the demolition to unlock your first factory."));
            game.Sounds.Chime();
        }
    }
}
