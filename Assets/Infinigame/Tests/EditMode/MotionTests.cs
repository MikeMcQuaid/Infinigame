using System.Linq;
using Infinigame.Core;
using NUnit.Framework;
using Unity.Mathematics;

namespace Infinigame.Tests
{
    static class World
    {
        public const float Dt = 1f / 60f;
        public static readonly SolarSystemDescriptor Solar = SolarSystemDescriptor.Generate(1);
        public static BodyDescriptor Planet => Solar.bodies[SolarSystemDescriptor.Home];
        public static BodyDescriptor Moon => Solar.bodies[SolarSystemDescriptor.Moon];

        public static float3 Land(BodyDescriptor body) => body.FindLanding(3);

        public static float3 Sea(BodyDescriptor body)
        {
            foreach (float3 direction in Check.Directions(5000, 3))
                if (body.GroundRadius(direction) - body.radius < -20f)
                    return direction;
            Assert.Fail("no sea found");
            return default;
        }

        public static float3 Tangent(float3 direction) => math.normalize(math.cross(direction, new float3(0.2f, 0.9f, 0.4f)));

        // Progress in radians round the great circle that starts at `start` heading `forward`.
        public static float Progress(float3 start, float3 forward, float3 position) =>
            math.atan2(math.dot(position, forward), math.dot(position, start));
    }

    public class WalkerTests
    {
        static WalkerState Spawn(BodyDescriptor body, out float3 direction)
        {
            direction = World.Land(body);
            return Walker.Spawn(body, direction, World.Tangent(direction));
        }

        static void Run(ref WalkerState s, WalkerInput input, BodyDescriptor body, float seconds)
        {
            for (float t = 0; t < seconds; t += World.Dt) Walker.Step(ref s, input, body, World.Dt);
        }

        static float Altitude(WalkerState s, BodyDescriptor body) => math.length(s.position) - body.GroundRadius(math.normalize(s.position));

        // Walks a lap the way a determined player would: hold forward, and whenever progress stalls
        // jetpack and sidestep round the obstacle. Returns the seconds taken.
        static float LapTime(BodyDescriptor body, float3 direction, float limit)
        {
            float3 forward = World.Tangent(direction);
            var s = Walker.Spawn(body, direction, forward);
            float swept = 0f, last = 0f, checkpoint = 0f, hopUntil = -1f, side = 1f;
            for (float t = 0; t < limit; t += World.Dt)
            {
                bool hop = t < hopUntil;
                Walker.Step(ref s, new WalkerInput { move = new float2(hop ? side : 0f, 1f), jump = hop }, body, World.Dt);
                float progress = World.Progress(direction, forward, s.position);
                swept += math.atan2(math.sin(progress - last), math.cos(progress - last));
                last = progress;
                if (swept >= 2 * math.PI) return t;
                if (t % 2f < World.Dt)
                {
                    if ((swept - checkpoint) * body.radius < 4f)
                    {
                        hopUntil = t + 6f;
                        side = -side;
                    }
                    checkpoint = swept;
                }
            }
            return limit;
        }

        [Test]
        public void StandsStillOnTheGround()
        {
            var s = Spawn(World.Planet, out _);
            float3 start = s.position;
            Run(ref s, default, World.Planet, 3f);
            Assert.IsTrue(s.grounded);
            Check.Near(start, s.position, 0.01f);
            Assert.Less(math.length(s.velocity), 0.01f);
        }

        [Test]
        public void FallsAndLands()
        {
            var s = Spawn(World.Planet, out float3 direction);
            s.position += direction * 30f;
            s.grounded = false;
            Run(ref s, default, World.Planet, 0.5f);
            Assert.IsFalse(s.grounded);
            Assert.Less(math.dot(s.velocity, direction), -3f);
            Run(ref s, default, World.Planet, 4f);
            Assert.IsTrue(s.grounded);
            Assert.AreEqual(0f, Altitude(s, World.Planet), 0.01f);
        }

