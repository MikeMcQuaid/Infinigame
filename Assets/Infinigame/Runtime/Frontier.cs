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
        int shownWorld = -1, shownRegion = -1, layout = -1;
        Task<SolarSystemDescriptor> next;
        Transform hole, impact, robotTemplate, shipTemplate;
        SolarSystemDescriptor worlds;
        readonly List<Transform> buildings = new List<Transform>();
        sealed class Unit
        {
            public Transform model;
            public LineRenderer beam;
            public bool ship, escort;
            public Front front;
            public float3 position, beamEnd;
            public float health = 70, age, fire;
        }
        readonly List<Unit> units = new List<Unit>();
        public string FleetHint => !game.Presence.fronts.Any(front => front.shipyard) ? "BUILD A SHIPYARD TO ASSEMBLE ESCORTS"
            : $"{units.Count(unit => unit.escort && unit.health > 0)}/{game.Escorts.count} ESCORTS DEPLOYED · " + (game.Escorts.order == EscortOrder.Follow ? "FOLLOW: stay beside you and defend against nearby ships, including during autopilot."
            : game.Escorts.order == EscortOrder.Attack ? "ATTACK: engage hostile ships within 2.5 km of you."
            : "PATROL: return to the shipyard and defend its orbit.");
        void Start() => game.System.ReferenceChanged += Rebase;
        void OnDestroy() { if (game?.System != null) game.System.ReferenceChanged -= Rebase; }
        void Rebase(int from, int to)
        {
            foreach (var unit in units.Where(unit => unit.escort))
            {
                float3 velocity = float3.zero;
                game.System.Descriptor.Rebase(from, to, game.System.Clock, ref unit.position, ref velocity);
                unit.beam.enabled = false; unit.fire = 0;
            }
        }
        public Front Local => State(game.System.Reference);
        public int Region(int world, float3 position)
        {
            if (world != SolarSystemDescriptor.Home) return 0;
            int nearest = 0; float distance = float.MaxValue;
            float3 direction = math.normalizesafe(position);
            for (int region = 0; region < 3; region++)
            {
                float gap = math.distancesq(direction, Campaign.Site(game.System.Descriptor, Campaign.FirstBurrow + region));
                if (gap < distance) { nearest = region; distance = gap; }
            }
            return nearest;
        }
        public float Total(int world, System.Func<Front, float> count) => Enumerable.Range(0, world == SolarSystemDescriptor.Home ? 3 : 1).Sum(region => count(State(world, region)));
        public bool GroundControlled(int world) => Enumerable.Range(0, world == SolarSystemDescriptor.Home ? 3 : 1).All(region => { var front = State(world, region); return front.factory && front.mindDead && front.aliens <= 0; });
        public Front State(int world, int region = -1)
        {
            if (region < 0) region = world == game.System.Reference ? Region(world, game.Player.Position) : 0;
            var front = game.Presence.fronts.FirstOrDefault(item => item.world == world && item.region == region);
            if (front != null) return front;
            int mission = Enumerable.Range(0, Campaign.Citadel + 1).FirstOrDefault(stage => Campaign.World(game.System.Descriptor, stage) == world);
            int rank = world == SolarSystemDescriptor.Home ? 0 : mission >= Campaign.Tower ? mission - Campaign.Tower + 1 : 8;
            front = War.Create(world, game.Depth, rank);
            front.region = region;
            if (world == SolarSystemDescriptor.Home) front.aliens /= 3;
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
            int signature = game.Colonies.Count * 4 + game.Presence.fronts.Count(front => front.shipyard) + (game.Fall.active ? 1000 : 0);
            if (!rebuilt && layout == signature) return;
            layout = signature;
            foreach (var building in buildings) if (building != null) Destroy(building.gameObject);
            buildings.Clear(); plumes.Clear();
            if (rebuilt)
            {
                foreach (var unit in units) if (unit.model != null) Destroy(unit.model.gameObject);
                units.Clear(); shownWorld = -1;
            }
            foreach (var colony in game.Colonies)
            {
                var front = State(colony.world, colony.region);
                if (!front.factory) continue;
                if (rebuilt) game.System.Bodies[colony.world].Level(colony.site, front.shipyard ? 18 : 13);
                colony.site = math.normalizesafe(colony.site) * game.System.Descriptor.bodies[colony.world].GroundRadius(math.normalizesafe(colony.site));
                var foundations = game.System.Descriptor.bodies[colony.world].foundations;
                foundations.RemoveAll(item => math.distance(item.xyz, colony.site) < 2);
                foundations.Add(new float4(colony.site, front.shipyard ? 18 : 13));
                var model = Models.Building(front.shipyard ? Building.ShipFactory : Building.Factory);
                model.name = front.shipyard ? "Robodog shipyard" : "Robodog factory";
                model.SetParent(game.System.Bodies[colony.world].transform, false);
                model.localPosition = colony.site;
                model.localRotation = Quaternion.FromToRotation(Vector3.up, math.normalizesafe(colony.site));
                var defence = model.gameObject.AddComponent<FactoryDefence>(); defence.game = game; defence.world = colony.world;
                buildings.Add(model);
            }
            if (game.Fall.active && !game.Fall.destroyed)
                for (int i = 0; i < 3; i++)
                {
                    var moon = game.System.Bodies[game.Fall.moon];
                    float angle = i * math.PI * 2 / 3;
                    float3 up = math.normalize(new float3(math.cos(angle), 0.5f, math.sin(angle)));
                    var casing = new MeshKit();
                    casing.Lathe(new[] { 0f, 0f, 3f, 0f, 3.5f, 1f, 2.6f, 6f, 3.2f, 9f, 2.4f, 9f, 1.9f, 6f, 0f, 1f }, 64);
                    var engine = Shapes.Part(moon.transform, "Moon engine", casing.Mesh("Moon drive casing"), Shapes.Solid(Surface.Metal, new Color(0.6f, 0.7f, 0.75f)), up * moon.Descriptor.GroundRadius(up), Vector3.one * math.max(4, moon.Descriptor.radius * 0.025f)).transform;
                    engine.localRotation = Quaternion.FromToRotation(Vector3.up, up);
                    var plume = Shapes.Part(engine, "Drive plume", Shapes.Sphere(1, 24), Shapes.Material("Glow", new Color(1.5f, 0.45f, 0.08f)), Vector3.up * 13, new Vector3(2, 8, 2), false).transform;
                    plume.gameObject.SetActive(false); plumes.Add(plume);
                    buildings.Add(engine);
                }
        }
        public bool CanBuild(int choice) => choice == 0 ? !game.Player.flying && !game.System.ReferenceBody.Dry(game.Player.Position) && game.Player.Altitude < 5 && game.System.ReferenceBody.kind == BodyKind.Rock && !Local.factory && game.FactoryUnlocked && Local.kills >= War.RequiredKills(Local)
            : choice == 1 ? Local.factory && !Local.shipyard && GroundControlled(game.System.Reference)
            : game.Stage >= Campaign.Won && Local.shipyard && GroundControlled(game.System.Reference) && Total(game.System.Reference, front => front.alienShips) <= 0 && !game.Fall.active;
        public string FactoryHint(int choice)
        {
            var front = Local;
            return choice == 0 && front.factory ? "FACTORY BUILT · " + (front.mindDead ? "Its robodogs replace losses and will finish clearing this region." : "Kill the living mind to release the robodogs’ full strength.")
                : choice == 0 && !game.FactoryUnlocked ? "RECOVER THE SECOND BURROW’S THRUSTERS TO UNLOCK FACTORIES"
                : choice == 0 && (game.Player.flying || game.System.ReferenceBody.Dry(game.Player.Position) || game.Player.Altitude >= 5) ? "LAND ON CLEAR SURFACE GROUND TO BUILD"
                : choice == 0 ? $"REGION {front.region + 1} · ROBODOG FACTORY · {front.kills}/{War.RequiredKills(front)} ALIENS DEFEATED\nReplicating laser robodogs hunt the remaining {math.ceil(front.aliens):0} bodies. Production stops when this region is clear."
                : choice == 1 ? $"SHIPYARD UPGRADE · {(front.shipyard ? "BUILT" : !front.factory ? "BUILD A ROBODOG FACTORY FIRST" : !GroundControlled(front.world) ? "CONTROL EVERY REGION WITH A FACTORY" : "READY")}\nYour robots assemble ships to defend this planet and its immediate neighbours."
                : $"MOON ENGINES · {(game.Stage < Campaign.Won ? "DESTROY ALL CITADEL MINDS" : !front.shipyard ? "BUILD THIS WORLD’S SHIPYARD" : Total(front.world, state => state.alienShips) > 0 ? $"{math.ceil(Total(front.world, state => state.alienShips)):0} HOSTILE SHIPS REMAIN" : game.Fall.active ? "ENGINES BUILT" : "READY")}\nThe impact collapses the buried mind into a singularity that consumes this system.";
        }
        public void BuildFactory(int choice)
        {
            var front = Local;
            if (choice == 2)
            {
                if (game.Stage >= Campaign.Won && front.shipyard && CanBuild(2)) Launch();
                else game.Notify(FactoryHint(choice).Split('\n')[0]);
                return;
            }
            var body = game.System.ReferenceBody;
            if (body.kind != BodyKind.Rock) { game.Notify("A ROCKY WORLD IS REQUIRED"); return; }
            if (!CanBuild(choice) || !War.Build(front, choice == 1)) { game.Notify(FactoryHint(choice).Split('\n')[0]); return; }
            if (!game.Colonies.Any(colony => colony.world == front.world && colony.region == front.region))
            {
                float3 ahead = math.normalizesafe(game.Player.Position + game.Player.walker.forward * 12);
                float3 site = ahead * body.GroundRadius(ahead);
                game.System.Bodies[front.world].Level(site, 13);
                site = ahead * body.GroundRadius(ahead);
                game.Colonies.Add(new Colony { world = front.world, region = front.region, site = site });
            }
            if (choice == 1)
            {
                game.Escorts.count = 1;
                game.Escorts.order = EscortOrder.Follow;
                var colony = game.Colonies.First(item => item.world == front.world && item.region == front.region);
                game.System.Bodies[front.world].Level(colony.site, 18);
                colony.site = math.normalizesafe(colony.site) * body.GroundRadius(math.normalizesafe(colony.site));
            }
            front.established = true;
            assembly = deployed = 0;
            Settle();
            game.Hermit.Remark(choice == 0 ? "Factory online. Robodogs control the alien population in this region. Kill its mind so our machines can replace their losses and win. Each Earth region needs its own factory. Spend recovered salvage in Equipment to modify your suit." : "Shipyard online. Your first escort is launching now and will follow you, including on autopilot. Open Fleet orders in the pause menu to command up to three ships. The rest defend this planet and its neighbours.");

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
        public void Punch(float3 from, float3 step)
        {
            foreach (var unit in units)
                if (Combat.Crosses(from, step, unit.position + math.normalizesafe(unit.position), unit.ship ? 3 : 1.2f)) { game.Hermit.Punched(); break; }
        }
        public IEnumerable<(float3 position, bool ship)> Contacts => units.Where(unit => unit.health > 0).Select(unit => (unit.position, unit.ship));
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
                for (int region = 0; region < (i == SolarSystemDescriptor.Home ? 3 : 1); region++) War.Step(State(i, region), dt, game.Pressure);
                var front = State(i);
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
                deployed = math.max(1.5f, 8 - game.Threat * 0.5f) / game.Pressure;
                var body = game.System.ReferenceBody;
                var front = Local;
                if (body.kind == BodyKind.Rock && front.aliens > 0 && game.Battle.Foes.Count(foe => foe.patrol) < math.min(40, 4 + game.Threat * 3))
                {
                    var rnd = new Unity.Mathematics.Random((uint)(game.Presence.played * 100 + game.Seed) | 1);
                    var factory = game.Colonies.FirstOrDefault(colony => colony.world == front.world && colony.region == front.region && math.distance(colony.site, game.Player.Position) < 700);
                    float3 up = math.normalizesafe(factory?.site ?? game.Player.Position);
                    float3 direction = math.normalize(up + Walker.Project(rnd.NextFloat3Direction(), up) * ((factory == null ? rnd.NextFloat(70, 160) : rnd.NextFloat(25, 55)) / body.radius));
                    game.Battle.Add(FoeKind.Sentry, direction * (body.GroundRadius(direction) + 0.9f), game.Stage <= 2 ? MotileCaste.Stalker : (MotileCaste)rnd.NextInt(0, 3));
                    var foe = game.Battle.Foes[^1]; foe.patrol = true; game.Battle.Foes[^1] = foe;
                }
            }
            if (!game.Battle.quiet && game.Player.flying && Local.alienShips > 0 && units.Any(unit => unit.ship && math.distance(unit.position, game.Player.Position) < 2000)
                && game.Battle.Foes.Count(foe => foe.kind == FoeKind.Raider) < 3 && deployed > 0 && deployed <= dt)
            {
                float3 point = game.Player.Position + math.rotate(game.Player.ship.rotation, new float3(180, 80, 500));
                float3 up = math.normalizesafe(point);
                if (game.System.ReferenceBody.kind == BodyKind.Rock) point = up * math.max(math.length(point), game.System.ReferenceBody.GroundRadius(up) + 100);
                game.Battle.Add(FoeKind.Raider, point);
            }
            if (GroundControlled(game.System.Reference))
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
                game.Player.ship.position = moon - direction * (radius * 5 + 400) + above * radius;
                float ignition = math.saturate((float)(game.System.Clock - game.Fall.began - Moonfall.Assembly) / Moonfall.Ignition);
                for (int i = 0; i < plumes.Count; i++)
                {
                    var plume = plumes[i]; var engine = plume.parent;
                    float angle = i * math.PI * 2 / 3;
                    float3 aft = math.normalize(-direction + (above * math.cos(angle) + math.cross(direction, above) * math.sin(angle)) * 0.24f);
                    engine.position = moon - game.System.Anchor + aft * radius;
                    engine.rotation = Quaternion.FromToRotation(Vector3.up, aft);
                    plume.gameObject.SetActive(ignition > 0);
                    plume.localScale = new Vector3(2, (10 + math.sin(Time.time * 19 + i) * 0.6f) * ignition, 2);
                }
                game.Player.ship.velocity = float3.zero;
                game.Player.ship.rotation = quaternion.LookRotationSafe(moon - game.Player.ship.position, new float3(0, 1, 0));
                game.Player.ship.landed = false;
            }
            if (game.Fall.Finished(game.System.Clock))
            {
                game.Fall.destroyed = true;
                game.System.Bodies[game.Fall.moon].gameObject.SetActive(false);
                game.System.Bodies[game.Fall.world].gameObject.SetActive(false);
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
            var source = game.Presence.fronts.Where(item => item.shipyard && item.ships > 0).OrderBy(item => item == front ? 0 : 1).FirstOrDefault();
            int escorts = source == null ? 0 : math.min(math.clamp(game.Escorts.count, 1, 3), (int)math.ceil(source.ships));
            int dogs = math.min(7, (int)math.ceil(front.dogs)), ships = math.min(3 - escorts, (int)front.ships - (source == front ? escorts : 0));
            assembly -= dt;
            if (shownWorld != game.System.Reference || shownRegion != front.region)
            {
                foreach (var unit in units.Where(unit => !unit.escort)) Destroy(unit.model.gameObject);
                units.RemoveAll(unit => !unit.escort);
                shownWorld = game.System.Reference; shownRegion = front.region;
            }
            for (int i = units.Count - 1; i >= 0; i--)
            {
                var unit = units[i];
                if (unit.health <= 0 || units.Count(other => other.ship == unit.ship && other.escort == unit.escort) > (unit.escort ? escorts : unit.ship ? ships : dogs))
                { Destroy(unit.model.gameObject); units.RemoveAt(i); }
            }
            // Keep assembly staggered, including escorts, and launch the actual AI model.
            for (int step = 0; step < 3 && assembly <= 0; step++)
            {
                int kind = step == 0 ? 2 : step == 1 ? 0 : 1;
                bool ship = kind > 0, escort = kind == 2;
                if (units.Count(unit => unit.ship == ship && unit.escort == escort) >= (escort ? escorts : ship ? ships : dogs)) continue;
                if (ship && shipTemplate == null) { shipTemplate = Models.Ship(); shipTemplate.SetParent(transform, false); shipTemplate.gameObject.SetActive(false); }
                if (robotTemplate == null) { robotTemplate = Models.Robot(); robotTemplate.SetParent(transform, false); robotTemplate.gameObject.SetActive(false); }
                var model = Instantiate(ship ? shipTemplate : robotTemplate); model.gameObject.SetActive(true);
                model.name = escort ? "Fleet escort" : ship ? "Robot patrol ship" : "Robodog patrol";
                if (!ship) model.gameObject.AddComponent<MachineMotion>().quadruped = true;
                else
                {
                    model.localScale = Vector3.one * 0.35f;
                    var pilot = Instantiate(robotTemplate); pilot.gameObject.SetActive(true); pilot.name = "Robot pilot"; pilot.SetParent(model, false);
                    pilot.localPosition = new Vector3(0, 0.7f, 1.5f); pilot.localScale = Vector3.one * 0.45f;
                }
                var beam = new GameObject("Dorsal laser").AddComponent<LineRenderer>();
                beam.transform.SetParent(model, false); beam.positionCount = 2; beam.startWidth = beam.endWidth = ship ? 0.18f : 0.06f;
                beam.sharedMaterial = Shapes.Material("Glow", new Color(0.2f, 2, 3)); beam.enabled = false;
                var origin = escort ? source : front;
                var colony = game.Colonies.FirstOrDefault(item => item.world == origin.world && item.region == origin.region);
                float3 position = colony?.site ?? game.Player.Position, velocity = float3.zero;
                if (origin.world != game.System.Reference) game.System.Descriptor.Rebase(origin.world, game.System.Reference, game.System.Clock, ref position, ref velocity);
                units.Add(new Unit { model = model, beam = beam, ship = ship, escort = escort, front = origin, health = ship || origin.mindDead ? 350 : 25, position = position });
                assembly = 3;
            }
            int escortIndex = 0;
            foreach (var unit in units)
            {
                var colony = game.Colonies.FirstOrDefault(item => item.world == unit.front.world && item.region == unit.front.region);
                float3 home = colony?.site ?? unit.position, homeUp = math.normalizesafe(home), still = float3.zero;
                if (unit.front.world != game.System.Reference)
                {
                    homeUp = math.rotate(game.System.Descriptor.Turn(unit.front.world, game.System.Reference, game.System.Clock), homeUp);
                    game.System.Descriptor.Rebase(unit.front.world, game.System.Reference, game.System.Clock, ref home, ref still);
                }
                unit.age += dt;
                bool following = unit.escort && game.Escorts.order != EscortOrder.Patrol;
                float patrol = math.min(180, 12 + math.sqrt(unit.front.dogs) * 6);
                float angle = Time.time * 0.14f + units.IndexOf(unit) * 2.4f;
                float3 target = home + (float3)(Quaternion.FromToRotation(Vector3.up, homeUp) * new Vector3(math.cos(angle) * patrol, unit.ship ? 130 : 0, math.sin(angle) * patrol));
                if (following)
                {
                    int place = escortIndex++;
                    target = game.Player.Position + math.rotate(game.Player.ship.rotation, new float3((place % 2 == 0 ? -1 : 1) * (28 + place * 12), 18, -45 - place * 20));
                }
                int enemy = -1; float nearest = unit.ship ? 2500 : patrol + 35;
                for (int i = 0; i < game.Battle.Foes.Count; i++)
                {
                    var foe = game.Battle.Foes[i]; float distance = math.distance(foe.position, unit.position);
                    if (unit.age < 5 || foe.friendly || foe.health <= 0 || foe.protectedCore || (foe.kind == FoeKind.Raider) != unit.ship || distance >= nearest) continue;
                    if (unit.ship ? math.distance(foe.position, following ? game.Player.Position : home) > (following && game.Escorts.order == EscortOrder.Follow ? 700 : 2500) : math.distance(foe.position, home) > patrol + 35) continue;
                    if (!game.Outpost.Visible(unit.position + math.normalizesafe(unit.position) * 1.4f, foe.position)) continue;
                    enemy = i; nearest = distance;
                }
                float3 aim = enemy >= 0 ? game.Battle.Foes[enemy].position : target;
                if (enemy >= 0 && (!following || game.Escorts.order == EscortOrder.Attack)) target = aim;
                float3 before = unit.position;
                if (unit.age < 5)
                    unit.position = home + homeUp * (unit.age < 3 ? math.lerp(unit.ship ? -3 : -1.6f, 0, unit.age / 3) : unit.ship ? (unit.age - 3) * 20 : 0)
                        + (float3)(Quaternion.FromToRotation(Vector3.up, homeUp) * Vector3.forward) * math.max(0, unit.age - 3) * 5;
                else if (unit.ship)
                {
                    if (following || enemy < 0 || nearest > 80) unit.position = War.Follow(unit.position, target, following ? game.Player.Velocity : float3.zero, dt);
                    float3 velocity = unit.position - before;
                    game.Outpost.Shield.Bounce(before, ref unit.position, ref velocity, 4);
                    if (game.Outpost.World == game.System.Reference && Combat.Crosses(before, unit.position - before, game.Outpost.Position, game.Stage == Campaign.Citadel ? 1500 : 120))
                        unit.position = game.Outpost.Resolve(before, unit.position, 3, out _);
                }
                else if (enemy < 0 || nearest > 12)
                    unit.position = game.Outpost.Resolve(before, before + math.normalizesafe(Walker.Project(target - before, math.normalizesafe(before))) * dt * 7, 0.7f, out _);
                float3 up = math.normalizesafe(unit.position);
                if (unit.age >= 5 && game.System.ReferenceBody.kind == BodyKind.Rock)
                    unit.position = up * (unit.ship ? math.max(math.length(unit.position), game.System.ReferenceBody.GroundRadius(up) + 20) : game.System.ReferenceBody.GroundRadius(up));
                float3 forward = math.normalizesafe(unit.ship ? aim - unit.position : Walker.Project(target - unit.position, up), math.cross(up, new float3(1, 0, 0)));
                unit.model.SetPositionAndRotation(unit.position - game.System.Anchor, quaternion.LookRotationSafe(forward, up));
                if (!unit.ship) unit.model.GetComponent<MachineMotion>().speed = enemy >= 0 ? 9 : 3;
                if (game.Hermit.Provoked && math.distance(unit.position, game.Player.Position) < 50 && game.Outpost.Visible(unit.position, game.Player.Position))
                {
                    unit.beam.enabled = true; unit.beam.SetPosition(0, unit.position + up - game.System.Anchor);
                    unit.beam.SetPosition(1, game.Player.Position + up - game.System.Anchor);
                    game.Player.Hurt(100 * dt, "Destroyed by robot defensive fire after repeated attacks");
                    continue;
                }
                unit.beam.enabled = enemy >= 0 && nearest < (unit.ship ? 650 : 50) && game.Outpost.Visible(unit.position + up * 1.4f, aim) && !game.Outpost.Shield.Blocks(unit.position + up * 1.4f, aim - unit.position - up * 1.4f, out _);
                if (unit.beam.enabled)
                {
                    if ((unit.fire -= dt) <= 0)
                    {
                        unit.fire = 0.15f;
                        var gun = Combat.Arms[1]; gun.damage = 0.15f * (unit.ship ? 22 : unit.front.mindDead ? 50 : 3);
                        unit.beamEnd = game.Battle.FriendlyFire(unit.position + up * 1.4f, aim, gun);
                    }
                    unit.beam.SetPosition(0, unit.position + up * 1.4f - game.System.Anchor); unit.beam.SetPosition(1, unit.beamEnd - game.System.Anchor);
                }
                for (int i = game.Battle.Bolts.Count - 1; i >= 0; i--)
                {
                    var bolt = game.Battle.Bolts[i];
                    if (!bolt.hostile || !Combat.Crosses(bolt.position, bolt.velocity * dt, unit.position + up, unit.ship ? 8 : 1.4f)) continue;
                    unit.health -= bolt.damage * (unit.front.mindDead ? 0.15f : 1); game.Battle.Bolts.RemoveAt(i);
                }
                if (unit.health <= 0)
                {
                    if (unit.ship) unit.front.ships = math.max(0, unit.front.ships - 1); else unit.front.dogs = math.max(0, unit.front.dogs - 1);
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
            hole.gameObject.SetActive(Whiteout < 0.75f);
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
                if (i == game.Fall.moon || i == game.Fall.world) { body.gameObject.SetActive(false); continue; }
                float delay = i == 0 ? 10 : 1 + i * 0.1f;
                float t = math.saturate((collapse - 3 - delay) / (Moonfall.Consumption - delay));
                float3 offset = system.ToReference(i) - system.Anchor - centre;
                // Compress astronomical distances for the visor's final system-wide view.
                offset = math.normalizesafe(offset, new float3(0, 0, 1)) * radius * (4.5f + i % 7 * 0.45f);
                body.transform.position = centre + math.rotate(quaternion.RotateY(t * t * 5), offset) * (1 - t * t);
                float display = radius * (i == 0 ? 0.8f : body.Descriptor.kind == BodyKind.Gas ? 0.4f : 0.18f);
                body.transform.localScale = Vector3.one * math.max(0.00001f, display / body.Descriptor.radius * (1 - t * t));
            }
            var view = Camera.main.transform;
            view.position = centre + new float3(radius * 4, radius * 3, -radius * 10);
            view.LookAt(centre);
            if (DeviceControls.VR && UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.Head).TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out Quaternion head)) view.rotation *= head;
        }
    }
}
