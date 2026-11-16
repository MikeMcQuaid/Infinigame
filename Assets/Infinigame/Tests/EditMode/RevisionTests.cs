using System;
using System.Linq;
using Infinigame.Core;
using NUnit.Framework;
using Unity.Mathematics;

namespace Infinigame.Tests
{
    public class RevisionTests
    {
        [Test]
        public void EarlyObjectivesStayWithinTheCurrentTravelBudget()
        {
            for (uint seed = 1; seed <= 24; seed++)
            {
                var system = SolarSystemDescriptor.Generate(seed);
                var home = system.bodies[SolarSystemDescriptor.Home];
                for (int stage = 1; stage <= 4; stage++)
                {
                    float3 from = Campaign.Site(system, stage - 1), to = Campaign.Site(system, stage);
                    float distance = math.distance(from * home.GroundRadius(from), to * home.GroundRadius(to));
                    Assert.LessOrEqual(distance + (stage >= 2 ? 36 : 0), stage <= 2 ? 300 : 800, $"seed {seed}, mission {stage}");
                }
            }
        }

        [Test]
        public void MissionSitesSurviveTerrainChanges()
        {
            var system = SolarSystemDescriptor.Generate(7);
            float3 camp = Campaign.Site(system, 1), target = Campaign.Site(system, 2);
            var body = system.bodies[SolarSystemDescriptor.Home];
            body.Dig(target, 60);
            body.Dig(camp, 60);
            Assert.AreEqual(target, Campaign.Site(system, 2));
        }

        [Test]
        public void FactoriesReplicateOnlyWhileEnemiesRemain()
        {
            var front = War.Create(3, 1, 0);
            front.kills = War.RequiredKills(front);
            Assert.IsTrue(War.Build(front, false));
            front.aliens = 20; // The player has tipped the balance.
            War.Step(front, 30);
            Assert.Greater(front.dogs, 4);
            front.aliens = 0;
            float before = front.dogs;
            War.Step(front, 240);
            Assert.AreEqual(before, front.dogs);
        }

        [Test]
        public void RadarGivesWorkersMoreTimeBetweenRaids()
        {
            var exposed = new Colony();
            var watched = new Colony { buildings = 1 << (int)Building.Radar };
            exposed.Step(181, 1);
            watched.Step(181, 1);
            Assert.AreEqual(0, exposed.robots);
            Assert.AreEqual(1, watched.robots);
            watched.Step(100, 1);
            Assert.AreEqual(0, watched.robots);
        }

        [Test]
        public void ALevelledFoundationCanBeExcavated()
        {
            var body = new BodyDescriptor { kind = BodyKind.Rock, radius = 1000 };
            float3 up = new float3(0, 1, 0);
            body.Level(up, 15);
            float before = body.GroundRadius(up);
            body.Dig(up, 5);
            Assert.Less(body.GroundRadius(up), before - 1);
        }

        [Test]
        public void SentriesKeepTheirFloorHeightWhenScatteringInsideAStation()
        {
            var body = new BodyDescriptor { kind = BodyKind.Station };
            var foe = Combat.Spawn(FoeKind.Sentry, new float3(0, -29.1f, -150), new float3(0, 0, 1), 1, 43);
            foe.scattered = true;
            for (int i = 0; i < 180; i++) Combat.Hunt(ref foe, body, new float3(20, 10, -160), float3.zero, 1, 1f / 60, out _);
            Assert.AreEqual(-29.1f, foe.position.y, 0.01f);
        }

        static BodyDescriptor Sea() => new BodyDescriptor
        {
            kind = BodyKind.Rock, radius = 1000, surfaceGravity = 10,
            terrain = new TerrainParams { seaDepth = 20, seaBias = -10 },
        };

        [Test]
        public void AWalkerSpawnsOnTheSeabedAndCanWalkThere()
        {
            var body = Sea();
            var walker = Walker.Spawn(body, new float3(0, 1, 0), new float3(0, 0, 1));
            Assert.AreEqual(body.GroundRadius(new float3(0, 1, 0)), math.length(walker.position), 0.01f);
            for (int i = 0; i < 180; i++) Walker.Step(ref walker, new WalkerInput { bare = true, move = new float2(0, 1) }, body, 1f / 60);
            Assert.IsTrue(walker.grounded);
            Assert.Less(math.length(walker.position), body.SeaRadius - 15);
            Assert.Greater(walker.position.z, 8);
        }