        [Test]
        public void WalksAtWalkingPace()
        {
            var s = Spawn(World.Planet, out _);
            float3 start = s.position;
            Run(ref s, new WalkerInput { move = new float2(0, 1) }, World.Planet, 10f);
            Assert.That(math.distance(start, s.position), Is.InRange(9f * Tuning.WalkSpeed, 10.2f * Tuning.WalkSpeed));
        }

        [Test]
        public void SprintIsFasterThanWalking()
        {
            var walk = Spawn(World.Planet, out _);
            var sprint = walk;
            float3 start = walk.position;
            Run(ref walk, new WalkerInput { move = new float2(0, 1) }, World.Planet, 5f);
            Run(ref sprint, new WalkerInput { move = new float2(0, 1), sprint = true }, World.Planet, 5f);
            Assert.Greater(math.distance(start, sprint.position), math.distance(start, walk.position) * 1.3f);
        }

        [Test]
        public void PositiveYawTurnsRight()
        {
            var s = Spawn(World.Planet, out float3 direction);
            float3 right = math.cross(direction, s.forward);
            Walker.Step(ref s, new WalkerInput { yaw = 0.5f }, World.Planet, World.Dt);
            Assert.Greater(math.dot(s.forward, right), 0.4f);
            Assert.AreEqual(0f, math.dot(s.forward, math.normalize(s.position)), 1e-4f);
        }

        static float JumpHeight(BodyDescriptor body)
        {
            var s = Spawn(body, out _);
            Walker.Step(ref s, new WalkerInput { jump = true }, body, World.Dt);
            float highest = 0f;
            for (float t = 0; t < 20f; t += World.Dt)
            {
                Walker.Step(ref s, default, body, World.Dt);
                highest = math.max(highest, Altitude(s, body));
            }
            Assert.IsTrue(s.grounded);
            return highest;
        }

        [Test]
        public void JumpsHigherOnTheMoon()
        {
            float planet = JumpHeight(World.Planet), moon = JumpHeight(World.Moon);
            Assert.That(planet, Is.InRange(0.8f, 2f));
            Assert.Greater(moon, planet * 3f);
        }

        [Test]
        public void JetpackOutclimbsAJumpThenRunsDry()
        {
            var s = Spawn(World.Planet, out _);
            float highest = 0f;
            for (float t = 0; t < 4f; t += World.Dt)
            {
                Walker.Step(ref s, new WalkerInput { jump = true }, World.Planet, World.Dt);
                highest = math.max(highest, Altitude(s, World.Planet));
                if (t > 2.5f) Assert.LessOrEqual(s.fuel, 0.05f, "fuel must not recharge in the air");
            }
            Assert.Greater(highest, JumpHeight(World.Planet) * 3f);
        }

        [Test]
        public void TheJetpackCarriesYouForwardAndSteeringInTheAirNeverSlowsALeap()
        {
            var s = Spawn(World.Planet, out _);
            float3 forward = s.forward;
            float Along() => math.dot(s.velocity, forward);
            // straight up off the ground, then leaning into the jetpack
            Walker.Step(ref s, new WalkerInput { jump = true }, World.Planet, World.Dt);
            for (float t = 0; t < 1f; t += World.Dt) Walker.Step(ref s, new WalkerInput { jump = true, move = new float2(0, 1) }, World.Planet, World.Dt);
            Assert.Greater(Along(), Tuning.SprintSpeed + 2f, "faster than a run");
            Assert.LessOrEqual(Along(), Walker.JetpackSpeed + 0.5f);
            // out of fuel and still holding forward, the speed it gave is kept until the ground
            float carried = Along();
            for (float t = 0; t < 0.4f && !s.grounded; t += World.Dt) Walker.Step(ref s, new WalkerInput { move = new float2(0, 1) }, World.Planet, World.Dt);
            Assert.GreaterOrEqual(Along(), carried - 0.5f);
        }

