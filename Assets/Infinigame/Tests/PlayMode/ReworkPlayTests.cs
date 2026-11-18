using System.Collections;
using System.IO;
using System.Linq;
using Infinigame.Core;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

namespace Infinigame.Tests
{
    public partial class PlayTests
    {
        IEnumerator ReworkShot(string name)
        {
            yield return null;
            // Camera choreography runs after this batch-mode coroutine.
            if (game.Fall.active) game.Frontier.SendMessage("LateUpdate");
            var camera = Camera.main;
            int width = DeviceControls.Touch ? Screen.width : 1280, height = DeviceControls.Touch ? Screen.height : 800;
            var target = new RenderTexture(width, height, 24);
            camera.targetTexture = target;
            // Overlay canvases do not render into a camera texture, so temporarily attach the visor.
            var visor = GameObject.Find("Suit glass").GetComponent<Canvas>();
            visor.renderMode = RenderMode.ScreenSpaceCamera; visor.worldCamera = camera;
            visor.planeDistance = game.Fall.active ? Mathf.Max(100, Mathf.NextPowerOfTwo((int)(camera.transform.position.magnitude * 0.02f))) : camera.nearClipPlane + 0.01f;
            var touch = GameObject.Find("Touch controls").GetComponent<Canvas>();
            touch.renderMode = RenderMode.ScreenSpaceCamera; touch.worldCamera = camera; touch.planeDistance = visor.planeDistance;
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = target;
            var picture = new Texture2D(width, height, TextureFormat.RGB24, false);
            picture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            Directory.CreateDirectory("Logs/Screenshots");
            File.WriteAllBytes("Logs/Screenshots/" + name + ".png", picture.EncodeToPNG());
            RenderTexture.active = null; camera.targetTexture = null; visor.renderMode = RenderMode.ScreenSpaceOverlay; touch.renderMode = RenderMode.ScreenSpaceOverlay;
            Object.Destroy(target); Object.Destroy(picture);
        }

        [UnityTest]
        public IEnumerator ReworkOpeningBootsInArmourAndPunchesOut()
        {
            game.Jump(0);
            yield return null;
            Assert.IsTrue(Player.Has(Gear.Suit)); Assert.IsFalse(Player.Powered);
            Assert.AreEqual(3, Player.energy, 0.01f);
            Assert.AreEqual("Emergency fists", Player.Weapon.name);
            yield return Press(new Controls { fire = true });
            Assert.AreEqual(0, game.Dug, "The boot sequence must finish before punching");
            yield return Seconds(2);
            yield return ReworkShot("110-suit-boot");
            yield return Seconds(Game.BootDuration - 1.5f);
            yield return ReworkShot("111-emergency-hud");
            for (int i = 0; i < Game.Digs; i++) yield return Press(new Controls { fire = true });
            Assert.AreEqual(1, game.Stage);
            Assert.Less(math.distance(Player.Position, game.Hermit.Position), 25);
            Assert.IsNotNull(GameObject.Find("Recovery companion/Hermit"));
            float3 locked = Player.walker.position, facing = Player.walker.forward;
            yield return Press(new Controls { talk = true, move = Vector2.up, look = new Vector2(80, 20) });
            Assert.IsFalse(game.Hermit.Talking, "The recovery briefing is inline and cannot be skipped");
            Assert.Less(math.distance(locked, Player.walker.position), 0.5f);
            Assert.Less(math.distance(facing, Player.walker.forward), 0.01f);
            yield return new WaitForSeconds(17.5f);
            yield return Press(new Controls { look = new Vector2(60, 0) });
            yield return Seconds(5.5f);
            input = new Controls { move = Vector2.up }; yield return Seconds(1); input = default;
            yield return Until(() => game.Battle.Foes.Exists(foe => foe.kind == FoeKind.Sentry));
            Assert.AreEqual(1, game.Battle.Foes.Count);
            var fighter = game.Battle.Foes[0]; fighter.health = 0; game.Battle.Foes[0] = fighter;
            yield return null; yield return null;
            Assert.AreEqual(2, game.Stage);
            Assert.IsFalse(Player.Powered);
        }

