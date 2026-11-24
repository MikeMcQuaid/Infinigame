using System.Collections.Generic;
using Infinigame.Core;
using NUnit.Framework;
using Unity.Mathematics;

namespace Infinigame.Tests
{
    public class FlightRevisionTests
    {
        [Test] public void OldCampaignSavesKeepTheirProgress()
        {
            Assert.AreEqual(10, Campaign.Won);
            Assert.AreEqual(Campaign.Citadel, Campaign.MigrateStage(11, 2));
            Assert.AreEqual(Campaign.Recovery, Campaign.MigrateStage(4, 2));
            Assert.AreEqual(Campaign.Citadel, Campaign.MigrateStage(14));
            Assert.AreEqual(Campaign.Won, Campaign.MigrateStage(15));
            Assert.AreEqual(Campaign.Citadel, Campaign.MigrateStage(11));
        }
        [TestCase(1.6f)]
        [TestCase(20f)]
        public void SurfacePackAcceleratesAlongHeadsetGazeAgainstLocalGravity(float gravity)
        {
            var body = new BodyDescriptor { kind = BodyKind.Rock, radius = 5000, surfaceGravity = gravity };
            var walker = new WalkerState { position = new float3(0, 5020, 0), height = 20, forward = new float3(0, 0, -1), fuel = 1 };
            Walker.Step(ref walker, new WalkerInput { jump = true, gaze = new float3(0, 0, 1) }, body, 0.05f);
            Assert.Greater(walker.velocity.z, 0.5f);
            Assert.AreEqual(0, walker.velocity.y, 0.02f);
            Assert.Less(walker.fuel, 1);
        }
        [Test] public void ContactWithMoltenGroundHurtsUntilItCools()
        {
            var up = new float3(0, 1, 0);
            var body = new BodyDescriptor { radius = 100, kind = BodyKind.Rock,
                blasts = new[] { new Blast { scar = Scar.Lava, crater = new float4(up, 10), pool = 0.5f, level = -3, born = 10 } } };
            float3 ground = up * body.GroundRadius(up);
            Assert.Greater(body.LavaDamage(ground, 0, 11), 0);
            Assert.AreEqual(0, body.LavaDamage(ground + up * 3, 0, 11));
            Assert.AreEqual(0, body.LavaDamage(ground, 0, 86));
        }

        [Test] public void ASubmergedBeamCanReachAnimalsInAFloodedCrater()
        {
            var up = new float3(0, 1, 0);
            var body = new BodyDescriptor { radius = 1000, kind = BodyKind.Rock, terrain = new TerrainParams { seaDepth = 4, plains = 1, seaBias = 1 } };
            Assert.Greater(body.GroundRadius(up), body.SeaRadius);
            body.Dig(up, 20);
            float3 eye = up * (body.SeaRadius - 2);
            Assert.Less(body.GroundRadius(up), math.length(eye));
            var end = Combat.Ray(new List<Foe>(), body, eye, new float3(1, 0, 0), 4, 1, false, default, null);
            Assert.AreEqual(4, math.distance(eye, end), 0.02f);
        }

        [Test] public void SpaceSuitBrakesWithoutInput()
        {
            var body = new BodyDescriptor { kind = BodyKind.Rock, radius = 100, surfaceGravity = 2 };
            var walker = new WalkerState { position = new float3(0, 150, 0), forward = new float3(0, 0, 1), velocity = new float3(0, 0, 20) };
            for (int i = 0; i < 120; i++) Walker.Step(ref walker, new WalkerInput { space = true }, body, 1f / 60);
            Assert.Less(math.length(walker.velocity), 0.01f);
            Assert.AreEqual(150, walker.position.y, 0.01f);
        }
        [Test] public void WeaponAdaptationRewardsSwitchingAndEndsWithTheMind()
        {
            var front = War.Create(3, 1, 0);
            War.Learn(front, 1, 100);
            Assert.Less(War.Resistance(front, 1), 0.8f);
            Assert.Greater(War.Resistance(front, 1), 0.7f);
            War.Step(front, 210); Assert.AreEqual(1, War.Resistance(front, 1));
            Assert.AreEqual(1, War.Resistance(front, 2));
            front.mindDead = true;
            Assert.AreEqual(1, War.Resistance(front, 1));
        }
        [Test] public void RagdollJointsHoldUntilShotAndLandOnTerrain()
        {
            var body = new BodyDescriptor { kind = BodyKind.Rock, radius = 100, surfaceGravity = 9.81f };
            var corpse = new Ragdoll(new[] { new float3(0, 104, 0), new float3(1, 104, 0) }, float3.zero);
            for (int i = 0; i < 90; i++) corpse.Step(body, 1f / 60, 0.2f);
            Assert.AreEqual(1, math.distance(corpse.positions[0], corpse.positions[1]), 0.02f);
            Assert.GreaterOrEqual(math.length(corpse.positions[0]), 100.19f);
            corpse.Strike(corpse.positions[1] - new float3(0, 0, 1), new float3(0, 0, 2), 70);
            Assert.IsFalse(corpse.joined[1]);
        }
        [Test] public void ShieldBlocksBothSidesAndHostileFire()
        {
            var shield = new Shield { active = true, radius = 10 };
            Assert.IsTrue(shield.Blocks(float3.zero, new float3(0, 0, 20), out var hit));
            Assert.AreEqual(10, hit.z, 0.001f);
            var bolts = new List<Bolt> { new Bolt { position = new float3(0, 0, 20), velocity = new float3(0, 0, -100), hostile = true, damage = 50, life = 1 } };
            var strikes = new List<Strike>();
            Assert.AreEqual(0, Combat.Step(bolts, new List<Foe>(), new BodyDescriptor { radius = 0 }, float3.zero, float3.zero, 1, 0.2f, strikes, shield));
            Assert.IsTrue(strikes[0].shield);
        }
        [Test] public void StationAutopilotKeepsClearOfTheHull()
        {
            var station = new BodyDescriptor { kind = BodyKind.Station, radius = 280, soiRadius = 0 };
            Assert.Greater(Autopilot.Standoff(station), 500);
        }
        [Test] public void ShipyardFitsAnExistingFactoryOnce()
        {
            var front = War.Create(3, 1, 0);
            Assert.IsFalse(War.Build(front, true));
            front.factory = true;
            Assert.IsTrue(War.Build(front, true));
            Assert.AreEqual(1, front.ships, "The first escort is ready without waiting for replication");
            Assert.IsFalse(War.Build(front, true));
            Assert.AreEqual(1, front.ships, "Repeated upgrade requests cannot create extra ships");
        }
        [Test] public void FriendlyBodiesIgnoreProjectilesAndSplash()
        {
            var body = new BodyDescriptor { radius = 100, kind = BodyKind.Rock };
            var friend = Combat.Spawn(FoeKind.Sentry, new float3(0, 110, 5), new float3(0, 0, 1), 1, 1);
            friend.friendly = true;
            var foes = new List<Foe> { friend };
            var bolts = new List<Bolt> { new Bolt { position = new float3(0, 110, 0), velocity = new float3(0, 0, 100), life = 1, damage = 50, blast = 20 } };
            Combat.Step(bolts, foes, body, new float3(0, 110, 0), float3.zero, 1, 0.1f, new List<Strike>());
            Assert.AreEqual(friend.health, foes[0].health);
        }
    }
}
