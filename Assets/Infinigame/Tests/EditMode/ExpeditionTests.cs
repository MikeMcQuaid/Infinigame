using System.Linq;
using Infinigame.Core;
using NUnit.Framework;
using Unity.Mathematics;

namespace Infinigame.Tests
{
    public class ExpeditionTests
    {
        [Test] public void SuitRecoveryHasDistinctDiagnosticsForLongLoads()
        {
            var reports = Enumerable.Range(0, 1600).Select(Suit.Diagnostic).ToArray();
            StringAssert.Contains("POWER LOSS", reports[0]);
            Assert.AreEqual(reports.Length, reports.Distinct().Count());
            var addresses = reports.Skip(12).Select(line => System.Text.RegularExpressions.Regex.Match(line, @"\[([0-9A-F]{4})\]").Groups[1].Value).ToArray();
            Assert.IsTrue(addresses.All(address => address.Length == 4));
            Assert.AreEqual(addresses.Length, addresses.Distinct().Count());
        }

        [Test] public void KnucklesCanBreakTheVisibleLipOfTheBurrowPlug()
        {
            var rooms = new Interior { walls = new[] { new Wall(new float3(0, 0, 2), new float3(5, 3, 1)) } };
            Assert.IsTrue(rooms.FistContact(new float3(0, 0, 0.1f), new float3(0, 0, 0.72f), out var contact));
            Assert.IsTrue(rooms.Breach(contact, 250));
            Assert.IsFalse(rooms.FistContact(float3.zero, new float3(0, 0, 1), out _));
        }

        [Test] public void EachCitadelTurnHasASectionFieldBeyondItsTurret()
        {
            var rooms = Interior.Citadel();
            for (int i = 2; i < rooms.route.Length - 1; i++)
            {
                int field = System.Array.FindIndex(rooms.walls, wall => wall.section == i);
                Assert.GreaterOrEqual(field, 0);
                rooms.walls[field].broken = false;
                Assert.IsFalse(rooms.Visible(rooms.route[i], rooms.route[i + 1], true));
                Assert.IsTrue(rooms.Visible(rooms.route[i - 1], rooms.route[i], true));
                rooms.walls[field].broken = true;
            }
        }

        [Test] public void PunchNeedsTravelAndRetractionButNoTimer()
        {
            PunchStroke stroke = default;
            bool Step(float z) => stroke.Step(new float3(0, 0, z), float3.zero, 0.012f, 2.2f);
            Assert.IsFalse(Step(0.2f));
            Assert.IsFalse(Step(0.21f));
            Assert.IsTrue(Step(0.34f));
            Assert.IsTrue(Step(0.37f), "Keep sweeping the glove until it connects");
            Assert.IsTrue(Step(0.38f), "The tip can decelerate as it reaches the wall");
            stroke.Contact();
            Assert.IsFalse(Step(0.41f));
            Assert.IsFalse(Step(0.39f));
            Assert.IsFalse(Step(0.23f));
            Assert.IsTrue(Step(0.38f));
            Assert.IsFalse(Step(0.38f));
        }

