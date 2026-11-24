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
        public float Whiteout => Collapsing ? math.saturate((collapse - Moonfall.Consumption - Moonfall.Infall) / Moonfall.WhiteHole) : arrival;
        public string Ending => !game.Fall.active ? "" : Collapsing
            ? collapse < 1 ? "IMPACT · THE BURIED MIND IS COLLAPSING" : collapse < Moonfall.Consumption ? $"BLACK HOLE SYSTEM CONSUMPTION {math.ceil(Moonfall.Consumption - collapse):0}s" : collapse < Moonfall.Consumption + Moonfall.Infall ? "BLACK HOLE · SHIP IN FREE FALL" : "WHITE HOLE · HOLD ON"
            : game.System.Clock - game.Fall.began < Moonfall.Assembly ? "MOON ENGINES BUILT · SHIP FOLLOW LINK ACQUIRED"
            : game.System.Clock - game.Fall.began < Moonfall.Assembly + Moonfall.Ignition ? "IGNITION · MOON ENGINES FIRING"
            : $"MOON IN TRANSIT · SHIP FOLLOWING · IMPACT IN {math.ceil(game.Fall.began + game.Fall.duration - game.System.Clock):0}s";
        float Infall => math.saturate((collapse - Moonfall.Consumption) / Moonfall.Infall);
        bool falling;
        float3 shipOffset;
        float saved, deployed, collapse, arrival, transfer, assembly;
        readonly List<Transform> plumes = new List<Transform>();
        int shownWorld = -1, shownRegion = -1, layout = -1;
        Task<SolarSystemDescriptor> next;
        Transform hole, impact, fireball, cloud, shockwave, debris, robotTemplate, shipTemplate;
        SolarSystemDescriptor worlds;
        readonly List<Transform> buildings = new List<Transform>();
        sealed class Unit
        {
            public Transform model, jets;
            public MachineMotion motion;
            public LineRenderer beam;
            public bool ship, escort, pack, airborne;
            public Front front;
            public float3 position, velocity, beamEnd;
            public float health = 70, age, fire;
        }
        readonly List<Unit> units = new List<Unit>();
        Front PackFactory => Local.factory && Local.dogs >= 1 ? Local
            : game.Presence.fronts.Find(front => front.world == game.System.Reference && front.factory && front.dogs >= 1);
        bool UnfinishedBurrows => game.Stage >= Campaign.ThirdBurrow && game.Stage < Campaign.Citadel && game.Stage != Campaign.Recovery && game.Outpost.World == game.System.Reference && !game.Outpost.Fallen;
        public bool CanCallDogs => !game.Player.flying && UnfinishedBurrows && PackFactory != null;
        public void CallDogs()
        {
            if (!CanCallDogs) return;
            game.Escorts.pack = !game.Escorts.pack;
            game.Escorts.packWorld = game.System.Reference;
            assembly = 0;
            game.Notify(game.Escorts.pack ? "ROBODOG ON THE WAY · GUARDS THE BURROW ENTRANCE" : "ROBODOG RETURNED TO PATROL");
            game.Write();
        }
        public string FleetHint => !game.Presence.fronts.Any(front => front.shipyard) ? "BUILD A SHIPYARD TO ASSEMBLE ESCORTS"
            : $"{units.Count(unit => unit.escort && unit.health > 0)}/1 SHIP ASSIGNED · " + (game.Escorts.order == EscortOrder.Follow ? "FOLLOW: stay beside you and defend against nearby ships, including during autopilot."
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
        public bool GroundControlled(int world) => Enumerable.Range(0, world == SolarSystemDescriptor.Home ? 3 : 1).All(region => War.Controlled(State(world, region)));
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
                deployed = collapse = 0; falling = false; next = null;
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
                    var engine = Shapes.Part(moon.transform, "Moon engine", Models.Parts("Moon engine")[0], Shapes.Solid(Surface.Metal, new Color(0.6f, 0.7f, 0.75f)), up * moon.Descriptor.GroundRadius(up), Vector3.one * math.max(4, moon.Descriptor.radius * 0.025f)).transform;
                    engine.localRotation = Quaternion.FromToRotation(Vector3.up, up);
                    var plume = Shapes.Flame(engine, Vector3.up * 8.8f, Vector3.up, 20, 2.5f);
                    plume.gameObject.SetActive(false); plumes.Add(plume);
                    buildings.Add(engine);
                }
        }
        Front BuildFront
        {
            get
            {
                var site = game.Colonies.Find(colony => colony.world == game.System.Reference && math.distancesq(colony.site, game.Player.Position) < 30 * 30);
                return site != null ? State(site.world, site.region) : Local;
            }
        }
        bool JointSite(Front site) => site.world != SolarSystemDescriptor.Home || game.Presence.fronts.Count(front => front.world == site.world && front.factory && front.region != site.region) >= 2;
        public string BuildLabel
        {
            get
            {
                var site = BuildFront;
                if (site.shipyard) return "Factory and shipyard built";
                if (site.factory) return JointSite(site) ? "Upgrade to shipyard" : "Robodog factory built";
                return JointSite(site) ? "Build factory and shipyard" : "Build robodog factory";
            }
        }
        public bool VisibleBuild(int choice) => choice == 0 || choice == 2 && (game.Fall.active || CanBuild(2));
        public bool CanBuild(int choice)
        {
            var front = BuildFront;
            if (choice == 2) return game.Stage >= Campaign.Won && front.shipyard && GroundControlled(front.world) && Total(front.world, state => state.alienShips) <= 0 && !game.Fall.active;
            if (game.Player.flying || game.System.ReferenceBody.Dry(game.Player.Position) || game.Player.Altitude >= 5 || game.System.ReferenceBody.kind != BodyKind.Rock) return false;
            if (front.factory)
            {
                var existing = game.Colonies.Find(colony => colony.world == front.world && colony.region == front.region);
                return !front.shipyard && JointSite(front) && (existing == null || math.distancesq(existing.site, game.Player.Position) < 30 * 30);
            }
            return choice == 0 && game.FactoryUnlocked && (front.mindDead || front.kills >= War.RequiredKills(front));
        }
        public string FactoryHint(int choice)
        {
            var front = BuildFront;
            return choice == 0 && JointSite(front) && front.factory && !front.shipyard && !CanBuild(0) ? "APPROACH THIS FACTORY TO ADD ITS SHIPYARD"
                : choice == 0 && JointSite(front) && (front.factory || CanBuild(0)) ? (front.shipyard ? "THIS FACTORY BUILDS ROBODOGS AND SHIPS" : front.factory ? "UPGRADE THIS REGIONAL FACTORY TO BUILD SHIPS HERE" : "BUILD HERE TO ASSEMBLE BOTH ROBODOGS AND SHIPS")
                : choice == 0 && front.factory ? "FACTORY BUILT · " + (front.mindDead ? game.Wording("factoryPack") : game.Wording("factoryMind"))
                : choice == 0 && !game.FactoryUnlocked ? game.Wording("factoryCore")
                : choice == 0 && (game.Player.flying || game.System.ReferenceBody.Dry(game.Player.Position) || game.Player.Altitude >= 5) ? game.Wording("factoryLand")
                : choice == 0 && front.mindDead ? $"REGION {front.region + 1} · READY TO BUILD · MIND SILENCED"
                : choice == 0 ? $"REGION {front.region + 1} · ROBODOG FACTORY · {front.kills}/{War.RequiredKills(front)} ALIENS DEFEATED\nReplicating laser robodogs hunt the remaining {math.ceil(front.aliens):0} bodies. A standing pack keeps new broods in check."
                : choice == 1 ? $"SHIPYARD UPGRADE · {(front.shipyard ? "BUILT" : !front.factory ? "BUILD A ROBODOG FACTORY FIRST" : !JointSite(front) ? "BUILD THE THREE REGIONAL FACTORIES" : "READY")}\n{game.Wording("factoryShips")}"
                : $"MOON ENGINES · {(game.Stage < Campaign.Won ? "DESTROY ALL CITADEL MINDS" : !front.shipyard ? "BUILD THIS WORLD’S SHIPYARD" : Total(front.world, state => state.alienShips) > 0 ? $"{math.ceil(Total(front.world, state => state.alienShips)):0} HOSTILE SHIPS REMAIN" : game.Fall.active ? "ENGINES BUILT" : "READY")}\n{game.Wording("factoryMoon")}";
        }
        public void BuildFactory(int choice)
        {
            var front = BuildFront;
            if (choice == 2)
            {
                if (game.Stage >= Campaign.Won && front.shipyard && CanBuild(2)) Launch();
                else game.Notify(FactoryHint(choice).Split('\n')[0]);
                return;
            }
            if (choice == 0 && front.factory && JointSite(front)) choice = 1;
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
            if (choice == 1 || JointSite(front) && War.Build(front, true))
            {
                choice = 1;
                game.Escorts.order = EscortOrder.Follow;
                var colony = game.Colonies.First(item => item.world == front.world && item.region == front.region);
                game.System.Bodies[front.world].Level(colony.site, 18);
                colony.site = math.normalizesafe(colony.site) * body.GroundRadius(math.normalizesafe(colony.site));
            }
            front.established = true;
            assembly = deployed = 0;
            Settle();
            game.Hermit.Remark(choice == 1 ? game.Wording("shipyard") : game.Outpost.SiteCleared && game.Outpost.World == front.world
                && front.region == Region(front.world, game.Outpost.Position) ? game.Wording("factoryDelivery") : game.Wording(front.mindDead ? "factoryCleared" : "factory"));

            DrawUnits(0);
            game.GetComponent<ForearmConsole>()?.Home();
            game.Write();
        }
        public void Launch()
        {
            game.Fall.began = game.System.Clock;
            game.Fall.duration = Moonfall.Assembly + Moonfall.Ignition + Moonfall.Transit;
            game.Player.flying = true;
            game.Fall.active = true;
            Settle();
            game.Hermit.Remark(game.Wording("ignition"));
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
        public float3? FollowingDog
        {
            get
            {
                if (game.Escorts.pack && game.Escorts.packWorld == game.System.Reference && !game.Player.flying)
                    foreach (var unit in units) if (unit.pack && unit.health > 0) return unit.position;
                return null;
            }
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
                collapse += Time.unscaledDeltaTime;
                if (Infall > 0)
                {
                    float3 centre = game.System.ToReference(game.Fall.world);
                    if (!falling) { shipOffset = game.Player.ship.position - centre; falling = true; }
                    game.Player.ship.position = centre + shipOffset * (1 - Infall * Infall);
                    game.Player.ship.velocity = -shipOffset * (2 * Infall / Moonfall.Infall);
                    game.Player.ShowShipPosition();
                }
                if (next == null) { uint seed = game.Seed + 1; next = Task.Run(() => { var system = SolarSystemDescriptor.Beyond(seed); Campaign.Site(system, 0); return system; }); }
                if (collapse >= Moonfall.Consumption + Moonfall.Infall + Moonfall.WhiteHole && next.IsCompletedSuccessfully)
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
                var body = game.System.ReferenceBody;
                var front = Local;
                var factory = game.Colonies.FirstOrDefault(colony => colony.world == front.world && colony.region == front.region && math.distance(colony.site, game.Player.Position) < 700);
                deployed = (factory != null ? 3 : math.max(1.5f, 8 - game.Threat * 0.5f)) / game.Pressure;
                if (body.kind == BodyKind.Rock && front.aliens > 0 && game.Battle.Foes.Count(foe => foe.patrol) < (factory != null ? 5 : math.min(40, 4 + game.Threat * 3)))
                {
                    var rnd = new Unity.Mathematics.Random((uint)(game.Presence.played * 100 + game.Seed) | 1);
                    float3 up = math.normalizesafe(factory?.site ?? game.Player.Position);
                    float3 direction = math.normalize(up + Walker.Project(rnd.NextFloat3Direction(), up) * ((factory == null ? rnd.NextFloat(70, 160) : rnd.NextFloat(25, 55)) / body.radius));
                    float3 ground = direction * (body.GroundRadius(direction) + Combat.SentryHeight);
                    if (!body.Dry(ground))
                    {
                        game.Battle.Add(FoeKind.Sentry, ground, game.Stage <= 2 ? MotileCaste.Stalker : (MotileCaste)rnd.NextInt(0, 3));
                        var foe = game.Battle.Foes[^1]; foe.patrol = true; game.Battle.Foes[^1] = foe;
                    }
                }
            }
            if (!game.Battle.quiet && game.Player.flying && Local.alienShips > 0 && units.Any(unit => unit.ship && math.distance(unit.position, game.Player.Position) < 2000)
                && game.Battle.Foes.Count(foe => foe.kind == FoeKind.Raider) < 3 && deployed > 0 && deployed <= dt)
            {
                float3 point = game.Player.Position - (float3)Camera.main.transform.forward * 1400 + math.normalizesafe(game.Player.Position) * 180;
                float3 up = math.normalizesafe(point);
                if (game.System.ReferenceBody.kind == BodyKind.Rock) point = up * math.max(math.length(point), game.System.ReferenceBody.GroundRadius(up) + 100);
                game.Battle.Add(FoeKind.Raider, point);
            }
            if (GroundControlled(game.System.Reference))
            {
                game.Battle.Foes.RemoveAll(foe => foe.kind != FoeKind.Mind && !foe.patrol && !foe.friendly && foe.kind != FoeKind.Raider && foe.kind != FoeKind.Base);
            }
            if (game.Escorts.pack && (!UnfinishedBurrows || game.Player.flying || game.Escorts.packWorld >= 0 && game.Escorts.packWorld != game.System.Reference))
                game.Escorts.pack = false;
            DrawUnits(dt);
            if (game.Fall.active && !game.Fall.destroyed)
            {
                float3 moon = game.System.ToReference(game.Fall.moon);
                float radius = game.System.Descriptor.bodies[game.Fall.moon].radius;
                float3 planet = game.System.ToReference(game.Fall.world);
                float3 direction = math.normalizesafe(planet - moon, new float3(0, 0, 1));
                float3 above = math.normalizesafe(math.cross(direction, new float3(1, 0, 0)), new float3(0, 1, 0));
                // Stay in formation throughout the burn.
                game.Player.ship.position = moon - direction * (radius * 3 + 150)
                    + math.cross(above, direction) * (radius * 2 + 120) + above * (radius * 2 + 40);
                float ignition = math.saturate((float)(game.System.Clock - game.Fall.began - Moonfall.Assembly) / Moonfall.Ignition);
                for (int i = 0; i < plumes.Count; i++)
                {
                    var plume = plumes[i]; var engine = plume.parent;
                    float angle = i * math.PI * 2 / 3;
                    float3 aft = math.normalize(-direction + (above * math.cos(angle) + math.cross(direction, above) * math.sin(angle)) * 0.24f);
                    engine.position = moon - game.System.Anchor + aft * radius;
                    engine.rotation = Quaternion.FromToRotation(Vector3.up, aft);
                    plume.gameObject.SetActive(ignition > 0);
                    plume.localScale = new Vector3(2.5f, 10 * ignition, 2.5f);
                    plume.localPosition = Vector3.up * (8.8f + 10 * ignition);
                }
                game.Player.ship.velocity = (game.System.ToReference(game.Fall.moon, 0.1) - moon) * 10;
                float3 sightline = math.normalize(moon - game.Player.ship.position) + math.normalize(planet - game.Player.ship.position);
                game.Player.ship.rotation = quaternion.LookRotationSafe(math.normalize(sightline), above);
                game.Player.ship.landed = false;
            }
            if (game.Fall.Finished(game.System.Clock))
            {
                game.Fall.destroyed = true;
                game.System.Bodies[game.Fall.moon].gameObject.SetActive(false);
                collapse = 0;
                game.Sounds.Nuke();
                game.Hermit.Remark(game.Wording("collapse"));
                game.Write();
            }
            if ((saved += dt) > 15) { saved = 0; game.Write(); }
        }
        float3 PackRally(float3 dog)
        {
            var body = game.System.ReferenceBody;
            float3 at = game.Player.Position, up = math.normalizesafe(at);
            foreach (var burrow in body.burrows)
                if (burrow.Contains(at))
                {
                    up = math.normalizesafe(burrow.origin + math.rotate(burrow.rotation, new float3(3, 0, -36)));
                    return up * math.max(body.GroundRadius(up), body.SeaRadius + 2);
                }
            float3 course = Walker.Project(game.Player.Velocity, up);
            float3 behind = math.normalizesafe(Walker.Project(dog - at, up), -game.Player.walker.forward);
            if (math.lengthsq(course) > 1)
            {
                float3 heading = math.normalize(course);
                behind = math.normalizesafe(-heading + math.cross(up, heading) * 0.28f);
            }
            float height = math.max(0, math.length(at) - math.max(body.GroundRadius(up), body.SeaRadius));
            up = math.normalizesafe(at + behind * 15);
            float3 point = up * math.max(body.GroundRadius(up) + height, body.SeaRadius + 2);
            foreach (var burrow in body.burrows)
                if (burrow.Contains(point))
                {
                    up = math.normalizesafe(burrow.origin + math.rotate(burrow.rotation, new float3(3, 0, -36)));
                    return up * math.max(body.GroundRadius(up), body.SeaRadius + 2);
                }
            return point;
        }
        void DrawUnits(float dt)
        {
            var front = Local;
            var source = game.Presence.fronts.Where(item => item.shipyard && item.ships > 0).OrderBy(item => item == front ? 0 : 1).FirstOrDefault();
            var packFactory = game.Escorts.pack && !game.Player.flying ? PackFactory : null;
            int pack = packFactory == null ? 0 : 1;
            int escorts = source != null && pack == 0 ? 1 : 0;
            int dogs = math.min(7 - pack, (int)math.ceil(front.dogs) - (packFactory == front ? pack : 0)), ships = math.min(3 - escorts, (int)front.ships - (source == front ? escorts : 0));
            assembly -= dt;
            if (shownWorld != game.System.Reference || shownRegion != front.region)
            {
                foreach (var unit in units.Where(unit => !unit.escort && (!unit.pack || shownWorld != front.world))) Destroy(unit.model.gameObject);
                units.RemoveAll(unit => !unit.escort && (!unit.pack || shownWorld != front.world));
                shownWorld = game.System.Reference; shownRegion = front.region;
            }
            for (int i = units.Count - 1; i >= 0; i--)
            {
                var unit = units[i];
                if (unit.health <= 0 || units.Count(other => other.ship == unit.ship && other.escort == unit.escort && other.pack == unit.pack) > (unit.pack ? pack : unit.escort ? escorts : unit.ship ? ships : dogs))
                { Destroy(unit.model.gameObject); units.RemoveAt(i); }
            }
            // Keep assembly staggered, including escorts, and launch the actual AI model.
            for (int step = 0; step < 4 && assembly <= 0; step++)
            {
                bool called = step == 0, escort = step == 1, ship = escort || step == 3;
                if (units.Count(unit => unit.ship == ship && unit.escort == escort && unit.pack == called) >= (called ? pack : escort ? escorts : ship ? ships : dogs)) continue;
                if (ship && shipTemplate == null) { shipTemplate = Models.Ship(); shipTemplate.SetParent(transform, false); shipTemplate.gameObject.SetActive(false); }
                if (robotTemplate == null) { robotTemplate = Models.Robot(); robotTemplate.SetParent(transform, false); robotTemplate.gameObject.SetActive(false); }
                var model = Instantiate(ship ? shipTemplate : robotTemplate); model.gameObject.SetActive(true);
                model.name = called ? "Robodog escort" : escort ? "Fleet escort" : ship ? "Robot patrol ship" : "Robodog patrol";
                MachineMotion motion = null;
                if (!ship) { motion = model.gameObject.AddComponent<MachineMotion>(); motion.quadruped = true; }
                else
                {
                    model.localScale = Vector3.one * 0.35f;
                    var pilot = Instantiate(robotTemplate); pilot.gameObject.SetActive(true); pilot.name = "Robot pilot"; pilot.SetParent(model, false);
                    pilot.localPosition = new Vector3(0, 0.7f, 1.5f); pilot.localScale = Vector3.one * 0.45f;
                }
                var beam = new GameObject("Dorsal laser").AddComponent<LineRenderer>();
                beam.transform.SetParent(model, false); beam.positionCount = 2; beam.startWidth = beam.endWidth = ship ? 0.18f : 0.06f;
                beam.sharedMaterial = Shapes.Beam(new Color(0.2f, 2, 3)); beam.enabled = false;
                var origin = called ? packFactory : escort ? source : front;
                var colony = game.Colonies.FirstOrDefault(item => item.world == origin.world && item.region == origin.region);
                float3 position = colony?.site ?? game.Player.Position, velocity = float3.zero;
                if (origin.world != game.System.Reference) game.System.Descriptor.Rebase(origin.world, game.System.Reference, game.System.Clock, ref position, ref velocity);
                if (called && colony == null) position = PackRally(game.Player.Position);
                if (called)
                    foreach (var burrow in game.System.ReferenceBody.burrows)
                        if (burrow.Contains(position))
                        {
                            float3 up = math.normalizesafe(burrow.origin + math.rotate(burrow.rotation, new float3(0, 0, -32)));
                            position = up * game.System.ReferenceBody.GroundRadius(up);
                        }
                Transform jets = null;
                if (called)
                {
                    jets = new GameObject("Escort jetpack").transform;
                    jets.SetParent(model.Find("Torso"), false);
                    foreach (float side in new[] { -1f, 1f })
                    {
                        var nozzle = Shapes.Part(jets, "Lift engine", Models.Parts("Moon engine")[0], Shapes.Solid(Surface.Metal, Color.gray),
                            new Vector3(side * 0.37f, 0.13f, -0.23f), Vector3.one * 0.045f).transform;
                        nozzle.localRotation = Quaternion.Euler(160, 0, 0);
                        Shapes.Flame(nozzle, Vector3.up * 8.8f, Vector3.up, 17, 1.9f);
                    }
                }
                units.Add(new Unit { jets = jets, pack = called, age = called ? 5 : 0, model = model, motion = motion, beam = beam, ship = ship, escort = escort, front = origin, health = ship || origin.mindDead ? 350 : 180, position = position });
                assembly = ship ? 3 : 1.5f;
            }
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
                    target = game.Player.Position + math.rotate(game.Player.ship.rotation, new float3(-28, 18, -45));
                if (unit.pack) target = PackRally(unit.position);
                int enemy = -1; float nearest = unit.pack ? 55 : unit.ship ? 2500 : patrol + 35;
                for (int i = 0; i < game.Battle.Foes.Count; i++)
                {
                    var foe = game.Battle.Foes[i]; float distance = math.distance(foe.position, unit.position);
                    if (unit.age < 5 || foe.kind == FoeKind.Mind || foe.friendly || foe.health <= 0 || foe.protectedCore || (foe.kind == FoeKind.Raider) != unit.ship || distance >= nearest) continue;
                    if (!unit.ship && game.System.ReferenceBody.Dry(foe.position)) continue;
                    if (unit.pack ? math.distance(foe.position, target) > 60 : unit.ship ? math.distance(foe.position, following ? game.Player.Position : home) > (following && game.Escorts.order == EscortOrder.Follow ? 700 : 2500) : math.distance(foe.position, home) > patrol + 35) continue;
                    if (!game.Outpost.Visible(unit.position + math.normalizesafe(unit.position) * 1.4f, foe.position)) continue;
                    enemy = i; nearest = distance;
                }
                float3 aim = enemy >= 0 ? game.Battle.Foes[enemy].position : target;
                if (enemy >= 0 && !unit.pack && (!following || game.Escorts.order == EscortOrder.Attack)) target = aim;
                float3 before = unit.position;
                var body = game.System.ReferenceBody;
                if (unit.pack)
                {
                    float gap = math.distance(before, target);
                    float3 normal = math.normalizesafe(before);
                    float ground = body.GroundRadius(normal);
                    float3 ahead = math.normalizesafe(before + math.normalizesafe(Walker.Project(target - before, normal)) * 6);
                    bool water = body.SeaRadius > 0 && math.min(ground, body.GroundRadius(ahead)) < body.SeaRadius + 0.4f;
                    bool catchingUp = gap > (unit.airborne ? 12 : 26);
                    unit.airborne = water || catchingUp || math.length(before) - ground > 0.55f || math.length(target) > body.GroundRadius(math.normalizesafe(target)) + 2;
                    if (catchingUp || water)
                    {
                        float3 direction = math.normalizesafe(target);
                        target = direction * math.max(math.length(target), math.max(body.GroundRadius(direction), body.SeaRadius) + 2);
                    }
                }
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
                else if (unit.pack)
                {
                    float3 delta = target - before;
                    float speed = math.min(unit.airborne ? math.max(35, math.length(game.Player.Velocity) + 10) : 9, math.max(0, math.length(delta) - 0.7f) * 1.5f);
                    unit.velocity = Walker.MoveTowards(unit.velocity, math.normalizesafe(delta) * speed, (unit.airborne ? 22 : 12) * dt);
                    unit.position = game.Outpost.Resolve(before, before + unit.velocity * dt, 0.55f, out _);
                }
                else if (enemy < 0 || nearest > 12)
                    unit.position = game.Outpost.Resolve(before, before + math.normalizesafe(Walker.Project(target - before, math.normalizesafe(before))) * dt * 7, 0.7f, out _);
                float3 up = math.normalizesafe(unit.position);
                if (unit.age >= 5 && body.kind == BodyKind.Rock)
                {
                    float ground = body.GroundRadius(up);
                    if (unit.pack && ground < body.SeaRadius) unit.airborne = true;
                    unit.position = up * (unit.pack && unit.airborne ? math.max(math.length(unit.position), math.max(ground, body.SeaRadius + 2)) : unit.ship ? math.max(math.length(unit.position), ground + 20) : ground);
                }
                if (!unit.ship && body.Dry(unit.position)) { unit.position = before; unit.velocity = float3.zero; }
                if (unit.jets != null)
                    foreach (Transform nozzle in unit.jets) nozzle.GetChild(0).gameObject.SetActive(unit.airborne);
                float3 forward = math.normalizesafe(unit.ship ? aim - unit.position : Walker.Project(target - unit.position, up), math.cross(up, new float3(1, 0, 0)));
                if (unit.pack)
                {
                    float3 bearing = Walker.Project(enemy >= 0 ? aim - unit.position : unit.velocity, up);
                    forward = math.normalizesafe(bearing, unit.model.forward);
                }
                Quaternion orientation = quaternion.LookRotationSafe(forward, up);
                unit.model.SetPositionAndRotation(unit.position - game.System.Anchor, unit.pack ? Quaternion.RotateTowards(unit.model.rotation, orientation, 180 * dt) : orientation);
                float3 travel = dt > 0 ? (unit.position - before) / dt : float3.zero;
                if (unit.motion != null)
                {
                    unit.motion.speed = math.length(Walker.Project(travel, up));
                    if (dt > 0 && unit.motion.LandedFoot) game.Sounds.Encounter(EncounterCue.Footfall, unit.position);
                }
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
                        if (unit.motion != null)
                        {
                            unit.motion.recoil = 1;
                            game.Sounds.Encounter(EncounterCue.Emitter, unit.position + up * 1.4f);
                        }
                        var gun = Combat.Arms[1]; gun.damage = 0.15f * (unit.ship ? 22 : unit.front.mindDead ? 64 : 28);
                        unit.beamEnd = game.Battle.FriendlyFire(unit.position + up * 1.4f, aim, gun);
                    }
                    unit.beam.SetPosition(0, unit.position + up * 1.4f - game.System.Anchor); unit.beam.SetPosition(1, unit.beamEnd - game.System.Anchor);
                }
                for (int i = game.Battle.Bolts.Count - 1; i >= 0; i--)
                {
                    var bolt = game.Battle.Bolts[i];
                    if (!bolt.hostile || !Combat.Crosses(bolt.position, bolt.velocity * dt, unit.position + up, unit.ship ? 8 : 1.4f)) continue;
                    unit.health -= bolt.damage * (unit.front.mindDead ? 0.15f : unit.ship ? 1 : 0.65f); game.Battle.Bolts.RemoveAt(i);
                }
                unit.health -= body.LavaDamage(unit.position, unit.ship ? Ship.Clearance : 0.7f, Time.time) * dt;
                if (unit.health <= 0)
                {
                    if (unit.ship) unit.front.ships = math.max(0, unit.front.ships - 1); else unit.front.dogs = math.max(0, unit.front.dogs - 1);
                    game.Corpses.AddModel(unit.model, unit.position, travel, unit.ship);
                    unit.model.gameObject.SetActive(false);
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
                hole = Shapes.Part(transform, "Singularity", Shapes.Orb(1, 48), Shapes.Material("Glow", Color.black), Vector3.zero, Vector3.one).transform;
                Shapes.Part(hole, "Accretion disc", Models.Parts("Accretion")[0], new Material(Resources.Load<Shader>("Accretion")), Vector3.zero, Vector3.one, false);
                var lens = Shapes.Part(hole, "Lensed light", Models.Parts("Light ring")[0], new Material(Resources.Load<Shader>("Accretion")), Vector3.zero, Vector3.one, false);
                lens.transform.localRotation = Quaternion.Euler(74, 0, 0);
            }
            hole.position = centre;
            hole.gameObject.SetActive(Whiteout < 0.75f);
            float radius = 300 + collapse * 45;
            hole.localScale = Vector3.one * radius * math.saturate(collapse / 3);
            if (impact == null)
            {
                impact = new GameObject("Moon impact").transform;
                impact.SetParent(transform, false);
                fireball = Shapes.Part(impact, "Flash", Shapes.Orb(1, 48), Shapes.Material("Glow", Color.white), Vector3.zero, Vector3.one, false).transform;
                cloud = new GameObject("Rising fire cloud").transform;
                cloud.SetParent(impact, false);
                var plumeMesh = Shapes.Rock(12);
                var embers = Shapes.Hull(new Color(0.95f, 0.46f, 0.10f), 24, new Color(0.42f, 0.09f, 0.012f));
                Shapes.Part(cloud, "Column", plumeMesh, embers, Vector3.up * 0.7f, new Vector3(0.85f, 2.3f, 0.85f), false);
                for (int i = 0; i < 12; i++)
                {
                    float angle = i * math.PI / 6;
                    Shapes.Part(cloud, "Billow", plumeMesh, embers, new Vector3(math.cos(angle) * 0.65f, 1.35f + 0.08f * math.cos(3 * angle), math.sin(angle) * 0.65f),
                        new Vector3(1.6f, 1.05f, 1.6f), false);
                }
                shockwave = Shapes.Part(impact, "Shock front", Models.Parts("Shock front")[0], Shapes.Material("Glow", new Color(1, 0.66f, 0.21f)), Vector3.zero, Vector3.one, false).transform;
                debris = new GameObject("Impact ejecta").transform;
                debris.SetParent(impact, false);
                var rock = Shapes.Rock(3);
                var crust = Shapes.Hull(new Color(0.23f, 0.1f, 0.06f), 1, new Color(0.8f, 0.19f, 0.025f));
                var scatter = new Unity.Mathematics.Random(731);
                for (int i = 0; i < 96; i++)
                {
                    var chunk = Shapes.Part(debris, "Fragment", rock, crust, scatter.NextFloat3Direction() * scatter.NextFloat(0.5f, 1),
                        scatter.NextFloat3(0.018f, 0.075f), false);
                    chunk.transform.localRotation = scatter.NextQuaternionRotation();
                }
            }
            float infall = Infall;
            bool whiteHole = infall >= 1;
            float3 outward = math.normalizesafe(system.ToReference(game.Fall.moon) - system.ToReference(game.Fall.world), new float3(0, 1, 0));
            impact.position = whiteHole ? centre : centre + outward * system.Descriptor.bodies[game.Fall.world].radius;
            impact.up = outward;
            float burst = math.saturate(collapse / 3);
            fireball.localScale = Vector3.one * radius * (whiteHole ? Whiteout * 14 : (1 - burst) * math.exp(-4 * burst) * (0.7f + 6 * math.sqrt(burst)));
            fireball.GetComponent<MeshRenderer>().sharedMaterial.color = whiteHole ? Color.white : Color.Lerp(Color.white, new Color(1, 0.22f, 0.035f), burst);
            float plume = math.smoothstep(0, 0.55f, burst) * (1 - math.smoothstep(2.5f, 4, collapse));
            cloud.localScale = Vector3.one * (radius * 1.45f * plume);
            cloud.localPosition = Vector3.up * (radius * burst);
            cloud.gameObject.SetActive(plume > 0);
            shockwave.gameObject.SetActive(burst < 1);
            shockwave.localScale = Vector3.one * radius * (0.7f + 5 * burst);
            debris.gameObject.SetActive(collapse < 7);
            debris.localScale = Vector3.one * radius * (0.7f + 8 * math.sqrt(math.saturate(collapse / 7)));
            float overview = math.smoothstep(1, 3, collapse);
            for (int i = 0; i < system.Bodies.Length; i++)
            {
                var body = system.Bodies[i];
                if (i == game.Fall.moon || i == game.Fall.world)
                {
                    body.gameObject.SetActive(i == game.Fall.world && collapse < 0.75f);
                    continue;
                }
                if (overview == 0) continue;
                float delay = i == 0 ? 4 : math.min(5, 1 + i * 0.1f);
                float t = math.saturate((collapse - delay) / (Moonfall.Consumption - delay));
                float3 offset = system.ToReference(i) - system.Anchor - centre;
                // Compress astronomical distances for the visor's final system-wide view.
                float3 orbit = math.normalizesafe(offset, new float3(0, 0, 1)) * radius * (4.5f + i % 7 * 0.45f);
                body.transform.position = centre + math.lerp(offset, math.rotate(quaternion.RotateY(t * t * 5), orbit) * (1 - t * t), overview);
                var parent = body.Descriptor.orbit.parent > 0 ? system.Descriptor.bodies[body.Descriptor.orbit.parent] : body.Descriptor;
                float scale = i == 0 ? 0.8f / body.Descriptor.radius : (parent.kind == BodyKind.Gas ? 0.4f : 0.18f) / parent.radius;
                body.transform.localScale = Vector3.one * math.lerp(1, radius * scale * (1 - t * t), overview);
            }
        }
    }
}
