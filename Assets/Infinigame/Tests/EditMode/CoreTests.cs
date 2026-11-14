using System.Collections.Generic;
using Infinigame.Core;
using NUnit.Framework;
using Unity.Mathematics;

namespace Infinigame.Tests
{
    static class Check
    {
        public static void Near(float3 expected, float3 actual, float tolerance) =>
            Assert.Less(math.distance(expected, actual), tolerance, $"expected {expected} but was {actual}");

        public static void Near(double3 expected, double3 actual, double tolerance) =>
            Assert.Less(math.distance(expected, actual), tolerance, $"expected {expected} but was {actual}");

        public static IEnumerable<float3> Directions(int count, uint seed = 1)
        {
            var rnd = new Random(seed);
            for (int i = 0; i < count; i++) yield return rnd.NextFloat3Direction();
        }
    }

    public class CubeSphereTests
    {
        [Test]
        public void DirectionRoundTripsThroughFace()
        {
            for (int face = 0; face < CubeSphere.Faces; face++)
            for (float u = -0.95f; u < 1f; u += 0.19f)
            for (float v = -0.95f; v < 1f; v += 0.19f)
            {
                float3 direction = CubeSphere.Direction(face, new float2(u, v));
                Assert.AreEqual(1f, math.length(direction), 1e-5f);
                Assert.AreEqual(face, CubeSphere.Face(direction, out float2 uv));
                Assert.Less(math.distance(new float2(u, v), uv), 1e-4f);
            }
        }

        [Test]
        public void GridTrianglesFaceOutwards()
        {
            for (int face = 0; face < CubeSphere.Faces; face++)
            {
                float3 a = CubeSphere.Direction(face, 0f);
                float3 b = CubeSphere.Direction(face, new float2(0.1f, 0f));
                float3 c = CubeSphere.Direction(face, new float2(0f, 0.1f));
                Assert.Greater(math.dot(math.cross(b - a, c - a), a), 0f, $"face {face}");
            }
        }

        [Test]
        public void NeighbouringFacesMeetAtEdges()
        {
            for (int face = 0; face < CubeSphere.Faces; face++)
            {
                float3 beyondEdge = CubeSphere.Direction(face, new float2(1.03f, 0.3f));
                int neighbour = CubeSphere.Face(beyondEdge, out float2 uv);
                Assert.AreNotEqual(face, neighbour);
                Check.Near(beyondEdge, CubeSphere.Direction(neighbour, uv), 1e-5f);
            }
        }
    }

    public class TerrainTests
    {
        static TerrainParams Planet(uint seed) => SolarSystemDescriptor.Generate(seed).bodies[1].terrain;
        static TerrainParams Moon(uint seed) => SolarSystemDescriptor.Generate(seed).bodies[2].terrain;

        [Test]
        public void HeightIsDeterministicAndBounded()
        {
            foreach (var terrain in new[] { Planet(1), Moon(1) })
            foreach (float3 direction in Check.Directions(3000))
            {
                float height = TerrainNoise.Height(direction, terrain);
                Assert.AreEqual(height, TerrainNoise.Height(direction, terrain));
                Assert.That(height, Is.InRange(terrain.MinHeight, terrain.MaxHeight));
            }
        }

        [Test]
        public void PlanetHasBothLandAndSea()
        {
            for (uint seed = 1; seed <= 8; seed++)
            {
                var terrain = Planet(seed);
                int land = 0, count = 4000;
                foreach (float3 direction in Check.Directions(count))
                    if (TerrainNoise.Height(direction, terrain) > 0f)
                        land++;
                Assert.That(land / (float)count, Is.InRange(0.15f, 0.75f), $"seed {seed}");
            }
        }

        [Test]
        public void PlanetHasMountainsAndRivers()
        {
            var terrain = Planet(1);
            float highest = 0f;
            int riverBed = 0;
            foreach (float3 direction in Check.Directions(20000))
            {
                float height = TerrainNoise.Height(direction, terrain);
                highest = math.max(highest, height);
                if (height < 0f && height > -terrain.riverDepth * 1.01f) riverBed++;
            }
            Assert.Greater(highest, terrain.mountainAmp * 0.5f);
            Assert.Greater(riverBed, 100);
        }

