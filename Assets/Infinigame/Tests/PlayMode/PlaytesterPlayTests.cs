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
        [UnityTest] public IEnumerator FestivalVoiceIsAvailableWithoutSystemVoices()
        {
            PlayerPrefs.SetString($"{Game.Prefs}.voice", "Zarvox");
            yield return Choose(Game.Settings);
            while (game.Selected != 4) yield return Press(new Controls { down = true });
            yield return Press(new Controls { confirm = true });
            Assert.AreEqual("cmu_us_slt_arctic_hts", Speech.Choice);
            StringAssert.StartsWith("FESTIVAL SLT", game.Items[0]);
            StringAssert.Contains("OFFLINE", game.Hint);
            yield return ReworkShot("festival-voice");
        }

        [UnityTest] public IEnumerator ShipyardImmediatelyLaunchesOneFollowingEscort()
        {
            game.Jump(Campaign.Tower);
            var front = game.Frontier.Local;
            front.factory = front.mindDead = true;
            front.aliens = front.ships = 0;
            float3 site = -Campaign.Site(Solar.Descriptor, Campaign.Tower);
            site *= Body.GroundRadius(site);
            game.Colonies.Add(new Colony { world = Moon, site = site });
            game.Escorts.order = EscortOrder.Patrol;
            game.Escorts.count = 3;
            game.Frontier.BuildFactory(1);
            Assert.IsTrue(front.shipyard);
            Assert.AreEqual(1, front.ships);
            Assert.AreEqual(EscortOrder.Follow, game.Escorts.order);
            Assert.AreEqual(1, game.Escorts.count);
            Assert.AreEqual(1, Object.FindObjectsByType<Transform>().Count(part => part.name == "Fleet escort"));
            HoverAt(site + math.normalizesafe(site) * 180);
            Player.hull = 10000;
            yield return Until(() => game.Frontier.Contacts.Any(unit => unit.ship && math.distance(unit.position, Player.Position) < 120));
            Assert.IsTrue(game.Frontier.Contacts.Any(unit => unit.ship && math.distance(unit.position, Player.Position) < 120), $"Ships: {front.ships}, distances: {string.Join(", ", game.Frontier.Contacts.Where(unit => unit.ship).Select(unit => math.distance(unit.position, Player.Position)))}");
        }

        [UnityTest] public IEnumerator PlaytesterCheatCodeTogglesAndConsumesEachCode()
        {
            yield return Press(new Controls { menu = true });
            yield return Press(new Controls { typed = "idgaf" });
            Assert.IsTrue(game.CheatsUnlocked);
            yield return Press(new Controls { typed = "x" });
            Assert.IsTrue(game.CheatsUnlocked);
            yield return Press(new Controls { typed = "idgaf" });
            Assert.IsFalse(game.CheatsUnlocked);
            Assert.IsFalse(game.Items.Contains(Game.Cheats));
        }

        [UnityTest] public IEnumerator PlaytesterCheatGlyphAlsoLocksInvulnerability()
        {
            yield return Press(new Controls { menu = true });
            var glyph = Object.FindObjectsByType<UnityEngine.UI.Text>().Single(text => text.text == "◇");
            Vector2 pointer = RectTransformUtility.WorldToScreenPoint(glyph.canvas.worldCamera, glyph.rectTransform.TransformPoint(glyph.rectTransform.rect.center));
            yield return Press(new Controls { click = true, pointer = pointer });
            Assert.IsTrue(game.CheatsUnlocked); Assert.AreEqual(Game.Cheats, game.Page);
            for (int i = 0; i < 5; i++) yield return Press(new Controls { down = true });
            yield return Press(new Controls { confirm = true });
            Assert.IsTrue(game.GodMode);
            yield return Press(new Controls { click = true, pointer = pointer });
            Assert.IsFalse(game.CheatsUnlocked); Assert.IsFalse(game.GodMode); Assert.IsNull(game.Page);
        }

        [UnityTest] public IEnumerator PlaytesterDeathsPersistentlyReducePressure()
        {
            game.Jump(Campaign.SecondBurrow);
            float before = game.Pressure;
            game.Progression.experience = 10;
            Player.Hurt(10000, "test");
            yield return null;
            Assert.IsTrue(game.Dead); Assert.Less(game.Pressure, before);
            Assert.AreEqual(0, game.Progression.experience, "The death save must restore the checkpoint even if the player quits before retrying");
            float after = game.Pressure;
            yield return Press(new Controls { confirm = true });
            Assert.AreEqual(after, game.Pressure);
            Player.Hurt(10000, "test");
            yield return null;
            Assert.Less(game.Pressure, after);
            StringAssert.Contains("\"deaths\":2", System.IO.File.ReadAllText(System.IO.Path.Combine(Application.persistentDataPath, Game.SaveFile)));
        }

        [UnityTest] public IEnumerator PlaytesterFactoryPatrolsFightVisibleAliens()
        {
            game.Jump(Campaign.ThirdBurrow);
            Stand(Campaign.Site(Solar.Descriptor, Campaign.Stranger), new float3(0, 0, 1));
            Player.health = 10000;
            var front = game.Frontier.Local;
            front.kills = 100; front.mindDead = true; front.aliens = 300;
            game.Frontier.BuildFactory(0);
            foreach (var defence in Object.FindObjectsByType<FactoryDefence>()) defence.enabled = false;
            float3 home = game.Colonies.Single().site;
            float3 direction = math.normalizesafe(home + Player.walker.forward * 24);
            game.Battle.Add(FoeKind.Sentry, direction * (Body.GroundRadius(direction) + 0.9f));
            var foe = game.Battle.Foes[^1]; foe.health = foe.maximumHealth = 1000; foe.patrol = true;
            game.Battle.Foes[^1] = foe;
            bool sawLaser = false;
            LineRenderer laser = null;
            for (float until = Time.time + 9; Time.time < until;)
            {
                if (laser == null) laser = GameObject.Find("Robodog patrol")?.GetComponentInChildren<LineRenderer>();
                sawLaser |= laser != null && laser.enabled;
                yield return null;
            }
            Assert.IsTrue(sawLaser, "The visible patrol, independent of the factory turret, engages nearby aliens");
            Assert.Less(game.Battle.Foes.Single(enemy => enemy.seed == foe.seed).health, 1000);
            if (!Application.isEditor) Debug.Log("Factory encounter frame samples\n" + System.IO.File.ReadAllText(FrameLog.File));
        }

        [UnityTest] public IEnumerator PlaytesterShipyardEscortsFollowAndAcceptOrders()
        {
            game.Jump(Campaign.Tower);
            var front = game.Frontier.Local;
            front.factory = front.shipyard = front.mindDead = true;
            front.aliens = 0; front.ships = 3;
            float3 site = Campaign.Site(Solar.Descriptor, Campaign.Tower);
            site *= Body.GroundRadius(site);
            game.Colonies.Add(new Colony { world = Moon, site = site });
            game.Frontier.Settle();
            HoverAt(site + math.normalizesafe(site) * 180);
            Player.hull = 10000;
            yield return Seconds(9);
            Assert.AreEqual(2, Object.FindObjectsByType<Transform>().Count(part => part.name == "Fleet escort"));
            Assert.IsTrue(game.Frontier.Contacts.Where(unit => unit.ship).Any(unit => math.distance(unit.position, Player.Position) < 120));
            yield return Choose("Fleet orders");
            yield return Press(new Controls { confirm = true });
            Assert.AreEqual(3, game.Escorts.count);
            yield return Press(new Controls { down = true });
            yield return Press(new Controls { confirm = true });
            Assert.AreEqual(EscortOrder.Attack, game.Escorts.order);
            yield return ReworkShot("playtester-fleet-orders");
            yield return Press(new Controls { menu = true });
            yield return Press(new Controls { menu = true });
            yield return HoverOver(Home, 200);
            Assert.AreEqual(2, Object.FindObjectsByType<Transform>().Count(part => part.name == "Fleet escort"), "Existing escorts survive a reference change");
        }

        [UnityTest] public IEnumerator PlaytesterCitadelKeepsItsOwnInstructions()
        {
            game.Jump(Campaign.Citadel);
            if (Solar.Reference != Solar.Station) yield return HoverOver(Solar.Station, 500);
            Player.ship.position = game.Outpost.Place(game.Outpost.Rooms.entrance);
            StringAssert.Contains(game.Mission.task, game.Objective);
            StringAssert.DoesNotContain("DIG THROUGH", game.Objective);
            yield return null;
        }

        [UnityTest] public IEnumerator PlaytesterReturningToLunaKeepsOneSetOfShieldGuards()
        {
            game.Jump(Campaign.Tower);
            if (Solar.Reference != Moon) yield return HoverOver(Moon, 100);
            Player.flying = false;
            Player.walker.position = game.Outpost.Place(game.Outpost.Rooms.entrance);
            game.Outpost.Tick();
            int count = game.Outpost.OuterLeft;
            Assert.Greater(count, 0);
            game.Battle.Rebase(Moon, Home);
            Assert.IsFalse(game.Battle.Foes.Any(foe => foe.outerGuard), "The outpost retains its guards; they must not also follow the player between worlds");
            game.Outpost.Tick();
            Assert.AreEqual(count, game.Outpost.OuterLeft);
        }

        [UnityTest] public IEnumerator PlaytesterLunaMindCanBeKilledAfterItsMarkedDefenders()
        {
            game.Jump(Campaign.Tower);
            if (Solar.Reference != Moon) yield return HoverOver(Moon, 100);
            Player.flying = false; Player.health = 10000;
            float3 entrance = game.Outpost.Place(game.Outpost.Rooms.entrance);
            Stand(math.normalizesafe(entrance), game.Outpost.Place(new float3(0, 0, -80)) - entrance);
            Player.walker.position = entrance;
            game.Outpost.Tick();
            StringAssert.Contains("SHIELD", game.Objective);
            yield return Seconds(2);
            yield return ReworkShot("playtester-luna-shield");
            foreach (var guard in game.Battle.Foes.Where(foe => foe.outerGuard).ToArray())
            {
                var gun = Combat.Arms[1]; gun.damage = 10000;
                game.Battle.FriendlyFire(guard.position + math.normalizesafe(guard.position) * 3, guard.position, gun);
            }
            game.Battle.Tick(default); game.Outpost.Tick();
            Assert.AreEqual(0, game.Outpost.OuterLeft);
            var mind = game.Battle.Foes.Single(foe => foe.kind == FoeKind.Mind);
            Assert.IsFalse(mind.protectedCore);
            Assert.Greater(math.length(mind.position), Body.GroundRadius(math.normalize(mind.position)));
            float3 from = game.Outpost.Place(game.Outpost.Local(mind.position) + new float3(0, 1, -4));
            Assert.IsTrue(game.Outpost.Visible(from, mind.position));
            var beam = Combat.Arms[1]; beam.damage = 10000;
            game.Battle.FriendlyFire(from, mind.position, beam);
            Assert.LessOrEqual(game.Battle.Foes.Single(foe => foe.kind == FoeKind.Mind).health, 0);
            game.Battle.Tick(default);
            Assert.IsTrue(game.Outpost.Fallen);
            yield return null;
        }
    }
}