        [Test]
        public void ALeapOffTheSmallestMoonCanBeFlownHome()
        {
            // off faster than anything so small a moon's pull could ever bring back
            var moon = World.Solar.bodies.Where(body => body.kind == BodyKind.Rock).OrderBy(body => body.radius).First();
            var s = Spawn(moon, out float3 direction);
            s.position += direction * 150f;
            s.velocity = direction * (2f * math.sqrt(2f * moon.surfaceGravity * moon.radius) + 2f);
            (s.grounded, s.height) = (false, 150f);
            // looking straight down at it and pushing forward
            for (float t = 0; t < 60f && !s.grounded; t += World.Dt) Walker.Step(ref s, new WalkerInput { move = new float2(0, 1), pitch = -1.5f }, moon, World.Dt);
            Assert.IsTrue(s.grounded, "back on the ground");
            Assert.Less(Combat.Fall(s.impact), 10f, "and little the worse for it");
        }

        [Test]
        public void WellOffTheGroundThePackPushesHardWhereYouLookButCannotClimbTheHomeWorld()
        {
            var s = Spawn(World.Planet, out float3 direction);
            s.position += direction * 60f;
            (s.grounded, s.height) = (false, 60f);
            float3 forward = s.forward;
            Run(ref s, new WalkerInput { move = new float2(0, 1) }, World.Planet, 1f);
            Assert.Greater(math.dot(s.velocity, forward), Tuning.SprintSpeed, "across: faster than a run within a second, and on no fuel");
            Assert.AreEqual(Walker.JetpackFuel, s.fuel);
            // looking straight up and pushing, without the burn
            Run(ref s, new WalkerInput { move = new float2(0, 1), pitch = 1.5f }, World.Planet, 1.5f);
            Assert.Less(math.dot(s.velocity, math.normalize(s.position)), -3f, "still falling");
            // and someone with no pack gets nothing
            var bare = Spawn(World.Planet, out _);
            bare.position += direction * 60f;
            (bare.grounded, bare.height) = (false, 60f);
            Run(ref bare, new WalkerInput { move = new float2(0, 1), bare = true }, World.Planet, 1f);
            Assert.LessOrEqual(math.dot(bare.velocity, forward), Tuning.WalkSpeed + 0.1f);
        }

        [Test]
        public void JetpackCannotEscapeTheMoon()
        {
            var s = Spawn(World.Moon, out _);
            float highest = 0f;
            for (float t = 0; t < 180f; t += World.Dt)
            {
                Walker.Step(ref s, new WalkerInput { jump = true }, World.Moon, World.Dt);
                highest = math.max(highest, math.length(s.position));
            }
            Assert.Less(highest, World.Moon.soiRadius * 0.9f);
        }

        [Test]
        public void FloatsAndSwimsInDeepWater()
        {
            float3 direction = World.Sea(World.Planet);
            var s = Walker.Spawn(World.Planet, direction, World.Tangent(direction));
            s.position += direction * 5f;
            s.grounded = false;
            Run(ref s, default, World.Planet, 6f);
            Assert.IsTrue(s.swimming);
            Assert.AreEqual(World.Planet.SeaRadius - Walker.FloatDepth, math.length(s.position), 0.2f);
            float3 start = s.position;
            Run(ref s, new WalkerInput { move = new float2(0, 1) }, World.Planet, 10f);
            Assert.That(math.distance(start, s.position), Is.InRange(24f, 31f));
        }

        [Test]
        public void WalksALapOfTheMoonInUnderFiveMinutes()
        {
            for (uint seed = 1; seed <= 8; seed++)
            {
                var moon = SolarSystemDescriptor.Generate(seed).bodies[SolarSystemDescriptor.Moon];
                Assert.Less(LapTime(moon, World.Land(moon), 300f), 290f, $"seed {seed}");
            }
        }

        [Test]
        public void WalksALapOfThePlanetInUnderAnHour()
        {
            for (uint seed = 1; seed <= 2; seed++)
            {
                var planet = SolarSystemDescriptor.Generate(seed).bodies[SolarSystemDescriptor.Home];
                Assert.Less(LapTime(planet, World.Land(planet), 3600f), 3600f, $"seed {seed}");
            }
        }
    }

    public class ShipTests
    {
        static ShipState Hover(BodyDescriptor body, float3 direction, float altitude)
        {
            var s = Ship.Spawn(body, direction, World.Tangent(direction));
            s.position += direction * altitude;
            s.landed = false;
            return s;
        }

