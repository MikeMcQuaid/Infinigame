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
        public float Whiteout => Collapsing ? math.saturate((collapse - 3 - Moonfall.Consumption) / Moonfall.Supernova) : arrival;
        public string Ending => !game.Fall.active ? "" : Collapsing
            ? collapse < 3 ? "IMPACT · THE BURIED MIND IS COLLAPSING" : collapse < 3 + Moonfall.Consumption ? $"SINGULARITY · SYSTEM CONSUMPTION {math.ceil(3 + Moonfall.Consumption - collapse):0}s" : "SUPERNOVA · HOLD ON"
            : game.System.Clock - game.Fall.began < Moonfall.Assembly ? "MOON ENGINES BUILT · SHIP FOLLOW LINK ACQUIRED"
            : game.System.Clock - game.Fall.began < Moonfall.Assembly + Moonfall.Ignition ? "IGNITION · MOON ENGINES FIRING"
            : $"MOON IN TRANSIT · SHIP FOLLOWING · IMPACT IN {math.ceil(game.Fall.began + game.Fall.duration - game.System.Clock):0}s";
        float saved, deployed, collapse, arrival, transfer, assembly;
        readonly List<Transform> plumes = new List<Transform>();
        int shownWorld = -1;
        Task<SolarSystemDescriptor> next;
        Transform hole, impact;
        SolarSystemDescriptor worlds;
        readonly List<Transform> buildings = new List<Transform>();
        sealed class Unit
        {
            public Transform model;
            public LineRenderer beam;
            public bool ship;
            public float3 position, beamEnd;
            public float health = 70, age, fire;
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
            bool rebuilt = worlds != game.System.Descriptor;
            if (rebuilt)
            {
                worlds = game.System.Descriptor;
                deployed = collapse = 0; next = null;
                if (hole != null) Destroy(hole.gameObject);
                if (impact != null) Destroy(impact.gameObject);
            }
            foreach (var building in buildings) if (building != null) Destroy(building.gameObject);
            buildings.Clear(); plumes.Clear();
            foreach (var unit in units) if (unit.model != null) Destroy(unit.model.gameObject);
            units.Clear();
            shownWorld = -1;
            foreach (var colony in game.Colonies)
            {
                var front = State(colony.world);
                if (!front.factory) continue;
                if (rebuilt) game.System.Bodies[colony.world].Level(colony.site, front.shipyard ? 35 : 13);
                colony.site = math.normalizesafe(colony.site) * game.System.Descriptor.bodies[colony.world].GroundRadius(math.normalizesafe(colony.site));
                var foundations = game.System.Descriptor.bodies[colony.world].foundations;
                foundations.RemoveAll(item => math.distance(item.xyz, colony.site) < 2);
                foundations.Add(new float4(colony.site, front.shipyard ? 35 : 13));
                var model = Models.Building(front.shipyard ? Building.ShipFactory : Building.Factory);
                model.name = front.shipyard ? "Robodog shipyard" : "Robodog factory";
                model.SetParent(game.System.Bodies[colony.world].transform, false);
                model.localPosition = colony.site;
                model.localRotation = Quaternion.FromToRotation(Vector3.up, math.normalizesafe(colony.site));
                var defence = model.gameObject.AddComponent<FactoryDefence>(); defence.game = game; defence.world = colony.world;
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
                    var engine = Shapes.Part(moon.transform, "Moon engine", casing.Mesh("Moon drive casing"), Shapes.Solid(Surface.Metal, new Color(0.6f, 0.7f, 0.75f)), up * moon.Descriptor.GroundRadius(up), Vector3.one * math.max(4, moon.Descriptor.radius * 0.025f)).transform;
                    engine.localRotation = Quaternion.FromToRotation(Vector3.up, up);
                    var plume = Shapes.Part(engine, "Drive plume", Shapes.Sphere(1, 24), Shapes.Material("Glow", new Color(0.2f, 2, 4)), Vector3.up * 13, new Vector3(2, 8, 2), false).transform;
                    plume.gameObject.SetActive(false); plumes.Add(plume);
                    buildings.Add(engine);
                }
        }
        public bool CanBuild(int choice) => choice == 0 ? game.System.ReferenceBody.kind == BodyKind.Rock && !Local.factory && Local.kills >= War.RequiredKills(Local)
            : choice == 1 ? Local.factory && !Local.shipyard && Local.aliens <= 0
            : game.Stage >= Campaign.Won && Local.shipyard && Local.aliens <= 0 && Local.alienShips <= 0 && !game.Fall.active;
        public string FactoryHint(int choice)
        {
            var front = Local;
            return choice == 0 && front.factory ? "FACTORY BUILT · " + (front.mindDead ? "Its robodogs replace losses and will finish clearing this planet." : "Kill the living mind to release the robodogs’ full strength.")
                : choice == 0 ? $"ROBODOG FACTORY · {front.kills}/{War.RequiredKills(front)} ALIENS DEFEATED\nReplicating laser robodogs hunt the remaining {math.ceil(front.aliens):0} bodies. Production stops when this world is clear."
                : choice == 1 ? $"SHIPYARD UPGRADE · {(front.shipyard ? "BUILT" : !front.factory ? "BUILD A ROBODOG FACTORY FIRST" : front.aliens > 0 ? $"{math.ceil(front.aliens):0} ALIENS REMAIN" : "READY")}\nYour robots assemble ships to defend this planet and its immediate neighbours."
                : $"MOON ENGINES · {(game.Stage < Campaign.Won ? "DESTROY ALL CITADEL MINDS" : !front.shipyard ? "BUILD THIS WORLD’S SHIPYARD" : front.alienShips > 0 ? $"{math.ceil(front.alienShips):0} HOSTILE SHIPS REMAIN" : game.Fall.active ? "ENGINES BUILT" : "READY")}\nThe impact collapses the buried mind into a singularity that consumes this system.";
        }
        public void BuildFactory(int choice)
        {
            var front = Local;
            if (choice == 2)
            {
                if (game.Stage >= Campaign.Won && front.shipyard && front.aliens <= 0 && front.alienShips <= 0 && !game.Fall.active) Launch();
                else game.Notify(FactoryHint(choice).Split('\n')[0]);
                return;
            }
            var body = game.System.ReferenceBody;
            if (body.kind != BodyKind.Rock) { game.Notify("A ROCKY WORLD IS REQUIRED"); return; }
            if (!War.Build(front, choice == 1)) { game.Notify(FactoryHint(choice).Split('\n')[0]); return; }
            if (!game.Colonies.Any(colony => colony.world == front.world))
            {
                float3 ahead = math.normalizesafe(game.Player.Position + game.Player.walker.forward * 12);
                float3 site = ahead * body.GroundRadius(ahead);
                game.System.Bodies[front.world].Level(site, 13);
                site = ahead * body.GroundRadius(ahead);
                game.Colonies.Add(new Colony { world = front.world, site = site });
            }
            if (choice == 1)
            {
                var colony = game.Colonies.First(item => item.world == front.world);
                game.System.Bodies[front.world].Level(colony.site, 35);
                colony.site = math.normalizesafe(colony.site) * body.GroundRadius(math.normalizesafe(colony.site));
            }
            front.established = true;
            assembly = 0;
            Settle();
            game.Hermit.Remark(choice == 0 ? "Factory seed awake. Each robodog helps assemble more robodogs. A living mind can overwhelm our early patrols. Kill it to unlock their full strength. Then this factory will replace losses and conquer the planet." : "Our robots are assembling autonomous ships. Help them break the living fleet. Their reach stops at the neighbouring planet.");
            if (game.Stage == 3 && choice == 0) game.Advance();
            DrawUnits(0);
            game.Write();
        }
        public void Launch()
        {
            game.Fall.began = game.System.Clock;
            var descriptor = game.System.Descriptor;
            game.Fall.duration = Moonfall.Assembly + Moonfall.Ignition + math.max(18, math.distance(descriptor.OrbitalPosition(game.Fall.moon, game.System.Clock), descriptor.OrbitalPosition(game.Fall.world, game.System.Clock)) / Core.Autopilot.Speed);
            game.Player.flying = true;
            game.Fall.active = true;
            Settle();
            game.Hermit.Remark("The moon engines are built. Our ship is linked to follow from a safe distance. Watch the ignition. Then we strike the buried mind.");
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
            foreach (var unit in units) things.Add(new Scannable { name = unit.ship ? "Foundry autonomous combat ship" : "Foundry laser robodog", foot = unit.position, height = unit.ship ? 8 : 2, girth = unit.ship ? 10 : 1.2f });
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
                if (collapse >= 3 + Moonfall.Consumption + Moonfall.Supernova && next.IsCompletedSuccessfully)
                {
                    game.PreparedSystem = next.Result; next = null;
                    game.NextSystem(); collapse = 0; arrival = 1;
                    var body = game.System.ReferenceBody;
                    float3 up = math.normalizesafe(game.Player.Position);
                    game.Player.flying = true;
                    game.Player.ship.position = up * (body.radius * Shapes.AirTop(body) + 150);
                    game.Player.ship.velocity = float3.zero; game.Player.ship.landed = false;
                    game.Player.ship.rotation = quaternion.LookRotationSafe(-up, math.normalizesafe(Walker.Project(new float3(0, 1, 0), up), new float3(0, 0, 1)));
                    if (hole != null) Destroy(hole.gameObject);
                    if (impact != null) Destroy(impact.gameObject);
                }
                return;
            }
            if (game.Buried) return;
            game.Presence.Step(dt);
            for (int i = 0; i < game.System.Descriptor.bodies.Length; i++)
            {
                var front = State(i);
                War.Step(front, dt);
                game.System.Descriptor.bodies[i].blight = game.System.Descriptor.bodies[i].SeaRadius > 0 ? math.saturate(front.aliens / 600) : 0;
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
            if (!game.Battle.quiet && game.Stage >= 2 && (deployed -= dt) <= 0)
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
            if (game.Fall.active && !game.Fall.destroyed)
            {
                float3 moon = game.System.ToReference(game.Fall.moon);
                float radius = game.System.Descriptor.bodies[game.Fall.moon].radius;
                float3 direction = math.normalizesafe(game.System.ToReference(game.Fall.world) - moon, new float3(0, 0, 1));
                float3 above = math.normalizesafe(math.cross(direction, new float3(1, 0, 0)), new float3(0, 1, 0));
                game.Player.ship.position = moon - direction * (radius * 2.5f + 200) + above * radius;
                float ignition = math.saturate((float)(game.System.Clock - game.Fall.began - Moonfall.Assembly) / Moonfall.Ignition);
                foreach (var plume in plumes) { plume.gameObject.SetActive(ignition > 0); plume.localScale = new Vector3(2, 8 * ignition, 2); }
                game.Player.ship.velocity = float3.zero;
                game.Player.ship.rotation = quaternion.LookRotationSafe(moon - game.Player.ship.position, new float3(0, 1, 0));
                game.Player.ship.landed = false;
            }
            if (game.Fall.Finished(game.System.Clock))
            {
                game.Fall.destroyed = true;
                collapse = 0;
                game.Sounds.Nuke();
                impact = Shapes.Part(transform, "Moon impact", Shapes.Sphere(1, 48), Shapes.Material("Glow", new Color(1, 0.3f, 0.7f)), Vector3.zero, Vector3.one, false).transform;
                game.Hermit.Remark("The mind is collapsing. Everything is falling towards it. You are not dead. Stay with me.");
                game.Write();
            }
            if ((saved += dt) > 15) { saved = 0; game.Write(); }
        }
        void DrawUnits(float dt)
        {
            var front = Local;
            int dogs = math.min(7, (int)math.ceil(front.dogs)), ships = math.min(3, (int)front.ships);
            assembly -= dt;
            if (shownWorld != game.System.Reference)
            {
                foreach (var unit in units) Destroy(unit.model.gameObject);
                units.Clear();
                shownWorld = game.System.Reference;
            }
            for (int i = units.Count - 1; i >= 0; i--)
                if (units[i].health <= 0 || units.Count(unit => unit.ship == units[i].ship) > (units[i].ship ? ships : dogs))
                { Destroy(units[i].model.gameObject); units.RemoveAt(i); }
            float3 home = game.Colonies.FirstOrDefault(colony => colony.world == shownWorld)?.site ?? game.Player.Position;
            // Add only the new unit. Existing meshes, animation and positions remain intact.
            for (int kind = 0; kind < 2; kind++)
                for (int i = units.Count(unit => unit.ship == (kind == 1)); assembly <= 0 && i < (kind == 1 ? ships : dogs); i++)
                {
                    bool ship = kind == 1;
                    var model = ship ? Models.Ship() : Models.Robot();
                    if (!ship) model.gameObject.AddComponent<MachineMotion>().quadruped = true;
                    var beam = new GameObject("Dorsal laser").AddComponent<LineRenderer>();
                    beam.transform.SetParent(model, false); beam.positionCount = 2; beam.startWidth = beam.endWidth = ship ? 0.18f : 0.06f;
                    beam.sharedMaterial = Shapes.Material("Glow", new Color(0.2f, 2, 3)); beam.enabled = false;
                    units.Add(new Unit { model = model, beam = beam, ship = ship, health = front.mindDead ? 350 : 25, position = home + (float3)(Quaternion.FromToRotation(Vector3.up, math.normalizesafe(home)) * new Vector3(0, ship ? 6 : 0, 1)) });
                    assembly = 3;
                }
            foreach (var unit in units)
            {
                float3 up = math.normalizesafe(unit.position);
                unit.age += dt;
                float patrol = math.min(180, 12 + math.sqrt(front.dogs) * 6);
                float angle = Time.time * 0.14f + units.IndexOf(unit) * 2.4f;
                float3 target = home + (float3)(Quaternion.FromToRotation(Vector3.up, up) * new Vector3(math.cos(angle) * patrol, 0, math.sin(angle) * patrol));
                int enemy = -1; float nearest = unit.ship ? 2500 : patrol;
                for (int i = 0; i < game.Battle.Foes.Count; i++)
                {
                    var foe = game.Battle.Foes[i]; float distance = math.distance(foe.position, unit.position);
                    if (unit.age < 3 || foe.friendly || foe.health <= 0 || (foe.kind == FoeKind.Raider) != unit.ship || distance >= nearest || !unit.ship && math.distance(foe.position, home) > patrol) continue;
                    enemy = i; nearest = distance; target = foe.position;
                }
                if (unit.age < 3) target = home + (float3)(Quaternion.FromToRotation(Vector3.up, up) * Vector3.forward * 9);
                float3 forward = math.normalizesafe(Walker.Project(target - unit.position, up), math.cross(up, new float3(1, 0, 0)));
                if (enemy < 0 || nearest > (unit.ship ? 80 : 12)) unit.position += forward * dt * (unit.ship ? 100 : unit.age < 3 ? 3 : 7);
                up = math.normalizesafe(unit.position);
                unit.position = up * (game.System.ReferenceBody.GroundRadius(up) + (unit.ship ? 130 : 0));
                unit.model.SetPositionAndRotation(unit.position - game.System.Anchor, quaternion.LookRotationSafe(forward, up));
                if (!unit.ship) unit.model.GetComponent<MachineMotion>().speed = enemy >= 0 ? 9 : 3;
                unit.beam.enabled = enemy >= 0 && nearest < (unit.ship ? 650 : 50) && !game.Outpost.Shield.Blocks(unit.position + up * 1.4f, target - unit.position - up * 1.4f, out _);
                if (unit.beam.enabled)
                {
                    unit.beam.SetPosition(0, unit.position + up * 1.4f - game.System.Anchor); unit.beam.SetPosition(1, unit.beamEnd - game.System.Anchor);
                    if ((unit.fire -= dt) <= 0)
                    {
                        unit.fire = 0.15f;
                        var gun = Combat.Arms[1]; gun.damage = 0.15f * (unit.ship ? 22 : front.mindDead ? 50 : 3);
                        unit.beamEnd = game.Battle.FriendlyFire(unit.position + up * 1.4f, target, gun);
                        unit.beam.SetPosition(1, unit.beamEnd - game.System.Anchor);
                    }
                }
                for (int i = game.Battle.Bolts.Count - 1; i >= 0; i--)
                {
                    var bolt = game.Battle.Bolts[i];
                    if (!bolt.hostile || !Combat.Crosses(bolt.position, bolt.velocity * dt, unit.position + up, unit.ship ? 8 : 1.4f)) continue;
                    unit.health -= bolt.damage * (front.mindDead ? 0.15f : 1); game.Battle.Bolts.RemoveAt(i);
                }
                if (unit.health <= 0)
                {
                    if (unit.ship) front.ships = math.max(0, front.ships - 1); else front.dogs = math.max(0, front.dogs - 1);
                    game.Corpses.AddModel(unit.model, unit.position, float3.zero, unit.ship);
                    unit.model.gameObject.SetActive(false);
                }
            }
        }
        void LateUpdate()
        {
            if (game.Fall.active && !Collapsing)
            {
                if (DeviceControls.VR) return;
                float3 moon = game.System.ToReference(game.Fall.moon);
                var followView = Camera.main.transform;
                Vector3 behind = math.rotate(game.Player.ship.rotation, new float3(0, 8, -35));
                followView.position = (Vector3)(game.Player.ship.position - game.System.Anchor) + behind;
                followView.rotation = Quaternion.LookRotation(moon - game.System.Anchor - (float3)followView.position, math.rotate(game.Player.ship.rotation, new float3(0, 1, 0)));
                return;
            }
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
            hole.localScale = Vector3.one * radius * math.saturate(collapse / 3);
            if (impact != null)
            {
                bool supernova = collapse >= 3 + Moonfall.Consumption;
                impact.position = centre;
                impact.localScale = Vector3.one * (supernova ? radius * Whiteout * 14 : radius * math.max(0, 2 - collapse));
                impact.GetComponent<MeshRenderer>().sharedMaterial.color = supernova ? Color.white : new Color(1, 0.3f, 0.7f);
            }
            for (int i = 0; i < system.Bodies.Length; i++)
            {
                var body = system.Bodies[i];
                float delay = i == 0 ? 10 : 1 + i * 0.1f;
                float t = math.saturate((collapse - 3 - delay) / (Moonfall.Consumption - delay));
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
            if (DeviceControls.VR && UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.Head).TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out Quaternion head)) view.rotation *= head;
        }
    }
}