        [UnityTest]
        public IEnumerator ReworkBurrowHasOnlyStalkersAndCoreRestoresTheBeam()
        {
            game.Jump(2);
            yield return null;
            Player.walker.position = game.Outpost.Place(game.Outpost.Rooms.entrance);
            Player.walker.forward = math.normalize(Walker.Project(game.Outpost.Core - Player.Position, math.normalize(Player.Position)));
            Player.health = 10000;
            yield return Seconds(1);
            Assert.Less(game.Outpost.Rooms.core.y, -5);
            Assert.IsTrue(Body.Dry(game.Outpost.Core));
            Assert.IsFalse(game.Outpost.Use());
            Assert.IsNull(GameObject.Find("Control terminal"));
            Assert.AreEqual(3, game.Battle.Foes.Count(foe => foe.kind == FoeKind.Sentry));
            Assert.IsTrue(game.Battle.Foes.Where(foe => foe.kind == FoeKind.Sentry).All(foe => foe.caste == MotileCaste.Stalker));
            yield return ReworkShot("112-burrow-entrance");
            Assert.IsFalse(game.Outpost.Rooms.walls.Last().broken, "The burrow starts sealed");
            for (int i = 0; i < 16 && !game.Outpost.Rooms.walls.Last().broken; i++)
            {
                yield return Press(new Controls { fire = true });
                yield return Seconds(0.6f);
            }
            Assert.IsTrue(game.Outpost.Rooms.walls.Last().broken, "Emergency fists must open the entrance");
            float3 entrance = Player.Position;
            input = new Controls { move = Vector2.up };
            yield return Seconds(3);
            input = default;
            Assert.Greater(math.distance(entrance, Player.Position), 10, "The descending entrance is walkable");
            Assert.IsFalse(Player.walker.swimming);
            yield return ReworkShot("113-burrow-interior");
            game.Award(FoeKind.Mind);
            Player.walker.position = game.Outpost.Prize;
            yield return Press(new Controls { interact = true });
            yield return EscapeAndBuild(Campaign.FirstBurrow);
            Assert.AreEqual(3, game.Stage);
            Assert.IsTrue(Player.Has(Gear.Beams)); Assert.IsFalse(Player.Has(Gear.Thrusters));
            while (game.Levelling) yield return Press(new Controls { confirm = true });
            Assert.Greater(Player.energy, 90);
            game.Jump(Campaign.Citadel); Player.flying = false;
            yield return Press(new Controls { select = 5 });
            Assert.IsTrue(Player.Weapon.sweep);
            input.fire = true;
            yield return Until(() => Object.FindObjectsByType<LineRenderer>().Any(line => line.name == "Hemisphere ray" && line.enabled));
            input = default;
            yield return ReworkShot("114-hemisphere-lasers");
        }

        [UnityTest]
        public IEnumerator ReworkFactoryAndOrbitRecoveryArePlayable()
        {
            game.Jump(Campaign.Recovery);
            Stand(Campaign.Site(Solar.Descriptor, Campaign.Stranger), new float3(0, 0, 1));
            var front = game.Frontier.Local;
            front.kills = War.RequiredKills(front);
            front.aliens = 20;
            game.Frontier.BuildFactory(0);
            yield return null;
            Assert.AreEqual(Campaign.Recovery, game.Stage); Assert.IsTrue(front.factory);
            Assert.IsTrue(Player.captive); Assert.IsFalse(Player.Has(Gear.Ship));
            Assert.Greater(math.length(Player.ship.position), Body.radius * Shapes.AirTop(Body));
            Assert.Less(math.length(Player.ship.position), Body.radius * Shapes.AirTop(Body) + 60);
            var cave = Body.burrows[0];
            float3 outside = cave.origin + math.rotate(cave.rotation, new float3(0, 0, -38));
            Player.walker = Walker.Spawn(Body, math.normalize(outside), math.rotate(cave.rotation, new float3(0, 0, -1)));
            float3 up = math.normalize(Player.Position);
            float height = Player.Altitude;
            input.jump = true; yield return Seconds(4); input = default;
            Assert.Greater(Player.Altitude, height + 10);
            Player.walker.position = Player.ship.position - up * 3;
            Player.walker.velocity = float3.zero;
            yield return ReworkShot("115-orbital-recovery");
            yield return Press(new Controls { interact = true });
            Assert.IsTrue(Player.flying); Assert.IsTrue(Player.Has(Gear.Ship)); Assert.AreEqual(Campaign.Tower, game.Stage);
            yield return HoverOver(Moon, 20);
            Player.flying = false; Player.walker = Walker.Spawn(Body, math.normalize(Player.ship.position), new float3(0, 0, 1));
            front = game.Frontier.Local; front.kills = War.RequiredKills(front); front.aliens = 20;
            game.Frontier.BuildFactory(0);
            Assert.IsFalse(front.shipyard);
            front.aliens = 0; front.mindDead = true; game.Frontier.BuildFactory(1);
            yield return Seconds(1);
            Assert.IsTrue(front.shipyard);
            Assert.IsNotNull(GameObject.Find("Robodog shipyard"));
            yield return ReworkShot("116-factory-war");
        }

