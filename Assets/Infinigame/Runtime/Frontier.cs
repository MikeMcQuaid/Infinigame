using System;
using System.Collections.Generic;
using System.Linq;
using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;

namespace Infinigame
{
    public class Frontier : MonoBehaviour
    {
        [NonSerialized] public Game game;
        readonly List<Transform> built = new List<Transform>();
        Transform rubble;
        readonly List<Transform> incursions = new List<Transform>();
        int spreading = -1, growth = -1;
        float saved, deployed;
        public void Settle()
        {
            foreach (var item in built) if (item != null) Destroy(item.gameObject);
            built.Clear();
            foreach (var item in incursions) if (item != null) Destroy(item.gameObject);
            incursions.Clear();
            spreading = growth = -1;
            if (rubble != null) Destroy(rubble.gameObject);
            rubble = null;
            foreach (var colony in game.Colonies)
            {
                if (colony.world >= game.System.Bodies.Length || game.Fall.destroyed && colony.world == game.Fall.world) continue;
                var root = new GameObject("Robot colony").transform;
                built.Add(root);
                root.SetParent(game.System.Bodies[colony.world].transform, false);
                float3 up = math.normalizesafe(colony.site);
                root.localPosition = colony.site;
                root.localRotation = quaternion.LookRotationSafe(Walker.Project(new float3(0, 0, 1), up), up);
                foreach (Building building in Enum.GetValues(typeof(Building)))
                {
                    if (building == Building.Robot || !colony.Has(building)) continue;
                    var model = Models.Building(building);
                    model.SetParent(root, false);
                    model.localPosition = new Vector3(-20 + (int)building * 8, 0, -22);
                }
                for (int i = 0; i < math.min(colony.robots, 16); i++)
                {
                    var robot = Models.Robot();
                    robot.SetParent(root, false);
                    robot.localPosition = new Vector3(-10 + (i % 8) * 3, 0, -15 - i / 8 * 3);
                    robot.localScale = Vector3.one * 0.7f;
                    var movement = robot.gameObject.AddComponent<MachineMotion>();
                    movement.quadruped = movement.working = true;
                    movement.planet = game.System.Bodies[colony.world].transform;
                    movement.ground = game.System.Descriptor.bodies[colony.world];
                }
            }
            game.System.Surveyed = game.Colonies.Any(colony => colony.Has(Building.Radar));
            if (game.Fall.active && !game.Fall.destroyed)
            {
                var moon = game.System.Bodies[game.Fall.moon];
                for (int i = 0; i < 6; i++)
                {
                    float angle = i * math.PI / 3;
                    float3 up = math.normalize(new float3(math.cos(angle), 0.5f, math.sin(angle)));
                    var engine = Models.Building(Building.Drill);
                    engine.name = "Moon engine";
                    engine.SetParent(moon.transform, false);
                    engine.localPosition = up * moon.Descriptor.GroundRadius(up);
                    engine.localRotation = Quaternion.FromToRotation(Vector3.up, up);
                    built.Add(engine);
                }
            }
            if (!game.Fall.destroyed)
            {
                var body = game.System.Descriptor.bodies[game.Fall.world];
                float3 up = body.FindLanding(game.Seed + 503);
                var last = new GameObject("Buried final mind").transform;
                built.Add(last);
                last.SetParent(game.System.Bodies[game.Fall.world].transform, false);
                last.localPosition = up * body.GroundRadius(up);
                last.localRotation = Quaternion.FromToRotation(Vector3.up, up);
                MeshKit bunker = new MeshKit();
                bunker.Box(new Vector3(0, 1, 0), new Vector3(36, 2, 36), 1);
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * math.PI / 4;
                    Vector3 at = new Vector3(math.cos(angle), 0, math.sin(angle)) * 15;
                    bunker.Rod(at, at + Vector3.up * 18, 1.8f, 8, 0.25f);
                }
                Shapes.Part(last, "Buried fortress", bunker.Mesh("Fortress"), Shapes.Material("Props", new Color(0.22f, 0.16f, 0.27f)), Vector3.zero, Vector3.one);
                if (game.Stage < Campaign.Won)
                    Shapes.Part(last, "Final shield", Shapes.Sphere(50, 24), new Material(Resources.Load<Shader>("Shield")), Vector3.zero, Vector3.one, false);
            }
            var conduit = game.System.Bodies[game.System.Gate].transform.Find("Gate/Conduit");
            if (conduit != null) conduit.gameObject.SetActive(game.GatePowered);
            if (game.Fall.destroyed) Ruin();
        }
        public void Launch()
        {
            var system = game.System.Descriptor;
            game.Fall.began = game.System.Clock;
            game.Fall.active = true;
            game.Hermit.Remark($"Engines lit on {system.bodies[game.Fall.moon].name}. Clear {system.bodies[game.Fall.world].name}. The last mind is too deep for anything smaller.");
        }
        public void Tick()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            game.Presence.Step(dt);
            foreach (var colony in game.Colonies)
            {
                if (colony.world == game.System.Reference && colony.Has(Building.Turret))
                    for (int i = 0; i < game.Battle.Foes.Count; i++)
                    {
                        var foe = game.Battle.Foes[i];
                        if (foe.friendly || math.distance(foe.position, colony.site) > 100 || !game.Outpost.Visible(colony.site + math.normalizesafe(colony.site) * 3, foe.position)) continue;
                        foe.health -= dt * math.min(colony.robots * 4, 40);
                        game.Battle.Foes[i] = foe;
                    }
                int robots = colony.robots;
                colony.Step(dt, game.Fall.destroyed ? 0 : game.Presence.Reach(colony.world));
                if (colony.robots != robots)
                {
                    if (colony.robots < robots) game.Notify($"COLONY RAID ON {game.System.Descriptor.bodies[colony.world].name.ToUpperInvariant()}  ·  ROBOT LOST");
                    else game.Hermit.Remark("The workshop has assembled another worker. It will gather resources and help us reclaim this world.");
                    Settle();
                }
            }
            for (int i = 1; i < game.System.Descriptor.bodies.Length; i++) game.System.Descriptor.bodies[i].blight = game.Fall.destroyed ? 0 : math.max(0, game.Presence.Reach(i) - game.Colonies.Where(colony => colony.world == i && colony.Has(Building.Turret)).Sum(colony => colony.robots) * 0.025f);
            int reference = game.System.Reference;
            int count = game.Fall.destroyed || game.System.ReferenceBody.kind != BodyKind.Rock ? 0 : (int)(game.System.ReferenceBody.blight * 4);
            if (reference != spreading || count != growth)
            {
                foreach (var item in incursions) if (item != null) Destroy(item.gameObject);
                incursions.Clear();
                spreading = reference;
                growth = count;
                var body = game.System.ReferenceBody;
                for (int i = 0; i < count; i++)
                {
                    float3 up = body.FindLanding(math.hash(new uint2(game.Seed + (uint)reference, (uint)i + 901)) | 1u);
                    var tower = Models.Nest();
                    tower.name = "Presence outpost";
                    tower.SetParent(game.System.Bodies[reference].transform, false);
                    tower.localPosition = up * body.GroundRadius(up);
                    tower.localRotation = Quaternion.FromToRotation(Vector3.up, up);
                    tower.localScale = Vector3.one * 3;
                    incursions.Add(tower);
                }
            }
            if (!game.Battle.quiet && (deployed -= dt) <= 0)
            {
                deployed = 15;
                foreach (var post in incursions)
                    if (math.distance(game.Player.Position, (float3)post.localPosition) < 500 && game.Battle.Foes.Count(foe => foe.kind == FoeKind.Drone && !foe.scattered) < 8)
                        game.Battle.Add(FoeKind.Drone, (float3)post.localPosition + math.normalizesafe((float3)post.localPosition) * 5);
            }
            if (!game.Fall.destroyed && game.Fall.Finished(game.System.Clock))
            {
                float3 impact = game.System.ToReference(game.Fall.world);
                float radius = game.System.Descriptor.bodies[game.Fall.world].radius;
                game.Effects.Mushroom(impact, math.normalizesafe(game.Player.Position - impact), radius * 4);
                if (math.distance(game.Player.Position, impact) < radius * 3) game.Player.Hurt(10000, "Caught in the moon impact");
                game.Fall.destroyed = true;
                Ruin();
                game.Hermit.Remark("The last mind is gone. I helped make you to break this prison. But its radiation was keeping them in. Power the gate only now, while it is silent.");
                game.Write();
            }
            if ((saved += dt) > 15) { saved = 0; game.Write(); }
        }
        void Ruin()
        {
            var system = game.System;
            for (int world = 1; world < system.Bodies.Length; world++)
            {
                if (world != game.Fall.world && system.Descriptor.bodies[world].orbit.parent != game.Fall.world) continue;
                system.Descriptor.bodies[world].destroyed = true;
                system.Bodies[world].gameObject.SetActive(false);
            }
            game.Colonies.RemoveAll(colony => system.Descriptor.bodies[colony.world].destroyed);
            if (rubble != null) return;
            var body = system.Descriptor.bodies[game.Fall.world];
            var remains = new BodyDescriptor { radius = body.radius, surfaceGravity = body.surfaceGravity, ringInner = body.radius * 1.2f,
                ringOuter = body.radius * 3.5f, ringDensity = 0.7f, ringRock = 12, ringThick = 80, ringPeriod = 300, terrain = body.terrain };
            rubble = new GameObject("Shattered world").transform;
            rubble.SetParent(system.transform, false);
            rubble.gameObject.AddComponent<RingField>().Init(remains);
        }
        void LateUpdate()
        {
            if (rubble != null) rubble.position = game.System.ToReference(game.Fall.world) - game.System.Anchor;
        }
    }
}
