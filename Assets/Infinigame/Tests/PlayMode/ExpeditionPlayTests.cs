using System.Linq;
using System.Collections;
using Infinigame.Core;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

namespace Infinigame.Tests
{
    public partial class PlayTests
    {
        [UnityTest] public IEnumerator ExpeditionRepairWaitsForTheBriefing()
        {
            game.Jump(Campaign.Stranger);
            yield return Seconds(0.5f);
            Assert.IsFalse(game.Tutorial.Repairing, "The repair begins after the spoken instruction, not during it");
            Assert.IsFalse(GameObject.Find("Suit repair beam").GetComponent<LineRenderer>().enabled);
            yield return new WaitForSeconds(13);
            Assert.IsTrue(game.Tutorial.Repairing);
            yield return ReworkShot("expedition-repair");
        }
        [UnityTest] public IEnumerator ExpeditionLevelRequiresAChoiceAndPauses()
        {
            game.Jump(Campaign.SecondBurrow);
            game.Progression.experience = game.Progression.Required;
            yield return null; yield return null;
            Assert.IsTrue(game.Levelling);
            Assert.AreEqual(0, Time.timeScale);
            Assert.AreEqual(6, game.Items.Length);
            yield return new WaitForSecondsRealtime(0.3f);
            yield return ReworkShot("expedition-level");
            yield return Press(new Controls { menu = true });
            Assert.IsTrue(game.Levelling);
            yield return Press(new Controls { confirm = true });
            yield return Press(new Controls { confirm = true });
            Assert.IsTrue(game.Levelling, "Activating the same attribute twice must not spend XP");
            Assert.AreEqual(0, game.Progression.level);
            yield return Press(new Controls { up = true });
            yield return Press(new Controls { confirm = true });
            Assert.IsFalse(game.Paused);
            Assert.AreEqual(1, game.Progression.level);
        }
        [UnityTest] public IEnumerator ExpeditionZoomRevealsTheLocalPlanetAfterShipRecovery()
        {
            yield return Press(new Controls { systemView = true });
            Assert.IsFalse(Player.PlanetView);
            for (int i = 0; i < 4; i++) yield return Press(new Controls { cycle = 1 });
            Assert.IsTrue(Player.PlanetView, "Zooming in reveals local units and regional minds");
        }
        [UnityTest] public IEnumerator ExpeditionLaterRegionsHaveLinkedMinds()
        {
            game.Jump(Campaign.SecondBurrow); yield return null;
            Assert.AreEqual(2, game.Outpost.Left);
            Assert.AreEqual(2, game.Battle.Foes.Count(foe => foe.kind == FoeKind.Mind));
            game.Outpost.Killed(FoeKind.Mind);
            Assert.IsFalse(game.Frontier.State(Home, 1).mindDead);
            game.Outpost.Killed(FoeKind.Mind);
            Assert.IsTrue(game.Frontier.State(Home, 1).mindDead);
            game.Jump(Campaign.ThirdBurrow); yield return null;
            Assert.AreEqual(3, game.Outpost.Left);
            Assert.AreEqual(3, game.Battle.Foes.Count(foe => foe.kind == FoeKind.Mind));
        }
        [UnityTest] public IEnumerator ExpeditionDefeatsBelongToTheEnemiesRegion()
        {
            game.Jump(Campaign.ThirdBurrow);
            game.Battle.Foes.Clear();
            Player.walker.position = Campaign.Site(Solar.Descriptor, Campaign.SecondBurrow);
            var first = game.Frontier.State(Home, 0);
            var second = game.Frontier.State(Home, 1);
            first.aliens = second.aliens = 40;
            game.Battle.Add(FoeKind.Sentry, Campaign.Site(Solar.Descriptor, Campaign.FirstBurrow));
            var foe = game.Battle.Foes[0]; foe.health = 0; game.Battle.Foes[0] = foe;
            game.Battle.Tick(default);
            Assert.AreEqual(39, first.aliens);
            Assert.AreEqual(40, second.aliens);
            yield return null;
        }
        [UnityTest] public IEnumerator ExpeditionRegionsRequireSeparateFactories()
        {
            game.Jump(Campaign.ThirdBurrow);
            for (int region = 0; region < 3; region++)
            {
                var front = game.Frontier.State(Home, region);
                front.factory = front.mindDead = true; front.aliens = 0;
            }
            Assert.IsTrue(game.Frontier.GroundControlled(Home));
            float3 normal = math.normalize(Player.Position + Player.walker.forward * 75);
            game.Battle.Add(FoeKind.Sentry, normal * (Body.GroundRadius(normal) + Combat.SentryHeight));
            var patrol = game.Battle.Foes.Last();
            patrol.patrol = true; game.Battle.Foes[^1] = patrol;
            yield return null; yield return null;
            Assert.IsTrue(game.Battle.Foes.Any(foe => foe.seed == patrol.seed && foe.patrol), "Patrols can still fight the pack in controlled regions");
            game.Frontier.State(Home, 1).factory = false;
            Assert.IsFalse(game.Frontier.GroundControlled(Home));
            yield return Press(new Controls { systemView = true });
            yield return Press(new Controls { cycle = 1 });
            yield return new WaitForSecondsRealtime(0.5f);
            yield return ReworkShot("expedition-regions");
        }
        [UnityTest] public IEnumerator ExpeditionFactoriesRequireTheSurfaceAndEveryFleetCleared()
        {
            game.Jump(Campaign.ThirdBurrow);
            Player.walker.position = game.Outpost.Core;
            var front = game.Frontier.Local;
            front.kills = 100;
            Assert.IsFalse(game.Frontier.CanBuild(0), "Factories cannot grow inside the mind tunnel");
            game.Frontier.BuildFactory(0);
            Assert.IsFalse(front.factory);
            game.Jump(Campaign.Won);
            if (Solar.Reference != Home) yield return HoverOver(Home, 100);
            Stand(Campaign.Site(Solar.Descriptor, 1), new Unity.Mathematics.float3(0, 0, 1));
            for (int region = 0; region < 3; region++)
            {
                front = game.Frontier.State(Home, region);
                front.factory = front.shipyard = front.mindDead = true;
                front.aliens = front.alienShips = 0;
            }
            int other = (game.Frontier.Local.region + 1) % 3;
            game.Frontier.State(Home, other).alienShips = 5;
            Assert.IsFalse(game.Frontier.CanBuild(2), "Moon engines require every local fleet to be cleared");
            yield return null;
        }
        [UnityTest] public IEnumerator ExpeditionRewardsAndRecallFollowThreeBurrows()
        {
            game.Jump(Campaign.FirstBurrow);
            for (int stage = Campaign.FirstBurrow; stage <= Campaign.ThirdBurrow; stage++)
            {
                game.Battle.Foes.Clear();
                while (game.Outpost.Left > 0) game.Outpost.Killed(FoeKind.Mind);
                Player.walker.position = game.Outpost.Prize;
                yield return Press(new Controls { interact = true });
                yield return EscapeAndBuild(stage);
                Assert.AreEqual(stage + 1, game.Stage);
                while (game.Levelling) yield return ConfirmLevel();
                Assert.IsTrue(Player.Has(Gear.Beams));
                Assert.AreEqual(stage >= Campaign.SecondBurrow, Player.Has(Gear.Thrusters));
                Assert.AreEqual(stage >= Campaign.ThirdBurrow, Player.Has(Gear.Orbiter));
            }
            Assert.IsFalse(Player.CanRecall);
            StringAssert.Contains("ATMOSPHERE", Player.Travel());
            Player.walker.position = Player.ship.position - Unity.Mathematics.math.normalizesafe(Player.ship.position) * 3;
            yield return null;
            Assert.IsTrue(Player.CanRecall);
            yield return Press(new Controls { interact = true });
            Assert.IsTrue(Player.Has(Gear.Ship)); Assert.AreEqual(Campaign.Tower, game.Stage);
        }
        [UnityTest] public IEnumerator ExpeditionSurfaceThrustersCannotReachOrbit()
        {
            game.Jump(Campaign.ThirdBurrow);
            var up = Unity.Mathematics.math.normalizesafe(Player.Position);
            Player.walker.position = up * (Body.GroundRadius(up) + 200);
            Player.walker.grounded = false;
            yield return Press(new Controls { jump = true });
            Assert.LessOrEqual(Player.Altitude, Suit.SurfaceCeiling + 0.1f);
            game.Jump(Campaign.Recovery);
            up = Unity.Mathematics.math.normalizesafe(Player.Position);
            Player.walker.position = up * (Body.GroundRadius(up) + 200);
            Player.walker.grounded = false;
            yield return Press(new Controls { jump = true });
            Assert.Greater(Player.Altitude, 150);
        }
        [UnityTest] public IEnumerator ExpeditionDeathReturnsToCurrentBurrow()
        {
            game.Jump(Campaign.SecondBurrow);
            game.Progression.experience = 0;
            Player.Hurt(10000, "test");
            yield return null;
            Assert.IsTrue(game.Dead);
            yield return Press(new Controls { confirm = true });
            Assert.IsFalse(game.Dead); Assert.IsFalse(Player.flying);
            Assert.AreEqual(Campaign.SecondBurrow, game.Stage);
            Assert.Less(Unity.Mathematics.math.distance(Player.Position, game.Outpost.Place(game.Outpost.Rooms.entrance)), 45);
        }
        [UnityTest] public IEnumerator ExpeditionRepeatedRobotAssaultTriggersDefence()
        {
            game.Jump(Campaign.SecondBurrow);
            Stand(Campaign.Site(Solar.Descriptor, 1), new Unity.Mathematics.float3(0, 0, 1));
            game.Hermit.Rescue();
            for (int i = 0; i < 4; i++) game.Hermit.Punched();
            Assert.IsFalse(game.Hermit.Provoked);
            game.Hermit.Punched(); Assert.IsTrue(game.Hermit.Provoked);
            yield return Seconds(2);
            Assert.IsTrue(game.Dead);
        }
        [UnityTest] public IEnumerator ExpeditionFinaleStartsAndRestartsFromOneAction()
        {
            game.PreviewFinale();
            yield return null;
            Assert.IsTrue(game.Fall.active);
            Assert.AreEqual(3, GameObject.FindObjectsByType<Transform>().Count(part => part.name == "Moon engine"));
            yield return Seconds(0.3f);
            yield return ReworkShot("expedition-moon-engines");
            game.Fall.destroyed = true;
            game.PreviewFinale();
            yield return null;
            Assert.IsTrue(game.Fall.active); Assert.IsFalse(game.Fall.destroyed);
        }
    }
}
