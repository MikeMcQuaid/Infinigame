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
        [Test]
        public void WindowsMonitorRecoversNativeSizeAndHonoursExplicitDisplayChoices()
        {
            var native = typeof(DeviceControls).GetMethod("UseNativeMonitor", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(native, "A 720p size left by the headset must not select the monitor resolution");
            bool Select(params string[] args) => (bool)native.Invoke(null, new object[] { args });
            Assert.IsTrue(Select());
            Assert.IsTrue(Select("-monitor", "2"));
            Assert.IsTrue(Select("-screen-fullscreen", "1", "-window-mode", "borderless"));
            Assert.IsFalse(Select("-screen-fullscreen", "0"));
            Assert.IsFalse(Select("-screen-width", "1600", "-screen-height", "900"));
            Assert.IsFalse(Select("-window-mode", "exclusive"));
        }

        [UnityTest] public IEnumerator TouchReadoutsStayAboveBothThumbs()
        {
            var touch = typeof(DeviceControls).GetProperty("Touch");
            touch.SetValue(null, true);
            try
            {
                yield return null;
                var vitals = (RectTransform)typeof(SuitHud).GetField("metersPlate", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(game.GetComponent<SuitHud>());
                var weapon = (CanvasGroup)typeof(Hud).GetField("weaponCard", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(game.GetComponent<Hud>());
                var controls = game.GetComponent<DeviceControls>();
                var area = typeof(DeviceControls).GetMethod("Area", BindingFlags.Instance | BindingFlags.NonPublic);
                float fireTop = ((Rect)area.Invoke(controls, new object[] { 0 })).yMax;
                float useTop = ((Rect)area.Invoke(controls, new object[] { 2 })).yMax;
                foreach (var rect in new[] { vitals, (RectTransform)weapon.transform })
                {
                    var corners = new Vector3[4]; rect.GetWorldCorners(corners);
                    var canvas = rect.GetComponentInParent<Canvas>().rootCanvas;
                    var point = RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, corners[0]);
                    Assert.Greater(point.y, Mathf.Max(Screen.safeArea.center.y, rect == vitals ? fireTop : useTop), rect.name + " crosses a thumb zone");
                }
            }
            finally { touch.SetValue(null, false); }
        }

        [UnityTest] public IEnumerator FirstBurrowLeavesRoomToLearnTheFists()
        {
            game.Jump(Campaign.FirstBurrow);
            yield return null;
            Assert.AreEqual(Difficulty.Normal, game.Challenge);
            Assert.AreEqual(1, game.Battle.Foes.Count(foe => foe.kind == FoeKind.Sentry));
            game.Battle.quiet = false;
            for (int wave = 0; wave < 4; wave++)
            {
                typeof(Frontier).GetField("deployed", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(game.Frontier, 0f);
                game.Frontier.Tick();
            }
            Assert.LessOrEqual(game.Battle.Foes.Count(foe => foe.patrol), 1, "Ambient patrols must leave room to learn");
        }

        [UnityTest] public IEnumerator OrbitalApproachTargetsTheSiteBeforeChangingGravityFrame()
        {
            game.Jump(Campaign.Tower);
            var world = game.Outpost.World;
            yield return HoverOver(Home, 100);
            Assert.AreNotEqual(world, Solar.Reference);
            float3 site = (float3)Solar.Bodies[world].transform.TransformPoint(game.Outpost.LandingSite) + Solar.Anchor;
            Assert.Less(math.distance(game.Goal.Value, site), 0.15f);
        }

        [UnityTest] public IEnumerator MissionAutopilotRoundsLunaAndFacesTheShield()
        {
            game.Jump(Campaign.Tower);
            float3 site = game.Outpost.LandingSite;
            HoverAt(-math.normalize(site) * (Body.radius + 90));
            Player.TravelTo(Solar.Reference);
            Assert.IsTrue(Player.Autopilot);
            yield return Until(() => !Player.Autopilot, 30);
            Assert.Greater(math.dot(math.normalize(Player.Position), math.normalize(site)), 0.93f);
            Assert.Less(math.distance(Player.Position, site), 380);
            Assert.Greater(math.dot(math.rotate(Player.ship.rotation, new float3(0, 0, 1)), math.normalize(game.Outpost.Position - Player.Position)), 0.92f);
        }

        [UnityTest] public IEnumerator NormalSurfaceTurretFallsToFourCannonHits()
        {
            game.Jump(Campaign.Tower);
            yield return null;
            var turret = game.Battle.Foes.First(foe => foe.outerGuard && foe.stationary);
            for (int shot = 0; shot < 4; shot++) Combat.Hurt(ref turret, Combat.Guns[0].damage, game.Challenge);
            Assert.LessOrEqual(turret.health, 0);
        }

        [UnityTest] public IEnumerator MoonFactoryGunsStopAtTheAtmosphere()
        {
            game.Jump(Campaign.Won);
            yield return null;
            var mount = Object.FindObjectsByType<FactoryDefence>().First(defence => defence.world == Solar.Reference);
            float3 up = math.normalize((float3)mount.transform.localPosition);
            float air = Body.radius * Shapes.AirTop(Body);
            var target = Combat.Spawn(FoeKind.Drone, up * (air + 5), -up, 1, 1);
            game.Battle.Foes.Clear();
            game.Battle.Foes.Add(target);
            typeof(FactoryDefence).GetField("weapon", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(mount, 1);
            mount.SendMessage("Update");
            Assert.AreEqual(target.health, game.Battle.Foes[0].health);
            target.position = up * (air - 3);
            game.Battle.Foes[0] = target;
            mount.SendMessage("Update");
            Assert.Less(game.Battle.Foes[0].health, target.health);
        }

        [UnityTest] public IEnumerator ReplayingMoonIgnitionShowsGroundUnderTheEnginesOnItsFirstFrame()
        {
            game.Jump(Campaign.Won);
            game.Frontier.Launch();
            var moon = Solar.Bodies[game.Fall.moon];
            moon.SendMessage("Update");
            Assert.GreaterOrEqual(moon.GetComponentsInChildren<MeshFilter>().Count(part => part.name == "Patch"
                && part.GetComponent<MeshRenderer>().enabled && part.sharedMesh.vertexCount > 0), 6);
            yield return null;
        }

#if UNITY_EDITOR
        [UnityTest] public IEnumerator IndexStrokesBreakTheFirstBurrowPlugAtItsVisibleSurface()
        {
            DeviceControls.SimulateHeadset = true;
            try
            {
                game.Jump(Campaign.FirstBurrow);
                yield return null;
                var room = game.Outpost;
                var arms = game.GetComponent<Arms>();
                var plug = room.Rooms.walls[^1];
                var contact = plug.centre;
                contact.y = 1;
                contact.z -= plug.half.z + 0.35f;
                float3 direction = math.normalize(room.Place(contact + new float3(0, 0, 1)) - room.Place(contact));
                for (int count = 0; count < 4 && !room.Rooms.walls[^1].broken; count++)
                {
                    int hand = count % 2;
                    arms.punchOrigins[hand] = room.Place(contact) - direction * 0.4f;
                    arms.punchDirections[hand] = direction;
                    arms.punchLengths[hand] = 0.4f;
                    typeof(Arms).GetProperty("PunchHands").SetValue(arms, 1 << hand);
                    game.Battle.Tick(default);
                }
                Assert.IsTrue(room.Rooms.walls[^1].broken);
            }
            finally { DeviceControls.SimulateHeadset = false; }
        }
#endif

        [UnityTest] public IEnumerator RocksInTheStarBeltCanHitAShipNearTheCitadel()
        {
            game.Jump(Campaign.Citadel);
            if (Solar.Reference != Solar.Station) yield return HoverOver(Solar.Station, 2000);
            var map = Solar.Descriptor;
            var star = map.bodies[0];
            float3 origin = map.ToLocal(0, 0, map.PositionAt(Solar.Station, 0));
            const int size = 3;
            float cell = Rings.Cell(star, size);
            var rocks = new System.Collections.Generic.List<Rock>();
            for (int band = (int)((math.length(origin.xz) - star.ringInner) / cell) - 1; band <= (int)((math.length(origin.xz) - star.ringInner) / cell) + 1; band++)
            {
                int sectors = Rings.Sectors(star, size, band);
                int middle = (int)math.floor(math.atan2(origin.z, origin.x) * sectors / (2 * math.PI));
                for (int j = middle - 2; j <= middle + 2; j++)
                    Rings.Rocks(star, size, band, (j % sectors + sectors) % sectors, rocks);
            }
            var rock = rocks.First(r => math.distance(origin, r.position) > 1400 && math.distance(origin, r.position) < Body.soiRadius - 100);
            float3 centre = Rings.Now(star, size, 0, Solar.Clock, rock.position);
            float3 velocity = math.cross(new float3(0, Rings.Turned(star, size, 0, 1), 0), centre);
            map.Rebase(0, Solar.Reference, Solar.Clock, ref centre, ref velocity);
            HoverAt(centre + new float3(0, rock.radius + Combat.ShipRadius - 0.25f, 0));
            Player.ship.velocity = velocity - new float3(0, 45, 0);
            float hull = Player.hull;
            Player.Tick(default);
            Assert.Less(Player.hull, hull);
            StringAssert.Contains("asteroid", Player.Cause);
            Assert.Greater(math.distance(centre, Player.ship.position), rock.radius + Combat.ShipRadius);
        }

        [UnityTest] public IEnumerator TheYardLaunchesItsPilotedShipIntoOrbitWhileYouAreOnFoot()
        {
            game.Jump(Campaign.FirstBurrow);
            var front = game.Frontier.Local;
            front.factory = front.shipyard = true;
            front.ships = 1;
            float3 site = Player.Position;
            game.Colonies.Add(new Colony { world = Home, region = front.region, site = site });
            var step = (System.Action<float>)System.Delegate.CreateDelegate(typeof(System.Action<float>), game.Frontier, "DrawUnits");
            step(0);
            var escort = GameObject.Find("Fleet escort");
            Assert.IsNotNull(escort);
            Assert.IsNotNull(escort.transform.Find("Robot pilot"));
            Assert.Less(math.distance((float3)escort.transform.position + Solar.Anchor, site), 2);
            for (int i = 0; i < 600; i++) step(0.05f);
            Assert.Greater(math.length((float3)escort.transform.position + Solar.Anchor), Body.radius * Shapes.AirTop(Body));
            yield return null;
        }

        [UnityTest] public IEnumerator MoonEnginesCanBeBuiltFarFromAnySettlement()
        {
            game.Jump(Campaign.Won);
            int moon = Solar.Reference;
            float3 up = -math.normalize(game.Colonies.Single(site => site.world == moon).site);
            Stand(up, World(up));
            game.Presence.fronts.ForEach(front => { front.shipyard = false; front.alienShips = 4; });
            StringAssert.Contains("BUILD MOON ENGINES HERE", game.Objective);
            Player.flying = true;
            StringAssert.Contains("ANY MOON", game.Objective);
            Player.flying = false;
            Assert.IsTrue(game.Frontier.CanBuild(2));
            game.Frontier.BuildFactory(2);
            Assert.IsTrue(game.Fall.armed);
            Assert.AreEqual(moon, game.Fall.moon);
            Assert.Greater(Vector3.Dot(up, Solar.Bodies[moon].transform.Find("Moon engine").up), 0.7f);
            yield return null;
        }

        [UnityTest] public IEnumerator MoonEnginesWaitForThePilotToClearTheirMoon()
        {
            game.Jump(Campaign.Won);
            var moon = game.Fall.moon;
            Assert.AreEqual(moon, Solar.Reference);
            var yard = game.Colonies.Single(site => site.world == moon);
            Stand(math.normalize(yard.site), World(math.normalize(yard.site)));
            Assert.IsTrue(game.Frontier.CanBuild(2));
            game.Frontier.BuildFactory(2);
            Assert.IsFalse(game.Fall.active);
            Assert.IsFalse(Player.flying);
            Assert.IsNotNull(Solar.Bodies[moon].transform.Find("Moon engine"));
            Player.flying = true;
            HoverAt(math.normalize(yard.site) * (Body.radius + math.max(210, Body.radius * 0.50f)));
            yield return null;
            Assert.IsFalse(game.Fall.active);
            HoverAt(math.normalize(yard.site) * (Body.radius * 2.5f + 1000));
            yield return null; yield return null;
            Assert.IsTrue(game.Fall.active);
            Assert.AreEqual(moon, game.Fall.moon);
            yield return Seconds(Moonfall.Assembly + 0.3f);
            Vector3 away = math.normalize(Solar.ToReference(moon) - Solar.ToReference(game.Fall.world));
            foreach (var engine in Solar.Bodies[moon].GetComponentsInChildren<Transform>().Where(part => part.name == "Moon engine"))
            {
                Assert.Greater(Vector3.Dot(engine.up, away), 0.75f);
                Assert.Greater(Vector3.Dot(Camera.main.transform.forward, (engine.position - Camera.main.transform.position).normalized), 0.90f);
            }
        }

        [UnityTest] public IEnumerator NewExpeditionDisplaysZeroBeforeItsFirstFrame()
        {
            Game.Start = Campaign.Buried;
            typeof(Game).GetMethod("Switch", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, new object[] { 1, Difficulty.Creative });
            Assert.IsFalse(game.Ready);
            var progress = GameObject.Find("Loading/Progress").GetComponent<UnityEngine.UI.Text>();
            StringAssert.StartsWith("SUIT STARTUP  0%", progress.text);
            StringAssert.Contains("POWER LOSS RECORDED", progress.text);
            Vector2 position = progress.rectTransform.anchoredPosition;
            int font = progress.fontSize;
            yield return Until(() => game.Ready);
            yield return null;
            Assert.IsTrue(progress.isActiveAndEnabled);
            Assert.AreEqual(position, progress.rectTransform.anchoredPosition);
            Assert.AreEqual(font, progress.fontSize);
            StringAssert.StartsWith("SUIT STARTUP  100%", progress.text);
            Assert.AreEqual(Difficulty.Creative, game.Challenge);
            yield return Press(new Controls { fire = true });
            Assert.AreEqual(1, game.Dug, "The first frame of play accepts a punch");
        }

        [UnityTest] public IEnumerator RecoveringSuitReportsProgressAndSheltersFromRain()
        {
            game.Jump(Campaign.Buried);
            var expedition = typeof(Game).GetField("script", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(game);
            var waiting = expedition.GetType().GetProperty("WaitingForOpening");
            waiting.SetValue(expedition, true);
            try
            {
                yield return null;
                var boot = GameObject.Find("Loading/Progress").GetComponent<UnityEngine.UI.Text>();
                StringAssert.Contains("100%", boot.text);
                yield return Press(new Controls { fire = true });
                StringAssert.Contains("1/2", boot.text);
                Assert.IsTrue(game.GetComponent<Rainfall>().Sheltered);
            }
            finally { waiting.SetValue(expedition, false); }
        }

        [UnityTest] public IEnumerator DesktopRadarFitsTheCornerWhenZoomed()
        {
            Assert.IsFalse(GameObject.Find("Touch controls").GetComponent<Canvas>().enabled);
            var camera = Camera.main;
            camera.fieldOfView = 34;
            var radar = Object.FindAnyObjectByType<Radar>();
            radar.SendMessage("LateUpdate");
            Vector3 centre = camera.WorldToViewportPoint(radar.transform.position);
            Vector3 edge = camera.WorldToViewportPoint(radar.transform.position + camera.transform.up * radar.transform.localScale.y);
            Assert.Greater(centre.x, 0.75f);
            Assert.Greater(centre.y, 0.8f);
            Assert.That(edge.y, Is.InRange(centre.y + 0.04f, 0.98f));
            yield return null;
        }

        [UnityTest] public IEnumerator CreativeStillRegistersSuitAndHullHits()
        {
            yield return Choose(Game.Settings);
            Assert.AreEqual(Difficulty.Normal, game.Challenge);
            yield return Pick("DIFFICULTY");
            yield return Pick("DIFFICULTY");
            yield return Pick("DIFFICULTY");
            Assert.AreEqual(Difficulty.Creative, game.Challenge);
            foreach (bool flying in new[] { false, true })
            {
                Player.flying = flying;
                Player.energy = 0;
                Player.health = Player.hull = 0;
                Assert.IsFalse(Player.Dead);
                Player.Hurt(10000, "Burned by lava");
                Player.Hurt(10000, "Gunned down by Skimmers on Luna");
                Assert.AreEqual(1, flying ? Player.hull : Player.health);
                Assert.IsFalse(Player.Dead);
            }
        }

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
            foreach (string item in new[] { Game.Cheats, "Choose mission", $"{Campaign.Missions.Length + 1}. Build moon engines" })
            {
                for (int i = 0; i < game.Items.Length && game.Items[game.Selected] != item; i++)
                    yield return Press(new Controls { down = true });
                Assert.AreEqual(item, game.Items[game.Selected]);
                yield return Press(new Controls { confirm = true });
            }
            Assert.IsFalse(game.Fall.active);
            Assert.IsTrue(game.Frontier.CanBuild(2));
            game.Frontier.BuildFactory(2);
            Assert.IsTrue(game.Fall.armed);
            Assert.IsFalse(game.Fall.active);
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
                float unit = Mathf.Clamp(Screen.safeArea.height / 720f, 0.5f, 2.5f);
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
                var muzzle = Camera.main.WorldToViewportPoint(game.GetComponent<Arms>().Muzzle(Player.Weapon).position);
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
            Assert.AreEqual("Choose difficulty", game.Page);
            CollectionAssert.AreEqual(new[] { "Creative", "Easy", "Normal", "Hard", "Realistic", "Back" }, game.Items);
            yield return Pick("Realistic");
            Assert.AreEqual(Difficulty.Realistic, game.Challenge);
            Assert.IsFalse(game.Confirming);
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
            Assert.Less(math.length(Infinigame.Core.Walker.Project(emerging - (float3)Models.Kennel(site), normal)), 1, "The deployed robodog leaves the workshop alongside the berth");
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