        [Test]
        public void HeightHasNoSteps()
        {
            // 0.0001 rad is 10 cm on a 1 km planet: nothing may jump a metre over that
            foreach (var terrain in new[] { Planet(1), Planet(2), Moon(1) })
            foreach (float3 direction in Check.Directions(3000))
            {
                float3 nearby = math.normalize(direction + math.cross(direction, new float3(0.3f, 0.5f, 0.8f)) * 1e-4f);
                Assert.Less(math.abs(TerrainNoise.Height(direction, terrain) - TerrainNoise.Height(nearby, terrain)), 1f);
            }
        }

        [Test]
        public void SeedsGiveDifferentTerrain()
        {
            float difference = 0f;
            foreach (float3 direction in Check.Directions(200))
                difference += math.abs(TerrainNoise.Height(direction, Planet(1)) - TerrainNoise.Height(direction, Planet(2)));
            Assert.Greater(difference / 200f, 5f);
        }

        [Test]
        public void MoonIsDryAndCratered()
        {
            var terrain = Moon(1);
            Assert.AreEqual(0f, terrain.seaDepth);
            float lowest = 0f;
            foreach (float3 direction in Check.Directions(5000))
                lowest = math.min(lowest, TerrainNoise.Height(direction, terrain));
            Assert.Less(lowest, -terrain.craterDepth * 0.5f);
        }
    }

    public class OrbitTests
    {
        static readonly SolarSystemDescriptor Solar = SolarSystemDescriptor.Generate(1);

        [Test]
        public void SunIsStatic()
        {
            Check.Near(double3.zero, Solar.PositionAt(0, 12345), 1e-9);
            Check.Near(double3.zero, Solar.VelocityAt(0, 12345), 1e-9);
        }

        [Test]
        public void BodiesStayAtTheirOrbitRadius()
        {
            for (double time = 0; time < 5000; time += 137)
            {
                Assert.AreEqual(Solar.bodies[1].orbit.radius, math.length(Solar.PositionAt(1, time)), 1e-6);
                Assert.AreEqual(Solar.bodies[2].orbit.radius, math.distance(Solar.PositionAt(2, time), Solar.PositionAt(1, time)), 1e-6);
            }
        }

        [Test]
        public void OrbitClosesAfterOnePeriod() =>
            Check.Near(Solar.PositionAt(1, 100), Solar.PositionAt(1, 100 + Solar.bodies[1].orbit.period), 1e-6);

        [Test]
        public void VelocityMatchesMotion()
        {
            const double time = 321, dt = 1e-3;
            for (int body = 1; body <= 2; body++)
                Check.Near((Solar.PositionAt(body, time + dt) - Solar.PositionAt(body, time - dt)) / (2 * dt), Solar.VelocityAt(body, time), 1e-4);
        }
    }

    public class GravityTests
    {
        static readonly BodyDescriptor Planet = SolarSystemDescriptor.Generate(1).bodies[1];
        static readonly float3 Up = math.normalize(new float3(1, 2, -3));

        [Test]
        public void SurfaceGravityPointsAtTheCentre() =>
            Check.Near(-Up * Planet.surfaceGravity, Planet.GravityAt(Up * Planet.radius), 1e-4f);

        [Test]
        public void FallsOffWithTheSquareOfDistance() =>
            Assert.AreEqual(Planet.surfaceGravity / 4f, math.length(Planet.GravityAt(Up * Planet.radius * 2f)), 1e-4f);

        [Test]
        public void IsConstantBelowTheSurface() =>
            Assert.AreEqual(Planet.surfaceGravity, math.length(Planet.GravityAt(Up * Planet.radius * 0.5f)), 1e-4f);
    }

