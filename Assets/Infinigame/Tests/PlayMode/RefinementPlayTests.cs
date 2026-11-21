using System.Collections;
using System.Linq;
using System.Reflection;
using Infinigame.Core;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

namespace Infinigame.Tests
{
    public partial class PlayTests
    {
#if UNITY_EDITOR
        [UnityTest] public IEnumerator IndexVisorSeparatesInstrumentsAndMovesToWindscreen()
        {
            DeviceControls.SimulateHeadset = true;
            try
            {
                game.Jump(Campaign.Stranger);
                yield return Seconds(0.5f);
                Assert.IsFalse(Camera.main.allowHDR, "Starting SteamVR after the scene loads must disable headset bloom");
                Assert.IsNotNull(game.GetComponent<ForearmConsole>());
                var ground = Solar.ReferenceBody;
                Assert.AreEqual(ground.GroundRadius(math.normalizesafe(game.Hermit.Position)), math.length(game.Hermit.Position), 0.12f);
                Assert.Greater(Vector3.Dot(Camera.main.transform.forward, ((Vector3)(game.Hermit.Position - Player.Position)).normalized), 0.8f);
                Canvas.ForceUpdateCanvases();
                var hud = game.GetComponent<Hud>();
                T Field<T>(string name) => (T)typeof(Hud).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(hud);
                Rect Project(RectTransform rect)
                {
                    var corners = new Vector3[4];
                    rect.GetWorldCorners(corners);
                    var points = corners.Select(point => Camera.main.WorldToViewportPoint(point)).ToArray();
                    return Rect.MinMaxRect(points.Min(p => p.x), points.Min(p => p.y), points.Max(p => p.x), points.Max(p => p.y));
                }
                var weapon = Project(Field<CanvasGroup>("weaponCard").GetComponent<RectTransform>());
                var vitals = Project(GameObject.Find("Helmet instruments/Visor circuit").GetComponent<RectTransform>());
                Assert.That(vitals.xMin, Is.InRange(0f, 0.48f), $"Gauges {vitals}");
                Assert.That(vitals.yMin, Is.InRange(0f, 0.48f), $"Gauges {vitals}");
                Assert.Greater(vitals.width, 0.06f);
                var lesson = Project(Field<UnityEngine.UI.Text>("lesson").rectTransform);
                Assert.IsFalse(weapon.Overlaps(vitals) || lesson.Overlaps(vitals) || weapon.Overlaps(lesson), $"Weapon {weapon}, gauges {vitals}, tutorial {lesson}");
                Assert.IsFalse(Field<UnityEngine.UI.Image>("reticle").enabled);
                Assert.Greater(math.length(game.Hermit.Position), ground.SeaRadius);
                yield return Seconds(4f);
                yield return ReworkShot("index-suit-hud");
                game.Jump(Campaign.Citadel);
                Player.flying = true; yield return null; yield return null;
                Assert.IsTrue(Field<CanvasGroup>("run").transform.IsChildOf(Player.Cockpit.transform));
                Assert.IsTrue(GameObject.Find("Helmet instruments").transform.IsChildOf(Player.Cockpit.transform));
                Assert.IsFalse(Field<UnityEngine.UI.Image>("reticle").enabled);
                Assert.IsNotNull(Player.Cockpit.transform.Find("Windscreen gunsight"));
                yield return ReworkShot("index-windscreen");
            }
            finally { DeviceControls.SimulateHeadset = false; }
        }
        [UnityTest] public IEnumerator IndexWatchCanBeReadOnOutsideOfLeftForearm()
        {
            DeviceControls.SimulateHeadset = true;
            ForearmConsole console = null;
            try
            {
                game.Jump(Campaign.ThirdBurrow);
                float3 land = Body.FindLanding(244);
                Stand(land, World(land));
                yield return Seconds(0.6f);
                var eye = Camera.main.transform;
                console = game.GetComponent<ForearmConsole>();
                console.enabled = false; // Supply a raised Index pose for the capture.
                var hand = eye.TransformPoint(0.20f, -0.03f, 0.6f);
                var wrist = Quaternion.AngleAxis(-90, eye.forward) * eye.rotation * Quaternion.LookRotation(new Vector3(0.26f, 1, 0), Vector3.back);
                Assert.AreEqual(new Vector3(90, -22, 80), game.FistAngles);
                var grip = wrist * Quaternion.Inverse(Quaternion.Euler(game.FistAngles));
                console.Place(hand, grip);
                var screen = Resources.FindObjectsOfTypeAll<Canvas>().Single(c => c.name == "Forearm console");
                screen.gameObject.SetActive(true); screen.enabled = true;
                var face = screen.GetComponent<RectTransform>();
                Assert.Greater(Vector3.Dot(-face.forward, -eye.forward), 0.98f);
                Assert.Greater(Vector3.Dot(face.up, eye.up), 0.95f);
                var glove = eye.Find("Gauntlets/Left gauntlet");
                var pose = game.GlovePose(0, hand, grip);
                glove.SetPositionAndRotation(pose.position, pose.rotation);
                Assert.IsFalse(glove.GetComponentsInChildren<MeshFilter>().SelectMany(part =>
                    part.sharedMesh.vertices.Select(part.transform.TransformPoint)).Select(face.InverseTransformPoint)
                    .Any(vertex => Mathf.Abs(vertex.z) < 5 && face.rect.Contains(vertex)), "The viewing surface must clear the knuckles");
                var sleeve = face.Find("Forearm pressure sleeve");
                Assert.IsNotNull(sleeve, "The mount must connect to the gauntlet across the clearance below the face");
                Assert.Greater(Vector3.Dot(sleeve.TransformPoint(Vector3.forward) - pose.position, pose.forward), -0.25f);
                Assert.Less(Vector3.Dot(sleeve.TransformPoint(Vector3.back) - pose.position, pose.forward), -game.WristFit.x * 0.01f);
                Vector3 map = face.Find("MAP").position;
                Assert.IsTrue(console.Point(new Ray(map - face.forward, face.forward), out _));
                console.Refresh();
                yield return Seconds(0.3f);
                var instruments = GameObject.Find("Helmet instruments").GetComponent<CanvasGroup>();
                Assert.Less(instruments.alpha, 0.01f, "Reading the watch clears the visor gauges from its buttons");
                yield return ReworkShot("revision2-wrist-console");
                console.ClearContact(); screen.enabled = false;
                yield return Seconds(0.3f);
                Assert.Greater(instruments.alpha, 0.95f, "Lowering the watch restores the visor");
            }
            finally
            {
                if (console != null) console.enabled = true;
                DeviceControls.SimulateHeadset = false;
            }
        }
        [UnityTest] public IEnumerator IndexFittingMovesTheGlovesAndWatchIndependently()
        {
            DeviceControls.SimulateHeadset = true;
            try
            {
                yield return Choose(Game.Settings); yield return Pick(Game.HandAlignment);
                Assert.AreEqual(11, game.PalmSetback);
                Assert.AreEqual(new Vector4(25, 11, 0, 90), game.WristFit);
                foreach (var setting in game.Items.Where(item => item.Contains("−"))) StringAssert.Contains(" 0", setting);
                var glove = game.GlovePose(0, Vector3.zero, Quaternion.identity);
                yield return Pick("PALM SETBACK +");
                var moved = game.GlovePose(0, Vector3.zero, Quaternion.identity);
                Assert.AreEqual(0.01f, Vector3.Dot(glove.position - moved.position, glove.forward), 0.0001f);
                yield return Pick("Back"); yield return Pick(Game.WristAlignment);
                foreach (var setting in game.Items.Where(item => item.Contains("−"))) StringAssert.Contains(" 0", setting);
                var wrist = game.GetComponent<ForearmConsole>();
                wrist.Place(Vector3.zero, Quaternion.identity);
                var screen = Resources.FindObjectsOfTypeAll<Canvas>().Single(c => c.name == "Forearm console").transform;
                Vector3 start = screen.position, face = -screen.forward;
                var sleeve = screen.Find("Forearm pressure sleeve");
                Vector3 join = sleeve.TransformPoint(Vector3.forward);
                Assert.Greater(Vector3.Dot(face, moved.up), 0.99f);
                yield return Pick("TOWARDS ELBOW +"); yield return Pick("FACE HEIGHT +"); yield return Pick("AROUND FOREARM +");
                wrist.Place(Vector3.zero, Quaternion.identity);
                Assert.Greater(Vector3.Distance(start, screen.position), 0.01f);
                Assert.AreEqual(5, Vector3.Angle(face, -screen.forward), 0.05f);
                Assert.Less(Vector3.Distance(join, sleeve.TransformPoint(Vector3.forward)), 0.001f);
                Assert.AreEqual(moved.position, game.GlovePose(0, Vector3.zero, Quaternion.identity).position);
                yield return Pick("Reset wrist display");
                wrist.Place(Vector3.zero, Quaternion.identity);
                Assert.Less(Vector3.Distance(start, screen.position), 0.0001f);
            }
            finally { DeviceControls.SimulateHeadset = false; }
        }

#endif
        [UnityTest] public IEnumerator WildlifeCanBeHitOnceWithoutBleeding()
        {
            game.Jump(Campaign.SecondBurrow);
            int depth = QuadtreeLod.DepthFor(Body.radius, Wildlife.CellSize(Creature.Grazer));
            Troop herd = default;
            for (int cell = 0; herd.count < 3; cell++)
                Wildlife.Find(Body, Creature.Grazer, new NodeId(cell % 6, depth, cell / 6 % (1 << depth), cell / 6 / (1 << depth)), out herd);
            Stand(math.normalize(herd.centre + World(herd.centre) * (16f / Body.radius)), -World(herd.centre));
            var wildlife = Solar.Bodies[Home].GetComponent<Creatures>();
            yield return Until(() => wildlife.Seen.Any(animal => animal.name.Contains("hoofed")));
            var grazer = wildlife.Seen.First(animal => animal.name.Contains("hoofed"));
            var centre = grazer.foot + math.normalizesafe(grazer.foot) * grazer.height * 0.4f;
            Assert.IsTrue(wildlife.Strike(centre, float3.zero));
            Assert.IsFalse(wildlife.Strike(centre, float3.zero));
        }

