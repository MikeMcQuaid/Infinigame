using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;

namespace Infinigame
{
    public class Frontier : MonoBehaviour
    {
        [NonSerialized] public Game game;
        public bool Collapsing => game.Fall.destroyed;
        public float Whiteout => Collapsing ? math.saturate((collapse - 24) / 4) : arrival;
        float saved, deployed, collapse, arrival, transfer;
        int shownWorld = -1, shownDogs = -1, shownShips = -1;
        Task<SolarSystemDescriptor> next;
        Transform hole;
        SolarSystemDescriptor worlds;
        readonly List<Transform> buildings = new List<Transform>();
        sealed class Unit
        {
            public Transform model;
            public LineRenderer beam;
            public bool ship;
            public float3 position;
            public float health = 70;
        }
        readonly List<Unit> units = new List<Unit>();
        public Front Local => State(game.System.Reference);
        public Front State(int world)
        {
            var front = game.Presence.fronts.FirstOrDefault(item => item.world == world);
            if (front != null) return front;
            int mission = Enumerable.Range(0, Campaign.Citadel + 1).FirstOrDefault(stage => Campaign.World(game.System.Descriptor, stage) == world);
            int rank = world == SolarSystemDescriptor.Home ? 0 : mission >= 5 ? mission - 4 : 8;
            front = War.Create(world, game.Depth, rank);
            if (game.System.Descriptor.bodies[world].kind != BodyKind.Rock) front.aliens = 0;
            game.Presence.fronts.Add(front);
            return front;
        }
        public void Settle()
        {
            if (worlds != game.System.Descriptor)
            {
                worlds = game.System.Descriptor;
                deployed = collapse = 0; next = null;
                if (hole != null) Destroy(hole.gameObject);
            }
            foreach (var building in buildings) if (building != null) Destroy(building.gameObject);
            buildings.Clear();
            foreach (var unit in units) if (unit.model != null) Destroy(unit.model.gameObject);
            units.Clear();
            shownWorld = shownDogs = shownShips = -1;
            foreach (var colony in game.Colonies)
            {
                var front = State(colony.world);
                if (!front.factory) continue;
                var model = Models.Building(front.shipyard ? Building.ShipFactory : Building.Factory);
                model.name = front.shipyard ? "Ship and dog factory" : "Dog factory";
                model.SetParent(game.System.Bodies[colony.world].transform, false);
                model.localPosition = colony.site;
                model.localRotation = Quaternion.FromToRotation(Vector3.up, math.normalizesafe(colony.site));
                buildings.Add(model);
            }
            if (game.Fall.active && !game.Fall.destroyed)
                for (int i = 0; i < 6; i++)
                {
                    var moon = game.System.Bodies[game.Fall.moon];
                    float angle = i * math.PI / 3;
                    float3 up = math.normalize(new float3(math.cos(angle), 0.5f, math.sin(angle)));
                    var casing = new MeshKit();
                    casing.Lathe(new[] { 0f, 0f, 3f, 0f, 3.5f, 1f, 2.6f, 6f, 3.2f, 9f, 2.4f, 9f, 1.9f, 6f, 0f, 1f }, 64);
                    var engine = Shapes.Part(moon.transform, "Moon engine", casing.Mesh("Moon drive casing"), Shapes.Solid(Surface.Metal, new Color(0.6f, 0.7f, 0.75f)), up * moon.Descriptor.GroundRadius(up), Vector3.one).transform;
                    engine.localRotation = Quaternion.FromToRotation(Vector3.up, up);
                    Shapes.Part(engine, "Drive plume", Shapes.Sphere(1, 24), Shapes.Material("Glow", new Color(0.2f, 2, 4)), Vector3.up * 13, new Vector3(2, 8, 2), false);
                    buildings.Add(engine);
                }
        }
        public string FactoryHint(int choice)
        {
            var front = Local;
            return choice == 0 ? $"DOG FACTORY · {front.kills}/{War.RequiredKills(front)} ALIENS DEFEATED\nReplicating laser dogs hunt the remaining {math.ceil(front.aliens):0} bodies. Production stops when this world is clear."
                : choice == 1 ? "SHIP FACTORY · REQUIRES A DOG FACTORY ON A MOON\nYour dogs assemble ships. They defend this planet and its immediate neighbours, never the whole system."
                : "MOON ENGINES · REQUIRES THE CITADEL CORE DESTROYED AND 16 ROBOT DOGS\nThe impact will collapse the buried mind into a singularity. This entire system will be consumed.";
        }
        public void BuildFactory(int choice)
        {
            var front = Local;
            if (choice == 2)
            {
                if (game.Stage >= Campaign.Won && front.factory && front.dogs >= 16 && !game.Fall.active) Launch();
                else game.Notify("DESTROY THE CITADEL CORE AND GROW 16 DOGS AT THIS FACTORY");
                return;
            }
            var body = game.System.ReferenceBody;
            bool orbital = body.kind == BodyKind.Rock && body.orbit.parent > 0;
            if (choice == 1 && !orbital) { game.Notify("ESTABLISH A DOG FACTORY ON A MOON FIRST"); return; }
            if (!War.Build(front, choice == 1)) { game.Notify(choice == 0 ? $"CLEAR A FOOTHOLD · {front.kills}/{War.RequiredKills(front)} KILLS" : "A DOG FACTORY IS REQUIRED, OR THIS FACTORY ALREADY EXISTS"); return; }
            if (!game.Colonies.Any(colony => colony.world == front.world))
            {
                float3 ahead = math.normalizesafe(game.Player.Position + game.Player.walker.forward * 12);
                float3 site = ahead * body.GroundRadius(ahead);
                game.System.Bodies[front.world].Level(site, 9);
                game.Colonies.Add(new Colony { world = front.world, site = site });
            }
            front.established = true;
            Settle();
            game.Hermit.Remark(choice == 0 ? "Factory seed awake. Each dog helps assemble more dogs. Their dorsal lasers will clear this world, but they need you to break the enemy's advantage." : "Our dogs are assembling autonomous ships. Help them break the living fleet. Their reach stops at the neighbouring planet.");
            if (game.Stage == 3 && choice == 0) game.Advance();
            game.Write();
        }
        public void Launch()
        {
            game.Fall.began = game.System.Clock;
            game.Fall.active = true;
            Settle();
            game.Hermit.Remark("The moon engines are burning. The buried mind will collapse. Stay with me when the light goes out.");
            uint seed = game.Seed + 1;
            next = Task.Run(() => { var system = SolarSystemDescriptor.Beyond(seed); Campaign.Site(system, 0); return system; });
        }
        public float3 Target(float3 from, float3 player)
        {
            float nearest = math.distancesq(from, player);
            foreach (var unit in units)
                if (unit.health > 0 && math.distancesq(from, unit.position) < nearest)
                { player = unit.position; nearest = math.distancesq(from, player); }
            return player;
        }
        public void Collect(List<Scannable> things)
        {
            foreach (var unit in units) things.Add(new Scannable { name = unit.ship ? "Foundry autonomous combat ship" : "Foundry laser dog", foot = unit.position, height = unit.ship ? 8 : 2, girth = unit.ship ? 10 : 1.2f });
            foreach (var colony in game.Colonies.Where(item => item.world == game.System.Reference))
                things.Add(new Scannable { name = "Foundry replication factory", foot = colony.site, height = 7, girth = 8 });
        }
        public void Tick()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            arrival = math.max(0, arrival - dt * 0.6f);
            if (Collapsing)
            {
                collapse += dt;
                if (next == null) { uint seed = game.Seed + 1; next = Task.Run(() => { var system = SolarSystemDescriptor.Beyond(seed); Campaign.Site(system, 0); return system; }); }
                if (collapse >= 29 && next.IsCompletedSuccessfully)
                {
                    game.PreparedSystem = next.Result; next = null;
                    game.NextSystem(); collapse = 0; arrival = 1;
                    if (hole != null) Destroy(hole.gameObject);
                }
                return;
            }
            if (game.Buried) return;
            game.Presence.Step(dt);
            for (int i = 0; i < game.System.Descriptor.bodies.Length; i++)
            {
                var front = State(i);
                War.Step(front, dt);
                game.System.Descriptor.bodies[i].blight = math.saturate(front.aliens / 600);
            }
            // Only player-established factories send support. Recipients never relay it further.
            if ((transfer += dt) > 30)
            {
                transfer = 0;
                var planets = Enumerable.Range(1, game.System.Bodies.Length - 1).Where(i => game.System.Descriptor.bodies[i].orbit.parent == 0 && game.System.Descriptor.bodies[i].kind != BodyKind.Station).ToArray();
                foreach (var source in game.Presence.fronts.Where(front => front.established && front.shipyard && front.ships > 8).ToArray())
                {
                    int origin = Array.IndexOf(planets, game.System.Descriptor.Planet(source.world));
                    foreach (var target in game.Presence.fronts)
                    {
                        int destination = Array.IndexOf(planets, game.System.Descriptor.Planet(target.world));
                        if (origin < 0 || destination < 0 || target == source || !War.CanSpread(origin, destination) || target.alienShips <= 0) continue;
                        float count = math.min(2, source.ships * 0.05f);
                        source.ships -= count; target.ships += count;
                    }
                }
            }
            if (!game.Battle.quiet && game.Stage >= 1 && (deployed -= dt) <= 0)
            {
                deployed = 8;
                var body = game.System.ReferenceBody;
                var front = Local;
                if (body.kind == BodyKind.Rock && front.aliens > 0 && game.Battle.Foes.Count(foe => foe.patrol) < math.min(10, 3 + game.Threat))
                {
                    var rnd = new Unity.Mathematics.Random((uint)(game.Presence.played * 100 + game.Seed) | 1);
                    float3 up = math.normalize(game.Player.Position);
                    float3 direction = math.normalize(up + Walker.Project(rnd.NextFloat3Direction(), up) * (rnd.NextFloat(70, 160) / body.radius));
                    game.Battle.Add(FoeKind.Sentry, direction * (body.GroundRadius(direction) + 0.9f), game.Stage <= 2 ? MotileCaste.Stalker : (MotileCaste)rnd.NextInt(0, 3));
                    var foe = game.Battle.Foes[^1]; foe.patrol = true; game.Battle.Foes[^1] = foe;
                }
            }
            if (Local.factory && Local.aliens <= 0)
            {
                if (game.Battle.Foes.Any(foe => foe.kind == FoeKind.Mind)) game.Award(FoeKind.Mind);
                game.Battle.Foes.RemoveAll(foe => !foe.friendly && foe.kind != FoeKind.Raider && foe.kind != FoeKind.Base);
            }
            DrawUnits(dt);
            if (game.Fall.Finished(game.System.Clock))
            {
                game.Fall.destroyed = true;
                collapse = 0;
                game.Hermit.Remark("The mind is collapsing. Everything is falling towards it. You are not dead. Stay with me.");
                game.Write();
            }
            if ((saved += dt) > 15) { saved = 0; game.Write(); }
        }
        void DrawUnits(float dt)
        {
            var front = Local;
            int dogs = math.min(10, (int)front.dogs), ships = math.min(3, (int)front.ships);
            if (shownWorld != game.System.Reference || shownDogs != dogs || shownShips != ships)
            {
                foreach (var unit in units) Destroy(unit.model.gameObject);
                units.Clear();
                shownWorld = game.System.Reference; shownDogs = dogs; shownShips = ships;
                float3 home = game.Colonies.FirstOrDefault(colony => colony.world == shownWorld)?.site ?? game.Player.Position;
                for (int i = 0; i < dogs + ships; i++)
                {
                    bool ship = i >= dogs;
                    var model = ship ? Models.Ship() : Models.Robot();
                    if (!ship) model.gameObject.AddComponent<MachineMotion>().quadruped = true;
                    var beam = new GameObject("Dorsal laser").AddComponent<LineRenderer>();
                    beam.transform.SetParent(model, false); beam.positionCount = 2; beam.startWidth = beam.endWidth = ship ? 0.18f : 0.06f;
                    beam.sharedMaterial = Shapes.Material("Glow", new Color(0.2f, 2, 3)); beam.enabled = false;
                    units.Add(new Unit { model = model, beam = beam, ship = ship, position = home + new float3(i * 3, 0, 8) });
                }
            }
            foreach (var unit in units)
            {
                float3 up = math.normalizesafe(unit.position), target = unit.position;
                int enemy = -1; float nearest = unit.ship ? 2500 : 250;
                for (int i = 0; i < game.Battle.Foes.Count; i++)
                {
                    var foe = game.Battle.Foes[i]; float distance = math.distance(foe.position, unit.position);
                    if (foe.friendly || foe.health <= 0 || (foe.kind == FoeKind.Raider) != unit.ship || distance >= nearest) continue;
                    enemy = i; nearest = distance; target = foe.position;
                }
                float3 forward = math.normalizesafe(Walker.Project(target - unit.position, up), math.cross(up, new float3(1, 0, 0)));
                if (enemy >= 0 && nearest > (unit.ship ? 80 : 12)) unit.position += forward * dt * (unit.ship ? 100 : 9);
                up = math.normalizesafe(unit.position);
                unit.position = up * (game.System.ReferenceBody.GroundRadius(up) + (unit.ship ? 130 : 0));
                unit.model.SetPositionAndRotation(unit.position - game.System.Anchor, quaternion.LookRotationSafe(forward, up));
                if (!unit.ship) unit.model.GetComponent<MachineMotion>().speed = enemy >= 0 ? 9 : 0;
                unit.beam.enabled = enemy >= 0 && nearest < (unit.ship ? 650 : 50) && game.Outpost.Visible(unit.position + up * 1.4f, target);
                if (unit.beam.enabled)
                {
                    unit.beam.SetPosition(0, unit.position + up * 1.4f - game.System.Anchor); unit.beam.SetPosition(1, target - game.System.Anchor);
                    var foe = game.Battle.Foes[enemy]; foe.health -= dt * (unit.ship ? 22 : 12); foe.recoil = 0.2f; game.Battle.Foes[enemy] = foe;
                }
                for (int i = game.Battle.Bolts.Count - 1; i >= 0; i--)
                {
                    var bolt = game.Battle.Bolts[i];
                    if (!bolt.hostile || !Combat.Crosses(bolt.position, bolt.velocity * dt, unit.position + up, unit.ship ? 8 : 1.4f)) continue;
                    unit.health -= bolt.damage; game.Battle.Bolts.RemoveAt(i);
                }
                if (unit.health <= 0)
                {
                    if (unit.ship) front.ships = math.max(0, front.ships - 1); else front.dogs = math.max(0, front.dogs - 1);
                    game.Effects.Explode(unit.position, float3.zero, unit.ship ? 8 : 1);
                    shownDogs = -1;
                }
            }
        }
        void LateUpdate()
        {
            if (!Collapsing) return;
            var system = game.System;
            float3 centre = system.ToReference(game.Fall.world) - system.Anchor;
            if (hole == null)
            {
                hole = Shapes.Part(transform, "Singularity", Shapes.Sphere(1, 48), Shapes.Material("Glow", Color.black), Vector3.zero, Vector3.one).transform;
                Shapes.Part(hole, "Accretion disc", Shapes.Ring(1.045f, 3.4f, 192), new Material(Resources.Load<Shader>("Accretion")), Vector3.zero, Vector3.one, false);
                var lens = Shapes.Part(hole, "Lensed light", Shapes.Ring(1.045f, 1.3f, 128), new Material(Resources.Load<Shader>("Accretion")), Vector3.zero, Vector3.one, false);
                lens.transform.localRotation = Quaternion.Euler(74, 0, 0);
            }
            hole.position = centre;
            float radius = 300 + collapse * 45;
            hole.localScale = Vector3.one * radius;
            for (int i = 0; i < system.Bodies.Length; i++)
            {
                var body = system.Bodies[i];
                float delay = i == 0 ? 12 : 2 + i * 0.25f;
                float t = math.saturate((collapse - delay) / (24 - delay));
                float3 offset = system.ToReference(i) - system.Anchor - centre;
                // Compress astronomical distances for the visor's final system-wide view.
                offset = math.normalizesafe(offset, new float3(0, 0, 1)) * radius * (3 + i % 7 * 0.35f);
                body.transform.position = centre + math.rotate(quaternion.RotateY(t * t * 5), offset) * (1 - t * t);
                float display = radius * (i == 0 ? 0.8f : body.Descriptor.kind == BodyKind.Gas ? 0.4f : 0.18f);
                body.transform.localScale = Vector3.one * math.max(0.00001f, display / body.Descriptor.radius * (1 - t * t));
            }
            var view = Camera.main.transform;
            view.position = centre + new float3(radius * 3, radius * 2, -radius * 7);
            view.LookAt(centre);
        }
    }
}
