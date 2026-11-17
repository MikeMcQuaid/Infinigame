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
            Assert.AreEqual(13, Campaign.Won);
            Assert.AreEqual(Campaign.Citadel, Campaign.MigrateStage(11, 2));
            Assert.AreEqual(Campaign.Recovery, Campaign.MigrateStage(4, 2));
            Assert.AreEqual(Campaign.Citadel, Campaign.MigrateStage(14));
            Assert.AreEqual(Campaign.Won, Campaign.MigrateStage(15));
            Assert.AreEqual(10, Campaign.MigrateStage(11));
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
        [Test] public void ShipFactoryWaitsForGroundVictory()
        {
            var front = War.Create(3, 1, 0); front.factory = true;
            Assert.IsFalse(War.Build(front, true));
            front.aliens = 0;
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