        [UnityTest] public IEnumerator WristConstructionReturnsHomeOnlyWhenTheFactoryIsBuilt()
        {
            game.Jump(Campaign.SecondBurrow);
            float3 site = Campaign.Site(Solar.Descriptor, Campaign.SecondBurrow);
            Stand(math.normalize(site * Body.radius + World(site) * 90), World(site));
            yield return null;
            var wrist = game.gameObject.AddComponent<ForearmConsole>(); wrist.game = game; wrist.Init();
            var canvas = GameObject.Find("Forearm console").transform;
            var control = new Controls(); wrist.Activate(0, ref control);
            var build = canvas.Find("BUILD").GetComponentInChildren<UnityEngine.UI.Text>();
            game.Frontier.Local.mindDead = false; game.Frontier.Local.kills = 0;
            wrist.Activate(0, ref control); Assert.AreEqual("BUILD ROBODOG FACTORY", build.text);
            game.Frontier.Local.mindDead = true;
            Assert.IsTrue(game.Frontier.CanBuild(0), game.Frontier.FactoryHint(0));
            wrist.Activate(0, ref control);
            Assert.IsTrue(game.Frontier.Local.factory); Assert.AreEqual("BUILD", build.text);
            Object.Destroy(wrist); yield return null;
        }

        [UnityTest] public IEnumerator IndexMapSticksPanAndZoomWithoutChoosingADestination()
        {
            yield return Press(new Controls { systemView = true });
            int destination = game.MapSelection;
            var controls = game.GetComponent<DeviceControls>();
            input = controls.ReadControllers(new DeviceControls.Controller { axis = new Vector2(1, 0.8f) },
                new DeviceControls.Controller { axis = Vector2.up });
            Assert.IsFalse(input.up || input.down);
            yield return null; input = default; yield return null;
            Assert.Greater(game.MapPan.x, 0); Assert.Greater(game.MapPan.y, 0);
            Assert.Less(game.MapZoom, 1);
            Assert.AreEqual(destination, game.MapSelection);
            yield return ClickText("SUMMARY");
            controls.ReadControllers(default, default);
            Assert.IsTrue(controls.ReadControllers(default, new DeviceControls.Controller { axis = Vector2.down }).down);
        }