    public class BriefTests
    {
        [Test]
        public void EverySeedMeetsTheLapTimes()
        {
            for (uint seed = 0; seed < 500; seed++)
            {
                var system = SolarSystemDescriptor.Generate(seed);
                float planet = system.bodies[1].radius, moon = system.bodies[2].radius;
                Assert.Less(2 * math.PI * planet / Tuning.WalkSpeed, 3600f, "walk round the planet in under an hour");
                Assert.Less(2 * math.PI * (planet + 150f) / Tuning.ShipCruiseSpeed, 60f, "fly round the planet in under a minute");
                Assert.Less(2 * math.PI * moon / Tuning.WalkSpeed, 300f, "walk round the moon in under five minutes");
                Assert.Less(2 * math.PI * (moon + 30f) / Tuning.ShipCruiseSpeed, 15f, "fly round the moon in under fifteen seconds");
            }
        }

        [Test]
        public void SameSeedSameSystem()
        {
            var a = SolarSystemDescriptor.Generate(7);
            var b = SolarSystemDescriptor.Generate(7);
            var other = SolarSystemDescriptor.Generate(8);
            for (int i = 0; i < a.bodies.Length; i++)
            {
                Assert.AreEqual(a.bodies[i].radius, b.bodies[i].radius);
                Assert.AreEqual(a.bodies[i].surfaceGravity, b.bodies[i].surfaceGravity);
                Assert.AreEqual(a.bodies[i].terrain.offset, b.bodies[i].terrain.offset);
                Assert.AreEqual(a.bodies[i].orbit.phase, b.bodies[i].orbit.phase);
            }
            Assert.AreNotEqual(a.bodies[1].radius, other.bodies[1].radius);
        }

        [Test]
        public void MoonOrbitsInsideThePlanetsInfluence()
        {
            for (uint seed = 0; seed < 100; seed++)
            {
                var system = SolarSystemDescriptor.Generate(seed);
                Assert.Less(system.bodies[2].orbit.radius + system.bodies[2].soiRadius, system.bodies[1].soiRadius);
                Assert.Less(system.bodies[1].soiRadius, system.bodies[1].orbit.radius - system.bodies[0].radius);
                Assert.Greater(system.bodies[2].orbit.radius - system.bodies[2].soiRadius, system.bodies[1].radius * 2f);
            }
        }
    }

