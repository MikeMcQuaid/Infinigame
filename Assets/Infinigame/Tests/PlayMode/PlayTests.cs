using System.Collections;
using System.IO;
using Infinigame.Core;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Infinigame.Tests
{
    // Drives the real scene with scripted controls. Any error logged while a test runs fails it.
    public class PlayTests
    {
        Game game;
        Controls input;
        int quality;

        Player Player => game.Player;
        SolarSystem Solar => game.System;
        BodyDescriptor Body => Solar.ReferenceBody;
        float Altitude => math.length(Player.Position) - math.max(Body.GroundRadius(math.normalize(Player.Position)), Body.SeaRadius);

        [UnitySetUp]
        public IEnumerator LoadGame()
        {
            quality = QualitySettings.GetQualityLevel();
            Game.SaveFile = "test-save.json";
            File.Delete(Path.Combine(Application.persistentDataPath, Game.SaveFile));
            yield return SceneManager.LoadSceneAsync("Main");
            game = Object.FindAnyObjectByType<Game>();
            input = default;
            Player.controls = () => input;
            yield return null;
        }

        [TearDown]
        public void Restore()
        {
            QualitySettings.SetQualityLevel(quality);
            Game.SaveFile = "save.json";
        }

        static IEnumerator Seconds(float seconds)
        {
            for (float t = 0; t < seconds; t += Time.deltaTime) yield return null;
        }

        // Holds the given controls for one frame, as a key press would.
        IEnumerator Press(Controls pressed)
        {
            var held = input;
            input = pressed;
            yield return null;
            input = held;
            yield return null;
        }

        // Puts the player in a hovering ship at a point given relative to the reference body.
        void HoverAt(float3 position)
        {
            Player.flying = true;
            Player.ship.landed = false;
            Player.ship.position = position;
            Player.ship.velocity = float3.zero;
        }

        IEnumerator HoverOverMoon(float altitude)
        {
            var system = Solar.Descriptor;
            float3 toMoon = system.ToLocal(Solar.Reference, Solar.Clock, system.PositionAt(2, Solar.Clock));
            HoverAt(toMoon + math.normalize(-toMoon) * (system.bodies[2].radius + altitude));
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator StandsOnThePlanetWhileTerrainStreamsIn()
        {
            yield return Seconds(3f);
            Assert.AreEqual(1, Solar.Reference);
            Assert.IsFalse(Player.flying);
            Assert.IsTrue(Player.walker.grounded);
            Assert.AreEqual(0f, Altitude, 0.05f);
            Assert.Greater(Solar.Bodies[1].PatchCount, 20);
            Assert.Less(Vector3.Distance(Camera.main.transform.position, Player.walker.position), 3f);
        }

        [UnityTest]
        public IEnumerator WalksForward()
        {
            yield return Seconds(0.5f);
            float3 start = Player.walker.position;
            input.move = new Vector2(0f, 1f);
            yield return Seconds(3f);
            Assert.That(math.distance(start, Player.walker.position), Is.InRange(9f, 17f));
        }

        [UnityTest]
        public IEnumerator BoardsFliesLandsAndLeaves()
        {
            Player.walker = Walker.Spawn(Body, Player.ship.position + math.rotate(Player.ship.rotation, new float3(3f, 0, 0)), Player.walker.forward);
            yield return Press(new Controls { interact = true });
            Assert.IsTrue(Player.flying, "F next to the ship boards it");

            input.lift = 1f;
            yield return Seconds(2f);
            input = default;
            Assert.IsFalse(Player.ship.landed);
            Assert.Greater(Altitude, 10f);

            yield return Press(new Controls { interact = true });
            Assert.IsTrue(Player.landing, "F in flight starts the landing assist");
            for (float t = 0; t < 30f && !Player.ship.landed; t += Time.deltaTime) yield return null;
            Assert.IsTrue(Player.ship.landed);

            yield return Press(new Controls { interact = true });
            Assert.IsFalse(Player.flying, "F on the ground leaves the ship");
            yield return Seconds(1f);
            Assert.IsTrue(Player.walker.grounded);
            Assert.Less(math.distance(Player.walker.position, Player.ship.position), 8f);
        }

        [UnityTest]
        public IEnumerator ReferenceSwitchesToTheMoonWithoutAJump()
        {
            yield return HoverOverMoon(60f);
            Assert.AreEqual(2, Solar.Reference);
            Assert.AreEqual(Solar.Descriptor.bodies[2].radius + 60f, math.length(Player.ship.position), 5f);
            Assert.Less(Solar.Bodies[2].transform.position.magnitude, 1e-3f, "the reference body sits at the origin");
            yield return Seconds(2f);
            Assert.AreEqual(2, Solar.Reference);
            Assert.Greater(Solar.Bodies[2].PatchCount, 6);
        }

        [UnityTest]
        public IEnumerator RegeneratingRebuildsTerrainGravityAndRelics()
        {
            uint seed = game.Seed;
            var before = Solar.Bodies[1];
            yield return Press(new Controls { regenerate = true });
            Assert.AreEqual(seed + 1, game.Seed);
            Assert.IsTrue(before == null, "the old planet is destroyed");
            Assert.AreEqual(SolarSystemDescriptor.Generate(seed + 1).bodies[1].surfaceGravity, Body.surfaceGravity);
            Assert.AreEqual(game.RelicTotal, game.Relics.Count);
            yield return Seconds(2f);
            Assert.IsTrue(Player.walker.grounded);
            Assert.Greater(Solar.Bodies[1].PatchCount, 20);
        }

        [UnityTest]
        public IEnumerator CollectingEveryRelicChargesTheWarp()
        {
            Assert.AreEqual(7, game.Relics.Count);
            Assert.IsFalse(game.CanWarp);
            while (game.Relics.Count > 0)
            {
                int left = game.Relics.Count;
                var site = game.Relics[0].site;
                if (site.body != Solar.Reference) yield return HoverOverMoon(60f);
                HoverAt(site.position);
                yield return null;
                yield return null;
                Assert.AreEqual(left - 1, game.Relics.Count);
            }

            HoverAt(math.normalize(Player.ship.position) * (Body.radius + 500f));
            yield return null;
            Assert.IsTrue(game.CanWarp);
            uint seed = game.Seed;
            yield return Press(new Controls { warp = true });
            Assert.AreEqual(seed + 1, game.Seed);
            Assert.AreEqual(1, game.SystemsVisited);
            Assert.AreEqual(7, game.Relics.Count);
            Assert.IsTrue(Player.flying);
            Assert.Greater(Altitude, 300f);
        }

        [UnityTest]
        public IEnumerator DetailTiersCanBeSwitchedWhilePlaying()
        {
            yield return Seconds(2f);
            int high = Solar.Bodies[1].PatchCount;
            yield return Press(new Controls { tierDown = true });
            yield return Press(new Controls { tierDown = true });
            Assert.AreEqual(0, QualitySettings.GetQualityLevel());
            yield return Seconds(3f);
            Assert.That(Solar.Bodies[1].PatchCount, Is.InRange(6, high - 1));
        }

        // Not an assertion: saves pictures to Logs/Screenshots for a look at what the game draws.
        [UnityTest]
        public IEnumerator CapturesScreenshots()
        {
            Directory.CreateDirectory("Logs/Screenshots");
            IEnumerator Shot(string name)
            {
                yield return Seconds(2.5f);
                ScreenCapture.CaptureScreenshot($"Logs/Screenshots/{name}.png");
                yield return null;
                yield return null;
            }

            yield return Shot("1-on-foot");

            float3 up = math.normalize(Player.walker.position);
            HoverAt(Player.walker.position + up * 60f);
            yield return Press(new Controls { toggleView = true });
            yield return Shot("2-ship-over-land");

            Player.ship.rotation = quaternion.LookRotationSafe(-up, Player.walker.forward);
            HoverAt(up * (Body.radius * 3f));
            yield return Shot("3-planet-from-space");

            yield return HoverOverMoon(40f);
            yield return Shot("4-over-the-moon");

            input.systemView = true;
            yield return Shot("5-system-view");
        }
    }
}