        [UnityTest] public IEnumerator LookingAroundLeavesSuitFeetPlantedUntilWalking()
        {
            game.Jump(Campaign.SecondBurrow);
            yield return Press(new Controls { toggleView = true });
            Assert.IsTrue(Player.ThirdPerson);
            var feet = GameObject.Find("Traveller").transform;
            yield return Seconds(0.3f);
            Vector3 heading = feet.forward;
            yield return Press(new Controls { look = new Vector2(65, 0) });
            yield return Seconds(0.4f);
            Assert.Less(Vector3.Angle(heading, feet.forward), 2);
            Assert.Greater(Vector3.Angle(heading, feet.Find("Torso").forward), 30);
            input.move = Vector2.up; yield return Seconds(0.6f); input = default;
            Assert.Greater(Vector3.Angle(heading, feet.forward), 35);
        }

        [UnityTest] public IEnumerator UndergroundRetryOpensWithTwoReachableOverheadHits()
        {
            game.Jump(Campaign.FirstBurrow); Player.flying = false;
            Player.Hurt(10000, "Lost suit"); yield return null;
            yield return Press(new Controls { confirm = true });
            Assert.IsTrue(game.Buried);
            var rubble = GameObject.Find("Burial rubble").transform;
            var up = (Vector3)math.normalizesafe(Player.Position);
            var head = (Vector3)(Player.Position - Solar.Anchor) + up * Walker.EyeHeight;
            var stone = rubble.GetChild(0).GetComponent<MeshFilter>();
            var vertices = stone.sharedMesh.vertices.Select(stone.transform.TransformPoint);
            Assert.IsTrue(vertices.Any(point => Vector3.Dot(point - head, up) > 0 && Vector3.Distance(point, head) < 0.75f),
                "There must be visible rubble within an upward arm swing");
            yield return Press(new Controls { fire = true });
            Assert.AreEqual(1, game.Dug);
            yield return Press(new Controls { fire = true });
            Assert.IsFalse(game.Buried); Assert.AreEqual(Campaign.FirstBurrow, game.Stage);
        }

        [UnityTest] public IEnumerator CapturesCoolingRailImpactOnTheGround()
        {
            game.Jump(Campaign.SecondBurrow);
            float3 up = Body.FindLanding(111);
            float3 forward = World(up);
            Stand(up, forward);
            yield return Seconds(1.5f);
            up = math.normalize(Player.Position + forward * 5);
            game.Effects.Impact(up * Body.GroundRadius(up), up, 1.8f, false);
            yield return Seconds(0.10f);
            yield return ReworkShot("rail-ground-impact");
        }