    public class RelicTests
    {
        [Test]
        public void SameSeedHidesTheSameRelics()
        {
            var a = Relics.Place(SolarSystemDescriptor.Generate(4));
            var b = Relics.Place(SolarSystemDescriptor.Generate(4));
            Assert.AreEqual(7, a.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].id, b[i].id);
                Assert.AreEqual(a[i].position, b[i].position);
            }
            Assert.AreNotEqual(a[0].position, Relics.Place(SolarSystemDescriptor.Generate(5))[0].position);
        }

        [Test]
        public void RelicsHoverAboveGroundAndSeaAndKeepApart()
        {
            for (uint seed = 1; seed <= 20; seed++)
            {
                var system = SolarSystemDescriptor.Generate(seed);
                var sites = Relics.Place(system);
                var ids = new HashSet<int>();
                foreach (var site in sites)
                {
                    var body = system.bodies[site.body];
                    float3 direction = math.normalize(site.position);
                    Assert.AreEqual(math.max(body.GroundRadius(direction), body.SeaRadius) + Relics.HoverHeight, math.length(site.position), 0.01f);
                    Assert.IsTrue(ids.Add(site.id), "ids are unique within a system");
                    foreach (var other in sites)
                        if (other.id != site.id && other.body == site.body)
                            Assert.Greater(math.distance(site.position, other.position), body.radius * 0.5f);
                }
            }
        }

        [Test]
        public void PlanetRelicsSpanPeaksSeaAndShore()
        {
            var system = SolarSystemDescriptor.Generate(1);
            var planet = system.bodies[1];
            float highest = float.MinValue, lowest = float.MaxValue;
            foreach (var site in Relics.Place(system))
                if (site.body == 1)
                {
                    float height = planet.GroundRadius(math.normalize(site.position)) - planet.radius;
                    highest = math.max(highest, height);
                    lowest = math.min(lowest, height);
                }
            Assert.Greater(highest, planet.terrain.mountainAmp * 0.3f);
            Assert.Less(lowest, -planet.terrain.seaDepth * 0.5f);
        }
    }

    public class SpinTests
    {
        static readonly SolarSystemDescriptor Solar = SolarSystemDescriptor.Generate(1);
        static readonly float3 East = new float3(1, 0, 0), Local = new float3(120, 990, -45);
        static BodyDescriptor Planet => Solar.bodies[1];

        [Test]
        public void SunDoesNotSpin() => Check.Near(East, math.rotate(Solar.bodies[0].Rotation(777), East), 1e-6f);

        [Test]
        public void PlanetTurnsOncePerDay()
        {
            float3 now = math.rotate(Planet.Rotation(50), East);
            Check.Near(now, math.rotate(Planet.Rotation(50 + Planet.spinPeriod), East), 1e-4f);
            Assert.Less(math.dot(now, math.rotate(Planet.Rotation(50 + Planet.spinPeriod / 2), East)), -0.99f);
        }

        [Test]
        public void SpinAxisIsTiltedAndFixed()
        {
            float3 axis = math.rotate(Planet.Rotation(0), new float3(0, 1, 0));
            Check.Near(axis, math.rotate(Planet.Rotation(123), new float3(0, 1, 0)), 1e-5f);
            Assert.AreEqual(math.cos(Planet.tilt), axis.y, 1e-5f);
            Assert.Greater(Planet.tilt, 0.1f);
        }

        [Test]
        public void ADayIsMuchShorterThanAYear()
        {
            for (uint seed = 0; seed < 50; seed++)
            {
                var planet = SolarSystemDescriptor.Generate(seed).bodies[1];
                Assert.That(planet.spinPeriod, Is.InRange(300, 900));
                Assert.Greater(planet.orbit.period, planet.spinPeriod * 4);
            }
        }

        [Test]
        public void MoonKeepsOneFaceToThePlanet()
        {
            float3 Facing(double time) => math.normalize(Solar.ToLocal(2, time, Solar.PositionAt(1, time)));
            for (double time = 0; time < 3000; time += 211) Check.Near(Facing(0), Facing(time), 1e-3f);
        }

        [Test]
        public void LocalAndInertialRoundTrip()
        {
            for (int body = 0; body < 3; body++) Check.Near(Local, Solar.ToLocal(body, 400, Solar.ToInertial(body, 400, Local)), 0.01f);
        }

        [Test]
        public void GroundCarriesYouRoundWithTheSpin()
        {
            const double time = 400, dt = 0.1;
            for (int body = 1; body < 3; body++)
                Check.Near((Solar.ToInertial(body, time + dt, Local) - Solar.ToInertial(body, time - dt, Local)) / (2 * dt),
                    Solar.InertialVelocity(body, time, Local, float3.zero), 0.02);
        }
    }

    public class FrameTests
    {
        static readonly SolarSystemDescriptor Solar = SolarSystemDescriptor.Generate(1);
        const double Time = 250;

        [Test]
        public void RebasingKeepsInertialState()
        {
            float3 position = new float3(10, 1005, -3), velocity = new float3(1, 2, 3);
            double3 inertialPosition = Solar.ToInertial(1, Time, position), inertialVelocity = Solar.InertialVelocity(1, Time, position, velocity);
            Solar.Rebase(1, 2, Time, ref position, ref velocity);
            Check.Near(inertialPosition, Solar.ToInertial(2, Time, position), 1e-2);
            Check.Near(inertialVelocity, Solar.InertialVelocity(2, Time, position, velocity), 1e-3);
        }

        [Test]
        public void TurnCarriesDirectionsBetweenFrames()
        {
            float3 heading = math.normalize(new float3(0.3f, -0.2f, 0.9f));
            Check.Near(math.rotate(Solar.bodies[1].Rotation(Time), heading),
                math.rotate(Solar.bodies[2].Rotation(Time), math.rotate(Solar.Turn(1, 2, Time), heading)), 1e-5f);
        }

        [Test]
        public void ReferenceBodyFollowsSpheresOfInfluence()
        {
            double3 planet = Solar.PositionAt(1, Time), moon = Solar.PositionAt(2, Time);
            Assert.AreEqual(1, Solar.ReferenceBody(planet + new double3(0, Solar.bodies[1].radius + 2, 0), Time, 0));
            Assert.AreEqual(2, Solar.ReferenceBody(moon + new double3(0, Solar.bodies[2].radius + 2, 0), Time, 1));
            Assert.AreEqual(0, Solar.ReferenceBody(planet * 0.3, Time, 1));
        }

        [Test]
        public void ReferenceBodyHasHysteresis()
        {
            double3 justOutsideMoon = Solar.PositionAt(2, Time) + new double3(0, Solar.bodies[2].soiRadius * 1.1, 0);
            Assert.AreEqual(2, Solar.ReferenceBody(justOutsideMoon, Time, 2));
            Assert.AreEqual(1, Solar.ReferenceBody(justOutsideMoon, Time, 1));
        }
    }

    public class QuadtreeTests
    {
        const float Radius = 1000f, SplitFactor = 2.5f;
        const int Depth = 6;
        static readonly float3 Camera = math.normalize(new float3(0.3f, 0.8f, -0.5f)) * (Radius + 2f);

        static List<NodeId> Draw(float splitFactor, System.Func<NodeId, bool> ready = null)
        {
            var draw = new List<NodeId>();
            QuadtreeLod.Select(Camera, Radius, Depth, splitFactor, ready, draw, new List<NodeId>());
            return draw;
        }

        static void AssertTilesSphereOnce(List<NodeId> draw)
        {
            var drawn = new HashSet<NodeId>(draw);
            Assert.AreEqual(draw.Count, drawn.Count);
            double area = 0;
            foreach (var node in draw)
            {
                area += 1.0 / (1 << (2 * node.depth));
                for (int depth = node.depth - 1, x = node.x >> 1, y = node.y >> 1; depth >= 0; depth--, x >>= 1, y >>= 1)
                    Assert.IsFalse(drawn.Contains(new NodeId(node.face, depth, x, y)), "a node and its ancestor are both drawn");
            }
            Assert.AreEqual(6.0, area, 1e-9);
        }

        [Test]
        public void DrawnNodesTileTheSphereExactlyOnce() => AssertTilesSphereOnce(Draw(SplitFactor));

        [Test]
        public void StillTilesWhileDetailStreamsIn()
        {
            bool Ready(NodeId node) => node.depth < 3 || (node.x + node.y) % 3 != 0;
            var draw = Draw(SplitFactor, Ready);
            AssertTilesSphereOnce(draw);
            foreach (var node in draw) Assert.IsTrue(node.depth == 0 || Ready(node));
        }

        [Test]
        public void DetailIsFinestUnderTheCamera()
        {
            int face = CubeSphere.Face(math.normalize(Camera), out float2 uv);
            foreach (var node in Draw(SplitFactor))
                if (node.face == face && math.all(uv >= node.UvMin) && math.all(uv <= node.UvMin + node.UvSize))
                {
                    Assert.AreEqual(Depth, node.depth);
                    return;
                }
            Assert.Fail("no drawn node contains the camera");
        }

        [Test]
        public void LowerTiersDrawFewerPatches() => Assert.Less(Draw(1.5f).Count, Draw(SplitFactor).Count);

        [Test]
        public void WantedIncludesEveryDrawnNode()
        {
            var draw = new List<NodeId>();
            var wanted = new List<NodeId>();
            QuadtreeLod.Select(Camera, Radius, Depth, SplitFactor, node => node.depth < 2, draw, wanted);
            var wantedSet = new HashSet<NodeId>(wanted);
            foreach (var node in draw) Assert.IsTrue(wantedSet.Contains(node));
            Assert.Greater(wanted.Count, draw.Count, "children that are still streaming in are wanted too");
        }

        [Test]
        public void MaxDepthReachesTheFinestQuad()
        {
            int depth = QuadtreeLod.MaxDepth(Radius, 32, 0.75f);
            Assert.LessOrEqual(QuadtreeLod.NodeSize(Radius, depth) / 32f, 0.75f);
            Assert.Greater(QuadtreeLod.NodeSize(Radius, depth - 1) / 32f, 0.75f);
        }
    }
}
