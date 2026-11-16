using System.Collections;
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


        [UnityTest]
        public IEnumerator RevisionColonyExplanationFitsBelowTheHeading()
        {
            game.Jump(Campaign.Won);
            Player.flying = false;
            yield return Press(new Controls { ride = true });
            yield return null;
            var labels = GameObject.Find("HUD").GetComponentsInChildren<UnityEngine.UI.Text>();
            var note = labels.Single(text => text.text == game.Hint);
            var subtitle = labels.Single(text => text.text.Contains("DEPTH") && text.text.Contains("JOURNAL"));
            Assert.Less(note.rectTransform.anchoredPosition.y + note.rectTransform.rect.height / 2,
                subtitle.rectTransform.anchoredPosition.y - subtitle.rectTransform.rect.height / 2, "the explanation does not overlap the subtitle");
            Assert.LessOrEqual(note.preferredHeight, note.rectTransform.rect.height, "all of the explanation fits");
            StringAssert.Contains(game.Hermit.Name, note.text);
        }

        [UnityTest]
        public IEnumerator FidelityCastesSpawnAndBeamPersistsWhileHeld()
        {
            game.Jump(2);
            Player.walker.position = game.Outpost.Place(new float3(0, 1, -8));
            yield return Seconds(0.2f);
            Assert.AreEqual(1, game.Battle.Foes.Where(foe => foe.kind == FoeKind.Sentry).Select(foe => foe.caste).Distinct().Count());
            foreach (var foe in game.Battle.Foes)
                Assert.AreEqual(Combat.Health(foe.kind, game.Threat, foe.caste), foe.health);
            game.Jump(4);
            Player.fitted = 1 << (int)Gear.Beams;
            yield return Press(new Controls { select = 2 });
            input = new Controls { fire = true };
            yield return Seconds(0.5f);
            Assert.IsTrue(game.Battle.Beaming, "A held beam remains active beyond its initial shot");
            input = default;
            yield return Seconds(0.2f);
            Assert.IsFalse(game.Battle.Beaming);
        }

        [UnityTest]
        public IEnumerator RevisionAuthoredPatrolKindsAreUsed()
        {
            game.Jump(2);
            Player.walker.position = game.Outpost.Place(new float3(0, 1, -8));
            yield return Seconds(0.2f);
            Assert.AreEqual(game.Mission.count, game.Battle.Foes.Count(foe => foe.kind == FoeKind.Sentry));
            Assert.IsTrue(game.Battle.Foes.All(foe => foe.kind == FoeKind.Sentry || foe.kind == FoeKind.Mind));
        }





        [UnityTest]
        public IEnumerator RevisionCompletedOutpostsRemainWithTheirRooms()
        {
            game.Jump(2);
            yield return null;
            var fort = Object.FindObjectsByType<Transform>().Single(part => part.name == "Mind burrow");
            float3 location = game.Outpost.Position;
            game.Advance();
            yield return null;
            Assert.IsTrue(fort != null && fort.gameObject.activeInHierarchy, "finishing the mission keeps its building");
            Assert.Greater(fort.GetComponentsInChildren<MeshRenderer>().Length, 4);
            Assert.Less(math.distance((float3)fort.localPosition, location), 0.1f);
        }

        [UnityTest]
        public IEnumerator RevisionOnFootViewShowsTheWholeTraveller()
        {
            game.Jump(3);
            yield return null;
            yield return Press(new Controls { toggleView = true });
            float3 eye = Player.Position + math.normalize(Player.Position) * Walker.EyeHeight - Solar.Anchor;
            Assert.Greater(math.distance(eye, Camera.main.transform.position), 3);
            var body = Object.FindObjectsByType<Transform>().Single(part => part.name == "Traveller");
            Assert.IsTrue(body.gameObject.activeInHierarchy);
            Assert.Greater(body.GetComponentsInChildren<MeshRenderer>().Length, 4);
            Assert.IsFalse(Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).Single(part => part.name == "Gauntlets").gameObject.activeInHierarchy);
            yield return Press(new Controls { toggleView = true });
            Assert.Less(math.distance(Player.Position + math.normalize(Player.Position) * Walker.EyeHeight - Solar.Anchor, Camera.main.transform.position), 0.2f);
        }

        [UnityTest]
        public IEnumerator RevisionUnarmouredWaterHurtsButTheSuitProtectsYou()
        {
            game.Jump(Campaign.Stranger);
            Player.kit &= ~(1 << (int)Gear.Suit);
            yield return null;
            var rnd = new Unity.Mathematics.Random(79);
            float3 up = new float3(0, 1, 0);
            for (int i = 0; i < 10000 && Body.GroundRadius(up) > Body.SeaRadius - 5; i++) up = rnd.NextFloat3Direction();
            Assert.Less(Body.GroundRadius(up), Body.SeaRadius - 5);
            Player.walker = Walker.Spawn(Body, up, math.cross(up, new float3(1, 0, 0)));
            Player.walker.position = up * (Body.SeaRadius - 3);
            Player.walker.grounded = false;
            float health = Player.health;
            yield return Seconds(0.5f);
            Assert.Less(Player.health, health);
            StringAssert.Contains("swim", Player.Cause.ToLowerInvariant());
            Player.kit |= 1 << (int)Gear.Suit;
            health = Player.health;
            yield return Seconds(0.5f);
            Assert.AreEqual(health, Player.health, 0.1f);
        }

        [UnityTest]
        public IEnumerator RevisionCopilotIsVisibleAndAnswersTheTalkButton()
        {
            game.Jump(Campaign.Tower + 1);
            yield return Seconds(0.3f);
            var companion = Object.FindObjectsByType<Transform>().Single(part => part.name == "Hermit");
            Assert.AreEqual("Copilot", companion.parent.name);
            Vector3 eye = Camera.main.WorldToViewportPoint(companion.Find("Torso/Neck/Head").position);
            Assert.Greater(eye.z, 0);
            Assert.That(eye.x, Is.InRange(0f, 1f));
            Assert.That(eye.y, Is.InRange(0f, 1f));
            yield return Seconds(2);
            var target = new RenderTexture(1600, 1000, 24);
            Camera.main.targetTexture = target;
            Camera.main.Render();
            RenderTexture.active = target;
            var picture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            picture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            System.IO.Directory.CreateDirectory("Logs/Screenshots");
            System.IO.File.WriteAllBytes("Logs/Screenshots/97-companion-in-cockpit.png", picture.EncodeToPNG());
            Camera.main.targetTexture = null;
            RenderTexture.active = null;
            Object.Destroy(picture);
            Object.Destroy(target);
            yield return Press(new Controls { talk = true });
            Assert.IsTrue(game.Hermit.Talking);
            StringAssert.Contains("NO MODEL", game.Hermit.Source);
        }

        [UnityTest]
        public IEnumerator RevisionBreachesSurviveFinishingAndReloading()
        {
            game.Jump(2);
            yield return null;
            game.Outpost.Breach(game.Outpost.Place(game.Outpost.Rooms.walls[0].centre), 1000);
            Assert.IsNotEmpty(game.Ruins.Single(ruin => ruin.stage == 2).broken);
            game.Advance();
            game.Write();
            yield return UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("Main");
            yield return null;
            Assert.IsNotEmpty(game.Ruins.Single(ruin => ruin.stage == 2).broken);
            Assert.AreEqual(1, Object.FindObjectsByType<Transform>().Count(part => part.name == "Mind burrow"));
        }

        [UnityTest]
        public IEnumerator RevisionCapturesDetailedModels()
        {
            System.IO.Directory.CreateDirectory("Logs/Screenshots");
            var target = new RenderTexture(1600, 1000, 24);
            Camera.main.targetTexture = target;
            IEnumerator Shot(string name, Transform subject, Vector3 from, Vector3 at)
            {
                for (float t = 0; t < 2; t += Time.unscaledDeltaTime) yield return null;
                Camera.main.transform.SetPositionAndRotation(subject.TransformPoint(from), Quaternion.LookRotation(subject.TransformPoint(at) - subject.TransformPoint(from), subject.up));
                Camera.main.Render();
                RenderTexture.active = target;
                var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                System.IO.File.WriteAllBytes($"Logs/Screenshots/{name}.png", image.EncodeToPNG());
                RenderTexture.active = null;
                Object.Destroy(image);
            }
            game.Jump(Campaign.Stranger);
            yield return null;
            var robot = Object.FindObjectsByType<Transform>().Single(part => part.name == "Hermit");
            var light = new GameObject("Model review fill").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.5f;
            light.transform.rotation = robot.rotation * Quaternion.Euler(45, -30, 0);
            yield return Shot("90-foundry-robot", robot, new Vector3(3, 2, 4), new Vector3(0, 0.9f, 0));
            var suit = Models.Traveller(); suit.SetParent(robot.parent, false); suit.localPosition = new Vector3(4, 0, 0);
            yield return Shot("91-powered-armour", suit, new Vector3(0.8f, 1.65f, 3.8f), new Vector3(0, 1.1f, 0));
            var nest = Models.Nest();
            nest.SetParent(robot.parent, false);
            nest.localPosition = new Vector3(-6, 0, -3);
            yield return Shot("92-presence-mind", nest, new Vector3(5, 3, 6), new Vector3(0, 1.6f, 0));
            Object.Destroy(nest.gameObject);
            var display = new GameObject("Presence review").transform;
            display.SetParent(robot.parent, false);
            display.localPosition = new Vector3(-6, 1, -3);
            var hull = Shapes.Hull(new Color(0.5f, 0.43f, 0.35f), 1.2f);
            var glow = Shapes.Hull(Color.black, 1.2f, new Color(2.5f, 0.12f, 0.015f));
            var metrics = new System.Text.StringBuilder();
            foreach (var caste in new[] { MotileCaste.Stalker, MotileCaste.Breacher, MotileCaste.Spitter })
            {
                var (shell, senses, limb) = Models.Sentry(caste);
                int triangles = (shell.triangles.Length + senses.triangles.Length + Models.LimbCount(caste) * limb.triangles.Length) / 3;
                metrics.AppendLine($"{caste}: {triangles:N0} triangles");
                Assert.Greater(triangles, 30000, "Close bodies must retain curved anatomy and shell detail");
                Shapes.Part(display, "Body", shell, hull, Vector3.zero, Vector3.one);
                Shapes.Part(display, "Eyes", senses, glow, Vector3.zero, Vector3.one);
                for (int i = 0; i < Models.LimbCount(caste); i++)
                {
                    var part = Shapes.Part(display, "Leg", limb, hull, Models.LimbRoot(caste, i), Vector3.one);
                    part.transform.localRotation = Quaternion.Euler(0, i < Models.LimbCount(caste) / 2 ? 180 : 0, 0);
                }
                string name = caste == MotileCaste.Stalker ? "94-presence-sentry" : "99-" + caste.ToString().ToLowerInvariant();
                yield return Shot(name, display, new Vector3(3, 1.7f, 4), Vector3.zero);
                foreach (Transform part in display) Object.Destroy(part.gameObject);
                yield return null;
            }
            int Triangles(Transform subject) => subject.GetComponentsInChildren<MeshFilter>(true).Sum(part => part.sharedMesh.triangles.Length / 3);
            metrics.AppendLine($"Foundry robot: {Triangles(robot):N0} triangles");
            metrics.AppendLine($"Powered suit: {Triangles(suit):N0} triangles");
            foreach (var surface in new[] { Surface.Metal, Surface.Ceramic, Surface.Seal, Surface.Chitin, Surface.Rock })
            {
                var map = Textures.Albedo(surface);
                Assert.AreEqual(LodSettings.Tier == 0 ? 1024 : 2048, map.width);
                Assert.Greater(map.mipmapCount, 1);
                Assert.IsFalse(map.isReadable, "Generated CPU copies are released");
                metrics.AppendLine($"{surface}: {map.width} × {map.height}, {map.mipmapCount} mip levels");
            }
            var (body, eyes) = Models.Drone();
            metrics.AppendLine($"Scout: {(body.triangles.Length + eyes.triangles.Length) / 3:N0} triangles");
            Shapes.Part(display, "Body", body, hull, Vector3.zero, Vector3.one);
            Shapes.Part(display, "Eyes", eyes, glow, Vector3.zero, Vector3.one);
            yield return Shot("95-presence-drone", display, new Vector3(2.8f, 1.6f, 3.4f), Vector3.zero);
            foreach (Transform part in display) Object.Destroy(part.gameObject);
            yield return null;
            (body, eyes) = Models.Raider();
            metrics.AppendLine($"Presence vessel: {(body.triangles.Length + eyes.triangles.Length) / 3:N0} triangles");
            System.IO.File.WriteAllText("Logs/Screenshots/model-detail.txt", metrics.ToString());
            Shapes.Part(display, "Body", body, hull, Vector3.zero, Vector3.one);
            Shapes.Part(display, "Eyes", eyes, glow, Vector3.zero, Vector3.one);
            display.localPosition += Vector3.up * 8;
            yield return Shot("96-presence-vessel", display, new Vector3(11, 7, 14), Vector3.zero);
            foreach (Transform part in display) Object.Destroy(part.gameObject);
            yield return null;
            display.localPosition -= Vector3.up * 8;
            var skin = Shapes.Solid(Surface.Chitin, new Color(0.6f, 0.52f, 0.38f), true);
            Shapes.Part(display, "Grazer", Shapes.Grazer(), skin, Vector3.zero, Vector3.one);
            var (birdBody, wing) = Shapes.Bird();
            Shapes.Part(display, "Bird", birdBody, skin, new Vector3(-1.8f, 1.3f, 0), Vector3.one);
            Shapes.Part(display, "Right wing", wing, skin, new Vector3(-1.8f, 1.3f, 0), Vector3.one);
            var otherWing = Shapes.Part(display, "Left wing", wing, skin, new Vector3(-1.8f, 1.3f, 0), Vector3.one);
            otherWing.transform.localRotation = Quaternion.Euler(0, 180, 0);
            Shapes.Part(display, "Fish", Shapes.Fish(), skin, new Vector3(1.1f, 1, 0), Vector3.one * 2);
            yield return Shot("100-wildlife-detail", display, new Vector3(3, 2.4f, 5), new Vector3(-0.5f, 0.8f, 0));
            Object.Destroy(display.gameObject);
            game.Jump(3);
            yield return Press(new Controls { toggleView = true });
            yield return Seconds(2);
            // Use the actual chase camera for the player, with no review-camera offset.
            var camera = Camera.main.transform;
            yield return Shot("93-third-person", camera, Vector3.zero, Vector3.forward);
            game.Jump(Campaign.Tower + 1);
            if (!Player.Seated) yield return Press(new Controls { toggleView = true });
            yield return Seconds(1);
            yield return Shot("97-companion-in-cockpit", Camera.main.transform, Vector3.zero, Vector3.forward);
            game.Jump(Campaign.Won);
            Player.flying = false;
            var front = game.Frontier.Local; front.kills = War.RequiredKills(front);
            game.Frontier.BuildFactory(0);
            yield return Press(new Controls { ride = true });
            yield return Shot("81-colony", Camera.main.transform, Vector3.zero, Vector3.forward);
            yield return Press(new Controls { menu = true });
            int io = System.Array.FindIndex(Solar.Descriptor.bodies, world => world.name == "Io");
            yield return HoverOver(io, 100);
            float3 up = Body.volcanoes[0].direction;
            float height = Body.GroundRadius(up);
            HoverAt(up * (height + 30));
            Solar.Bodies[io].Dig(up * height, 20);
            var crater = new GameObject("Crater review").transform;
            crater.SetParent(Solar.Bodies[io].transform, false);
            crater.localPosition = up * height;
            crater.localRotation = Quaternion.FromToRotation(Vector3.up, up);
            light.transform.rotation = crater.rotation * Quaternion.Euler(35, -30, 0);
            yield return Shot("98-crater-lava", crater, new Vector3(0, 23, 30), new Vector3(0, -3, 0));
            Object.Destroy(crater.gameObject);
            Camera.main.targetTexture = null;
            Object.Destroy(light.gameObject);
            Object.Destroy(target);
        }

        [UnityTest]
        public IEnumerator RevisionRadarDoesNotRevealDistantMoons()
        {
            Solar.Surveyed = true;
            int moon = System.Array.FindIndex(Solar.Descriptor.bodies, body => body.name == "Triton");
            Assert.IsFalse(Solar.Charted(moon), "a colony radar cannot label remote moons across the system");
            yield return HoverOver(moon, 500);
            Assert.IsTrue(Solar.Charted(moon));
        }
    }
}