        [Test] public void EveryDeathEasesPressureWithoutChangingIndividualEnemyHealth()
        {
            Assert.AreEqual(1, War.Pressure(0));
            for (int deaths = 1; deaths < 30; deaths++)
                Assert.That(War.Pressure(deaths), Is.GreaterThan(0).And.LessThan(War.Pressure(deaths - 1)));
            var usual = War.Create(1, 1, 1);
            var eased = War.Create(1, 1, 1);
            War.Step(usual, 60);
            War.Step(eased, 60, War.Pressure(4));
            Assert.Less(eased.aliens, usual.aliens);
            Assert.Less(eased.alienShips, usual.alienShips);
        }
        [Test] public void EscortsCatchUpWithAnAutopilotedLeaderWithoutOvershooting()
        {
            float3 follower = new float3(0, 0, -600), leader = float3.zero;
            float3 velocity = new float3(0, 0, Autopilot.Speed);
            for (int i = 0; i < 300; i++)
            {
                leader += velocity / 30;
                follower = War.Follow(follower, leader, velocity, 1f / 30);
            }
            Assert.Less(math.distance(follower, leader), 20);
            Assert.LessOrEqual(follower.z, leader.z);
            Assert.AreEqual(leader, War.Follow(follower, leader, float3.zero, 10));
        }
        [Test] public void SuitSweepHitsExposedEnemiesInEveryDirectionWithinItsRadius()
        {
            var body = new BodyDescriptor { kind = BodyKind.Station };
            float3 origin = new float3(0, 100, 0);
            var foes = new[] { new float3(5, 0, 0), new float3(0, -5, 0), new float3(0, 0, -5),
                new float3(0, 0, 30), new float3(-5, 0, 0) }.Select(offset =>
                    Combat.Spawn(FoeKind.Sentry, origin + offset, new float3(0, 0, 1), 1, 2)).ToList();
            var hits = new System.Collections.Generic.List<Strike>();
            Combat.Sweep(foes, body, origin, Combat.Arms[4].damage, default, (from, to) => to.x >= 0, hits);
            Assert.AreEqual(3, hits.Count);
            Assert.IsTrue(foes.Take(3).All(foe => foe.health <= 0));
            Assert.IsTrue(foes.Skip(3).All(foe => foe.health == foe.maximumHealth));
        }
        [Test] public void BeamsReachMindsInDryTunnelsBelowSeaLevel()
        {
            var body = new BodyDescriptor { radius = 100, kind = BodyKind.Rock, terrain = new TerrainParams { seaDepth = 1 } };
            float3 from = new float3(0, 83, 0), target = new float3(0, 83, 5);
            body.Tunnel(new float3(0, 80, 0), 15);
            body.burrows = new[] { new Burrow { origin = new float3(0, 100, 0), rotation = quaternion.identity } };
            var foes = new System.Collections.Generic.List<Foe> { Combat.Spawn(FoeKind.Mind, target, new float3(0, 0, -1), 1, 1) };
            Combat.Ray(foes, body, from, math.normalizesafe(target - from), 8, 200, false, default, null);
            Assert.LessOrEqual(foes[0].health, 0);
        }
        [Test] public void AllSuitWeaponsAreAvailableBeforeBoardingTheShip()
        {
            int kit = Campaign.Kit(Campaign.Recovery);
            foreach (var weapon in Combat.Arms)
                Assert.IsTrue(Campaign.Has(kit, weapon.needs), weapon.name);
            Assert.IsFalse(Campaign.Has(kit, Gear.Ship));
        }
        [TestCase(false)] [TestCase(true)] public void FinalMoonAcceleratesFromADistantLaunchToSurfaceContact(bool beyond)
        {
            var system = beyond ? SolarSystemDescriptor.Beyond(2) : SolarSystemDescriptor.Generate(1);
            int moon = Campaign.FinalMoon(system), world = system.bodies[moon].orbit.parent;
            var fall = new Moonfall { moon = moon, world = world, began = 200, active = true };
            double Gap(double time) => math.distance(system.OrbitalPosition(world, time), fall.Position(system, time))
                - system.bodies[world].radius - system.bodies[moon].radius;
            double launch = fall.began + Moonfall.Assembly + Moonfall.Ignition;
            double initial = math.distance(system.OrbitalPosition(moon, fall.began), system.OrbitalPosition(world, fall.began))
                - system.bodies[moon].radius - system.bodies[world].radius;
            Assert.AreEqual(initial, Gap(fall.began), 0.01, "Launch from the actual moon without a jump");
            Assert.AreEqual(Gap(fall.began), Gap(launch), 0.01, "Ignition must not send the moon backwards");
            Assert.Greater(Gap(launch + 0.5) - Gap(launch + 1), Gap(launch) - Gap(launch + 0.5));
            Assert.Greater(Gap(launch + 1) - Gap(launch + 2), initial * 0.1, "Reach cruising speed in the first second");
            Assert.Greater(Gap(launch + 9.9), 0);
            Assert.AreEqual(0, Gap(launch + 10), 0.01);
            Assert.IsTrue(fall.Finished(launch + 10));
        }
        [Test] public void LevelsCarryExperienceAndRequireEarnedXP()
        {
            var progress = new Progression { experience = 140 };
            Assert.AreEqual(1, progress.level);
            Assert.IsTrue(progress.Improve(Upgrade.Damage));
            Assert.AreEqual(95, progress.experience); Assert.AreEqual(2, progress.level);
            Assert.IsTrue(progress.Improve(Upgrade.Energy));
            Assert.IsFalse(progress.Pending);
            Assert.IsFalse(progress.Improve(Upgrade.Toughness));
            progress.experience = progress.Required;
            Assert.IsTrue(progress.Improve(Upgrade.Toughness));
            var checkpoint = progress.Copy(); progress.ranks[0]++;
            Assert.AreEqual(1, checkpoint.ranks[0]);
            Assert.Greater(progress.Damage, 1); Assert.Greater(progress.Energy, 1); Assert.Greater(progress.Toughness, 1);
        }
        [Test] public void NuclearSplashCannotCrossInteriorWalls()
        {
            var body = new BodyDescriptor { radius = 0, kind = BodyKind.Station };
            var first = Combat.Spawn(FoeKind.Mind, new float3(0, 0, 20), new float3(0, 0, -1), 1, 1);
            var second = Combat.Spawn(FoeKind.Mind, new float3(10, 0, 20), new float3(0, 0, -1), 1, 2);
            first.health = first.maximumHealth = 1000;
            var foes = new System.Collections.Generic.List<Foe> { first, second };
            var bolts = new System.Collections.Generic.List<Bolt> { new Bolt { position = float3.zero, velocity = new float3(0, 0, 100), damage = 800, blast = 60, life = 1 } };
            Combat.Step(bolts, foes, body, new float3(0, 200, 0), float3.zero, 1, 0.2f, null, default, (from, to) => to.x < 5);
            Assert.AreEqual(second.health, foes[1].health);
            Assert.AreEqual(840, foes[0].health);
        }
        [Test] public void RegionalMindsGrowWithoutMakingTheTutorialHarder()
        {
            Assert.AreEqual(1, Campaign.Minds(Campaign.FirstBurrow));
            Assert.AreEqual(2, Campaign.Minds(Campaign.SecondBurrow));
            Assert.AreEqual(3, Campaign.Minds(Campaign.ThirdBurrow));
            Assert.AreEqual(3, Campaign.Minds(Campaign.Citadel));
        }
        [Test] public void EarthRewardsSeparateWeaponsSurfaceFlightAndOrbit()
        {
            Assert.IsTrue(Campaign.Has(Campaign.Kit(3), Gear.Beams));
            Assert.IsFalse(Campaign.Has(Campaign.Kit(3), Gear.Thrusters));
            Assert.IsTrue(Campaign.Has(Campaign.Kit(4), Gear.Thrusters));
            Assert.IsFalse(Campaign.Has(Campaign.Kit(4), Gear.Orbiter));
            Assert.IsTrue(Campaign.Has(Campaign.Kit(5), Gear.Orbiter));
            Assert.IsFalse(Campaign.Has(Campaign.Kit(5), Gear.Ship));
            Assert.IsTrue(Campaign.Has(Campaign.Kit(6), Gear.Ship));
            Assert.IsTrue(Campaign.Has(Campaign.Kit(6), Gear.Nova));
            for (int stage = 2; stage <= 4; stage++) Assert.Greater(Campaign.Missions[stage].count, 1);
        }
        [Test] public void LaterSwarmsDoNotBecomeDamageSponges()
        {
            foreach (var kind in new[] { FoeKind.Sentry, FoeKind.Raider, FoeKind.Drone })
            {
                Assert.AreEqual(Combat.Health(kind, 1), Combat.Health(kind, 20));
                Assert.AreEqual(Combat.Damage(kind, 1), Combat.Damage(kind, 20));
            }
        }
        [Test] public void CapturedWeaponsNeverIncludeNuclearTorpedoes()
        {
            for (uint choice = 0; choice < 50; choice++)
                Assert.Less(Combat.CapturedWeapon(Campaign.Kit(Campaign.Won), choice).blast, Combat.Nuclear);
        }
        [Test] public void CitadelWallsCannotBeBreachedByAnyWeapon()
        {
            var rooms = Interior.Citadel();
            Assert.IsFalse(rooms.Breach(rooms.walls[0].centre, 100000));
            Assert.IsFalse(rooms.walls.Any(wall => wall.broken));
        }
        [Test] public void LaterBurrowsOfferFlankingLanesAndThrustObstacles()
        {
            var branch = Interior.Burrow(1);
            float z = 0, floor = Interior.TunnelFloor(z);
            Assert.IsFalse(branch.Visible(new float3(-2.5f, floor + 2, z), new float3(2.5f, floor + 2, z), false));
            Assert.IsTrue(branch.Visible(new float3(-2.5f, floor + 2, z), new float3(-2.5f, floor + 2, z + 3), false));
            var deep = Interior.Burrow(2);
            z = -8; floor = Interior.TunnelFloor(z);
            Assert.IsFalse(deep.Visible(new float3(0, floor + 1, z - 3), new float3(0, floor + 1, z + 3), false));
            Assert.IsTrue(deep.Visible(new float3(0, floor + 4, z - 3), new float3(0, floor + 4, z + 3), false));
        }
        [Test] public void BurrowRearWallClosesTheWholePassage()
        {
            var rooms = Interior.Burrow();
            Assert.IsFalse(rooms.Visible(rooms.core, rooms.core + new float3(0, 0, 30), false));
            Assert.Less(rooms.core.y, -15);
        }
    }
}