        [UnityTest]
        public IEnumerator ReworkFactoryLeavesRoomAndMarksTheUnownedShip()
        {
            game.Jump(Campaign.Recovery);
            Stand(Campaign.Site(Solar.Descriptor, Campaign.Stranger), new float3(0, 0, 1));
            var front = game.Frontier.Local; front.kills = War.RequiredKills(front);
            game.Frontier.BuildFactory(0);
            yield return null;
            var labels = GameObject.Find("HUD").GetComponentsInChildren<UnityEngine.UI.Text>();
            Assert.Greater(math.distance(Player.Position, game.Colonies.Single().site), 8, "A new factory must leave room for the player");
            Assert.IsTrue(labels.Any(label => label.text == "UNMANNED SHIP" && label.enabled), "The recovery ship needs a visible marker before ownership");
        }

        [UnityTest]
        public IEnumerator ReworkImpactDriveLaunchesThePoweredSuit()
        {
            game.Jump(3);
            Player.walker = Walker.Spawn(Body, Campaign.Site(Solar.Descriptor, 0), new float3(0, 0, 1));
            yield return null;
            float3 start = Player.Position;
            yield return Press(new Controls { select = 1, fire = true });
            Assert.IsFalse(Player.walker.grounded, "An impact punch must actually leave the ground");
            yield return Seconds(0.25f);
            Assert.Greater(math.distance(start, Player.Position), 0.5f);
            Assert.Less(math.length(Player.Velocity), 14, "Punches cannot stack unbounded speed");
        }

        [UnityTest]
        public IEnumerator ReworkVoiceUsesBoundedStateAndNeverARealMicrophone()
        {
            game.Jump(5);
            game.GetComponent<VoiceInput>().Toggle();
            yield return null;
            Assert.AreEqual(1, asked);
            StringAssert.Contains("System state (JSON", lastPrompt);
            StringAssert.Contains("\"power\"", lastPrompt);
            Assert.Less(lastPrompt.Length, 6500);
            Assert.LessOrEqual(lastPersona.Length, 4000);
            Assert.IsTrue(File.Exists(Path.Combine(Application.persistentDataPath, "companion-state.json")));
            yield return Press(new Controls { menu = true });
            game.Battle.Foes.Clear();
            float3 eye = (float3)Camera.main.transform.position + Solar.Anchor;
            game.Battle.Add(FoeKind.Sentry, eye + (float3)Camera.main.transform.forward * 12);
            game.Scanner.Scan(); yield return null;
            StringAssert.Contains("Stalker", game.Scanner.Subject);
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator ReworkMoonfallConsumesTheSystemAndArrivesElsewhere()
        {
            game.Jump(Campaign.Won);
            uint seed = game.Seed; int depth = game.Depth;
            game.Frontier.Launch();
            game.Fall.began = Solar.Clock - game.Fall.duration;
            yield return Seconds(1);
            Assert.IsTrue(game.Frontier.Collapsing);
            Assert.IsTrue(GameObject.Find("HUD").GetComponent<Canvas>().enabled, "The ending keeps its phase captions visible");
            Assert.IsFalse(Solar.Bodies[game.Fall.moon].gameObject.activeSelf);
            Assert.IsFalse(Solar.Bodies[game.Fall.world].gameObject.activeSelf);
            yield return Seconds(12);
            yield return ReworkShot("117-singularity");
            for (float waited = 0; game.Frontier.Whiteout < 0.95f && waited < 45; waited += Time.unscaledDeltaTime) yield return null;
            Assert.GreaterOrEqual(game.Frontier.Whiteout, 0.95f);
            yield return ReworkShot("118-whiteout");
            for (float t = 0; game.Depth == depth && t < 15; t += Time.unscaledDeltaTime) yield return null;
            Assert.AreEqual(depth + 1, game.Depth); Assert.AreEqual(seed + 1, game.Seed);
            Assert.IsTrue(Solar.Descriptor.generated);
            Assert.IsFalse(game.Frontier.Collapsing);
        }
    }
}