        static void Run(ref ShipState s, ShipInput input, BodyDescriptor body, bool assist, float seconds)
        {
            for (float t = 0; t < seconds; t += World.Dt) Ship.Step(ref s, input, body, assist, World.Dt);
        }

        // Height of the hull's underside above the ground or sea.
        static float Gap(ShipState s, BodyDescriptor body)
        {
            float3 up = math.normalize(s.position);
            return math.length(s.position) - math.max(body.GroundRadius(up), body.SeaRadius) - Ship.Clearance;
        }

        static float3 Forward(ShipState s) => math.rotate(s.rotation, new float3(0, 0, 1));

        // Flies a lap holding forward thrust. Returns the seconds taken and the lowest the hull came.
        static float LapTime(BodyDescriptor body, float altitude, float limit, out float lowest)
        {
            float3 direction = World.Land(body), forward = World.Tangent(direction);
            var s = Hover(body, direction, altitude);
            float swept = 0f, last = 0f;
            lowest = float.MaxValue;
            for (float t = 0; t < limit; t += World.Dt)
            {
                Ship.Step(ref s, new ShipInput { thrust = new float3(0, 0, 1) }, body, true, World.Dt);
                lowest = math.min(lowest, Gap(s, body));
                float progress = World.Progress(direction, forward, s.position);
                swept += math.atan2(math.sin(progress - last), math.cos(progress - last));
                last = progress;
                if (swept >= 2 * math.PI) return t;
            }
            return limit;
        }

        [Test]
        public void HoversWithAssistAndNoInput()
        {
            var s = Hover(World.Planet, World.Land(World.Planet), 50f);
            float3 start = s.position;
            Run(ref s, default, World.Planet, true, 5f);
            Check.Near(start, s.position, 0.5f);
            Assert.IsFalse(s.landed);
        }

        [Test]
        public void FallsWithoutAssist()
        {
            float3 direction = World.Land(World.Planet);
            var s = Hover(World.Planet, direction, 400f);
            Run(ref s, default, World.Planet, false, 2f);
            Assert.AreEqual(-2f * math.length(World.Planet.GravityAt(s.position)), math.dot(s.velocity, direction), 1f);
        }

        [Test]
        public void OrbitsWithoutAssist()
        {
            var planet = World.Planet;
            float3 direction = World.Land(planet), forward = World.Tangent(direction);
            float r0 = planet.radius + 400f, speed = math.sqrt(planet.surfaceGravity * planet.radius * planet.radius / r0);
            var s = new ShipState { position = direction * r0, velocity = forward * speed, rotation = quaternion.identity };
            float swept = 0f, last = 0f, period = 2 * math.PI * r0 / speed;
            for (float t = 0; t < period; t += World.Dt)
            {
                Ship.Step(ref s, default, planet, false, World.Dt);
                Assert.AreEqual(r0, math.length(s.position), r0 * 0.02f);
                float progress = World.Progress(direction, forward, s.position);
                swept += math.atan2(math.sin(progress - last), math.cos(progress - last));
                last = progress;
            }
            Assert.AreEqual(2 * math.PI, swept, 0.1f);
        }

        [Test]
        public void AssistedLapOfTheMoonTakesUnderFifteenSeconds()
        {
            for (uint seed = 1; seed <= 3; seed++)
            {
                Assert.Less(LapTime(SolarSystemDescriptor.Generate(seed).bodies[SolarSystemDescriptor.Moon], 30f, 15f, out float lowest), 15f, $"seed {seed}");
                Assert.Greater(lowest, 0f, "the hull must not touch the ground");
            }
        }

        [Test]
        public void AssistedLapOfThePlanetTakesUnderAMinute()
        {
            for (uint seed = 1; seed <= 3; seed++)
            {
                Assert.Less(LapTime(SolarSystemDescriptor.Generate(seed).bodies[SolarSystemDescriptor.Home], 150f, 60f, out float lowest), 60f, $"seed {seed}");
                Assert.Greater(lowest, 0f, "the hull must not touch the ground");
            }
        }

        [Test]
        public void SkimsOverTerrainWithoutTouching()
        {
            LapTime(World.Planet, 10f, 60f, out float lowest);
            Assert.Greater(lowest, 0f);
        }

