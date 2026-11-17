using System.Collections;
using System.Reflection;
using System.Linq;
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
                yield return ReworkShot("revision2-touch");
                var thrustLabel = GameObject.Find("Touch 1/Label").GetComponent<UnityEngine.UI.Text>();
                Assert.AreEqual(1, thrustLabel.cachedTextGenerator.lineCount, "A touch button must not wrap one word across two lines");
                yield return Press(new Controls { help = true });
                yield return Seconds(0.5f);
                yield return ReworkShot("revision2-touch-controls");
            }
            finally { InputSystem.RemoveDevice(screen); InputSystem.RemoveDevice(keys); }
        }
        [UnityTest] public IEnumerator DeviceRevisionCockpitAndForearmControls()
        {
            Player.flying = true;
            yield return null;
            var cockpit = Player.Cockpit;
            var assist = cockpit.transform.Find("ASSIST").position;
            Assert.AreEqual(5, cockpit.TouchControl(assist));
            Assert.AreEqual(5, cockpit.ScreenControl(Camera.main.WorldToScreenPoint(assist), Camera.main));
            var action = new Controls();
            Cockpit.Activate(5, ref action);
            Assert.IsTrue(action.toggleAssist);
            Cockpit.Activate(3, ref action);
            Assert.AreEqual(4, action.select, "The railgun rack selects the fourth weapon");
            Cockpit.Activate(7, ref action);
            Assert.IsTrue(action.interact, "The landing switch uses the normal landing control");
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
            Vector3 map = canvas.Find("MAP").position;
            Assert.IsTrue(panel.Point(new Ray(map - canvas.forward, canvas.forward), out var hit));
            Assert.AreEqual(1, panel.At(hit));
            Assert.AreEqual(-1, panel.At(map + canvas.forward * 0.1f));
            canvas.SetPositionAndRotation(Camera.main.transform.TransformPoint(0, 0, 0.5f), Camera.main.transform.rotation);
            yield return ReworkShot("revision2-wrist-console");
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
            Assert.GreaterOrEqual(game.Outpost.OuterLeft, 2);
            Assert.GreaterOrEqual(game.Battle.Foes.FindAll(foe => foe.guardian).Count, 2);
            Assert.IsTrue(GameObject.Find("Locked equipment field").activeInHierarchy);
            foreach (var foe in game.Battle.Foes) if (foe.stationary) Assert.AreEqual(Infinigame.Core.FoeKind.Sentry, foe.kind);
            game.Outpost.Killed(Infinigame.Core.FoeKind.Mind);
            game.Outpost.ArmDemolition();
            Assert.That(game.Outpost.Demolition, Is.InRange(19, 20));
            game.Advance();
            var front = game.Frontier.Local;
            front.kills = Infinigame.Core.War.RequiredKills(front);
            Stand(math.normalizesafe(game.Outpost.Position + Player.walker.forward * 100), Player.walker.forward);
            game.Frontier.BuildFactory(0);
            var units = (System.Collections.IList)typeof(Frontier).GetField("units", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(game.Frontier);
            Assert.AreEqual(1, units.Count, "The first robot exits immediately; later robots wait for the assembly line");
            var site = game.Colonies.First(colony => colony.world == Solar.Reference).site;
            var normal = math.normalize(site);
            var firstUnit = units[0];
            var emerging = (float3)firstUnit.GetType().GetField("position").GetValue(firstUnit);
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
