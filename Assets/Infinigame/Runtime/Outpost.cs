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
        public bool Fallen => World >= 0 && Left == 0;
        public bool Near => Fallen && game.Stage != Campaign.Citadel && World == game.System.Reference && !game.Player.flying && math.distance(game.Player.Position, Prize) < 6f;
        public string Errand => game.Stage == Campaign.Citadel ? "ESCAPE THROUGH THE TUNNEL ENTRANCE" : $"TAKE {Campaign.Spoils(game.Stage)}";
        public Interior Rooms { get; private set; }
        public bool Open { get; private set; }
        public Shield Shield => new Shield { centre = Position, radius = game.Stage == Campaign.Citadel ? 310 : 45, active = World == game.System.Reference && !Fallen };
        public bool AtConsole => Rooms != null && World == game.System.Reference && math.distance(Local(game.Player.Position), Rooms.console) < 5f;
        public float Escape { get; private set; }
        public float3 Core => Place(Rooms.core);
        sealed class Kept
        {
            public int stage, world;
            public float3 position;
            public quaternion rotation;
            public Interior rooms;
            public Transform root;
            public List<Transform> walls;
        }
        readonly List<Kept> kept = new List<Kept>();
        SolarSystemDescriptor system;
        int stage = -1;
        quaternion rotation;
        Transform fort, crate, door, shell;
        Material shimmer;
        bool spawned;
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
                    Build(completed);
                    Keep();
                }
            }
            else if (fort != null && stage < game.Stage) Keep();
            else if (fort != null) Destroy(fort.gameObject);
            spawned = Open = false;
            waiting.Clear();
            Left = 1;
            Escape = 0;
            walls.Clear();
            Rooms = null;
            World = -1;
            if (game.Stage > Campaign.Stranger && game.Stage <= Campaign.Citadel) Build(game.Stage);
        }

        void Keep()
        {
            if (stage == Campaign.Citadel) { Destroy(fort.gameObject); fort = null; return; }
            shell.gameObject.SetActive(false);
            crate.gameObject.SetActive(false);
            if (door != null) door.gameObject.SetActive(false);
            kept.Add(new Kept { stage = stage, world = World, position = Position, rotation = rotation, rooms = Rooms, root = fort, walls = new List<Transform>(walls) });
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
            if (!station) game.System.Bodies[World].Level(Position, 42f);
            Rooms = station ? Interior.Citadel() : Interior.Outpost();
            var damage = game.Ruins.FirstOrDefault(ruin => ruin.stage == stage);
            if (damage != null)
            {
                Open = damage.open;
                foreach (int index in damage.broken)
                    if (index >= 0 && index < Rooms.walls.Length) { var wall = Rooms.walls[index]; wall.broken = true; Rooms.walls[index] = wall; }
            }
            fort = new GameObject("Shielded outpost").transform;
            fort.SetParent(game.System.Bodies[World].transform, false);
            fort.localPosition = Position;
            fort.localRotation = rotation;
            var lit = Shapes.Material("Glow", new Color(0.3f, 2.5f, 3.5f));
            foreach (var wall in Rooms.walls)
            {
                var part = Models.RoomPanel(wall.half, wall.door);
                part.SetParent(fort, false);
                part.localPosition = wall.centre;
                part.gameObject.SetActive(!wall.broken && !(wall.door && (Open || stage < game.Stage)));
                walls.Add(part);
                if (wall.door) door = part;
            }
            var console = new MeshKit();
            console.Box(Vector3.zero, new Vector3(1, 1.8f, 0.6f), 0.1f);
            Shapes.Part(fort, "Control terminal", console.Mesh("Terminal"), lit, Rooms.console, Vector3.one);
            for (int i = 0; i < 4; i++)
            {
                var lamp = new GameObject("Room light").AddComponent<Light>();
                lamp.transform.SetParent(fort, false);
                lamp.transform.localPosition = station ? Vector3.Lerp(Rooms.entrance, Rooms.core, i / 3f) : new Vector3(0, 4.8f, -8 + i * 9);
                lamp.color = new Color(0.3f, 0.7f, 1);
                lamp.range = station ? 100 : 18;
                lamp.intensity = station ? 12 : 3;
            }
            shimmer = new Material(Resources.Load<Shader>("Shield"));
            shell = Shapes.Part(fort, "Shield", Shapes.Sphere(station ? 310 : 45, 24), shimmer, Vector3.zero, Vector3.one, false).transform;
            Prize = Place(station ? Rooms.entrance : new float3(0, 1.5f, 18));
            crate = Shapes.Part(fort, "Prize", Shapes.Sphere(0.7f, 12), lit, Local(Prize), Vector3.one, false).transform;
            crate.gameObject.SetActive(false);
        }

        public void Leave(int world)
        {
            if (world != World) return;
            waiting.Clear();
            waiting.AddRange(game.Battle.Foes.Where(foe => foe.kind == FoeKind.Mind || foe.kind == FoeKind.Sentry));
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
            if (Rooms != null && World == game.System.Reference)
            {
                Hit(Rooms, Local(point), stage, walls);
                Open |= Rooms.walls.Any(wall => wall.door && wall.broken);
            }
            foreach (var site in kept)
                if (site.world == game.System.Reference) Hit(site.rooms, math.rotate(math.inverse(site.rotation), point - site.position), site.stage, site.walls);
        }
        public bool Use()
        {
            if (!AtConsole || Fallen) return false;
            Open = true;
            var saved = game.Ruins.FirstOrDefault(ruin => ruin.stage == stage);
            if (saved == null) { saved = new Ruin { stage = stage }; game.Ruins.Add(saved); }
            saved.open = true;
            game.Write();
            for (int i = 0; i < game.Battle.Foes.Count; i++)
            {
                var foe = game.Battle.Foes[i];
                if (foe.kind == FoeKind.Sentry) { foe.friendly = true; game.Battle.Foes[i] = foe; }
            }
            game.Notify("DOOR OPEN  ·  TURRETS TURNED AGAINST THE MIND");
            return true;
        }
        public float3 Resolve(float3 from, float3 to, float radius, out bool hit)
        {
            hit = false;
            if (Rooms != null && World == game.System.Reference)
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
            if (Rooms != null && World == game.System.Reference && !Rooms.Visible(Local(from), Local(to), Open)) return false;
            foreach (var site in kept)
                if (site.world == game.System.Reference && !site.rooms.Visible(math.rotate(math.inverse(site.rotation), from - site.position), math.rotate(math.inverse(site.rotation), to - site.position), true)) return false;
            return true;
        }
        void Update()
        {
            if (shell == null) return;
            shell.gameObject.SetActive(!Fallen);
            flash = Mathf.MoveTowards(flash, 0, Time.deltaTime * 2);
            shimmer.SetFloat("_Hit", flash);
            for (int i = 0; i < walls.Count; i++) walls[i].gameObject.SetActive(!Rooms.walls[i].broken && !(Rooms.walls[i].door && Open));
        }
        public void Tick()
        {
            if (World != game.System.Reference) return;
            if (Fallen)
            {
                crate.gameObject.SetActive(true);
                if (game.Stage == Campaign.Citadel)
                {
                    Escape -= Time.deltaTime;
                    if (math.distance(game.Player.Position, Position) > 320f)
                    {
                        game.Effects.Explode(Position, float3.zero, 200);
                        game.Advance();
                    }
                    else if (Escape <= 0) game.Player.Hurt(10000, "Caught inside the collapsing citadel");
                }
                return;
            }
            if (spawned || math.distance(game.Player.Position, Position) > (game.Stage == Campaign.Citadel ? 1800 : 600)) return;
            spawned = true;
            if (waiting.Count > 0)
            {
                game.Battle.Foes.AddRange(waiting);
                waiting.Clear();
                return;
            }
            game.Battle.Add(FoeKind.Mind, Core);
            for (int i = 0; i < game.Mission.count; i++)
            {
                // Two sentries watch the rooms; the remaining guards use the authored patrol kind.
                FoeKind kind = i < 2 || game.Mission.guards == FoeKind.Base ? FoeKind.Sentry : game.Mission.guards;
                float3 position = game.Stage == Campaign.Citadel
                    ? new float3(i < 3 ? 0 : 160, -29.1f, i < 3 ? -200 + 50 * i : 30 + 40 * (i - 3))
                    : kind == FoeKind.Raider ? new float3((i % 4 - 1.5f) * 35, 25, -90 - i / 4 * 30)
                    : kind == FoeKind.Drone ? new float3((i % 4 - 1.5f) * 12, 3, -18 - i / 4 * 8)
                    : new float3(-6 + i % 4 * 4, 0.9f, -7 + i / 4 * 5);
                game.Battle.Add(kind, Place(position));
            }
        }
        public void Killed(FoeKind kind)
        {
            if (kind != FoeKind.Mind || World != game.System.Reference || Fallen) return;
            Left = 0;
            Open = true;
            Escape = 45;
            game.Presence.Liberate(World);
            for (int i = 0; i < game.Battle.Foes.Count; i++)
            {
                var foe = game.Battle.Foes[i]; foe.scattered = true; game.Battle.Foes[i] = foe;
            }
            game.Notify(game.Stage == Campaign.Citadel ? "CORE DESTROYED  ·  45 SECONDS TO ESCAPE" : "THE MIND IS SILENT  ·  ITS BODIES SCATTER");
            game.Hermit.Remark(game.Stage == Campaign.Citadel ? "That shield held the final mind safe. Get us out. We have forty-five seconds." : "One voice has stopped. The others no longer know where to look.");
            game.Sounds.Chime();
        }
    }
}
