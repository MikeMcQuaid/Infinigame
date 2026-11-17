using System.Linq;
using Infinigame.Core;
using NUnit.Framework;
using Unity.Mathematics;

namespace Infinigame.Tests
{
    public class ExpeditionTests
    {
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
        [Test] public void BeamsReachMindsInDryTunnelsBelowSeaLevel()
        {
            var body = new BodyDescriptor { radius = 100, kind = BodyKind.Rock, terrain = new TerrainParams { seaDepth = 1 } };
            float3 from = new float3(0, 83, 0), target = new float3(0, 83, 5);
            body.Tunnel(new float3(0, 80, 0), 15);
            body.burrows.Add(new Burrow { origin = new float3(0, 100, 0), rotation = quaternion.identity });
            var foes = new System.Collections.Generic.List<Foe> { Combat.Spawn(FoeKind.Mind, target, new float3(0, 0, -1), 1, 1) };
            Combat.Ray(foes, body, from, math.normalizesafe(target - from), 8, 200, false, default, null);
            Assert.LessOrEqual(foes[0].health, 0);
        }
        [Test] public void LevelsCarryExperienceAndPurchasesRequireSalvage()
        {
            var progress = new Progression { experience = 140 };
            Assert.IsTrue(progress.Improve(Upgrade.Damage, false));
            Assert.AreEqual(95, progress.experience); Assert.AreEqual(1, progress.level);
            Assert.IsTrue(progress.Improve(Upgrade.Energy, false));
            Assert.IsFalse(progress.Pending);
            Assert.IsFalse(progress.Improve(Upgrade.Toughness, false));
            Assert.IsFalse(progress.Improve(Upgrade.Toughness, true));
            progress.salvage = 15;
            Assert.IsTrue(progress.Improve(Upgrade.Toughness, true)); Assert.AreEqual(0, progress.salvage);
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
            Assert.IsFalse(Campaign.Has(Campaign.Kit(6), Gear.Nova));
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