        [Test]
        public void LandingAssistSettlesUprightAndStays()
        {
            var planet = World.Planet;
            var s = Hover(planet, World.Land(planet), 40f);
            s.velocity = World.Tangent(math.normalize(s.position)) * 30f;
            Run(ref s, new ShipInput { land = true }, planet, true, 30f);
            Assert.IsTrue(s.landed);
            Assert.AreEqual(0f, Gap(s, planet), 0.01f);
            float3 up = math.normalize(s.position);
            Assert.Greater(math.dot(math.rotate(s.rotation, new float3(0, 1, 0)), planet.GroundNormal(up)), 0.99f);
            float3 parked = s.position;
            Run(ref s, default, planet, true, 5f);
            Check.Near(parked, s.position, 1e-4f);
        }

        [Test]
        public void TakesOffWithLift()
        {
            var planet = World.Planet;
            float3 direction = World.Land(planet);
            var s = Ship.Spawn(planet, direction, World.Tangent(direction));
            Run(ref s, default, planet, true, 2f);
            Assert.IsTrue(s.landed);
            Run(ref s, new ShipInput { thrust = new float3(0, 1, 0) }, planet, true, 2f);
            Assert.IsFalse(s.landed);
            Assert.Greater(Gap(s, planet), 10f);
        }

        [Test]
        public void DescendingIntoTheGroundLandsInsteadOfTunnelling()
        {
            var planet = World.Planet;
            var s = Hover(planet, World.Land(planet), 100f);
            for (float t = 0; t < 10f; t += World.Dt)
            {
                Ship.Step(ref s, new ShipInput { thrust = new float3(0, -1, 0) }, planet, true, World.Dt);
                Assert.GreaterOrEqual(Gap(s, planet), -0.01f);
            }
            Assert.IsTrue(s.landed);
        }

        [Test]
        public void FloatsOnTheSea()
        {
            var planet = World.Planet;
            var s = Hover(planet, World.Sea(planet), 20f);
            Run(ref s, new ShipInput { land = true }, planet, true, 20f);
            Assert.IsTrue(s.landed);
            Assert.AreEqual(planet.SeaRadius + Ship.Clearance, math.length(s.position), 0.01f);
        }

        [Test]
        public void SpeedLimitRisesWithAltitude()
        {
            Assert.AreEqual(Tuning.ShipCruiseSpeed, Ship.MaxSpeed(World.Planet, 0f));
            Assert.AreEqual(Tuning.ShipSpaceSpeed, Ship.MaxSpeed(World.Planet, World.Planet.radius * 3f));
        }

        [Test]
        public void TurnsTheWayItIsTold()
        {
            var planet = World.Planet;
            float3 direction = World.Land(planet);
            var start = Hover(planet, direction, 2000f);
            float3 right = math.rotate(start.rotation, new float3(1, 0, 0));

            var s = start;
            Run(ref s, new ShipInput { turn = new float3(0, 1, 0) }, planet, true, 0.5f);
            Assert.Greater(math.dot(Forward(s), right), 0.2f, "yaw right");

            s = start;
            Run(ref s, new ShipInput { turn = new float3(1, 0, 0) }, planet, true, 0.5f);
            Assert.Greater(math.dot(Forward(s), direction), 0.2f, "nose up");

            s = start;
            Run(ref s, new ShipInput { turn = new float3(0, 0, 1) }, planet, true, 0.5f);
            Assert.Less(math.dot(math.rotate(s.rotation, new float3(1, 0, 0)), direction), -0.2f, "roll right drops the right wing");
        }

        [Test]
        public void LevelsItsWingsNearTheGround()
        {
            var planet = World.Planet;
            float3 direction = World.Land(planet);
            var s = Hover(planet, direction, 50f);
            s.rotation = math.mul(s.rotation, quaternion.RotateZ(0.8f));
            Run(ref s, default, planet, true, 6f);
            Assert.AreEqual(0f, math.dot(math.rotate(s.rotation, new float3(1, 0, 0)), math.normalize(s.position)), 0.05f);
        }
    }
}
