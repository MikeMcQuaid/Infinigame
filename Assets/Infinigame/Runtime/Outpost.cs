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
        Foe? sectionGuard;
        TextMesh chargeReadout;
        public bool Recovered => recovered != null && recovered.stage == game.Stage && recovered.detonates > 0;
        public bool SiteCleared => Recovered && recovered.destroyed;
        public float3 Exit => Place(new float3(0, 1, -34));
        public int OuterLeft => game.Battle.Foes.Count(foe => foe.outerGuard && foe.stationary && foe.health > 0);
        public float Demolition => (float)game.Ruins.Where(ruin => ruin.detonates > 0 && !ruin.destroyed).Select(ruin => math.max(0, ruin.detonates - game.System.Clock)).DefaultIfEmpty(0).Min();
        Transform equipment, equipmentLid;
        internal Colony RegionalFactory => (stage >= Campaign.ThirdBurrow && stage < Campaign.Citadel
            ? game.Colonies.Find(colony => colony.world == World && game.Frontier.State(World, colony.region).shipyard) : null) ?? game.Colonies.Find(colony => colony.world == World && colony.region == (World == SolarSystemDescriptor.Home ? stage - Campaign.FirstBurrow : 0));
        public bool AwaitingShipyard => SiteCleared && stage >= Campaign.ThirdBurrow && stage < Campaign.Citadel && RegionalFactory != null && !game.Frontier.State(World, RegionalFactory.region).shipyard;
        public float3 DeliverySite => RegionalFactory?.site ?? Exit;
        public bool EquipmentReady => SiteCleared && equipment != null;
        public bool Fallen => World >= 0 && Left == 0;
        public bool Near => Fallen && (!Recovered || EquipmentReady) && game.Stage != Campaign.Citadel && World == game.System.Reference && !game.Player.flying && math.distance(game.Player.Position, Prize) < 6f;
        public string FactoryOrder => AwaitingShipyard ? game.Wording("upgradeForDelivery") : World != SolarSystemDescriptor.Home ? game.Wording("buildShipyard")
            : game.Presence.fronts.Count(front => front.world == World && front.factory) switch
            {
                1 => game.Wording("buildAnother"),
                2 => game.Wording("buildLast"),
                _ => game.Wording("buildRegion")
            };
        public bool ClearOfDemolition => Recovered && (World != game.System.Reference || math.distancesq(game.Player.Position, Core) >= 35 * 35);
        public string Errand => EquipmentReady ? game.Wording("collect") : SiteCleared ? FactoryOrder.ToUpperInvariant() : Recovered ? game.Wording(ClearOfDemolition ? "waitForBlast" : "exit") : game.Stage == Campaign.Citadel ? game.Wording(Rooms.walls[Rooms.escapeWall].broken ? "flyBreach" : "citadelExit") : game.Wording("armCore");
        public bool Entering => stage == Campaign.FirstBurrow && !Rooms.walls.Last().broken
            || !new Burrow { origin = Position, rotation = rotation }.Contains(game.Player.Position) || Local(game.Player.Position).z < -20;
        public string Instruction => Fallen ? Errand : stage == Campaign.Citadel ? Rooms.InsideCitadel(Local(game.Player.Position)) ? CitadelMark : game.Wording(game.Mission.task) : OuterLeft > 0
            ? $"{game.Wording("clearLanding")} · {OuterLeft} {(OuterLeft == 1 ? "TURRET" : "TURRETS")}"
            : game.Player.flying && Shield.active ? game.Wording("landOutside")
            : Entering ? game.Wording("entranceHint")
            : game.Wording(Left == 1 ? "followMind" : "followMinds").Replace("{count}", Left.ToString());
        public float3 LandingSite { get; private set; }
        public string CitadelMark => Fallen ? Rooms.walls[Rooms.escapeWall].broken ? "FLY THROUGH THE BREACH" : "SHOOT THE REAR WALL" : !Rooms.InsideCitadel(Local(game.Player.Position)) ? "CITADEL ENTRANCE" : game.Wording(sectionGuard?.stationary == true ? "citadelTurret" : sectionGuard.HasValue ? "citadelRelay" : "citadelMind");
        public float3 Waypoint => stage == Campaign.Citadel && Rooms != null
            ? Fallen ? Place(Rooms.walls[Rooms.escapeWall].broken ? Rooms.core + new float3(0, 0, 140) : Rooms.walls[Rooms.escapeWall].centre)
            : !Rooms.InsideCitadel(Local(game.Player.Position)) ? Entrance
            : sectionGuard?.position ?? Core
            : OuterLeft > 0 ? game.Battle.Foes.Where(foe => foe.outerGuard && foe.stationary && foe.health > 0).OrderBy(foe => math.distancesq(foe.position, game.Player.Position)).First().position
            : game.Player.flying && Shield.active ? LandingSite : Entering ? Entrance
            : game.Battle.Foes.Where(foe => foe.kind == FoeKind.Mind && foe.health > 0).OrderBy(foe => math.distancesq(foe.position, game.Player.Position)).Select(foe => foe.position).DefaultIfEmpty(Core).First();
        float3 Entrance
        {
            get
            {
                float3 at = Place(Rooms.entrance);
                if (stage != Campaign.FirstBurrow || Rooms.walls.Last().broken) return at;
                float3 up = math.normalizesafe(Place(new float3(0, 0, -24)));
                return up * (game.System.Descriptor.bodies[World].GroundRadius(up) + 0.12f);
            }
        }
        public Interior Rooms { get; private set; }
        public int GeometryVersion { get; private set; }
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
            public Transform root, lever;
            public TextMesh counter;
            public int seconds;
            public List<Transform> walls;
        }
        readonly List<Kept> kept = new List<Kept>();
        SolarSystemDescriptor system;
        int stage = -1;
        quaternion rotation;
        Transform fort, crate, door, shell, prizeShield;
        Material shimmer, citadelTissue;
        void OnDestroy() { if (citadelTissue != null) Destroy(citadelTissue); }
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
            if (equipment != null) Destroy(equipment.gameObject);
            equipment = null;
            recovered = null;
            sectionGuard = null;
            spawned = Open = explainedShield = false;
            waiting.Clear();
            Left = Campaign.Minds(game.Stage);
            Escape = 0;
            walls.Clear();
            Rooms = null;
            GeometryVersion++;
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
                var body = game.System.Descriptor.bodies[World];
                body.burrows = body.burrows.Where(burrow => math.distance(burrow.origin, Position) >= 1).ToArray();
                body.foundations.RemoveAll(item => math.distance(item.xyz, Place(Rooms.core)) < 80);
                game.System.Bodies[World].Dig(Place(Rooms.core), 30);
                Destroy(fort.gameObject); fort = null; return;
            }
            shell.gameObject.SetActive(false);
            bool armed = ruin != null && ruin.detonates > 0;
            crate.gameObject.SetActive(armed);
            var site = new Kept { stage = stage, world = World, position = Position, rotation = rotation, rooms = Rooms, ruin = ruin, root = fort, walls = new List<Transform>(walls) };
            if (armed)
            {
                prizeShield.gameObject.SetActive(false);
                crate.Find("Charge strips").GetComponent<Renderer>().sharedMaterial = Shapes.Material("Glow", new Color(1, 0.09f, 0.018f));
                site.lever = crate.Find("Arming lever");
                site.counter = chargeReadout;
                site.counter.color = new Color(1, 0.64f, 0.26f);
                site.seconds = Mathf.Max(0, Mathf.CeilToInt((float)(ruin.detonates - game.System.Clock)));
                site.counter.text = $"ARMED  {site.seconds} s";
            }
            if (door != null) door.gameObject.SetActive(false);
            kept.Add(site);
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
            if (!station && stage >= Campaign.Tower)
            {
                // Prefer dry, level ground on the entrance side with room for the ship to turn.
                float best = float.NegativeInfinity;
                for (int i = 0; i < 24; i++)
                {
                    float angle = i * math.PI / 12;
                    float3 up = math.normalize(Place(new float3(math.sin(angle), 0, -math.cos(angle)) * 80));
                    float radius = body.GroundRadius(up);
                    float score = math.dot(body.GroundNormal(up), up) + 0.02f * math.cos(angle) - (radius < body.SeaRadius ? 2 : 0);
                    if (score <= best) continue;
                    best = score; LandingSite = up * radius;
                }
            }
            var damage = game.Ruins.FirstOrDefault(ruin => ruin.stage == stage);
            if (damage != null)
            {
                Open = damage.open;
                foreach (int index in damage.broken)
                    if (index >= 0 && index < Rooms.walls.Length && index != Rooms.escapeWall && Rooms.walls[index].section == 0) { var wall = Rooms.walls[index]; wall.broken = true; Rooms.walls[index] = wall; }
            }
            fort = new GameObject("Mind burrow").transform;
            fort.SetParent(game.System.Bodies[World].transform, false);
            fort.localPosition = Position;
            fort.localRotation = rotation;
            if (station)
            {
                if (citadelTissue == null)
                {
                    citadelTissue = new Material(game.Battle.MindTissue);
                    citadelTissue.EnableKeyword("_EMISSION");
                    citadelTissue.SetTexture("_EmissionMap", citadelTissue.mainTexture);
                    citadelTissue.SetColor("_EmissionColor", new Color(0.35f, 0.19f, 0.26f));
                }
                var mantle = Models.CitadelCarapace();
                mantle.SetParent(fort, false);
                mantle.Find("Fluted mantle").GetComponent<MeshRenderer>().sharedMaterial = citadelTissue;
            }
            var lit = Shapes.Material("Glow", new Color(0.25f, 0.9f, 1.1f));
            var rock = Shapes.Rock(6);
            var chitin = station ? citadelTissue : game.Battle.MindTissue;
            shimmer = new Material(Resources.Load<Shader>("Shield"));
            foreach (var wall in Rooms.walls)
            {
                Transform part;
                if (wall.section > 0)
                    part = Shapes.Part(fort, "Section shield", Shapes.Box(), shimmer, Vector3.zero, wall.half * 2, false).transform;
                else if (station)
                {
                    bool horizontal = wall.half.y < 2;
                    part = Models.CitadelWall();
                    part.SetParent(fort, false);
                    part.localRotation = horizontal ? Quaternion.Euler(90, 0, 0) : wall.half.x < 2 ? Quaternion.Euler(0, 90, 0) : Quaternion.identity;
                    part.localScale = new Vector3(1, horizontal ? 1 : wall.half.y / 29f, 1);
                    part.Find("Folded membrane").GetComponent<MeshRenderer>().sharedMaterial = chitin;
                    if (horizontal && wall.centre.y > 0)
                    {
                        var roof = Models.CitadelRoof(Rooms.route.Any(turn => math.distancesq(turn.xz, wall.centre.xz) < 1));
                        roof.SetParent(part, false);
                        roof.localRotation = Quaternion.Inverse(part.localRotation);
                        roof.Find("Vault and ribs").GetComponent<MeshRenderer>().sharedMaterial = chitin;
                    }
                }
                else part = Shapes.Part(fort, "Grown tunnel rock", rock, chitin, Vector3.zero, wall.half * 2.8f).transform;
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
            var lamps = station
                ? Rooms.walls.Where(w => w.centre.y < 0 && w.half.y < 2).Select(w => new Vector3(w.centre.x, 15f, w.centre.z))
                : Enumerable.Range(0, 4).Select(i => new Vector3(0, Interior.TunnelFloor(-12 + i * 14) + 4, -12 + i * 14));
            foreach (Vector3 position in lamps)
            {
                var lamp = new GameObject("Room light").AddComponent<Light>();
                lamp.transform.SetParent(fort, false);
                bool warm = ((Mathf.RoundToInt(position.x / 58) + Mathf.RoundToInt(position.z / 58)) & 1) == 0;
                lamp.transform.localPosition = station ? position + new Vector3(warm ? 13 : -13, 0, warm ? -9 : 9) : position;
                lamp.color = station ? warm ? new Color(1, 0.67f, 0.46f) : new Color(0.67f, 0.74f, 1) : new Color(0.65f, 0.2f, 0.55f);
                lamp.range = station ? 90 : 18;
                lamp.intensity = station ? 160 : 0.8f;
            }
            shell = Shapes.Part(fort, "Shield", Shapes.Orb(station ? 22 : 45, 24), shimmer, station ? (Vector3)Rooms.core : Vector3.zero, Vector3.one, false).transform;
            Prize = Place(station ? Rooms.entrance : Rooms.core + new float3(0, 0, 4));
            if (!station) { float3 up = math.normalizesafe(Prize); Prize = up * (body.GroundRadius(up) + 0.03f); }
            crate = Models.Equipment(true); crate.SetParent(fort, false); crate.localPosition = Local(Prize);
            crate.gameObject.SetActive(!station);
            if (!station)
            {
                chargeReadout = Shapes.Readout(crate, new Vector3(0, 0.91f, -0.331f), 0.072f, TextAnchor.MiddleCenter, new Color(0.65f, 0.84f, 0.84f));
                chargeReadout.text = "LOCKED";
            }
            prizeShield = Shapes.Part(crate, "Demolition interlock", Shapes.Orb(1.7f, 32), shimmer, Vector3.up * 0.8f, new Vector3(1, 0.8f, 1), false).transform;
            var locked = new MaterialPropertyBlock();
            locked.SetFloat("_FootOpen", 0);
            prizeShield.GetComponent<Renderer>().SetPropertyBlock(locked);
            {
                var tissue = station ? citadelTissue : game.Battle.MindTissue;
                for (int i = 0; i < (station ? Rooms.route.Length : 12); i++)
                {
                    float z = -20 + i * 4.5f;
                    var rib = Shapes.Part(fort, "Living tunnel tissue", station ? Models.CitadelButtress() : Shapes.Orb(1, 24), tissue,
                        station ? (Vector3)Rooms.route[i] + new Vector3(i % 2 == 0 ? -23 : 23, 0, 0) : new Vector3(i % 2 == 0 ? -4.2f : 4.2f, Interior.TunnelFloor(z) + 2.5f, z), station ? Vector3.one : new Vector3(0.7f, 2.8f, 1.1f)).transform;
                    if (station) rib.localRotation = Quaternion.Euler(0, i * 137.5f, 0);
                    rib.gameObject.AddComponent<LivingTissue>();
                }
            }
        }

        public void Leave(int world)
        {
            if (world != World) return;
            waiting.Clear();
            waiting.AddRange(game.Battle.Foes.Where(foe => foe.health > 0 && (foe.kind == FoeKind.Mind || foe.kind == FoeKind.Sentry || foe.guardian || foe.outerGuard)));
            spawned = false;
        }
        public void Flash() => flash = 1f;
        public void Breach(float3 point, float damage)
        {
            void Hit(Interior rooms, float3 local, int mission, List<Transform> parts)
            {
                if (!rooms.Breach(local, damage)) return;
                GeometryVersion++;
                var saved = game.Ruins.FirstOrDefault(ruin => ruin.stage == mission);
                if (saved == null) { saved = new Ruin { stage = mission }; game.Ruins.Add(saved); }
                saved.broken = Enumerable.Range(0, rooms.walls.Length).Where(i => rooms.walls[i].broken && rooms.walls[i].section == 0).ToList();
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
        public bool PunchWall(float3 from, float3 to, float damage)
        {
            if (Rooms == null || World != game.System.Reference || !Rooms.FistContact(Local(from), Local(to), out float3 point)) return false;
            Breach(Place(point), damage);
            return true;
        }
        public bool Use() => false;
        public float3 Resolve(float3 from, float3 to, float radius, out bool hit, bool stepUp = true)
        {
            hit = false;
            if (fort != null && Rooms != null && World == game.System.Reference)
                to = Place(Rooms.Move(Local(from), math.rotate(math.inverse(rotation), to - from), radius, 1, Open, out hit, stepUp && radius < 2 ? 0.4f : 0));
            foreach (var site in kept)
            {
                if (site.world != game.System.Reference) continue;
                var turn = math.inverse(site.rotation);
                float3 moved = site.rooms.Move(math.rotate(turn, from - site.position), math.rotate(turn, to - from), radius, 1, true, out bool blocked, stepUp && radius < 2 ? 0.4f : 0);
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
            shimmer.SetFloat("_FootOpen", stage < Campaign.Citadel && !LandingLocked ? 1 : 0);
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
            game.Hermit.Remark(game.Wording("escape"));
        }
        // Search across the finished passage and along it when a bend blocks the preferred spot.
        float3 BroodSite(float x, float z, bool mind)
        {
            var body = game.System.ReferenceBody;
            float radius = mind ? 2f : 1.2f;
            float3 best = float3.zero;
            float bestGap = float.NegativeInfinity;
            for (int row = 0; row < 115; row++)
            {
                float depth = z + (row % 2 == 0 ? row / 2 : -(row + 1) / 2);
                if (depth < -21 || depth > 35) continue;
                for (int column = 0; column < 15; column++)
                {
                    float side = x + (column % 2 == 0 ? column / 2 : -(column + 1) / 2);
                    if (math.abs(side) > 3.5f) continue;
                    float3 up = math.normalizesafe(Place(new float3(side, Interior.TunnelFloor(depth), depth)));
                    float3 at = up * (body.GroundRadius(up) + (mind ? 1.5f : Combat.SentryHeight));
                    if (!Rooms.Fits(Local(at), radius, Open)) continue;
                    float gap = float.MaxValue;
                    foreach (var foe in game.Battle.Foes)
                        gap = math.min(gap, math.distance(foe.position, at) - foe.size);
                    if (gap >= radius) return at;
                    if (gap > bestGap) { best = at; bestGap = gap; }
                }
            }
            if (bestGap > float.NegativeInfinity) return best;
            float3 entrance = math.normalize(Place(Rooms.entrance - new float3(0, 0, 5)));
            return entrance * (body.GroundRadius(entrance) + (mind ? 1.5f : Combat.SentryHeight));
        }

        static bool CountdownBeat(float remaining)
        {
            float beats = remaining <= 5 ? 2 : 1;
            return remaining > 0 && Mathf.CeilToInt(remaining * beats) < Mathf.CeilToInt((remaining + Time.deltaTime) * beats);
        }

        public void Tick()
        {
            for (int index = kept.Count - 1; index >= 0; index--)
            {
                var site = kept[index];
                var ruin = site.ruin;
                if (ruin == null || ruin.destroyed || ruin.detonates <= 0) continue;
                double remaining = ruin.detonates - game.System.Clock;
                if (remaining > 0)
                {
                    int seconds = (int)math.ceil(remaining);
                    if (seconds != site.seconds) site.counter.text = $"ARMED  {seconds} s";
                    site.seconds = seconds;
                    float engaged = Mathf.SmoothStep(0, 1, (20 - (float)remaining) / 0.45f);
                    site.lever.localRotation = Quaternion.Euler(68 * engaged, 0, 0);
                    if (site.world == game.System.Reference && CountdownBeat((float)remaining))
                        game.Sounds.Encounter(EncounterCue.Countdown, (float3)site.counter.transform.position + game.System.Anchor);
                    continue;
                }
                float3 centre = site.position + math.rotate(site.rotation, site.rooms.core);
                ruin.destroyed = true;
                Destroy(site.root.gameObject); kept.RemoveAt(index);
                var body = game.System.Descriptor.bodies[site.world];
                body.burrows = body.burrows.Where(burrow => math.distance(burrow.origin, site.position) >= 1).ToArray();
                body.foundations.RemoveAll(item => math.distance(item.xyz, centre) < 80);
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
                    game.Hermit.Remark(AwaitingShipyard ? FactoryOrder : RegionalFactory != null ? game.Wording("factoryDelivery") : FactoryOrder + ". " + game.Wording("deliveryAfterBuild"));
                game.Write();
            }
            if (Recovered)
            {
                if (SiteCleared && equipment == null)
                {
                    var factory = RegionalFactory;
                    if (factory != null && !AwaitingShipyard)
                    {
                        var body = game.System.Descriptor.bodies[World];
                        Vector3 up = math.normalizesafe(factory.site);
                        float3 beside = math.normalizesafe(factory.site + (float3)(Quaternion.FromToRotation(Vector3.up, up) * Vector3.right) * 10);
                        Prize = beside * (body.GroundRadius(beside) + 0.05f);
                        equipment = Models.Equipment(false);
                        equipmentLid = equipment.Find("Hinged lid");
                        equipment.name = "Factory equipment";
                        equipment.SetParent(game.System.Bodies[World].transform, false);
                        equipment.localPosition = Prize;
                        equipment.localRotation = Quaternion.FromToRotation(Vector3.up, beside);
                    }
                }
                if (equipment != null && World == game.System.Reference)
                {
                    bool approaching = !game.Player.flying && math.distancesq(game.Player.Position, Prize) < 9 * 9;
                    equipmentLid.localRotation = Quaternion.RotateTowards(equipmentLid.localRotation,
                        approaching ? Quaternion.Euler(105, 0, 0) : Quaternion.identity, 150 * Time.deltaTime);
                }
                return;
            }
            if (World != game.System.Reference) return;
            if (Fallen)
            {
                crate.gameObject.SetActive(stage != Campaign.Citadel);
                if (game.Stage == Campaign.Citadel)
                {
                    Escape -= Time.deltaTime;
                    if (CountdownBeat(Escape)) game.Sounds.Countdown();
                    if (Local(game.Player.Position).z <= -329 || Rooms.walls[Rooms.escapeWall].broken && Local(game.Player.Position).z > 900)
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
                game.Hermit.Remark(game.Wording("land"));
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
            if (spawned && stage == Campaign.Citadel)
            {
                sectionGuard = null;
                for (int i = Rooms.walls.Length - (Rooms.route.Length - 3); i < Rooms.walls.Length; i++)
                {
                    int section = Rooms.walls[i].section;
                    float3 turn = Place(Rooms.route[section]);
                    int guard = game.Battle.Foes.FindIndex(foe => foe.health > 0 && (foe.stationary || foe.kind == FoeKind.Mind)
                        && math.distancesq(foe.position, turn) < 35 * 35);
                    bool open = guard < 0;
                    if (!open && !sectionGuard.HasValue) sectionGuard = game.Battle.Foes[guard];
                    if (Rooms.walls[i].broken != open) { Rooms.walls[i].broken = open; GeometryVersion++; }
                }
            }
            if (spawned) return;
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
                    float3 point = i == 2 ? Rooms.core : Rooms.route[(i + 1) * (Rooms.route.Length - 1) / 3];
                    point.y = -16;
                    game.Battle.Add(FoeKind.Mind, Place(point));
                    var mind = game.Battle.Foes[^1]; mind.size = 6 + i * 2; mind.health = mind.maximumHealth *= 3 + i; game.Battle.Foes[^1] = mind;
                    for (int guard = 0; guard < 6; guard++)
                    {
                        bool airborne = guard < 4;
                        game.Battle.Add(airborne ? FoeKind.Drone : FoeKind.Sentry, Place(point + new float3(guard % 2 == 0 ? -12 : 12, airborne ? 19 + guard : -13 + Combat.SentryHeight, guard < 2 ? 12 : -12)), MotileCaste.Spitter);
                        var sentry = game.Battle.Foes[^1]; sentry.guardian = true; game.Battle.Foes[^1] = sentry;
                    }
                }
                for (int i = 2; i < Rooms.route.Length - 1; i++)
                {
                    float3 point = Rooms.route[i] + new float3(i % 4 == 0 ? -12 : 12, -24, 0);
                    game.Battle.Add(FoeKind.Sentry, Place(point), MotileCaste.Breacher);
                    var turret = game.Battle.Foes[^1]; turret.stationary = true; turret.size = 3; game.Battle.Foes[^1] = turret;
                }
            }
            else
                for (int i = 0; i < Campaign.Minds(stage); i++)
                {
                    float z = i == Campaign.Minds(stage) - 1 ? 32 : Campaign.Minds(stage) == 2 ? 17 : i == 0 ? -2 : 20;
                    float3 at = BroodSite(0, z, true);
                    game.Battle.Add(FoeKind.Mind, at);
                    float3 floor = Local(at - math.normalizesafe(at) * 1.5f);
                    foreach (int side in new[] { -1, 1 })
                    {
                        float3 wall = new float3(side * 4.85f, floor.y, floor.z + side * 1.25f);
                        float3 over = math.normalizesafe(Place(wall));
                        wall = Local(over * game.System.ReferenceBody.GroundRadius(over));
                        Vector3 length = wall - floor;
                        var root = Shapes.Part(fort, "Wall root", Models.Parts("Burrow root")[0], game.Battle.MindTissue, floor,
                            new Vector3(1, 1, length.magnitude / 4)).transform;
                        root.localRotation = Quaternion.LookRotation(length, Vector3.up);
                    }
                }
            if (stage >= Campaign.Tower && stage < Campaign.Citadel)
                for (int i = 0; i < Campaign.OuterDefences(stage); i++)
                {
                    float3 at = Place(new float3((i % 2 == 0 ? -1 : 1) * (18 + i * 7), i % 2 == 0 ? 100 : 1, -68));
                    float3 up = math.normalizesafe(at);
                    var body = game.System.ReferenceBody;
                    float surface = body.GroundRadius(up);
                    at = up * (i % 2 == 0 ? math.max(surface + 100, body.radius * Shapes.AirTop(body) + 15) : surface + 1);
                    game.Battle.Add(i % 2 == 0 ? FoeKind.Raider : FoeKind.Sentry, at, MotileCaste.Breacher);
                    var guard = game.Battle.Foes[^1]; guard.outerGuard = true; guard.stationary = i % 2 != 0;
                    if (guard.stationary && game.Challenge <= Difficulty.Normal)
                    {
                        guard.health = guard.maximumHealth = Combat.Guns[0].damage * 4;
                        guard.shield = 0;
                    }
                    game.Battle.Foes[^1] = guard;
                    if (guard.stationary) game.System.ReferenceBody.foundations.Add(new float4(guard.position, 4));
                }
            if (stage == Campaign.Citadel) return;
            int coreIndex = game.Battle.Foes.FindIndex(foe => foe.kind == FoeKind.Mind);
            var core = game.Battle.Foes[coreIndex]; core.protectedCore = OuterLeft > 0; game.Battle.Foes[coreIndex] = core;
            var minds = game.Battle.Foes.Where(foe => foe.kind == FoeKind.Mind).Select(foe => Local(foe.position)).ToArray();
            int guards = stage == Campaign.FirstBurrow && game.Challenge <= Difficulty.Normal ? 1 : math.max(minds.Length * 2, game.Mission.count);
            for (int i = 0; i < guards; i++)
            {
                float z = -16 + i * 44f / math.max(1, game.Mission.count - 1);
                float3 position = i < minds.Length * 2 ? minds[i / 2] + new float3(i % 2 == 0 ? -2.5f : 2.5f, 0, -3)
                    : new float3((i % 2 * 2 - 1) * 2, Interior.TunnelFloor(z), z);
                game.Battle.Add(FoeKind.Sentry, BroodSite(position.x, position.z, false), Combat.Caste(game.Stage, i));
                var sentry = game.Battle.Foes[^1]; sentry.guardian = i < minds.Length * 2; game.Battle.Foes[^1] = sentry;
            }
        }
        public void Killed(FoeKind kind)
        {
            if (kind != FoeKind.Mind || World != game.System.Reference || Fallen) return;
            if (--Left > 0) { game.Notify($"MIND DESTROYED · {Left} MINDS REMAIN IN {(stage == Campaign.Citadel ? "THE CITADEL" : "THIS REGION")}"); return; }
            Open = true;
            if (stage == Campaign.Citadel) Rooms.coreDestroyed = true;
            else chargeReadout.text = "READY";
            Escape = 25;
            game.Frontier.State(World, World == SolarSystemDescriptor.Home ? stage - Campaign.FirstBurrow : 0).mindDead = true;
            if (game.Stage >= Campaign.ThirdBurrow && World == game.System.Reference) game.Escorts.pack = false;
            game.Presence.Liberate(World);
            for (int i = 0; i < game.Battle.Foes.Count; i++)
            {
                var foe = game.Battle.Foes[i]; foe.scattered = true; foe.shield = 0; foe.clever = false; game.Battle.Foes[i] = foe;
            }
            game.Notify(game.Stage == Campaign.Citadel ? "CORE DESTROYED · SHOOT THE REAR WALL AND FLY THROUGH · 25 SECONDS" : "MIND SILENT · ARM THE CHARGE BEHIND IT");
            game.Hermit.Remark(game.Stage == Campaign.Citadel ? game.Wording("citadelEscape") : game.Wording("arm"));
            game.Sounds.Chime();
        }
    }
}
