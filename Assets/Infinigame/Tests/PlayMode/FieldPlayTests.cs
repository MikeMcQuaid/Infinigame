using System.Collections;
using System.Diagnostics;
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
        IEnumerator EscapeAndBuild(int stage)
        {
            Assert.AreEqual(stage, game.Stage);
            Assert.IsTrue(game.Outpost.Recovered);
            Assert.Greater(game.Outpost.Demolition, 0);
            if (stage == Campaign.FirstBurrow)
            {
                Assert.IsFalse(Player.Has(Gear.Beams));
                Assert.IsFalse(game.Levelling);
            }
            float3 up = math.normalize(game.Outpost.Exit);
            float3 outward = math.normalizesafe(Walker.Project(game.Outpost.Exit - game.Outpost.Core, up));
            Stand(math.normalize(game.Outpost.Exit + outward * 12), outward);
            Solar.RestoreClock(Solar.Clock + 21);
            game.Outpost.Tick();
            Assert.IsTrue(game.Outpost.SiteCleared);
            Assert.AreEqual(stage, game.Stage, "The next region requires a local factory");
            Assert.IsTrue(Player.Has(Gear.Beams));
            if (stage == Campaign.FirstBurrow)
                for (int i = 0; i < 10; i++) game.Award(FoeKind.Sentry);
            yield return null; yield return null;
            while (game.Levelling) yield return Press(new Controls { confirm = true });
            if (stage == Campaign.FirstBurrow) Assert.AreEqual(1, game.Progression.level);
            Assert.IsTrue(game.Frontier.CanBuild(0), game.Frontier.FactoryHint(0));
            game.Frontier.BuildFactory(0);
            game.Outpost.Tick();
            Assert.AreEqual(stage + 1, game.Stage);
        }

        [UnityTest] public IEnumerator ReloadPreservesAnArmedCore()
        {
            game.Jump(Campaign.FirstBurrow);
            yield return null;
            game.Outpost.Killed(FoeKind.Mind);
            Player.walker.position = game.Outpost.Prize;
            yield return Press(new Controls { interact = true });
            Assert.IsTrue(game.Outpost.Recovered);
            yield return Choose("Quit to title");
            yield return Press(new Controls { confirm = true });
            yield return EscapeAndBuild(Campaign.FirstBurrow);
        }

        [UnityTest] public IEnumerator ProfilePopulatedBurrowWhileCuttingGrass()
        {
            game.Jump(Campaign.ThirdBurrow);
            float3 up = Campaign.Site(Solar.Descriptor, Campaign.Stranger);
            Stand(up, World(up));
            // Keep the same observation window even when the entire swarm reaches the camera.
            typeof(Game).GetProperty(nameof(Game.GodMode)).SetValue(game, true);
            var right = math.cross(up, Player.walker.forward);
            for (int i = 0; i < 36; i++)
            {
                float3 direction = math.normalize(Player.Position + Player.walker.forward * (13 + i / 6 * 5) + right * (i % 6 - 2.5f) * 4);
                game.Battle.Add(FoeKind.Sentry, direction * (Body.GroundRadius(direction) + 1), (MotileCaste)(i % 3));
                if (i < 18)
                {
                    var dead = game.Battle.Foes[^1]; dead.health = 0; game.Battle.Foes[^1] = dead;
                }
            }
            yield return Press(new Controls { select = 2, look = new Vector2(0, -24) });
            yield return Seconds(5);
            input.fire = true;
            yield return Seconds(8);
            input = default;
            Assert.GreaterOrEqual(game.Corpses.Count, 16);
            Assert.IsFalse(game.Dead);
            if (!Application.isEditor) UnityEngine.Debug.Log("Occupied burrow frame samples\n" + System.IO.File.ReadAllText(FrameLog.File));
        }

        [UnityTest] public IEnumerator MissedShotsLeaveSettledSwarmRemainsAsleep()
        {
            game.Jump(Campaign.ThirdBurrow);
            yield return null;
            var mesh = Shapes.Sphere(1, 12);
            var material = Shapes.Material("Props", Color.white);
            float3 at = Player.Position;
            for (int i = 0; i < 18; i++)
                game.Corpses.Add(mesh, material, at + Player.walker.forward * (8 + i), Quaternion.identity, float3.zero, 0.25f);
            var late = (System.Action)System.Delegate.CreateDelegate(typeof(System.Action), game.Corpses, "LateUpdate");
            // Age the bodies through normal simulation before firing well away from them.
            for (int i = 0; i < 15 / math.max(0.001f, math.min(Time.deltaTime, 0.05f)); i++) late();
            var samples = new double[12];
            for (int i = 0; i < samples.Length; i++)
            {
                var watch = Stopwatch.StartNew();
                game.Corpses.Strike(at + new float3(100, 100, 100), float3.zero, 30, 2);
                late();
                samples[i] = watch.Elapsed.TotalMilliseconds;
            }
            UnityEngine.Debug.Log($"Settled swarm, 18 corpses with missed impacts: median {samples.OrderBy(t => t).ElementAt(6):F3} ms");
            Assert.Zero(game.Corpses.ActiveCount, "A missed impact must leave resting bodies asleep");
            Object.Destroy(mesh); Object.Destroy(material);
        }
    }
}