        [UnityTest] public IEnumerator HemisphereBeamsLeaveTheBackpackSocket()
        {
            game.Jump(Campaign.Citadel); Player.flying = false;
            yield return Press(new Controls { select = 5 });
            input.fire = true;
            yield return Until(() => Object.FindObjectsByType<LineRenderer>().Any(line => line.name == "Hemisphere ray" && line.enabled));
            input = default;
            foreach (var beam in Object.FindObjectsByType<LineRenderer>().Where(line => line.name == "Hemisphere ray"))
                Assert.Less(Vector3.Distance(beam.GetPosition(0), Player.BackpackPort - Solar.Anchor), 0.05f);
        }

        [UnityTest] public IEnumerator IndexControlsOpenFromTheOverviewAndCloseByPointer()
        {
            yield return Press(new Controls { systemView = true });
            yield return ClickText("SUIT ATTRIBUTES");
            var labels = GameObject.Find("HUD").GetComponentsInChildren<UnityEngine.UI.Text>();
            StringAssert.DoesNotContain("→", labels.First(t => t.enabled && t.text.Contains("HIT DAMAGE")).text);
            yield return ClickText("CONTROLS");
            yield return Seconds(0.3f);
            var close = labels.First(t => t.enabled && t.text == "CLOSE CONTROLS ×");
            var point = Camera.main.WorldToScreenPoint(close.rectTransform.TransformPoint(close.rectTransform.rect.center));
            var hud = game.GetComponent<Hud>();
            Assert.IsTrue(hud.HighlightPointer((Vector2)point));
            Assert.AreSame(close.transform, GameObject.Find("VR pointer focus").transform.parent);
            Assert.IsTrue(game.ShowControls, "Hovering keeps the controls open until the trigger clicks");
            Assert.IsFalse(hud.HighlightPointer(null));
            yield return Press(new Controls { click = true, pointer = point });
            Assert.IsFalse(game.ShowControls);
            Assert.IsTrue(Player.SystemView);
        }
        [UnityTest] public IEnumerator IndexChargeRemainsArmedUntilTheBlast()
        {
            game.Jump(Campaign.FirstBurrow); yield return null;
            game.Outpost.Killed(FoeKind.Mind);
            var charge = GameObject.Find("Demolition core");
            game.Outpost.ArmDemolition();
            Assert.IsTrue(charge.activeInHierarchy);
            Assert.IsTrue(charge.GetComponentsInChildren<TextMesh>().Any(t => t.text.Contains("ARMED")));
            Assert.IsFalse(game.Outpost.EquipmentReady);
            StringAssert.Contains("beside", game.Wording("factoryDelivery"));
        }
        [UnityTest] public IEnumerator IndexOpeningWaitsForTheRobotsOwnWords()
        {
            var result = new System.Threading.Tasks.TaskCompletionSource<string>();
            string batch = null;
            Oracle.Ask = (persona, prompt) => { if (batch == null) { batch = prompt; return result.Task; } return System.Threading.Tasks.Task.FromResult(prompt); };
            game.Jump(0);
            Oracle.Voice = 1;
            yield return null;
            StringAssert.Contains("You are alive", batch, "Prepare the rescue with the waking line before starting the clock");
            yield return new WaitForSecondsRealtime(9);
            Assert.Less(game.Boot, 1);
            result.SetResult("```json\n" + batch.Replace("You are not dead.", "Your signal holds.").Replace("You are alive.", "There you are.") + "\n```");
            yield return Until(() => game.Prepared("wake"));
            StringAssert.Contains("Your signal holds.", game.Wording("wake"));
            game.Advance();
            yield return Until(() => game.Hermit.Line.Contains("There you are."));
            StringAssert.Contains("There you are.", game.Hermit.Line);
        }
        [UnityTest] public IEnumerator IndexClaudeOpensItsOwnLoginOrSetupPage()
        {
            string page = null;
            Oracle.Browse = url => page = url;
            var login = Oracle.SignIn(0, 0);
            yield return Finish(login);
            Assert.AreEqual("https://code.claude.com/docs/en/setup", page);
            StringAssert.Contains("INSTALL", login.Result);
            page = null;
            Oracle.Tools[0] = StandIn("claude-browser", "if [ \"$2\" = login ]; then echo 'Open https://claude.com/cai/oauth/authorize?state=test'; else echo '{\"loggedIn\":true}'; fi");
            login = Oracle.SignIn(0, 0);
            yield return Finish(login);
            Assert.AreEqual("https://claude.com/cai/oauth/authorize?state=test", page);
            StringAssert.Contains("SIGNED IN", login.Result);
        }
        [UnityTest] public IEnumerator IndexTriggersConfirmOnceAndStickClickBoosts()
        {
            var devices = game.GetComponent<DeviceControls>();
            Player.flying = false;
            var press = new DeviceControls.Controller { trigger = true };
            var click = devices.ReadControllers(new DeviceControls.Controller { click = true }, default);
            Assert.IsTrue(click.sprint); Assert.IsFalse(click.menu);
            yield return Press(new Controls { menu = true });
            foreach (bool left in new[] { true, false })
            {
                devices.ReadControllers(default, default);
                var selection = devices.ReadControllers(left ? press : default, left ? default : press);
                Assert.IsTrue(selection.confirm);
                Assert.IsFalse(selection.fire || selection.jump);
                Assert.IsFalse(devices.ReadControllers(left ? press : default, left ? default : press).confirm);
            }
            yield return Press(new Controls { menu = true });
            devices.ReadControllers(default, default);
            Player.flying = true;
            var stick = new DeviceControls.Controller { axis = new Vector2(0.6f, 0.8f) };
            var flight = devices.ReadControllers(stick, default);
            Assert.AreEqual(0, flight.roll); Assert.Greater(flight.look.x, 0); Assert.AreEqual(0, flight.lift); Assert.AreEqual(0.8f, flight.move.y);
            stick.grip = true;
            var roll = devices.ReadControllers(stick, new DeviceControls.Controller { axis = Vector2.one });
            Assert.Greater(roll.look.sqrMagnitude, 0); Assert.AreEqual(1, roll.roll); Assert.IsTrue(roll.sprint);
        }
        [UnityTest] public IEnumerator ASingleEscortCrossesWaterAndKeepsItsDistance()
        {
            game.Jump(Campaign.ThirdBurrow); yield return null;
            var random = new Unity.Mathematics.Random(491u);
            float3 land = math.normalize(Player.Position), sea = land;
            for (int i = 0; i < 512 && Body.GroundRadius(sea) >= Body.SeaRadius - 4; i++) sea = random.NextFloat3Direction();
            Assert.Less(Body.GroundRadius(sea), Body.SeaRadius - 4);
            // Find the shore along this arc and put the player sixty metres out over the water.
            float3 offshore = sea;
            for (int i = 0; i < 20; i++)
            {
                float3 between = math.normalize(land + sea);
                if (Body.GroundRadius(between) > Body.SeaRadius + 0.3f) land = between; else sea = between;
            }
            float3 site = land * Body.GroundRadius(land);
            var heading = math.normalize(Walker.Project(offshore, land));
            float3 destination = math.normalize(site + heading * 60);
            Player.walker.position = destination * (Body.SeaRadius + 2);
            Player.walker.forward = math.normalize(Walker.Project(heading, destination)); Player.walker.velocity = float3.zero;
            var front = game.Frontier.Local;
            front.factory = front.shipyard = true; front.dogs = 12; front.ships = 4;
            game.Colonies.RemoveAll(colony => colony.world == Home && colony.region == front.region);
            game.Colonies.Add(new Colony { world = Home, region = front.region, site = site });
            game.Frontier.CallDogs();
            var step = (System.Action<float>)System.Delegate.CreateDelegate(typeof(System.Action<float>), game.Frontier, "DrawUnits");
            for (int i = 0; i < 360; i++) step(1f / 30);
            var dog = GameObject.Find("Robodog escort").transform;
            float3 at = (float3)dog.position + Solar.Anchor;
            Assert.Less(Body.GroundRadius(math.normalize(at)), Body.SeaRadius);
            Assert.GreaterOrEqual(math.length(at), Body.SeaRadius + 1.8f);
            Assert.That(math.distance(at, Player.Position), Is.InRange(12f, 18f));
            Assert.Less(math.distance(at, game.Frontier.FollowingDog.Value), 0.1f);
            var hud = game.GetComponent<Hud>(); hud.SendMessage("LateUpdate");
            var link = Object.FindObjectsByType<UnityEngine.UI.Text>().Single(t => t.enabled && t.text.StartsWith("ROBODOG  ·  "));
            Assert.IsTrue(link.text.EndsWith(" m"));
            Assert.IsTrue(dog.Find("Torso/Escort jetpack").GetChild(0).GetChild(0).gameObject.activeSelf);
            Assert.IsNull(GameObject.Find("Fleet escort"));
            for (int i = 0; i < 60; i++) step(1f / 30);
            Assert.Less(Vector3.Distance(at, dog.position + (Vector3)Solar.Anchor), 0.75f);
            Player.ship.position = Player.walker.position; Player.flying = true;
            for (int i = 0; i < 120; i++) step(1f / 30);
            yield return null;
            Assert.IsNull(GameObject.Find("Robodog escort"));
            Assert.IsNull(game.Frontier.FollowingDog);
            Assert.AreEqual(1, Object.FindObjectsByType<Transform>().Count(t => t.name == "Fleet escort"));
        }

