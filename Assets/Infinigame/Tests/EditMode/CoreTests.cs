using System.Collections.Generic;
using System.Linq;
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
        static TerrainParams Planet(uint seed) => SolarSystemDescriptor.Generate(seed).bodies[SolarSystemDescriptor.Home].terrain;
        static TerrainParams Moon(uint seed) => SolarSystemDescriptor.Generate(seed).bodies[SolarSystemDescriptor.Moon].terrain;

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

        // Where a body is relative to the one it circles, at each of a lap's worth of moments.
        static IEnumerable<double3> Lap(int body, int steps = 720)
        {
            var orbit = Solar.bodies[body].orbit;
            for (int i = 0; i < steps; i++) yield return orbit.Position(orbit.period * i / steps);
        }

        [Test]
        public void OrbitsAreEllipsesThatSwingBetweenANearestAndAFurthestDistance()
        {
            for (int body = 1; body < Solar.bodies.Length; body++)
            {
                var orbit = Solar.bodies[body].orbit;
                var distances = Lap(body).Select(at => math.length(at)).ToArray();
                Assert.AreEqual(orbit.Nearest, distances.Min(), 1e-4 * orbit.radius, Solar.bodies[body].summary);
                Assert.AreEqual(orbit.Furthest, distances.Max(), 1e-4 * orbit.radius, Solar.bodies[body].summary);
            }
            // the innermost world's is the most lopsided, as Mercury's is, and the home world's is nearly a circle
            Assert.Greater(Solar.bodies[1].orbit.Furthest / Solar.bodies[1].orbit.Nearest, 1.5);
            Assert.Less(Solar.bodies[SolarSystemDescriptor.Home].orbit.Furthest / Solar.bodies[SolarSystemDescriptor.Home].orbit.Nearest, 1.04);
        }

        [Test]
        public void ABodyGoesFastestWhereItIsNearest()
        {
            // equal areas in equal times: speed times distance is the same at both ends of the ellipse
            var orbit = Solar.bodies[1].orbit;
            double nearest = Enumerable.Range(0, 100000).Select(i => orbit.period * i / 100000).OrderBy(time => math.length(orbit.Position(time))).First();
            double fast = math.length(orbit.Velocity(nearest)), slow = math.length(orbit.Velocity(nearest + orbit.period / 2));
            Assert.AreEqual(orbit.Furthest / orbit.Nearest, fast / slow, 1e-3);
            Assert.AreEqual(orbit.Furthest, math.length(orbit.Position(nearest + orbit.period / 2)), 1e-3 * orbit.radius, "half a year later it is at its furthest");
        }

        [Test]
        public void EachOrbitLiesInAFlatPlaneOfItsOwn()
        {
            // the way an orbit's axis points, by the same rule as a spin's: a world going round forwards has it along +y
            double3 Normal(int body) => math.normalize(math.cross(Solar.bodies[body].orbit.Position(math.abs(Solar.bodies[body].orbit.period) / 4), Solar.bodies[body].orbit.Position(0)));
            for (int body = 1; body < Solar.bodies.Length; body++)
                foreach (var at in Lap(body, 60))
                    Assert.AreEqual(0, math.dot(Normal(body), at), 1e-6 * Solar.bodies[body].orbit.radius, Solar.bodies[body].summary);
            // the home world's is the plane the rest are measured from; the innermost world's is tipped 7 degrees to it
            Check.Near(new double3(0, 1, 0), Normal(SolarSystemDescriptor.Home), 1e-9);
            Assert.AreEqual(7.0, math.degrees(math.acos(Normal(1).y)), 0.01);
            Assert.That(Lap(1).Max(at => at.y) / math.sin(math.radians(7.0)), Is.InRange(0.99 * Solar.bodies[1].orbit.Nearest, 1.01 * Solar.bodies[1].orbit.Furthest), "so it rises kilometres clear of that plane");
            // no two planets share one, nor tip the same way
            var planets = Solar.Planets.ToArray();
            for (int i = 0; i < planets.Length; i++)
                for (int j = 0; j < i; j++)
                    Assert.Less(math.dot(Normal(planets[i]), Normal(planets[j])), 1 - 1e-6);
        }

        [Test]
        public void OrbitClosesAfterOnePeriod() =>
            Check.Near(Solar.PositionAt(SolarSystemDescriptor.Home, 100), Solar.PositionAt(SolarSystemDescriptor.Home, 100 + Solar.bodies[SolarSystemDescriptor.Home].orbit.period), 1e-6);

        [Test]
        public void VelocityMatchesMotion()
        {
            const double time = 321, dt = 1e-3;
            for (int body = 1; body < Solar.bodies.Length; body++)
                Check.Near((Solar.PositionAt(body, time + dt) - Solar.PositionAt(body, time - dt)) / (2 * dt), Solar.VelocityAt(body, time), 1e-4);
        }
    }

    public class GravityTests
    {
        static readonly BodyDescriptor Planet = SolarSystemDescriptor.Generate(1).bodies[SolarSystemDescriptor.Home];
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
                float planet = system.bodies[SolarSystemDescriptor.Home].radius, moon = system.bodies[SolarSystemDescriptor.Moon].radius;
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
            Assert.AreNotEqual(a.bodies[SolarSystemDescriptor.Home].radius, other.bodies[SolarSystemDescriptor.Home].radius);
        }

        [Test]
        public void MoonOrbitsInsideThePlanetsInfluence()
        {
            for (uint seed = 0; seed < 100; seed++)
            {
                var system = SolarSystemDescriptor.Generate(seed);
                Assert.Less(system.bodies[SolarSystemDescriptor.Moon].orbit.Furthest + system.bodies[SolarSystemDescriptor.Moon].soiRadius, system.bodies[SolarSystemDescriptor.Home].soiRadius);
                Assert.Less(system.bodies[SolarSystemDescriptor.Home].soiRadius, system.bodies[SolarSystemDescriptor.Home].orbit.Nearest - system.bodies[0].radius);
                Assert.Greater(system.bodies[SolarSystemDescriptor.Moon].orbit.Nearest - system.bodies[SolarSystemDescriptor.Moon].soiRadius, system.bodies[SolarSystemDescriptor.Home].radius * 2f);
            }
        }
    }

    public class SpinTests
    {
        static readonly SolarSystemDescriptor Solar = SolarSystemDescriptor.Generate(1);
        static readonly float3 East = new float3(1, 0, 0), Local = new float3(120, 990, -45);
        static BodyDescriptor Planet => Solar.bodies[SolarSystemDescriptor.Home];

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
                var planet = SolarSystemDescriptor.Generate(seed).bodies[SolarSystemDescriptor.Home];
                Assert.That(planet.spinPeriod, Is.InRange(300, 900));
                Assert.Greater(planet.orbit.period, planet.spinPeriod * 4);
            }
        }

        [Test]
        public void MoonsKeepOneFaceToTheirPlanetAndRockAsTheyGo()
        {
            for (int moon = 1; moon < Solar.bodies.Length; moon++)
            {
                var orbit = Solar.bodies[moon].orbit;
                if (orbit.parent <= 0) continue;
                float rocked = 0f;
                for (int i = 0; i < 200; i++)
                {
                    double time = math.abs(orbit.period) * i / 200;
                    float3 planet = math.normalize(Solar.ToLocal(moon, time, Solar.PositionAt(orbit.parent, time)));
                    Assert.AreEqual(0f, planet.y, 1e-3f, "it turns about its orbit's own axis");
                    rocked = math.max(rocked, math.abs(math.atan2(planet.z, -planet.x)));
                }
                // On a lopsided orbit a moon runs ahead of its own steady spin and then behind, so its
                // face rocks from side to side: by about twice the lopsidedness, as our own moon's does.
                Assert.AreEqual(2f * orbit.eccentricity, rocked, 0.02f + 0.3f * orbit.eccentricity, Solar.bodies[moon].summary);
            }
        }

        [Test]
        public void WorldsLeanTheirOwnWays()
        {
            float3 Axis(int body) => math.rotate(Solar.bodies[body].Rotation(0), new float3(0, 1, 0));
            var planets = Solar.Planets.ToArray();
            // an axis keeps its lean whichever way it points
            foreach (int planet in planets) Assert.AreEqual(math.cos(Solar.bodies[planet].tilt), Axis(planet).y, 1e-5f);
            // the home world, the rust desert and the ringed giant lean about as far as each other, but not the same way
            Assert.Less(math.dot(math.normalize(Axis(planets[2]).xz), math.normalize(Axis(planets[3]).xz)), 0.999f);
            Assert.Less(math.dot(math.normalize(Axis(planets[2]).xz), math.normalize(Axis(planets[5]).xz)), 0.999f);
        }

        [Test]
        public void LocalAndInertialRoundTrip()
        {
            for (int body = 0; body < Solar.bodies.Length; body++) Check.Near(Local, Solar.ToLocal(body, 400, Solar.ToInertial(body, 400, Local)), 0.01f);
        }

        [Test]
        public void GroundCarriesYouRoundWithTheSpin()
        {
            const double time = 400, dt = 0.1;
            for (int body = 1; body < Solar.bodies.Length; body++)
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
            double3 inertialPosition = Solar.ToInertial(SolarSystemDescriptor.Home, Time, position), inertialVelocity = Solar.InertialVelocity(SolarSystemDescriptor.Home, Time, position, velocity);
            Solar.Rebase(SolarSystemDescriptor.Home, SolarSystemDescriptor.Moon, Time, ref position, ref velocity);
            Check.Near(inertialPosition, Solar.ToInertial(SolarSystemDescriptor.Moon, Time, position), 1e-2);
            Check.Near(inertialVelocity, Solar.InertialVelocity(SolarSystemDescriptor.Moon, Time, position, velocity), 1e-3);
        }

        [Test]
        public void TurnCarriesDirectionsBetweenFrames()
        {
            float3 heading = math.normalize(new float3(0.3f, -0.2f, 0.9f));
            Check.Near(math.rotate(Solar.bodies[SolarSystemDescriptor.Home].Rotation(Time), heading),
                math.rotate(Solar.bodies[SolarSystemDescriptor.Moon].Rotation(Time), math.rotate(Solar.Turn(SolarSystemDescriptor.Home, SolarSystemDescriptor.Moon, Time), heading)), 1e-5f);
        }

        [Test]
        public void ReferenceBodyFollowsSpheresOfInfluence()
        {
            double3 planet = Solar.PositionAt(SolarSystemDescriptor.Home, Time), moon = Solar.PositionAt(SolarSystemDescriptor.Moon, Time);
            Assert.AreEqual(SolarSystemDescriptor.Home, Solar.ReferenceBody(planet + new double3(0, Solar.bodies[SolarSystemDescriptor.Home].radius + 2, 0), Time, 0));
            Assert.AreEqual(SolarSystemDescriptor.Moon, Solar.ReferenceBody(moon + new double3(0, Solar.bodies[SolarSystemDescriptor.Moon].radius + 2, 0), Time, SolarSystemDescriptor.Home));
            // high above every orbit there is only the sun
            Assert.AreEqual(0, Solar.ReferenceBody(new double3(0, 50000, 0), Time, SolarSystemDescriptor.Home));
        }

        [Test]
        public void ReferenceBodyHasHysteresis()
        {
            double3 justOutsideMoon = Solar.PositionAt(SolarSystemDescriptor.Moon, Time) + new double3(0, Solar.bodies[SolarSystemDescriptor.Moon].soiRadius * 1.1, 0);
            Assert.AreEqual(SolarSystemDescriptor.Moon, Solar.ReferenceBody(justOutsideMoon, Time, SolarSystemDescriptor.Moon));
            Assert.AreEqual(SolarSystemDescriptor.Home, Solar.ReferenceBody(justOutsideMoon, Time, SolarSystemDescriptor.Home));
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
