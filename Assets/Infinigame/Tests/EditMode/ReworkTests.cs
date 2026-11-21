using Infinigame.Core;
using NUnit.Framework;
using Unity.Mathematics;

namespace Infinigame.Tests
{
    public class ReworkTests
    {
        [Test] public void LaterBurrowsMakeTheTravellerTurnInside()
        {
            for (int variant = 1; variant <= 2; variant++)
            {
                var rooms = Interior.Burrow(variant);
                Assert.GreaterOrEqual(rooms.route.Length, 5);
                Assert.IsFalse(rooms.Visible(rooms.entrance, rooms.core, false));
                for (int i = 1; i < rooms.route.Length; i++)
                    Assert.IsTrue(rooms.Visible(rooms.route[i - 1], rooms.route[i], false), $"Walkable turn {i}");
            }
        }

        [Test] public void EarthHasFrequentStormsAndConsumptionEndsTenSecondsAfterImpact()
        {
            var rnd = new Unity.Mathematics.Random(17);
            int wet = 0;
            for (int i = 0; i < 1500; i++)
                if (Weather.Rain(World.Planet, rnd.NextFloat3Direction(), i * 13) > 0.1f) wet++;
            Assert.That(wet, Is.InRange(190, 225));
            Assert.LessOrEqual(Moonfall.Consumption, 10);
        }

        [Test] public void BuriedTravellerAlreadyOwnsTheSuitButNeedsPower()
        {
            Assert.IsTrue(Campaign.Has(Campaign.Kit(0), Gear.Suit));
            Assert.IsFalse(Campaign.Has(Campaign.Kit(2), Gear.Thrusters));
            foreach (var gear in new[] { Gear.Beams, Gear.Capacitor })
                Assert.IsTrue(Campaign.Has(Campaign.Kit(3), gear));
            Assert.AreEqual(MotileCaste.Stalker, Combat.Caste(2, 3));
        }
        [Test] public void PoweredSuitCanUseEveryRecoveredWeapon()
        {
            int owned = Campaign.Kit(3);
            int fitted = Suit.Default(owned);
            foreach (var gear in Suit.Modules)
                if (Campaign.Has(owned, gear)) Assert.IsTrue(Campaign.Has(fitted, gear));
        }
        [Test] public void AliensRecoverWithoutAFactoryAndLaterWorldsGrowFaster()
        {
            var early = War.Create(3, 1, 0); var late = War.Create(6, 3, 6);
            float before = early.aliens, other = late.aliens;
            War.Step(early, 60); War.Step(late, 60);
            Assert.Greater(early.aliens, before);
            Assert.Greater(late.aliens / other, early.aliens / before);
            early.aliens = 1; War.Kill(early, false, 100);
            Assert.Greater(early.aliens, 0, "A player cannot eradicate a world without replicators");
            early.factory = true; War.Kill(early, false, 100);
            Assert.Greater(early.aliens, 0, "The replicators must finish the war");
        }
        [Test] public void AControlledRegionKeepsAHuntingPackAndAReproducingBrood()
        {
            var front = War.Create(3, 1, 0);
            front.kills = War.RequiredKills(front);
            Assert.IsTrue(War.Build(front, false));
            front.mindDead = true;
            float initial = front.dogs;
            for (int i = 0; i < 600; i++) War.Step(front, 1);
            Assert.That(front.aliens, Is.GreaterThan(0).And.LessThan(front.dogs / 4));
            Assert.Greater(front.dogs, initial);
            float ended = front.dogs;
            War.Step(front, 600);
            Assert.AreEqual(ended, front.dogs, 0.1f, "The patrol reaches a sustainable population");
            Assert.IsFalse(War.CanSpread(0, 2));
            Assert.IsTrue(War.CanSpread(0, 1));
        }
        [Test] public void NeighbouringShipsCanFinishAnEnemyFleet()
        {
            var front = War.Create(3, 1, 0);
            front.ships = 20; front.alienShips = 3;
            War.Step(front, 20);
            Assert.AreEqual(0, front.alienShips);
            Assert.IsFalse(front.shipyard, "Support does not create another factory");
        }
        [Test] public void StalkersCloseIntoEmergencyFistRange()
        {
            var body = new BodyDescriptor { kind = BodyKind.Rock, radius = 1000 };
            var foe = Combat.Spawn(FoeKind.Sentry, new float3(0, 1000.9f, 0), new float3(0, 0, 1), 1, 43);
            var player = new float3(0, 1001, 20);
            bool attacked = false;
            for (int i = 0; i < 300; i++)
                if (Combat.Hunt(ref foe, body, player, float3.zero, 1, 1f / 60, out var hit))
                {
                    attacked = true;
                    Assert.Less(math.distance(foe.position, player), 4);
                    Assert.Less(hit.life, 0.1f);
                }
            Assert.IsTrue(attacked);
        }
        [Test] public void SealedBurrowsStayDryBelowSeaLevel()
        {
            var body = SolarSystemDescriptor.Generate(1).bodies[SolarSystemDescriptor.Home];
            var origin = new float3(0, body.SeaRadius - 2, 0);
            body.burrows = new[] { new Burrow { origin = origin, rotation = quaternion.identity } };
            Assert.IsTrue(body.Dry(origin + new float3(0, -10, 30)));
            Assert.IsFalse(body.Dry(origin + new float3(20, -10, 30)));
            var walker = Walker.Spawn(body, new float3(0, 1, 0), new float3(0, 0, 1));
            walker.position = origin + new float3(0, -10, 30);
            Walker.Step(ref walker, default, body, 0.01f);
            Assert.IsFalse(walker.swimming);
        }
        [Test] public void SwarmDirectionsCoverTheUpperHemisphere()
        {
            float3 sum = 0;
            for (int i = 0; i < 32; i++)
            {
                var direction = Combat.SweepDirection(i, 32, new float3(0, 1, 0));
                Assert.GreaterOrEqual(direction.y, 0);
                Assert.AreEqual(1, math.length(direction), 0.001f);
                sum += direction;
            }
            Assert.Less(math.length(sum.xz), 2);
        }
    }
}
