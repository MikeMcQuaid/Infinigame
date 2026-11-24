using System.Collections;
using System.Reflection;
using System.Linq;
using Infinigame.Core;
using Unity.Mathematics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace Infinigame.Tests
{
    public partial class PlayTests
    {
        [UnityTest] public IEnumerator SkippingMissionsBuildsThePrerequisiteColonies()
        {
            game.Jump(Campaign.FirstBurrow);
            Assert.IsEmpty(game.Colonies);
            game.Jump(Campaign.ThirdBurrow);
            Assert.AreEqual(2, game.Colonies.Count);
            Assert.IsTrue(game.Frontier.CanCallDogs);
            Assert.IsFalse(game.Frontier.State(Home, 2).factory);
            var first = game.Colonies.Single(colony => colony.world == Home && colony.region == 0);
            first.ore = 37;
            game.Jump(Campaign.Tower);
            Assert.AreEqual(3, game.Colonies.Count);
            Assert.IsTrue(game.Frontier.State(Home, 2).shipyard);
            Assert.IsFalse(game.Frontier.State(Campaign.World(Solar.Descriptor, Campaign.Tower), 0).factory);
            game.Jump(Campaign.Citadel);
            game.Jump(Campaign.Citadel);
            Assert.AreEqual(6, game.Colonies.Count);
            Assert.AreEqual(37, first.ore);
            for (int stage = Campaign.FirstBurrow; stage < Campaign.Citadel; stage++)
            {
                if (stage == Campaign.Recovery) continue;
                int world = Campaign.World(Solar.Descriptor, stage), region = world == Home ? stage - Campaign.FirstBurrow : 0;
                var front = game.Frontier.State(world, region);
                Assert.IsTrue(front.factory && front.mindDead && front.established);
                Assert.GreaterOrEqual(front.dogs, 1);
                Assert.AreEqual(stage >= Campaign.ThirdBurrow, front.shipyard);
                Assert.IsTrue(game.Ruins.Single(ruin => ruin.stage == stage).destroyed);
                Assert.IsNotNull(game.Colonies.Single(colony => colony.world == world && colony.region == region));
            }
            yield return UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("Main");
            game = Object.FindAnyObjectByType<Game>();
            while (!game.Ready) yield return null;
            Assert.AreEqual(Campaign.Citadel, game.Stage);
            Assert.AreEqual(6, game.Colonies.Count);
            Assert.AreEqual(4, game.Presence.fronts.Count(front => front.shipyard));
            Assert.That(game.Colonies.Single(colony => colony.world == Home && colony.region == 0).ore, Is.GreaterThanOrEqualTo(37));
            game.Jump(Campaign.SecondBurrow);
            Assert.IsFalse(game.Frontier.State(Home, 1).mindDead);
            Assert.IsFalse(game.Ruins.Any(ruin => ruin.stage == Campaign.SecondBurrow && ruin.destroyed));
            Assert.AreEqual(6, game.Colonies.Count);
        }

        [UnityTest] public IEnumerator CheatsCanStopBetweenCitadelAndMoonEngineConstruction()
        {
            yield return Press(new Controls { menu = true });
            yield return Press(new Controls { typed = "idgaf" });
            foreach (string item in new[] { Game.Cheats, "Choose mission", "Citadel destroyed · build moon engines" })
            {
                for (int i = 0; i < game.Items.Length && game.Items[game.Selected] != item; i++)
                    yield return Press(new Controls { down = true });
                Assert.AreEqual(item, game.Items[game.Selected]);
                yield return Press(new Controls { confirm = true });
            }
            Assert.IsFalse(game.Fall.active);
            Assert.IsTrue(game.Frontier.CanBuild(2));
            game.Frontier.BuildFactory(2);
            Assert.IsTrue(game.Fall.active);
        }

        [UnityTest] public IEnumerator WeaponRecoveryStaysWithTheLauncher()
        {
            yield return Press(new Controls { select = 3 });
            yield return Press(new Controls { fire = true });
            Assert.Greater(game.Battle.Cooldown, 2);
            yield return Press(new Controls { select = 1 });
            Assert.AreEqual(0, game.Battle.Cooldown);
            yield return Press(new Controls { fire = true });
            yield return Press(new Controls { select = 3 });
            Assert.Greater(game.Battle.Cooldown, 1);
        }

        [UnityTest] public IEnumerator HoldingTouchFireKeepsTheCannonsFiring()
        {
            yield return null;
            var screen = InputSystem.AddDevice<Touchscreen>();
            try
            {
                var device = game.GetComponent<DeviceControls>();
                var area = (Rect)typeof(DeviceControls).GetMethod("Area", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(device, new object[] { 0 });
                InputSystem.QueueStateEvent(screen, new TouchState { touchId = 1, phase = UnityEngine.InputSystem.TouchPhase.Began, position = area.center });
                InputSystem.Update();
                Assert.IsTrue(device.Poll().fire);
                InputSystem.QueueStateEvent(screen, new TouchState { touchId = 1, phase = UnityEngine.InputSystem.TouchPhase.Stationary, position = area.center });
                InputSystem.Update();
                var held = device.Poll();
                Assert.IsTrue(held.fire && held.automaticFire, "Stationary contact keeps firing");
                InputSystem.QueueStateEvent(screen, new TouchState { touchId = 1, phase = UnityEngine.InputSystem.TouchPhase.Ended, position = area.center });
                InputSystem.Update();
                Assert.IsFalse(device.Poll().fire);
            }
            finally { InputSystem.RemoveDevice(screen); }
        }

        [UnityTest] public IEnumerator TouchThumbsMoveFromContactAndSwipeToLook()
        {
            var screen = InputSystem.AddDevice<Touchscreen>();
            var control = game.GetComponent<DeviceControls>();
            try
            {
                var safe = Screen.safeArea;
                var left = safe.min + Vector2.Scale(safe.size, new Vector2(0.32f, 0.50f));
                var right = safe.min + Vector2.Scale(safe.size, new Vector2(0.72f, 0.58f));
                void Finger(int id, Vector2 point, UnityEngine.InputSystem.TouchPhase phase) =>
                    InputSystem.QueueStateEvent(screen, new TouchState { touchId = id, position = point, phase = phase });
                Finger(1, left, UnityEngine.InputSystem.TouchPhase.Began);
                Finger(2, right, UnityEngine.InputSystem.TouchPhase.Began);
                InputSystem.Update();
                Assert.AreEqual(Vector2.zero, control.Poll().move);
                left.y += 0.18f * safe.height;
                right.x += 0.12f * safe.height;
                Finger(1, left, UnityEngine.InputSystem.TouchPhase.Moved);
                Finger(2, right, UnityEngine.InputSystem.TouchPhase.Moved);
                InputSystem.Update();
                var drag = control.Poll();
                Assert.Greater(drag.move.y, 0.9f);
                Assert.IsTrue(drag.sprint);
                Assert.Greater(drag.look.x, 3, "A swipe on open glass turns the camera in the same frame");
                InputSystem.Update();
                Assert.AreEqual(Vector2.zero, control.Poll().look, "Holding the aim thumb still stops turning");
                Finger(1, left, UnityEngine.InputSystem.TouchPhase.Ended);
                Finger(2, right, UnityEngine.InputSystem.TouchPhase.Ended);
                InputSystem.Update();
                var released = control.Poll();
                Assert.IsFalse(released.fire, "Dragging either thumb is not a tap");
                Assert.AreEqual(Vector2.zero, released.move);
            }
            finally { InputSystem.RemoveDevice(screen); }
            yield return null;
        }

        [UnityTest] public IEnumerator DeviceRevisionTouchFireAndKeyboardCanWorkTogether()
        {
            var screen = InputSystem.AddDevice<Touchscreen>();
            var keys = InputSystem.AddDevice<Keyboard>();
            try
            {
                yield return Press(new Controls { select = 4 });
                InputSystem.QueueStateEvent(keys, new KeyboardState(Key.W));
                float unit = Mathf.Clamp(Screen.safeArea.height / 720f, 0.8f, 2.5f);
                InputSystem.QueueStateEvent(screen, new TouchState { touchId = 7, phase = UnityEngine.InputSystem.TouchPhase.Began,
                    position = new Vector2(Screen.safeArea.xMin + 90 * unit, Screen.safeArea.yMin + 330 * unit) });
                InputSystem.Update();
                var control = game.GetComponent<DeviceControls>();
                var read = (Controls)typeof(DeviceControls).GetMethod("Read", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(control, null);
                Assert.IsTrue(read.fire, "The touch fire area must read the Input System directly");
                Assert.Greater(read.move.y, 0, "A hardware keyboard remains usable alongside touch");
                InputSystem.QueueStateEvent(screen, new TouchState { touchId = 7, phase = UnityEngine.InputSystem.TouchPhase.Ended });
                InputSystem.Update();
                Assert.IsTrue(control.Poll().fire, "A short tap keeps a railgun charging after the finger lifts");
                yield return Seconds(0.7f);
                Assert.IsFalse(control.Poll().fire, "The railgun releases automatically after charging");
                yield return Press(new Controls { select = 1 });
                foreach (int stick in new[] { 0, 1 })
                {
                    var rect = (Rect)typeof(DeviceControls).GetMethod("StickArea", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(control, new object[] { stick });
                    int finger = 8 + stick;
                    InputSystem.QueueStateEvent(screen, new TouchState { touchId = finger, position = rect.center, phase = UnityEngine.InputSystem.TouchPhase.Began });
                    InputSystem.Update();
                    Assert.IsFalse(control.Poll().fire);
                    InputSystem.QueueStateEvent(screen, new TouchState { touchId = finger, position = rect.center, phase = UnityEngine.InputSystem.TouchPhase.Ended });
                    InputSystem.Update();
                    Assert.IsTrue(control.Poll().fire, $"A tap on joystick {stick} at {rect.center} fires on release");
                }
                yield return Press(new Controls { select = 4 });
                var meters = GameObject.Find("Helmet instruments/Visor circuit").GetComponent<RectTransform>();
                var controls = GameObject.Find("Touch 3").GetComponent<RectTransform>();
                var gaugeCorners = new Vector3[4];
                var controlCorners = new Vector3[4];
                meters.GetWorldCorners(gaugeCorners);
                controls.GetWorldCorners(controlCorners);
                Assert.Greater(gaugeCorners[0].y, controlCorners[2].y, "The lower-left gauges must clear the touch toolbar");
                var muzzle = Camera.main.WorldToViewportPoint(game.GetComponent<Arms>().Muzzle(Player.Weapon) - Solar.Anchor);
                Assert.That(muzzle.x, Is.InRange(0f, 1f), "The rail gauntlet must be visible when aiming");
                Assert.That(muzzle.y, Is.InRange(0f, 1f));
                yield return ReworkShot("revision2-touch");
                var thrustLabel = GameObject.Find("Touch 1/Label").GetComponent<UnityEngine.UI.Text>();
                Assert.AreEqual(1, thrustLabel.cachedTextGenerator.lineCount, "A touch button must not wrap one word across two lines");
                yield return Press(new Controls { help = true });
                yield return Seconds(0.5f);
                yield return ReworkShot("revision2-touch-controls");
                var help = GameObject.Find("HUD").GetComponentsInChildren<UnityEngine.UI.Text>();
                Assert.IsFalse(help.Any(label => label.enabled && (label.text == "Full screen or window" || label.text == "Music on or off" || label.text == "Less or more detail")), "The touch guide lists only available settings");
            }
            finally { InputSystem.RemoveDevice(screen); InputSystem.RemoveDevice(keys); }
        }
        [UnityTest] public IEnumerator DeviceRevisionCockpitAndForearmControls()
        {
            Player.flying = true;
            yield return null;
            var cockpit = Player.Cockpit;
            Assert.IsNotNull(cockpit.transform.Find("SHIELD"));
            var action = new Controls();
            var panel = game.gameObject.AddComponent<ForearmConsole>(); panel.game = game;
            panel.Place(Vector3.zero, Quaternion.identity);
            var canvas = GameObject.Find("Forearm console").transform;
            Assert.Greater(canvas.Find("Wrist display casing").GetComponent<MeshFilter>().sharedMesh.bounds.min.z, 2,
                "The casing must sit behind the screen so it cannot obscure text");
            var build = canvas.Find("BUILD").position;
            action = new Controls();
            Assert.IsTrue(panel.Touch(build, ref action));
            Assert.IsTrue(action.interfaceActive, "Touching the wrist panel must suppress a motion punch");
            Assert.IsFalse(action.fire);
            panel.Touch(Vector3.one * 4, ref action);
            panel.Activate(3, ref action); // return from construction to the suit controls
            panel.Activate(1, ref action);
            Assert.IsTrue(action.systemView);
            var mapPlate = canvas.Find("MAP").GetComponent<UnityEngine.UI.Image>();
            Color idle = mapPlate.color;
            Vector3 map = mapPlate.transform.position;
            Assert.IsTrue(panel.Point(new Ray(map - canvas.forward, canvas.forward), out var hit));
            panel.Refresh();
            Assert.AreNotEqual(idle, mapPlate.color, "Pointing should highlight the button before pressing the trigger");
            Assert.AreEqual(1, panel.At(hit));
            Assert.AreEqual(-1, panel.At(map + canvas.forward * 0.1f));
            Object.Destroy(panel);
        }
        [UnityTest] public IEnumerator DeviceRevisionFactorySweepAndTutorialRespectLocks()
        {
            game.Jump(2);
            yield return null;
            float3 from = game.Outpost.Place(new float3(0, 100, -20));
            float3 direction = Infinigame.Core.Combat.SweepDirection(0, 16, math.normalizesafe(from));
            game.Outpost.Rooms.walls = new[] { new Infinigame.Core.Wall(game.Outpost.Local(from + direction * 10), new float3(2)) };
            game.Battle.Foes.Clear();
            game.Battle.Add(Infinigame.Core.FoeKind.Drone, from + direction * 20);
            float health = game.Battle.Foes[0].health;
            game.Battle.FriendlyFire(from, from + direction * 20, Infinigame.Core.Combat.Arms[4]);
            float after = game.Battle.Foes[0].health;
            game.Jump(1);
            var action = new Controls { fire = true };
            game.Tutorial.Filter(ref action);
            Assert.IsTrue(action.interfaceActive && health == after,
                $"Repair briefing blocked motion punches: {action.interfaceActive}; enemy behind wall: {after}/{health} health");
        }
        [UnityTest] public IEnumerator DeviceRevisionEmptySlotAndSubtitles()
        {
            yield return Choose(Game.Settings);
            for (int i = 0; i < 5; i++) yield return Press(new Controls { down = true });
            yield return Press(new Controls { confirm = true });
            Assert.IsFalse(game.Subtitles);
            Assert.IsFalse(Speech.Muted, "Subtitles and spoken voice are independent");
            yield return Press(new Controls { menu = true });
            yield return Press(new Controls { menu = true });
            yield return Choose("Quit to title");
            Game.Start = 0;
            uint previousSeed = game.Seed;
            yield return Press(new Controls { down = true });
            yield return Press(new Controls { confirm = true });
            Assert.IsFalse(game.Confirming, "An empty slot starts without a destructive-action confirmation");
            Assert.IsFalse(game.Title);
            Assert.AreEqual(1, game.Slot);
            Assert.AreNotEqual(previousSeed, game.Seed, "Each new slot starts a fresh procedural Earth");
            Assert.IsTrue(game.Buried);
        }
        [UnityTest] public IEnumerator DeviceRevisionMenusBurrowsAndFactories()
        {
            yield return Press(new Controls { systemView = true });
            Assert.IsTrue(Player.SystemView);
            yield return null;
            Assert.IsTrue(Player.SystemView, "The map stays open after releasing Tab");
            Assert.AreEqual(0, Time.timeScale);
            yield return Press(new Controls { systemView = true });
            Assert.IsFalse(Player.SystemView);
            yield return Press(new Controls { menu = true });
            yield return Press(new Controls { typed = "idgaf" });
            Assert.IsTrue(game.CheatsUnlocked, "The code works from a pause menu too");
            yield return Press(new Controls { menu = true });
            game.Jump(Infinigame.Core.Campaign.Tower);
            Player.flying = false;
            Player.walker.position = game.Outpost.Place(game.Outpost.Rooms.entrance);
            yield return null;
            Assert.Greater(game.Outpost.OuterLeft, 0);
            Assert.AreEqual(game.Battle.Foes.Count(foe => foe.outerGuard && foe.stationary), game.Outpost.OuterLeft);
            Assert.IsTrue(game.Battle.Foes.Any(foe => foe.outerGuard && !foe.stationary));
            Assert.GreaterOrEqual(game.Battle.Foes.FindAll(foe => foe.guardian).Count, 2);
            var shield = game.Outpost.Shield;
            Assert.Greater(Unity.Mathematics.math.distance(game.Outpost.LandingSite, shield.centre), shield.radius + Infinigame.Core.Combat.ShipRadius);
            foreach (var guard in game.Battle.Foes.FindAll(foe => foe.guardian))
            {
                var guardUp = math.normalize(guard.position);
                Assert.AreEqual(Body.GroundRadius(guardUp) + Infinigame.Core.Combat.SentryHeight, math.length(guard.position), 0.05f);
            }
            int sentry = game.Battle.Foes.FindIndex(foe => foe.guardian);
            var idle = game.Battle.Foes[sentry];
            idle.position += math.normalize(idle.position) * 3;
            game.Battle.Foes[sentry] = idle;
            yield return null; yield return null;
            idle = game.Battle.Foes[sentry];
            Assert.AreEqual(Body.GroundRadius(math.normalize(idle.position)) + Infinigame.Core.Combat.SentryHeight, math.length(idle.position), 0.05f);
            Assert.IsTrue(GameObject.Find("Demolition interlock").activeInHierarchy);
            foreach (var foe in game.Battle.Foes) if (foe.stationary) Assert.AreEqual(Infinigame.Core.FoeKind.Sentry, foe.kind);
            game.Outpost.Killed(Infinigame.Core.FoeKind.Mind);
            game.Outpost.ArmDemolition();
            Assert.That(game.Outpost.Demolition, Is.InRange(19, 20));
            game.Advance();
            var front = game.Frontier.Local;
            front.kills = Infinigame.Core.War.RequiredKills(front);
            Stand(math.normalizesafe(game.Outpost.Position + Player.walker.forward * 100), Player.walker.forward);
            game.Frontier.BuildFactory(0);
            var dogs = Object.FindObjectsByType<MachineMotion>().Where(unit => unit.name == "Robodog patrol").ToArray();
            Assert.AreEqual(1, dogs.Length, "The first robot exits immediately; later robots wait for the assembly line");
            var site = game.Colonies.Single(colony => colony.world == front.world && colony.region == front.region).site;
            var normal = math.normalize(site);
            var emerging = (float3)dogs[0].transform.position + Solar.Anchor;
            Assert.Less(math.length(Infinigame.Core.Walker.Project(emerging - site, normal)), 1, "The deployed robot rises from the factory assembly lift");
            Stand(math.normalize(site - World(normal) * 14), World(normal));
            Player.walker.position += normal * 9; Player.walker.grounded = false;
            yield return Press(new Controls { look = new Vector2(0, -30) });
            var light = new GameObject("Factory review light").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 2; light.transform.rotation = Camera.main.transform.rotation;
            yield return Seconds(0.5f);
            yield return ReworkShot("revision2-factory-deployment");
            Object.Destroy(light.gameObject);
            float before = (float)Solar.Clock;
            Solar.RestoreClock(before + 31);
            Player.walker.position += Player.walker.forward * 200;
            game.Outpost.Tick();
            Assert.IsTrue(game.Ruins.Exists(ruin => ruin.stage == Infinigame.Core.Campaign.Tower && ruin.destroyed));
            
        }
    }
}
