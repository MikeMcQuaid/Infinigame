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
        [UnityTest] public IEnumerator FirstBurrowKeepsConstructionForAfterTheDemolition()
        {
            game.Jump(Campaign.FirstBurrow);
            Assert.IsFalse(game.FactoryUnlocked);
            StringAssert.DoesNotContain("factor", game.Hermit.Briefing.ToLowerInvariant());
            StringAssert.DoesNotContain("factor", game.Wording("optics").ToLowerInvariant());
            StringAssert.DoesNotContain("factor", game.Wording("arm").ToLowerInvariant());
            yield return Press(new Controls { systemView = true });
            yield return ClickText("SUMMARY");
            var foothold = game.GetComponent<Hud>().ScreenPanel.GetComponentsInChildren<UnityEngine.UI.Text>().Single(t => t.text.StartsWith("FOOTHOLD"));
            StringAssert.DoesNotContain("FACTOR", foothold.text);
        }

        [UnityTest] public IEnumerator ExpeditionTabsOpenTheMainMenu()
        {
            yield return Press(new Controls { systemView = true });
            yield return ClickText("SUMMARY");
            yield return ClickText("MAIN MENU");
            Assert.IsTrue(game.Paused);
            Assert.IsFalse(Player.SystemView);
            CollectionAssert.Contains(game.Items, Game.Settings);
        }

        [UnityTest] public IEnumerator RegionalFactoryFinishesSecuringTheGroundWithWristOpen()
        {
            game.Jump(Campaign.ThirdBurrow);
            for (int i = 0; i < 3; i++)
            {
                var front = game.Frontier.State(Home, i);
                front.factory = front.mindDead = true;
                front.aliens = 14; front.dogs = 12;
            }
            var wrist = game.gameObject.AddComponent<ForearmConsole>(); wrist.game = game; wrist.Init();
            var control = new Controls(); wrist.Activate(0, ref control);
            var shipyard = GameObject.Find("Forearm console").transform.Find("MAP");
            Assert.IsFalse(shipyard.gameObject.activeSelf);
            yield return Until(() => game.Frontier.BuildLabel == "Upgrade to shipyard", 5);
            wrist.Refresh();
            Assert.IsFalse(shipyard.gameObject.activeSelf);
            Assert.AreEqual("UPGRADE TO SHIPYARD", shipyard.parent.Find("BUILD").GetComponentInChildren<UnityEngine.UI.Text>().text);
            Assert.AreEqual(1, Time.timeScale);
            Object.Destroy(wrist);
        }

        [UnityTest] public IEnumerator BurrowBroodsSpawnClearOfTheFinishedWalls()
        {
            foreach (int stage in new[] { Campaign.FirstBurrow, Campaign.SecondBurrow, Campaign.ThirdBurrow })
            {
                game.Jump(stage); game.Outpost.Tick();
                foreach (var foe in game.Battle.Foes.Where(foe => foe.kind == FoeKind.Mind || foe.kind == FoeKind.Sentry))
                {
                    float3 point = game.Outpost.Local(foe.position);
                    foreach (var wall in game.Outpost.Rooms.walls.Where(wall => !wall.broken))
                        Assert.IsFalse(math.all(math.abs(point - wall.centre) < wall.half + (foe.size - 0.06f)), $"Stage {stage} {foe.kind} at {point}, wall at {wall.centre}");
                }
            }
            var roots = GameObject.FindObjectsByType<MeshRenderer>().Where(r => r.name == "Wall root").ToArray();
            Assert.Greater(roots.Length, 0);
            Assert.IsTrue(roots.All(r => r.sharedMaterial == game.Battle.MindTissue));
            yield return null;
        }

        [UnityTest] public IEnumerator RobotVoiceAndSubtitlesAlwaysProvideOneOutput()
        {
            yield return Choose(Game.Settings);
            Assert.IsFalse(game.Items.Any(item => item.Contains("AUTOMATIC QUALITY")));
            yield return Pick("ROBOT VOICE");
            Assert.AreEqual(Game.Settings, game.Page);
            Assert.IsTrue(Speech.Muted && game.Subtitles);
            yield return Pick("ROBOT SUBTITLES");
            Assert.IsFalse(Speech.Muted || game.Subtitles);
            Assert.AreEqual("cmu_us_slt_arctic_hts", Speech.Choice);
            yield return ReworkShot("robot-accessibility");

            Assert.IsTrue(Oracle.UseKey(1, "sk-unit-test-placeholder"));
            yield return Pick(Game.Writers);
            yield return Pick("Sign out");
            Assert.IsFalse(Oracle.Enabled);
            CollectionAssert.DoesNotContain(game.Items, "Sign out");
        }

        [UnityTest] public IEnumerator FlightControlsAndFinaleKeepThePilotInTheSeat()
        {
            game.Jump(Campaign.Tower);
            Player.flying = true;
            var flight = game.GetComponent<DeviceControls>().ReadControllers(new DeviceControls.Controller { axis = new Vector2(0.6f, 1) }, new DeviceControls.Controller { axis = new Vector2(-0.4f, 0) });
            Assert.AreEqual(1, flight.move.y);
            Assert.AreEqual(0, flight.lift);
            Assert.AreEqual(-0.4f, flight.roll, 0.001f);
            var boost = game.GetComponent<DeviceControls>().ReadControllers(new DeviceControls.Controller { trigger = true }, default);
            Assert.IsTrue(boost.sprint);
            var cruise = game.GetComponent<DeviceControls>().ReadControllers(new DeviceControls.Controller { axis = Vector2.up, click = true, grip = true }, default);
            Assert.IsFalse(cruise.sprint);
            Assert.AreEqual(0, boost.lift);
            Assert.Greater(flight.look.x, 0);
            game.PreviewFinale(); yield return null; yield return null;
            Assert.IsTrue(Player.Seated);
            Assert.IsTrue(Player.Cockpit.gameObject.activeInHierarchy);
            var plate = (RectTransform)game.GetComponent<Hud>().ScreenPanel.Find("Emergency broadcast");
            Assert.GreaterOrEqual(-plate.anchoredPosition.y, game.GetComponent<Hud>().ScreenPanel.rect.height * 0.2f, "Mount the phase below the overhead warning lights");
        }

        [UnityTest] public IEnumerator EscortsAndConstructionRespectRegionalProgress()
        {
            game.Jump(Campaign.SecondBurrow);
            game.Frontier.Local.factory = true; game.Frontier.Local.dogs = 3;
            Assert.IsFalse(game.Frontier.CanCallDogs);
            yield return Press(new Controls { ride = true });
            Assert.IsFalse(game.Items.Any(item => item.Contains("shipyard") || item.Contains("moon engines")));
        }

        [UnityTest] public IEnumerator FactoriesRebuildTheWalkerAndShipAtTheAssemblyBay()
        {
            game.Jump(Campaign.Tower);
            var front = game.Frontier.Local;
            front.factory = front.shipyard = true;
            float3 at = Campaign.Site(Solar.Descriptor, Campaign.Tower);
            at *= Body.GroundRadius(at);
            game.Colonies.Add(new Colony { world = Solar.Reference, region = front.region, site = at });
            Player.flying = false;
            Player.Hurt(10000, "Lost suit"); yield return null;
            yield return Press(new Controls { confirm = true });
            Assert.IsFalse(game.Buried || Player.flying);
            Assert.Less(math.distance(Player.Position, Models.Kennel(at)), 4, "The suit is rebuilt in the dog workshop");
            Assert.Greater(math.distance(Player.Position, at), 8, "Leave the launch berth clear");
            Player.flying = true;
            Player.Hurt(10000, "Lost ship"); yield return null;
            yield return Press(new Controls { confirm = true });
            Assert.IsTrue(Player.flying && Player.ship.landed);
            Assert.Less(math.distance(Player.ship.position, at), 5);
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
            Stand(math.normalizesafe(site), World(math.normalizesafe(site)));
            game.Frontier.BuildFactory(0);
            Assert.IsTrue(front.shipyard);
            Assert.AreEqual(1, front.ships);
            Assert.AreEqual(EscortOrder.Follow, game.Escorts.order);
            Assert.AreEqual(1, Object.FindObjectsByType<Transform>().Count(part => part.name == "Fleet escort"));
            var pilot = GameObject.Find("Fleet escort").transform.Find("Robot pilot").GetComponent<MachineMotion>();
            Assert.IsTrue(pilot.quadruped && pilot.seated);
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
            yield return Pick("INVULNERABLE");
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
            Assert.IsFalse(game.Buried);
            Assert.AreEqual(Campaign.SecondBurrow, game.Stage);
            Assert.AreEqual(after, game.Pressure);
            Player.Hurt(10000, "test");
            yield return null;
            Assert.Less(game.Pressure, after);
            StringAssert.Contains("\"deaths\":2", System.IO.File.ReadAllText(System.IO.Path.Combine(Application.persistentDataPath, Game.SaveFile)));
        }

        [UnityTest] public IEnumerator PlaytesterFactoryPatrolsFightVisibleAliens()
        {
            game.Jump(Campaign.ThirdBurrow);
            float3 site = Campaign.Site(Solar.Descriptor, Campaign.ThirdBurrow);
            Stand(math.normalize(site * Body.radius + World(site) * 90), World(site));
            Player.health = 10000;
            var front = game.Frontier.Local;
            front.kills = 100; front.mindDead = true; front.aliens = 300;
            game.Frontier.BuildFactory(0);
            foreach (var defence in Object.FindObjectsByType<FactoryDefence>()) defence.enabled = false;
            float3 home = game.Colonies.Single(colony => colony.world == front.world && colony.region == front.region).site;
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
            float3 site = -Campaign.Site(Solar.Descriptor, Campaign.Tower);
            site *= Body.GroundRadius(site);
            game.Colonies.Add(new Colony { world = Moon, site = site });
            game.Frontier.Settle();
            HoverAt(site + math.normalizesafe(site) * 180);
            Player.hull = 10000;
            yield return Seconds(9);
            Assert.AreEqual(1, Object.FindObjectsByType<Transform>().Count(part => part.name == "Fleet escort"));
            yield return Until(() => game.Frontier.Contacts.Any(unit => unit.ship && math.distance(unit.position, Player.Position) < 120));
            Assert.IsTrue(game.Frontier.Contacts.Any(unit => unit.ship && math.distance(unit.position, Player.Position) < 120));
            yield return Choose("Expedition map");
            Assert.IsTrue(Player.SystemView);
            yield return ClickText("SUMMARY");
            Assert.IsTrue(game.MapSummary, "A click on Summary opens the expedition table");
            yield return ClickText("FLEET ORDERS  ›");
            Assert.AreEqual("Fleet orders", game.Page);
            yield return Press(new Controls { confirm = true });
            Assert.AreEqual(EscortOrder.Attack, game.Escorts.order);
            yield return ReworkShot("playtester-fleet-orders");
            yield return Press(new Controls { menu = true });
            yield return Press(new Controls { menu = true });
            yield return HoverOver(Home, 200);
            Assert.AreEqual(1, Object.FindObjectsByType<Transform>().Count(part => part.name == "Fleet escort"), "Existing escorts survive a reference change");
        }

        [UnityTest] public IEnumerator PlaytesterCitadelKeepsItsOwnInstructions()
        {
            game.Jump(Campaign.Citadel);
            if (Solar.Reference != Solar.Station) yield return HoverOver(Solar.Station, 500);
            Player.ship.position = game.Outpost.Place(game.Outpost.Rooms.entrance);
            StringAssert.Contains(game.Mission.task, game.Objective);
            StringAssert.DoesNotContain("DIG THROUGH", game.Objective);
            yield return null;
            var rooms = game.Outpost.Rooms;
            int gate = System.Array.FindIndex(rooms.walls, wall => wall.section == 2);
            var turn = game.Outpost.Place(rooms.route[2]);
            Assert.IsFalse(rooms.walls[gate].broken);
            for (int i = 0; i < game.Battle.Foes.Count; i++)
            {
                var foe = game.Battle.Foes[i];
                if ((!foe.stationary && foe.kind != FoeKind.Mind) || math.distance(foe.position, turn) >= 35) continue;
                foe.health = 0; game.Battle.Foes[i] = foe;
            }
            game.Outpost.Tick();
            Assert.IsTrue(rooms.walls[gate].broken);
            Assert.IsFalse(rooms.walls[gate + 1].broken, "Each chamber owns its field");
            Player.ship.position = game.Outpost.Core;
            Assert.Less(math.distance(game.Outpost.Waypoint, game.Outpost.Place(rooms.route[3])), 35, "Guide the pilot to the defender holding the first closed section");
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
            StringAssert.Contains("LANDING ZONE", game.Objective);
            var field = game.Outpost.Shield;
            var outside = field.centre + math.normalizesafe(field.centre) * (field.radius + 20);
            Assert.IsTrue(game.Outpost.Conceals(outside, game.Outpost.Core));
            Assert.IsFalse(game.Outpost.Conceals(game.Outpost.Core, game.Outpost.Core));
            yield return Seconds(2);
            yield return ReworkShot("playtester-luna-shield");
            foreach (var guard in game.Battle.Foes.Where(foe => foe.outerGuard && foe.stationary).ToArray())
            {
                var gun = Combat.Arms[1]; gun.damage = 10000;
                game.Battle.FriendlyFire(guard.position + math.normalizesafe(guard.position) * 3, guard.position, gun);
            }
            game.Battle.Tick(default); game.Outpost.Tick();
            Assert.AreEqual(0, game.Outpost.OuterLeft);
            Assert.IsFalse(game.Outpost.LandingLocked);
            Player.flying = true;
            StringAssert.Contains("LAND OUTSIDE", game.Outpost.Instruction);
            Player.flying = false;
            Assert.IsTrue(game.Battle.Foes.Any(foe => foe.outerGuard && !foe.stationary && foe.health > 0));
            Assert.IsTrue(game.Outpost.Shield.active, "The burrow still repels ships");
            var mind = game.Battle.Foes.Single(foe => foe.kind == FoeKind.Mind);
            Assert.IsFalse(mind.protectedCore);
            Assert.Greater(math.length(mind.position), Body.GroundRadius(math.normalize(mind.position)));
            float3 from = game.Outpost.Place(game.Outpost.Local(mind.position) + new float3(0, 1, -4));
            Assert.IsTrue(game.Outpost.Visible(from, mind.position));
            var beam = Combat.Arms[1]; beam.damage = 10000;
            game.Battle.FriendlyFire(from, mind.position, beam);
            Assert.AreEqual(mind.health, game.Battle.Foes.Single(foe => foe.kind == FoeKind.Mind).health, "Robodogs leave the mind for the player");
            Combat.Ray(game.Battle.Foes, Body, from, mind.position - from, 20, beam.damage, false, field, null);
            Assert.LessOrEqual(game.Battle.Foes.Single(foe => foe.kind == FoeKind.Mind).health, 0);
            game.Battle.Tick(default);
            Assert.IsTrue(game.Outpost.Fallen);
            yield return null;
        }
    }
}
