using Infinigame.Core;
using NUnit.Framework;
using Unity.Mathematics;

namespace Infinigame.Tests
{
    public class CasteTests
    {
        [Test]
        public void GroundFeetPlantThroughStanceAndReturnWithoutSnapping()
        {
            var touchdown = GroundStride.Foot(0);
            var stance = GroundStride.Foot(0.3f);
            var lift = GroundStride.Foot(0.6f);
            var swing = GroundStride.Foot(0.8f);
            Assert.AreEqual(0, stance.y);
            Assert.AreEqual((touchdown.x + lift.x) * 0.5f, stance.x, 0.0001f);
            Assert.Greater(touchdown.x, lift.x);
            Assert.Greater(swing.y, 0.9f);
            Assert.Less(math.distance(lift, GroundStride.Foot(0.6001f)), 0.002f);
            Assert.Less(math.distance(touchdown, GroundStride.Foot(0.9999f)), 0.002f);
        }

        [Test]
        public void FirstBurrowContainsOnlyStalkers()
        {
            Assert.AreEqual(MotileCaste.Stalker, Combat.Caste(2, 0));
            Assert.AreEqual(MotileCaste.Stalker, Combat.Caste(2, 1));
            Assert.AreEqual(MotileCaste.Stalker, Combat.Caste(2, 2));
            Assert.AreEqual(MotileCaste.Stalker, Combat.Caste(1, 2));
        }

        [Test]
        public void LaterInteriorPatrolsKeepSpittersInTheMix()
        {
            Assert.AreEqual(MotileCaste.Breacher, Combat.Caste(3, 0));
            Assert.AreEqual(MotileCaste.Spitter, Combat.Caste(3, 1));
            Assert.AreEqual(MotileCaste.Spitter, Combat.Caste(4, 0));
            Assert.AreEqual(MotileCaste.Stalker, Combat.Caste(4, 1));
        }

        [Test]
        public void BreachersTradeSpeedForArmourAndCloseRange()
        {
            var body = new BodyDescriptor { kind = BodyKind.Rock, radius = 1000 };
            var stalker = Combat.Spawn(FoeKind.Sentry, new float3(0, 1000.9f, 0), new float3(0, 0, 1), 1, 43);
            var breacher = Combat.Spawn(FoeKind.Sentry, stalker.position, stalker.forward, 1, 43, caste: MotileCaste.Breacher);
            Assert.Greater(breacher.health, stalker.health * 2);
            for (int i = 0; i < 30; i++)
            {
                Combat.Hunt(ref stalker, body, new float3(0, 1001, 40), float3.zero, 1, 1f / 60, out _);
                Combat.Hunt(ref breacher, body, new float3(0, 1001, 40), float3.zero, 1, 1f / 60, out _);
            }
            Assert.Greater(math.length(stalker.velocity), math.length(breacher.velocity));
            Assert.AreEqual(body.GroundRadius(math.normalize(breacher.position)) + 0.9f, math.length(breacher.position), 0.002f);
        }

        [Test]
        public void SpittersRetreatAndFireBeyondStalkerRange()
        {
            var body = new BodyDescriptor { kind = BodyKind.Rock, radius = 1000 };
            var spitter = Combat.Spawn(FoeKind.Sentry, new float3(0, 1000.9f, 0), new float3(0, 0, 1), 1, 43, caste: MotileCaste.Spitter);
            spitter.cooldown = 0;
            Assert.IsTrue(Combat.Hunt(ref spitter, body, new float3(0, 1001, 55), float3.zero, 1, 1f / 60, out var shot));
            Assert.Greater(shot.damage, Combat.Damage(FoeKind.Sentry, 1));
            Assert.Greater(shot.life * math.length(shot.velocity), 55);
            spitter.velocity = float3.zero;
            Combat.Hunt(ref spitter, body, spitter.position + new float3(0, 0, 18), float3.zero, 1, 1f / 60, out _);
            Assert.Less(spitter.velocity.z, 0, "The mortar body tries to keep its firing distance");
        }
    }
}