        [Test]
        public void WaterDoesNotHoldAnUnpoweredWalkerAtItsSurface()
        {
            var body = Sea();
            var walker = Walker.Spawn(body, new float3(0, 1, 0), new float3(0, 0, 1));
            walker.position = new float3(0, body.SeaRadius - 1, 0);
            walker.grounded = false;
            for (int i = 0; i < 180; i++) Walker.Step(ref walker, new WalkerInput { bare = true }, body, 1f / 60);
            Assert.Less(math.length(walker.position), body.SeaRadius - 10);
        }

        [Test]
        public void ThrustersCanLiftTheSuitFromSeabedThroughTheSurface()
        {
            var body = Sea();
            var walker = Walker.Spawn(body, new float3(0, 1, 0), new float3(0, 0, 1));
            for (int i = 0; i < 300; i++)
            {
                walker.fuel = Walker.JetpackFuel;
                Walker.Step(ref walker, new WalkerInput { jump = true }, body, 1f / 60);
            }
            Assert.Greater(math.length(walker.position), body.SeaRadius + 3);
        }

        [Test]
        public void SentriesStayOnGroundEvenWhenScatteringFromAnAirbornePlayer()
        {
            var body = Sea();
            var foe = Combat.Spawn(FoeKind.Sentry, new float3(0, 981, 0), new float3(0, 0, 1), 1, 43);
            for (int i = 0; i < 120; i++) Combat.Hunt(ref foe, body, new float3(0, 1030, 5), float3.zero, 1, 1f / 60, out _);
            Assert.AreEqual(body.GroundRadius(math.normalize(foe.position)) + 0.9f, math.length(foe.position), 0.05f);
            foe.scattered = true;
            for (int i = 0; i < 120; i++) Combat.Hunt(ref foe, body, new float3(0, 950, 5), float3.zero, 1, 1f / 60, out _);
            Assert.AreEqual(body.GroundRadius(math.normalize(foe.position)) + 0.9f, math.length(foe.position), 0.05f);
        }

        [Test]
        public void FlyingBodiesKeepTheirIdentityAndTurnSmoothly()
        {
            var foe = Combat.Spawn(FoeKind.Drone, new float3(0, 1002, 0), new float3(0, 0, 1), 1, 43);
            var body = new BodyDescriptor { kind = BodyKind.Rock, radius = 1000 };
            Combat.Hunt(ref foe, body, new float3(20, 1002, 0), float3.zero, 1, 1f / 60, out _);
            Assert.AreEqual(43u, foe.seed, "changing the visual noise seed each frame makes damaged bodies flicker");
            Assert.Less(math.acos(math.clamp(foe.forward.z, -1, 1)), 0.1f, "turn rate");
        }

        [Test]
        public void OuterWallsCanBeDestroyedToMakeAnEntrance()
        {
            var rooms = Interior.Outpost();
            float3 outside = new float3(-12, 2, -2), inside = new float3(0, 2, -2);
            Assert.IsFalse(rooms.Visible(outside, inside, false));
            Assert.IsFalse(rooms.Breach(new float3(-8.5f, 2, -2), 60));
            for (int i = 0; i < 12; i++) rooms.Breach(new float3(-8.5f, 2, -2), 60);
            Assert.IsTrue(rooms.Visible(outside, inside, false));
            Assert.Greater(rooms.Move(outside, new float3(8, 0, 0), 0.9f, 1, false, out _).x, -7);
        }

        [Test]
        public void MoltenRockCollectsInsideItsCraterBelowTheRim()
        {
            var body = SolarSystemDescriptor.Generate(1).bodies.First(world => world.volcanoes.Length > 0);
            float3 up = body.volcanoes[0].direction;
            body.Dig(up, 20);
            var blast = body.blasts[0];
            Assert.AreEqual(Scar.Lava, blast.scar);
            Assert.That(blast.pool, Is.InRange(0.2f, 0.8f));
            Assert.Greater(blast.level, -10000);
            Assert.Less(blast.level + body.radius, body.GroundRadius(up, true));
            float4 paint = 0; float born = 0;
            float3 side = math.normalize(math.cross(up, new float3(1, 0, 0)));
            TerrainNoise.Paint(math.normalize(up + side * (25 / body.radius)), blast, body.radius, 0.1f, ref paint, ref born);
            Assert.Less(paint.y, 0.01f, "lava does not spread beyond the hole");
        }
    }
}
