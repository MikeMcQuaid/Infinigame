using Infinigame.Core;
using NUnit.Framework;
using Unity.Mathematics;

namespace Infinigame.Tests
{
    public class CasteTests
    {
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
