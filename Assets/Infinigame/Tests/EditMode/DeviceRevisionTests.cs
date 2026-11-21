using Infinigame.Core;
using NUnit.Framework;
using Unity.Mathematics;
using System.Collections.Generic;

namespace Infinigame.Tests
{
    public class DeviceRevisionTests
    {
        [Test] public void ASpineLauncherTurnsToItsShotWhileItsBaseStaysGrounded()
        {
            var body = World.Moon;
            float3 up = World.Land(body), along = World.Tangent(up);
            var foe = Combat.Spawn(FoeKind.Sentry, up * (body.GroundRadius(up) + 1), -along, 1, 17);
            foe.stationary = true;
            foe.forward = -along;
            float3 target = foe.position + 45 * along + 25 * up;
            float3 basePosition = foe.position;
            Assert.IsFalse(Combat.Hunt(ref foe, body, target, float3.zero, 1, World.Dt, out _));
            Assert.Greater(math.dot(foe.forward, -along), 0.99f, "The rack must first turn towards the target");
            bool fired = false;
            for (int i = 0; i < 240 && !fired; i++)
                if (Combat.Hunt(ref foe, body, target, float3.zero, 1, World.Dt, out var shot))
                {
                    Assert.Greater(math.dot(foe.forward, math.normalize(shot.velocity)), 0.98f);
                    Assert.Less(math.distance(foe.position, basePosition), 0.05f);
                    Assert.Less(math.distance(shot.position, foe.position + foe.forward * 1.55f), 0.02f);
                    fired = true;
                }
            Assert.IsTrue(fired);
        }

        [TestCase(1, 0)] [TestCase(0, 1)] [TestCase(-1, 0)] [TestCase(0, -1)]
        public void EitherStickStepsAMenuOnceBeforeRepeating(int left, int right)
        {
            var nav = new StickNavigation();
            Assert.AreEqual(left + right, nav.Read(left, right, 0));
            Assert.AreEqual(0, nav.Read(left, right, 0.1f));
            Assert.AreEqual(left + right, nav.Read(left, right, 0.41f));
            Assert.AreEqual(0, nav.Read(0, 0, 0.42f));
            Assert.AreEqual(-left - right, nav.Read(-right, -left, 0.43f));
        }
        [TestCase(0, 1)] [TestCase(4, 3)] [TestCase(8, 5)]
        public void SilencingTheMindLetsAnEmptyFactoryRecoverAndWin(int rank, int depth)
        {
            var front = War.Create(3, depth, rank);
            front.factory = true; front.dogs = 0; front.mindDead = true;
            War.Step(front, 1800);
            Assert.That(front.aliens, Is.GreaterThan(0).And.LessThan(front.dogs / 4));
            Assert.Greater(front.dogs, 0);
        }
        [TestCase(2, 0)] [TestCase(6, 2)] [TestCase(7, 4)] [TestCase(8, 6)] [TestCase(Campaign.Citadel, 0)]
        public void LaterBurrowsGainGroundAndAirDefenders(int stage, int count) => Assert.AreEqual(count, Campaign.OuterDefences(stage));

        [Test] public void AFactoryCannotConquerAnActiveMind()
        {
            var front = War.Create(3, 1, 0);
            front.factory = true; front.dogs = 8;
            War.Step(front, 300);
            Assert.Greater(front.aliens, 0);
        }
        [Test] public void SuitMissilesReloadMoreSlowlyAndShipMissilesArePaired()
        {
            Assert.GreaterOrEqual(Combat.Arms[2].period, 3);
            Assert.AreEqual(2, Combat.Guns[1].pellets);
            Assert.Greater(Combat.Guns[3].damage, 500);
        }
        [Test] public void StaticTurretsStayGroundedAfterTheMindFalls()
        {
            var body = SolarSystemDescriptor.Generate(1).bodies[SolarSystemDescriptor.Home];
            var up = new float3(0, 1, 0);
            float3 at = up * (body.GroundRadius(up) + 1);
            var foe = Combat.Spawn(FoeKind.Sentry, at, new float3(0, 0, 1), 1, 1);
            foe.stationary = foe.scattered = true;
            Combat.Hunt(ref foe, body, at + new float3(0, 0, 8), float3.zero, 1, 1, out _);
            Assert.Less(math.distance(at, foe.position), 0.01f);
        }
        [Test] public void TouchAimAssistOnlySelectsNearbyLivingEnemiesNearTheCrosshair()
        {
            var foes = new List<Foe>
            {
                new Foe { position = new float3(0, 0, 40), health = 100, friendly = true },
                new Foe { position = new float3(0, 0, 40), health = 0 },
                new Foe { position = new float3(6, 0, 100), health = 100 },
                new Foe { position = new float3(20, 0, 100), health = 100 },
            };
            Assert.AreEqual(2, Combat.AssistedTarget(foes, float3.zero, new float3(0, 0, 1), 250));
            Assert.AreEqual(-1, Combat.AssistedTarget(foes, float3.zero, new float3(0, 0, 1), 50));
            foes.RemoveAt(2);
            Assert.AreEqual(-1, Combat.AssistedTarget(foes, float3.zero, new float3(0, 0, 1), 250));
        }
        [Test] public void SuitMissilesIgnoreGroundHeatTargets()
        {
            var body = SolarSystemDescriptor.Generate(1).bodies[SolarSystemDescriptor.Home];
            float3 from = new float3(0, body.radius + body.terrain.MaxHeight + 500, 0);
            var foes = new List<Foe> { Combat.Spawn(FoeKind.Sentry, from + new float3(20, 0, 100), new float3(0, 0, -1), 1, 1) };
            var bolts = new List<Bolt> { new Bolt { position = from, velocity = new float3(0, 0, 240), airOnly = true, homing = 3, life = 9 } };
            Combat.Step(bolts, foes, body, from, float3.zero, 1, 0.05f, new List<Strike>());
            Assert.AreEqual(0, bolts[0].velocity.x, 0.001f);
        }
    }
}