        [UnityTest] public IEnumerator IndexPackWaitsOutsideAnOccupiedBurrow()
        {
            game.Jump(Campaign.ThirdBurrow); yield return null;
            Player.walker.position = game.Outpost.Place(game.Outpost.Rooms.entrance);
            yield return Press(new Controls { systemView = true });
            yield return ClickText("SUMMARY");
            yield return ClickText("CALL ROBODOG");
            Assert.IsTrue(game.Escorts.pack);
            yield return Press(new Controls { systemView = true });
            yield return Until(() => Object.FindObjectsByType<MachineMotion>().Count(u => u.name == "Robodog escort") == 1, 12);
            var dogs = Object.FindObjectsByType<MachineMotion>().Where(u => u.name == "Robodog escort").ToArray();
            Assert.AreEqual(1, dogs.Length);
            foreach (var dog in dogs)
            {
                Assert.IsFalse(Body.Dry((float3)dog.transform.position + Solar.Anchor));
                Assert.IsNotNull(dog.transform.Find("Torso/Escort jetpack"));
            }
            foreach (var dog in Object.FindObjectsByType<MachineMotion>().Where(u => u.name == "Robodog patrol"))
                Assert.IsNull(dog.transform.Find("Torso/Escort jetpack"));
            game.Frontier.CallDogs(); yield return null; yield return null;
            Assert.IsNull(GameObject.Find("Robodog escort"));
        }
#if UNITY_EDITOR
        [UnityTest] public IEnumerator IndexEnemyCombatUsesMatteSurfacesAndNoBlastLamp()
        {
            DeviceControls.SimulateHeadset = true;
            try
            {
                yield return UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("Main");
                game = Object.FindAnyObjectByType<Game>();
                yield return Until(() => game.Ready, 60);
                yield return null;
                Assert.IsFalse(Camera.main.allowHDR);
                Assert.IsNull(GameObject.Find("Global Volume"));
                var shell = (Material)typeof(Battle).GetField("broodShell", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(game.Battle);
                var projectile = (Material)typeof(Battle).GetField("theirs", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(game.Battle);
                Assert.AreNotEqual("Infinigame/Hull", shell.shader.name);
                Assert.AreNotEqual("Infinigame/Flash", projectile.shader.name);
                Assert.AreEqual(0, shell.GetFloat("_Smoothness"));
                game.Jump(Campaign.FirstBurrow); yield return null;
                game.Effects.Explode(Player.Position, float3.zero, 2);
                yield return null; yield return null;
                Assert.IsFalse(Object.FindObjectsByType<Light>().Any(l => l.name == "Blast light" && l.enabled && l.intensity > 0));
            }
            finally { DeviceControls.SimulateHeadset = false; }
        }
#endif
        [UnityTest] public IEnumerator RefinementRepairBeamAndWeaponOutline()
        {
            game.Jump(1);
            yield return new WaitForSeconds(13);
            Assert.IsTrue(game.Tutorial.Repairing);
            var beam = GameObject.Find("Suit repair beam").GetComponent<LineRenderer>();
            Assert.IsTrue(beam.enabled);
            Assert.Greater(beam.sharedMaterial.color.b, beam.sharedMaterial.color.r);
            var glyph = Object.FindAnyObjectByType<WeaponGlyph>();
            Canvas.ForceUpdateCanvases();
            var mesh = glyph.canvasRenderer.GetMesh();
            Assert.Greater(mesh.vertexCount, 20, "The equipped weapon has a visible outline mesh");
            Assert.Greater(glyph.canvasRenderer.GetInheritedAlpha(), 0.5f);
            Assert.IsFalse(glyph.canvasRenderer.cull);
            Object.Destroy(mesh);
            yield return ReworkShot("revision3-repair");
        }
        [UnityTest] public IEnumerator RefinementFactoryUplinkExplainsLockedUpgrades()
        {
            game.Jump(Campaign.SecondBurrow); yield return null;
            var second = Campaign.Site(Solar.Descriptor, Campaign.SecondBurrow);
            Stand(math.normalize(second * Body.radius + World(second) * 90), World(second));
            yield return Press(new Controls { ride = true });
            Assert.AreEqual(Game.ColonyPage, game.Page);
            Assert.IsFalse(game.Frontier.CanBuild(1));
            CollectionAssert.AreEqual(new[] { "Build robodog factory", "Back" }, game.Items);
            Assert.IsFalse(game.Frontier.VisibleBuild(1) || game.Frontier.VisibleBuild(2));
            game.Frontier.Local.factory = true;
            Assert.AreEqual(game.Frontier.BuildLabel, game.Items[0]);
            StringAssert.Contains("FACTORY BUILT", game.Frontier.FactoryHint(0));
            for (int region = 0; region < 3; region++) { var front = game.Frontier.State(Home, region); front.aliens = 0; front.factory = front.mindDead = true; }
            game.Colonies.Add(new Colony { world = Home, region = game.Frontier.Region(Home, Player.Position), site = Player.Position });
            Assert.IsTrue(game.Frontier.CanBuild(1));
            CollectionAssert.Contains(game.Items, "Upgrade to shipyard");
            Assert.IsFalse(game.Frontier.CanBuild(2));
        }
        [UnityTest] public IEnumerator RefinementQualitySettingsKeepSixtyFrames()
        {
            yield return Choose(Game.Settings);
            Assert.AreEqual(60, Application.targetFrameRate);
            Assert.AreEqual(0, QualitySettings.vSyncCount, "A quality preset must not halve a 60 Hz display");
            Assert.IsFalse(game.Items.Any(item => item.StartsWith("AUTOMATIC QUALITY")));
        }
        [UnityTest] public IEnumerator RefinementConversationCanBeDismissedWithoutOpeningMenu()
        {
            game.Hermit.Greet();
            yield return null;
            yield return Press(new Controls { click = true, pointer = Vector2.zero });
            Assert.IsFalse(game.Hermit.Talking, "Clicking outside the dialogue closes it");
            Assert.IsFalse(game.Paused);
            game.Hermit.Greet();
            yield return Press(new Controls { menu = true });
            Assert.IsFalse(game.Hermit.Talking);
            Assert.IsFalse(game.Paused);
        }
        [UnityTest] public IEnumerator RefinementTouchActionsHideUnavailableEquipment()
        {
            game.Jump(2);
            yield return null;
            var device = game.GetComponent<DeviceControls>();
            var visible = typeof(DeviceControls).GetMethod("Visible", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsFalse((bool)visible.Invoke(device, new object[] { 8 }), "Recall is unavailable before the ship recovery mission");
            Assert.IsFalse((bool)visible.Invoke(device, new object[] { 13 }), "Cloak needs its equipment");
            Assert.IsNotNull(GameObject.Find("Move joystick"));
            Assert.IsNotNull(GameObject.Find("Look joystick"));
        }
        [UnityTest] public IEnumerator BurrowDemolitionCoreRestsBehindTheMind()
        {
            game.Jump(2);
            yield return null;
            Assert.Greater(game.Outpost.Local(game.Outpost.Prize).z, game.Outpost.Rooms.core.z);
            float3 core = math.normalizesafe(game.Outpost.Core);
            float floor = Body.GroundRadius(core);
            Body.Dig(core, 2);
            Assert.AreEqual(floor, Body.GroundRadius(core), "The living burrow must keep its floor until demolition");
            float3 up = math.normalizesafe(game.Outpost.Prize);
            Assert.AreEqual(Body.GroundRadius(up), math.length(game.Outpost.Prize), 0.15f);
        }
#if UNITY_EDITOR
        [UnityTest] public IEnumerator HeadsetMapKeepsTheViewUprightAndDamageSurroundsTheHead()
        {
            DeviceControls.SimulateHeadset = true;
            try
            {
                Player.flying = false;
                Player.health = Player.MaxHealth * 0.07f;
                yield return null;
                var visor = Camera.main.transform.Find("Full field visor tint").GetComponent<Renderer>();
                Assert.IsTrue(visor.enabled);
                Assert.Greater(visor.sharedMaterial.GetColor("_Tint").a, 0.3f);
                foreach (var axis in new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back })
                    Assert.IsTrue(visor.bounds.Contains(Camera.main.transform.position + axis));
                var forward = Camera.main.transform.forward;
                yield return Press(new Controls { systemView = true });
                Assert.IsTrue(Player.SystemView);
                Assert.Less(Vector3.Angle(forward, Camera.main.transform.forward), 1);
                Assert.IsNotNull(Player.MapCamera.targetTexture);
                Assert.AreNotEqual(Camera.main, Player.MapCamera);
                yield return Press(new Controls { menu = true });
                Assert.Less(Vector3.Angle(forward, Camera.main.transform.forward), 1);
                Player.flying = true;
                yield return null;
                Assert.Less(Vector3.Distance(Object.FindAnyObjectByType<Radar>().transform.position, Player.Cockpit.RadarSocket.position), 0.001f);
            }
            finally { DeviceControls.SimulateHeadset = false; }
        }
#endif
        [UnityTest] public IEnumerator TakingFireShowsTheFittedCloakControl()
        {
            game.Jump(Campaign.Citadel);
            Player.flying = false;
            Player.Hurt(2, "Enemy shot");
            yield return null; yield return null;
            var lesson = GameObject.Find("HUD").GetComponentsInChildren<UnityEngine.UI.Text>().First(item => item.text.Contains("CLOAK TO BREAK CONTACT"));
            StringAssert.Contains("R", lesson.text);
            yield return Press(new Controls { reload = true });
            Assert.IsTrue(Player.cloaked);
            StringAssert.Contains("FIRING REVEALS YOU", lesson.text);
        }

        IEnumerator ClickText(string words)
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            var text = UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Text>().FirstOrDefault(item => item.enabled && item.text == words);
            Assert.IsNotNull(text, $"Missing {words} on map page {game.MapPage}, menu {game.Page}");
            Vector3 centre = text.rectTransform.TransformPoint(text.rectTransform.rect.center);
            Vector2 pointer = RectTransformUtility.WorldToScreenPoint(Camera.main, centre);
            Assert.GreaterOrEqual(game.GetComponent<Hud>().MapControlAt(pointer), 0, $"{words} accepts a click at its visible centre");
            yield return Press(new Controls { click = true, pointer = pointer });
        }

        [UnityTest] public IEnumerator RefinementMapPagesZoomAndGodMode()
        {
            yield return Press(new Controls { systemView = true });
            Assert.IsTrue(Player.SystemView); Assert.IsFalse(game.MapSummary);
            double clock = Solar.Clock;
            yield return Press(new Controls { cycle = 1 });
            Assert.Less(game.MapZoom, 1);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return ReworkShot("revision3-map");
            float zoom = game.MapZoom;
            yield return Press(new Controls { mapDrag = Vector2.right * 0.1f, mapPinch = 1.5f });
            Assert.Less(game.MapZoom, zoom);
            Assert.Less(game.MapPan.x, 0);
            yield return ClickText("SUIT ATTRIBUTES");
            Assert.AreEqual(2, game.MapPage);
            yield return ClickText("SUMMARY");
            Assert.IsTrue(game.MapSummary); Assert.AreEqual(clock, Solar.Clock);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return ReworkShot("revision3-summary");
            yield return Press(new Controls { systemView = true });
            yield return Press(new Controls { menu = true });
            yield return Press(new Controls { typed = "idgaf" });
            yield return Press(new Controls { menu = true });
            yield return Choose(Game.Cheats);
            while (!game.Items[game.Selected].StartsWith("INVULNERABLE")) yield return Press(new Controls { down = true });
            yield return Press(new Controls { confirm = true });
            float health = Player.health, hull = Player.hull;
            Player.Hurt(10000, "test");
            Assert.IsTrue(game.GodMode); Assert.AreEqual(health, Player.health); Assert.AreEqual(hull, Player.hull);
        }
        [UnityTest] public IEnumerator RefinementTwinBeamsAndShipRailCharge()
        {
            HoverAt(math.normalizesafe(Player.Position) * (Body.radius + 1200));
            yield return null; yield return null;
            yield return Press(new Controls { select = 3 });
            input.fire = true; yield return null; yield return null;
            var rays = game.Battle.GetComponentsInChildren<LineRenderer>().Where(ray => ray.enabled && (ray.name == "Weapon beam" || ray.name == "Twin rail trail")).ToArray();
            Assert.AreEqual(2, rays.Length);
            Assert.Greater(Vector3.Distance(rays[0].GetPosition(0), rays[1].GetPosition(0)), 3);
            input = default; yield return Seconds(0.2f);
            yield return Press(new Controls { select = 4 });
            float fired = game.Battle.Fired;
            input.fire = true; yield return Seconds(0.5f);
            Assert.Less(Camera.main.fieldOfView, 58, "Charging the ship rail zooms the flat display");
            Assert.AreEqual(fired, game.Battle.Fired); Assert.Greater(game.Battle.RailCharge, 0.35f);
            input.fire = false; yield return null; yield return null;
            Assert.Greater(game.Battle.Fired, fired);
        }
        [UnityTest] public IEnumerator RefinementLivingCitadelAndLiquidRemains()
        {
            yield return HoverOver(Solar.Station, 500);
            game.Outpost.Tick();
            Assert.AreEqual(3, game.Battle.Foes.Count(foe => foe.kind == FoeKind.Mind));
            Assert.IsTrue(game.Battle.Foes.Where(foe => foe.kind == FoeKind.Mind).All(foe => foe.size >= 6));
            Assert.GreaterOrEqual(game.Battle.Foes.Count(foe => foe.stationary), 6);
            Assert.AreEqual(12, game.Battle.Foes.Count(foe => foe.guardian && foe.kind == FoeKind.Drone));
            Assert.AreEqual(6, game.Battle.Foes.Count(foe => foe.guardian && foe.kind == FoeKind.Sentry && foe.caste == MotileCaste.Spitter));
            Assert.IsTrue(game.Outpost.Shield.active);
            foreach (var mind in game.Battle.Foes.Where(foe => foe.kind == FoeKind.Mind).Take(2).ToArray())
            { int i = game.Battle.Foes.IndexOf(mind); var killed = mind; killed.health = 0; game.Battle.Foes[i] = killed; }
            yield return null; yield return null;
            Assert.AreEqual(1, game.Outpost.Left); Assert.IsFalse(game.Outpost.Fallen);
            yield return Until(() => Object.FindAnyObjectByType<IchorPool>() != null);
            var pool = Object.FindAnyObjectByType<IchorPool>();
            Vector3 centre = pool.transform.localPosition;
            yield return Seconds(0.3f);
            Assert.Greater(pool.GetComponent<MeshFilter>().sharedMesh.vertexCount, 100);
            Assert.AreEqual(centre, pool.transform.localPosition);
            Assert.AreEqual("Infinigame/Ichor", pool.GetComponent<Renderer>().sharedMaterial.shader.name);
            Assert.GreaterOrEqual(game.Corpses.Count, 2);
            HoverAt(game.Outpost.Place(game.Outpost.Rooms.route[5] + new float3(0, 0, -20)));
            yield return ReworkShot("revision3-citadel-remains");
            var objective = Object.FindObjectsByType<UnityEngine.UI.Text>().First(text => text.enabled && text.text == "MISSION · " + game.Outpost.CitadelMark);
            var marker = (RectTransform)objective.transform.parent;
            Assert.Greater(marker.anchoredPosition.y, ((RectTransform)marker.parent).rect.height * 0.25f, "The mission bearing sits above the cockpit instruments");
        }
    }
}
