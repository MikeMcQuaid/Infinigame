using System.Collections.Generic;
using System.Linq;
using Infinigame.Core;
using NUnit.Framework;
using Unity.Mathematics;

namespace Infinigame.Tests
{
    // The system is a copy of our own: its worlds keep their real sizes relative to each other,
    // their real moons and their real warmth.
    public class ScaleTests
    {
        static readonly SolarSystemDescriptor Solar = World.Solar;
        static BodyDescriptor[] Planets => Solar.Planets.Select(planet => Solar.bodies[planet]).ToArray();

        [Test]
        public void PlanetsKeepTheirRealSizesRelativeToEachOther()
        {
            // Mercury to Neptune, in Earth radii
            float[] real = { 0.383f, 0.949f, 1f, 0.532f, 11.21f, 9.45f, 4.01f, 3.88f };
            for (uint seed = 0; seed < 20; seed++)
            {
                var system = SolarSystemDescriptor.Generate(seed);
                float home = system.bodies[SolarSystemDescriptor.Home].radius;
                var planets = system.Planets.Select(planet => system.bodies[planet]).ToArray();
                for (int i = 0; i < real.Length; i++) Assert.AreEqual(real[i], planets[i].radius / home, 0.01f, planets[i].summary);
                Assert.Greater(system.bodies[0].radius, planets.Max(planet => planet.radius), "the star is still the biggest thing there is");
            }
        }

        [Test]
        public void EachPlanetHasItsMoons()
        {
            CollectionAssert.AreEqual(new[] { 0, 0, 1, 2, 4, 7, 5, 2 }, Solar.Planets.Select(Solar.Moons));
            float home = Solar.bodies[SolarSystemDescriptor.Home].radius;
            var moons = Solar.bodies.Where(body => body.orbit.parent > 0).ToArray();
            Assert.AreEqual(0.413f, moons.Max(moon => moon.radius) / home, 0.005f, "the biggest moon of all circles the biggest planet");
            Assert.AreEqual(2, moons.Count(moon => moon.radius > 0.4f * home), "two moons outsize the smallest planet");
            Assert.Greater(Planets[0].radius, Solar.bodies[SolarSystemDescriptor.Moon].radius, "and the home world's own is smaller than any planet");
            Assert.AreEqual(1, moons.Count(moon => moon.orbit.period < 0), "one great moon circles backwards");
            Assert.AreEqual(1, moons.Count(moon => moon.air > 0f), "and one has air");
        }

        [Test]
        public void MoonsCircleInsideTheirPlanetsInfluenceAndKeepOneFaceToIt()
        {
            for (uint seed = 0; seed < 20; seed++)
            {
                var system = SolarSystemDescriptor.Generate(seed);
                var seen = new List<(int parent, double month, double outer)>();
                foreach (var moon in system.bodies.Where(body => body.orbit.parent > 0))
                {
                    var planet = system.bodies[moon.orbit.parent];
                    Assert.Less(moon.orbit.Furthest + moon.soiRadius, planet.soiRadius, moon.summary);
                    Assert.Greater(moon.orbit.Nearest - moon.soiRadius, math.max(planet.radius * 2f, planet.ringOuter), moon.summary);
                    Assert.AreEqual(moon.orbit.period, moon.spinPeriod, moon.summary);
                    foreach (var other in seen.Where(other => other.parent == moon.orbit.parent))
                    {
                        Assert.Greater(moon.orbit.Nearest - moon.soiRadius, other.outer, "no two moons of a planet can meet");
                        Assert.Greater(math.abs(moon.orbit.period), other.month, "and the further out, the longer the month");
                    }
                    seen.Add((moon.orbit.parent, math.abs(moon.orbit.period), moon.orbit.Furthest + moon.soiRadius));
                }
            }
        }

        [Test]
        public void DaysAndMonthsAreTheRealOnesAndYearsAreTheRealOnesCutShort()
        {
            for (uint seed = 0; seed < 20; seed++)
            {
                var system = SolarSystemDescriptor.Generate(seed);
                var planets = system.Planets.Select(planet => system.bodies[planet]).ToArray();
                double day = planets[2].spinPeriod;
                Assert.That(day, Is.InRange(480, 720), "the home world's day is about ten minutes");
                Assert.That(planets[2].orbit.period / 3600, Is.InRange(1.5, 2.5), "its year is twelve of them, a couple of hours, so the seasons turn in a sitting");
                // Mercury to Neptune: the year in Earth years and, from the home world out, the day in Earth days, backwards where negative
                double[] years = { 0.241, 0.615, 1, 1.881, 11.86, 29.46, 84, 164.8 }, days = { 0, 0, 1, 1.03, 0.414, 0.444, -0.718, 0.671 };
                for (int i = 0; i < planets.Length; i++)
                {
                    Assert.AreEqual(years[i], planets[i].orbit.period / planets[2].orbit.period, 1e-6, planets[i].summary);
                    if (i > 1) Assert.AreEqual(days[i], planets[i].spinPeriod / day, 0.001, planets[i].summary);
                }
                Assert.AreEqual(27.32, system.bodies[SolarSystemDescriptor.Moon].orbit.period / day, 0.01, "the moon takes its month");
                // the two worlds nearest the star turn in step with their orbits, as the real ones do
                Assert.AreEqual(2.0 / 3, planets[0].spinPeriod / planets[0].orbit.period, 1e-6, "the scorched rock turns three times in two of its years");
                Assert.AreEqual(-243 / 224.7, planets[1].spinPeriod / planets[1].orbit.period, 0.01, "the furnace world's day outlasts its year, backwards");
            }
        }

        [Test]
        public void WorldsAreTiltedAsTheRealOnesAre()
        {
            // Mercury to Neptune, in degrees: the ringed giant and the rust desert lean much as the home world does, one ice giant lies on its side
            float[] tilts = { 0.03f, 2.6f, 23.4f, 25.2f, 3.1f, 26.7f, 82.2f, 28.3f };
            for (int i = 0; i < tilts.Length; i++) Assert.AreEqual(tilts[i], math.degrees(Planets[i].tilt), 0.01f, Planets[i].summary);
        }

        [Test]
        public void MoonsCircleOverTheirPlanetsEquatorsButTheHomeMoonKeepsToThePlaneOfThePlanets()
        {
            // the way an orbit's axis points, by the same rule as a spin's
            double3 Normal(BodyDescriptor body) => math.normalize(math.cross(body.orbit.Position(math.abs(body.orbit.period) / 4), body.orbit.Position(0)));
            foreach (var moon in Solar.bodies.Where(body => body.orbit.parent > 0))
            {
                var planet = Solar.bodies[moon.orbit.parent];
                double off = math.degrees(math.acos(math.clamp(math.dot(Normal(moon), (double3)math.rotate(planet.Rotation(0), new float3(0, 1, 0))), -1, 1)));
                if (moon.orbit.parent == SolarSystemDescriptor.Home)
                {
                    Assert.AreEqual(18.3, off, 0.1, "our own moon's path is nowhere near over the equator");
                    Assert.AreEqual(5.1, math.degrees(math.acos(Normal(moon).y)), 0.1, "it is 5 degrees off the plane the planets circle in");
                }
                else if (moon.orbit.period < 0) Assert.AreEqual(157, off, 0.1, "the captured moon goes round backwards, tipped 23 degrees");
                else Assert.Less(off, 16, moon.summary);
            }
            // the ringed giant's close moons run in the plane of its rings, which is tipped with the planet
            var ringed = Solar.bodies[Solar.Planets.ElementAt(5)];
            var close = Solar.bodies.First(body => body.orbit.parent > 0 && Solar.bodies[body.orbit.parent] == ringed && body.orbit.inclination == ringed.tilt);
            Assert.AreEqual(26.7, math.degrees(math.acos(Normal(close).y)), 0.1);
        }

        [Test]
        public void TheBeltLiesBetweenTheRockyWorldsAndTheGiantsWithTheRaidersBaseInIt()
        {
            for (uint seed = 0; seed < 20; seed++)
            {
                var system = SolarSystemDescriptor.Generate(seed);
                var star = system.bodies[0];
                var planets = system.Planets.Select(planet => system.bodies[planet]).ToArray();
                Assert.Greater(star.ringInner, planets[3].orbit.Furthest + planets[3].soiRadius);
                Assert.Less(star.ringOuter, planets[4].orbit.Nearest - planets[4].soiRadius);
                var station = system.bodies.Single(body => body.kind == BodyKind.Station);
                Assert.That(station.orbit.radius, Is.InRange(star.ringInner + station.soiRadius, star.ringOuter - station.soiRadius));
                Assert.AreEqual(0f, math.length(station.GravityAt(new float3(300, 0, 0))), "a station has no pull to speak of");
                // the belt turns as one, and the base turns with it
                double time = 5000;
                Assert.AreEqual(-2 * math.PI_DBL * time / station.orbit.period, Rings.Turned(star, 0, 0, time), 1e-4);
                Assert.AreEqual(Rings.Turned(star, 0, 0, time), Rings.Turned(star, 3, 5, time), 1e-6);
            }
        }

        [Test]
        public void BeltRocksAreMountainsFarApart()
        {
            var star = Solar.bodies[0];
            var rocks = new List<Rock>();
            for (int sector = 0; sector < 40; sector++) Rings.Rocks(star, 0, 1, sector, rocks);
            Assert.Greater(rocks.Count, 20);
            Assert.Greater(rocks.Min(rock => rock.radius), 40f);
            Assert.Greater(rocks.Max(rock => math.abs(rock.position.y)), 200f, "the belt is a deep sheet, not a thin ring");
            Assert.Less(rocks.Count / 40f, Rings.PerCell / 2f, "and mostly empty");
            var scratch = new List<Rock>();
            float3 now = Rings.Now(star, 0, 1, 1234, rocks[0].position);
            Assert.IsTrue(Rings.Hit(star, now + math.normalize(now) * rocks[0].radius, Combat.ShipRadius, 1234, scratch, out _, out float3 velocity));
            Assert.Less(math.length(velocity), 0.1f * Tuning.ShipSpaceSpeed, "its rocks drift by far more slowly than a ship flies");
        }
    }

    public class ClimateTests
    {
        static readonly SolarSystemDescriptor Solar = World.Solar;
        const double Time = 300;

        static int Find(string word) => System.Array.FindIndex(Solar.bodies, body => body.summary.Contains(word));

        // The temperature on the ground of a world where the sun is at this height: 1 overhead, -1 underfoot.
        static float Ground(int index, float noon)
        {
            var body = Solar.bodies[index];
            float3 sun = math.normalize(Solar.ToLocal(index, Time, Solar.PositionAt(0, Time)));
            float3 direction = math.normalize(sun * noon + World.Tangent(sun) * math.sqrt(1f - noon * noon));
            return Climate.At(Solar, index, direction * (Ship.Surface(body, direction) + 1f), Time);
        }

        [Test]
        public void SunlightFadesWithDistanceAsItReallyDoes()
        {
            Assert.AreEqual(278f, Climate.Sunlit(1f), 3f, "five degrees where the Earth is");
            Assert.AreEqual(122f, Climate.Sunlit(5.2f), 3f);
            Assert.Greater(Climate.Sunlit(Climate.SunRadius), 4500f);
            float last = float.MaxValue;
            foreach (float au in new[] { 0.01f, 0.1f, 0.4f, 1f, 5f, 30f })
            {
                Assert.Less(Climate.Sunlit(au), last);
                last = Climate.Sunlit(au);
            }
        }

        [Test]
        public void EveryPlanetStandsWhereItsRealOneDoes()
        {
            foreach (int planet in Solar.Planets)
                Assert.AreEqual(Solar.bodies[planet].au, Climate.Au(Solar, Solar.bodies[planet].orbit.radius), 0.01f * Solar.bodies[planet].au, Solar.bodies[planet].summary);
            Assert.AreEqual(Climate.SunRadius, Climate.Au(Solar, Solar.bodies[0].radius), 1e-5f);
            float last = 0f;
            for (double distance = Solar.bodies[0].radius; distance < 9e5; distance *= 1.3)
            {
                Assert.Greater(Climate.Au(Solar, distance), last, "further is always further");
                last = Climate.Au(Solar, distance);
            }
        }

        [Test]
        public void WorldsAreAsWarmAsTheRealOnes()
        {
            Assert.That(Climate.Celsius(Ground(SolarSystemDescriptor.Home, 0.9f)), Is.InRange(-10f, 30f), "the home world is mild");
            Assert.AreEqual(737f, Ground(Find("furnace"), 1f), 5f, "the shrouded world is an oven by day");
            Assert.AreEqual(737f, Ground(Find("furnace"), -1f), 5f, "and by night");
            Assert.Greater(Ground(Find("scorched"), 1f), 650f, "the innermost rock roasts at noon");
            Assert.Less(Ground(Find("scorched"), -1f), 150f, "and freezes at midnight");
            Assert.That(Ground(Find("desert"), 0.5f), Is.InRange(150f, 280f));
            Assert.Greater(Ground(SolarSystemDescriptor.Moon, 1f), 350f);
            Assert.Less(Ground(SolarSystemDescriptor.Moon, -1f), 150f);
            Assert.Less(Ground(Find("nitrogen frost"), 1f), 50f, "the far moons are colder than anywhere");
        }

        [Test]
        public void ALivingWorldIsColdAtItsPolesAndPeaks()
        {
            var home = Solar.bodies[SolarSystemDescriptor.Home];
            float3 low = default, high = default, sun = math.normalize(Solar.ToLocal(SolarSystemDescriptor.Home, Time, Solar.PositionAt(0, Time)));
            foreach (float3 direction in Check.Directions(20000))
            {
                float height = home.GroundRadius(direction) - home.radius;
                // near the equator and in full daylight, so that only height tells the two apart
                if (math.abs(direction.y) > 0.3f || math.dot(direction, sun) < 0.75f) continue;
                if (height > 3f && height < 15f) low = direction;
                if (height > 0.6f * home.terrain.MaxHeight) high = direction;
            }
            float At(float3 direction) => Climate.At(Solar, SolarSystemDescriptor.Home, direction * (home.GroundRadius(direction) + 1f), Time);
            Assert.Greater(At(low), At(high) + 10f, "summits are cold");
            float3 pole = new float3(0, 1, 0);
            Assert.Less(Climate.At(Solar, SolarSystemDescriptor.Home, pole * (Ship.Surface(home, pole) + 1f), Time), home.heat - 20f);
        }

        [Test]
        public void SpaceTakesOverAwayFromAWorld()
        {
            int furnace = Find("furnace");
            var body = Solar.bodies[furnace];
            float space = Climate.Sunlit(body.au);
            Assert.AreEqual(space, Climate.At(Solar, furnace, new float3(0, body.radius * 2.5f, 0), Time), 8f);
            Assert.That(Climate.At(Solar, furnace, new float3(0, body.radius * 1.1f, 0), Time), Is.InRange(space, 737f));
        }

        [Test]
        public void TheStarCooksAnythingThatComesClose()
        {
            var star = Solar.bodies[0];
            float At(float radii) => Climate.At(Solar, 0, new float3(star.radius * radii, 0, 0), Time);
            Assert.Greater(At(1.01f), 4500f);
            Assert.Greater(At(2f), Climate.HullLimit, "two radii out a hull is already failing");
            Assert.Less(At(Autopilot.Standoff(star) / star.radius), Climate.HullLimit, "the autopilot stops where it is bearable");
            Assert.Less(At(Autopilot.Berth(star) / star.radius), Climate.HullLimit, "and passes no closer than that on the way elsewhere");
            Assert.AreEqual(Solar.bodies[SolarSystemDescriptor.Home].au, Climate.Au(Solar, Solar.bodies[SolarSystemDescriptor.Home].orbit.radius), 0.02f);
        }

        [Test]
        public void GiantsAreColdOnTopAndHellBelow()
        {
            foreach (int planet in Solar.Planets.Where(planet => Solar.bodies[planet].kind == BodyKind.Gas))
            {
                var giant = Solar.bodies[planet];
                float At(float radii) => Climate.At(Solar, planet, new float3(0, giant.radius * radii, 0), Time);
                Assert.AreEqual(giant.heat, At(1f), 1f, giant.summary);
                Assert.Less(giant.heat, 170f);
                Assert.That(At(0.9985f), Is.InRange(giant.heat + 150f, Climate.HullLimit), "warm enough to melt lead just under the clouds");
                Assert.Greater(At(0.99f), Climate.HullLimit, "and past any hull a hundredth of the way down");
                Assert.Greater(At(0.9f), 5000f);
            }
        }
    }

    public class CraterTests
    {
        static BodyDescriptor Fresh => SolarSystemDescriptor.Generate(1).bodies[SolarSystemDescriptor.Home];

        [Test]
        public void ABlastLeavesABowlWithARim()
        {
            var planet = Fresh;
            float3 centre = World.Land(planet), along = World.Tangent(centre);
            float3 At(float metres) => math.normalize(centre + along * (metres / planet.radius));
            float[] before = { planet.GroundRadius(centre), planet.GroundRadius(At(5f)), planet.GroundRadius(At(11f)), planet.GroundRadius(At(30f)) };
            planet.Dig(centre, 10f);
            Assert.AreEqual(before[0] - 4.5f, planet.GroundRadius(centre), 0.05f, "nearly half as deep as it is wide");
            Assert.Less(planet.GroundRadius(At(5f)), before[1] - 1.5f);
            Assert.Greater(planet.GroundRadius(At(11f)), before[2] + 0.2f, "earth is thrown up round the edge");
            Assert.AreEqual(before[3], planet.GroundRadius(At(30f)), 1e-4f, "and further off nothing has changed");
        }

        [Test]
        public void CratersPileUpButNotForEver()
        {
            var planet = Fresh;
            float3 centre = World.Land(planet);
            float before = planet.GroundRadius(centre);
            planet.Dig(centre, 4f);
            planet.Dig(centre, 4f);
            Assert.AreEqual(before - 3.6f, planet.GroundRadius(centre), 0.05f, "a second blast in the same place digs deeper");
            foreach (float3 direction in Check.Directions(BodyDescriptor.MaxCraters + 50)) planet.Dig(direction, 3f);
            Assert.AreEqual(BodyDescriptor.MaxCraters, planet.craters.Length, "the oldest are forgotten");
            Assert.AreEqual(before, planet.GroundRadius(centre), 0.05f);
        }

        [Test]
        public void OnlyGroundCanBeDug()
        {
            var system = SolarSystemDescriptor.Generate(1);
            foreach (var body in system.bodies.Where(body => body.kind != BodyKind.Rock))
            {
                body.Dig(new float3(0, 1, 0), 10f);
                body.Level(new float3(0, 1, 0), 10f);
                Assert.AreEqual(0, body.craters.Length, body.summary);
            }
        }

        // The first of many random directions where the ground is of a kind and at least this near to level.
        static float3 Find(BodyDescriptor body, string kind, float level = 0f) => Check.Directions(40000)
            .First(direction => Scan.Terrain(body, direction) == kind && math.dot(body.GroundNormal(direction), direction) >= level);

        static float3 Step(float3 from, float3 heading, float metres, BodyDescriptor body) => math.normalize(from + heading * (metres / body.radius));

        [Test]
        public void ABlastLeavesWhatTheGroundItHitWouldMakeOfIt()
        {
            var planet = Fresh;
            float3 meadow = World.Land(planet);
            Assert.AreEqual(Scar.Steam, planet.Blasted(World.Sea(planet)), "the sea boils");
            Assert.AreEqual(Scar.Char, planet.Blasted(meadow), "grass and the soil under it char");
            Assert.AreEqual(Scar.Char, planet.Blasted(Find(planet, "beach sand")));
            Assert.AreEqual(Scar.Lava, planet.Blasted(Find(planet, "cliff rock")), "bare rock melts");
            Assert.AreEqual(Scar.Lava, planet.Blasted(Find(planet, "mountain rock")));
            Assert.AreEqual(Scar.Melt, planet.Blasted(Find(planet, "snow")), "snow thaws");
            Assert.AreEqual(Scar.Char, World.Moon.Blasted(World.Land(World.Moon)), "dust chars");

            // what a second blast leaves goes by the ground the first one hit, however that has been dug about
            planet.Dig(meadow, 10f, 7f);
            float3 wall = Step(meadow, World.Tangent(meadow), 6f, planet);
            Assert.Less(math.dot(planet.GroundNormal(wall), wall), 0.9f, "a crater's walls are steep");
            Assert.AreEqual("meadow grass", Scan.Terrain(planet, wall, true));
            Assert.AreEqual(Scar.Char, planet.Blasted(wall));
            Assert.AreEqual(Scar.Char, planet.blasts[0].scar);
            Assert.AreEqual(7f, planet.blasts[0].born, "and it is known when");
            Assert.AreEqual(planet.blasts[0].crater, planet.craters[0]);

            // a hole dug below the waterline on a beach lets the sea in, and then it is the sea that is hit
            float3 beach = Find(planet, "beach sand");
            planet.Dig(beach, 10f);
            Assert.AreEqual(Scar.Steam, planet.Blasted(beach));
        }

        // What a blast has painted on the ground at a spot, for points this far apart, and when its molten rock was struck.
        static float4 Painted(BodyDescriptor body, float3 at, float spread, out float born)
        {
            float4 paint = 0f;
            born = TerrainNoise.Cold;
            foreach (var blast in body.blasts) TerrainNoise.Paint(at, blast, body.radius, spread, ref paint, ref born);
            return paint;
        }

        [Test]
        public void CharredGroundIsBlackToWellBeyondTheRim()
        {
            var planet = Fresh;
            float3 centre = World.Land(planet), along = World.Tangent(centre);
            planet.Dig(centre, 10f);
            Assert.AreEqual(1f, Painted(planet, centre, 0.5f, out _).x, "soot");
            Assert.AreEqual(1f, Painted(planet, Step(centre, along, 9f, planet), 0.5f, out _).x);
            Assert.That(Painted(planet, Step(centre, along, 14f, planet), 0.5f, out _).x, Is.InRange(0.2f, 0.8f), "thinning out beyond the rim");
            Assert.AreEqual(0f, Painted(planet, Step(centre, along, 19f, planet), 0.5f, out _).x);
            Assert.AreEqual(0f, Painted(planet, centre, 0.5f, out _).w, "and nothing molten");
            // From far off, where the points painted are further apart than the crater is wide, it is
            // a fainter smudge as wide as they are apart: no point misses it and none overstates it.
            float far = Painted(planet, Step(centre, along, 25f, planet), 80f, out _).x;
            Assert.That(far, Is.InRange(0.005f, 0.1f));
            Assert.Less(Painted(planet, centre, 80f, out _).x, 0.1f);
        }

        [Test]
        public void MoltenRockFillsTheBowlAndCreepsDownhill()
        {
            var planet = Fresh;
            // a rock slope, steep enough to run down
            float3 centre = Check.Directions(40000).First(direction => Scan.Terrain(planet, direction) == "mountain rock"
                && math.dot(planet.GroundNormal(direction), direction) < 0.92f);
            planet.Dig(centre, 8f, 100f);
            var blast = planet.blasts[0];
            Assert.AreEqual(Scar.Lava, blast.scar);
            float3 downhill = math.normalize(blast.downhill), below = Step(centre, downhill, 16f, planet), above = Step(centre, -downhill, 16f, planet);
            Assert.AreEqual(0f, math.dot(downhill, centre), 1e-3f, "downhill lies along the ground");
            Assert.Less(planet.GroundRadius(below, true), planet.GroundRadius(above, true) - 4f);

            float4 bowl = Painted(planet, centre, 0.5f, out float born);
            Assert.AreEqual(new float2(1f, 1f), bowl.yw, "the bowl is molten from the first");
            Assert.AreEqual(100f, born);
            Assert.AreEqual(0f, bowl.x, "not soot");
            float4 tongue = Painted(planet, below, 0.5f, out born), tip = Painted(planet, Step(centre, downhill, 22f, planet), 0.5f, out _);
            Assert.That(tongue.y, Is.InRange(0.25f, 0.9f), "it reaches downhill later");
            Assert.AreEqual(1f, tongue.w);
            Assert.AreEqual(100f, born);
            Assert.Less(tip.y, tongue.y, "and further down later still");
            Assert.AreEqual(0f, Painted(planet, above, 0.5f, out _).w, "but never uphill");
            Assert.AreEqual(0f, Painted(planet, Step(centre, downhill, 8f * 3.7f, planet), 0.5f, out _).w, "nor for ever");
            Assert.Greater(TerrainNoise.Reach(blast), 22f);

            // newer rock is the hot rock, but an older flow is not reheated by one that only brushes it
            planet.Dig(Step(centre, downhill, 16f + 8f * 1.25f, planet), 8f, 200f);
            Painted(planet, below, 0.5f, out born);
            Assert.AreEqual(100f, born);
            planet.Dig(below, 8f, 300f);
            Painted(planet, below, 0.5f, out born);
            Assert.AreEqual(300f, born);
        }

        [Test]
        public void ThawedIceLeavesALevelPoolNoHigherThanItsRim()
        {
            // a polar plain: the gentlest of the ice
            var ice = SolarSystemDescriptor.Generate(1).bodies.First(body => body.name == "Mars");
            float3 centre = Check.Directions(40000).Where(direction => Scan.Terrain(ice, direction) == "polar ice")
                .OrderByDescending(direction => math.dot(ice.GroundNormal(direction), direction)).First(), along = World.Tangent(centre);
            float before = ice.GroundRadius(centre);
            ice.Dig(centre, 10f);
            var blast = ice.blasts[0];
            Assert.AreEqual(Scar.Melt, blast.scar);
            float surface = ice.GroundRadius(centre);
            Assert.AreEqual(ice.radius + blast.level, surface, 1e-3f, "water stands in the bowl");
            Assert.That(before - surface, Is.InRange(1.79f, 4.3f), "which it does not fill");
            for (float angle = 0f; angle < 6f; angle += 0.7f)
            {
                float3 heading = math.rotate(quaternion.AxisAngle(centre, angle), along);
                Assert.AreEqual(surface, ice.GroundRadius(Step(centre, heading, 1.5f, ice)), 1e-3f, "its surface is level");
                // out from the middle nothing steps up or down: the water ends where the bowl is as high as it is
                for (float metres = 0f; metres < 14f; metres += 0.1f)
                    Assert.Less(math.abs(ice.GroundRadius(Step(centre, heading, metres, ice)) - ice.GroundRadius(Step(centre, heading, metres + 0.1f, ice))), 0.35f);
            }
            Assert.AreEqual(0.49f, Painted(ice, centre, 0.5f, out _).z, 1e-3f, "and all the bowl is wet");

            // The snow of the home world lies on peaks too steep to hold a pool. There the bowl is
            // left a bowl, wet, and what melted has run off downhill.
            var planet = Fresh;
            float3 peak = Find(planet, "snow");
            before = planet.GroundRadius(peak);
            planet.Dig(peak, 10f);
            float3 downhill = math.normalize(planet.blasts[0].downhill);
            Assert.AreEqual(Scar.Melt, planet.blasts[0].scar);
            Assert.AreEqual(before - 4.5f, planet.GroundRadius(peak), 0.05f);
            Assert.AreEqual(0.49f, Painted(planet, peak, 0.5f, out _).z, 1e-3f);
            Assert.Greater(Painted(planet, Step(peak, downhill, 20f, planet), 0.5f, out _).z, 0.2f);
            Assert.AreEqual(0f, Painted(planet, Step(peak, -downhill, 20f, planet), 0.5f, out _).z);
            Assert.AreEqual(0f, Painted(planet, peak, 0.5f, out _).w, "none of it is molten");
        }

        [Test]
        public void ALandingPadIsFlatAndLevelAndBlendsIntoTheGroundRoundIt()
        {
            var planet = Fresh;
            // on a hillside
            float3 centre = Check.Directions(40000).First(direction => Scan.Terrain(planet, direction) == "meadow grass"
                && math.dot(planet.GroundNormal(direction), direction) < 0.9f), along = World.Tangent(centre);
            float level = planet.GroundRadius(centre), apron = TerrainNoise.Apron(12f);
            planet.Level(centre, 12f);
            Assert.AreEqual(Scar.Pad, planet.blasts[0].scar);
            for (float angle = 0f; angle < 6f; angle += 0.7f)
            {
                float3 heading = math.rotate(quaternion.AxisAngle(centre, angle), along);
                for (float metres = 0f; metres <= 12f; metres += 3f)
                {
                    float3 spot = Step(centre, heading, metres, planet);
                    Assert.AreEqual(level, planet.GroundRadius(spot), 2e-3f, "flat, at the height its middle was");
                    if (metres < 11f) Assert.Greater(math.dot(planet.GroundNormal(spot), spot), 0.99999f, "and level");
                }
                float3 beyond = Step(centre, heading, 12f + apron + 0.5f, planet);
                Assert.AreEqual(planet.GroundRadius(beyond, true), planet.GroundRadius(beyond), "beyond its apron the ground is as it was");
                // across the apron it neither clips into the hillside nor floats off it: no step, and never past both
                for (float metres = 12f; metres < 12f + apron; metres += 0.1f)
                {
                    float3 spot = Step(centre, heading, metres, planet), next = Step(centre, heading, metres + 0.1f, planet);
                    float here = planet.GroundRadius(spot), ground = planet.GroundRadius(spot, true);
                    Assert.Less(math.abs(here - planet.GroundRadius(next)), 0.5f);
                    Assert.That(here, Is.InRange(math.min(level, ground) - 1e-3f, math.max(level, ground) + 1e-3f));
                }
            }
        }

        [Test]
        public void PadsAndCratersSurviveEachOther()
        {
            var planet = Fresh;
            float3 centre = World.Land(planet), along = World.Tangent(centre);
            // a pad on the floor of a crater lies at the floor's height, and the crater still stands round it
            planet.Dig(centre, 10f);
            var dug = Fresh;
            dug.Dig(centre, 10f);
            float floor = planet.GroundRadius(centre);
            planet.Level(centre, 3f);
            Assert.AreEqual(floor, planet.GroundRadius(Step(centre, along, 3f, planet)), 2e-3f);
            foreach (float metres in new[] { 8f, 10f, 12f })
                Assert.AreEqual(dug.GroundRadius(Step(centre, along, metres, planet)), planet.GroundRadius(Step(centre, along, metres, planet)), "the walls and the rim are untouched");

            // a blast on the pad leaves it level, though it digs what lies beyond
            float3 outside = Step(centre, along, 12f, planet);
            float before = planet.GroundRadius(outside);
            planet.Dig(Step(centre, along, 6f, planet), 10f);
            Assert.AreEqual(floor, planet.GroundRadius(centre), 2e-3f);
            Assert.AreEqual(floor, planet.GroundRadius(Step(centre, along, 3f, planet)), 2e-3f);
            Assert.Less(planet.GroundRadius(outside), before - 1.5f);

            // craters are forgotten in the end, pads are not
            foreach (float3 direction in Check.Directions(BodyDescriptor.MaxCraters + 50)) planet.Dig(direction, 3f);
            Assert.AreEqual(BodyDescriptor.MaxCraters, planet.craters.Length);
            Assert.AreEqual(Scar.Pad, planet.blasts[0].scar);
            Assert.AreEqual(floor, planet.GroundRadius(centre), 2e-3f);

            // levelling ground that is level already changes nothing, and only so many pads are kept
            planet.Level(Step(centre, along, 1f, planet), 2f);
            Assert.AreEqual(1, planet.blasts.Count(blast => blast.scar == Scar.Pad));
            foreach (float3 direction in Check.Directions(BodyDescriptor.MaxPads + 5, 9)) planet.Level(direction, 8f);
            Assert.AreEqual(BodyDescriptor.MaxPads, planet.blasts.Count(blast => blast.scar == Scar.Pad));
            Assert.AreEqual(BodyDescriptor.MaxCraters, planet.craters.Length);
            Assert.AreNotEqual(centre, planet.blasts.First(blast => blast.scar == Scar.Pad).crater.xyz, "the oldest has gone");
        }

        [Test]
        public void APadNeverLiesBelowTheSea()
        {
            var planet = Fresh;
            float3 sea = World.Sea(planet);
            planet.Level(sea, 10f);
            Assert.AreEqual(planet.SeaRadius + 1f, planet.GroundRadius(sea), 1e-3f, "an island just proud of the water");
            Assert.AreEqual(planet.SeaRadius + 1f, planet.GroundRadius(Step(sea, World.Tangent(sea), 9f, planet)), 1e-3f);
            // a dry world has no sea to mind: the floor of a crater far below its mean radius will do
            var moon = SolarSystemDescriptor.Generate(1).bodies[SolarSystemDescriptor.Moon];
            float3 low = Check.Directions(3000).OrderBy(direction => moon.GroundRadius(direction)).First();
            float floor = moon.GroundRadius(low);
            Assert.Less(floor, moon.radius - 5f);
            moon.Level(low, 6f);
            Assert.AreEqual(floor, moon.GroundRadius(Step(low, World.Tangent(low), 5f, moon)), 2e-3f);
        }
    }

    public class WeaponTests
    {
        static readonly BodyDescriptor Body = World.Planet;
        static readonly float3 Up = World.Land(Body), Ahead = World.Tangent(World.Land(Body)), From = World.Land(Body) * (World.Planet.radius + 600f);

        // Fires a weapon straight ahead at one foe and steps until every bolt is spent. Returns the foe's health afterwards.
        static float Shoot(Weapon weapon, float3 foeAt, out int bolts)
        {
            var foes = new List<Foe> { Combat.Spawn(FoeKind.Raider, foeAt, -Ahead, 1, 3) };
            var flying = new List<Bolt>();
            var rnd = new Random(7);
            Combat.Fire(weapon, From, Ahead, float3.zero, ref rnd, flying);
            bolts = flying.Count;
            for (int i = 0; i < 600 && flying.Count > 0; i++) Combat.Step(flying, foes, Body, From, float3.zero, 1f, World.Dt, null);
            return foes[0].health;
        }

        [Test]
        public void TheShipAndSuitEachHaveFourDifferentWeapons()
        {
            foreach (var rack in new[] { Combat.Guns, Combat.Arms })
            {
                Assert.AreEqual(4, rack.Length);
                Assert.AreEqual(rack.Length, rack.Select(weapon => weapon.name).Distinct().Count());
                Assert.AreEqual(rack.Length, rack.Select(weapon => weapon.voice).Distinct().Count(), "each sounds different");
                // quick and light, or slow and heavy: no weapon is simply the best
                Assert.AreNotEqual(System.Array.IndexOf(rack, rack.OrderBy(weapon => weapon.period).First()), System.Array.IndexOf(rack, rack.OrderByDescending(weapon => weapon.damage * weapon.pellets).First()));
            }
            Assert.AreEqual(6, Combat.Guns.Concat(Combat.Arms).Select(weapon => weapon.voice).Distinct().Count(), "a missile sounds like a missile, from the ship or the shoulder");
        }

        [Test]
        public void SuitWeaponsOfferFourDifferentWaysToFight()
        {
            Assert.IsTrue(Combat.Arms[0].melee);
            Assert.IsTrue(Combat.Arms[1].beam);
            Assert.Greater(Combat.Arms[2].homing, 0);
            Assert.IsTrue(Combat.Arms[3].rail);
            Assert.Greater(Combat.Arms[3].period, Combat.Arms[2].period);
            Assert.IsTrue(Combat.Arms.All(weapon => weapon.clip == 0));
            Assert.IsTrue(Combat.Arms.Skip(1).All(weapon => weapon.energy > 0));
        }

        [Test]
        public void FistsCanBreakGroundAndMissilesReachFlyingTargets()
        {
            Assert.GreaterOrEqual(Combat.Arms[0].blast, 1.2f);
            Assert.Less(Combat.Arms[0].speed * Combat.Arms[0].life, 5f);
            Assert.Greater(Combat.Arms[2].speed * Combat.Arms[2].life, 1500);
        }

        [Test]
        public void OnlyANuclearBlastMarksTheCitadel()
        {
            var station = World.Solar.bodies.Single(body => body.kind == BodyKind.Station);
            float Left(Weapon weapon)
            {
                var foes = new List<Foe> { Combat.Spawn(FoeKind.Base, float3.zero, new float3(0, 0, 1), 1, 5, station.radius * 0.6f) };
                var flying = new List<Bolt>();
                var rnd = new Random(7);
                float3 from = new float3(0f, 0f, -900f);
                Combat.Fire(weapon, from, new float3(0, 0, 1), float3.zero, ref rnd, flying);
                for (int i = 0; i < 2000 && flying.Count > 0; i++) Combat.Step(flying, foes, station, from, float3.zero, 1f, World.Dt, null);
                return foes[0].health;
            }
            float full = Combat.Health(FoeKind.Base, 1);
            foreach (var weapon in Combat.Guns.Take(3)) Assert.AreEqual(full, Left(weapon), $"{weapon.name} does nothing to it");
            Assert.Less(Left(Combat.Guns[3]), full - 500f, "a torpedo takes a great piece out of it");
            Assert.GreaterOrEqual(Combat.Guns[3].blast, Combat.Nuclear);
            Assert.Less(full / Combat.Guns[3].damage, 3f, "and two finish it");
        }

        [Test]
        public void MissilesChaseWhatCannonsWouldMiss()
        {
            float3 off = From + Ahead * 900f + math.cross(Up, Ahead) * 250f;
            float full = Combat.Health(FoeKind.Raider, 1);
            Assert.AreEqual(full, Shoot(Combat.Guns[0], off, out _), "a cannon shot flies straight past");
            Assert.Less(Shoot(Combat.Guns[1], off, out _), full, "a missile turns and finds it");
        }

        [Test]
        public void ABigBlastCatchesWhatIsNearby()
        {
            var foes = new List<Foe>
            {
                Combat.Spawn(FoeKind.Drone, From + Ahead * 300f, -Ahead, 1, 3),
                Combat.Spawn(FoeKind.Drone, From + Ahead * 300f + Up * 5f, -Ahead, 1, 5),
                Combat.Spawn(FoeKind.Drone, From + Ahead * 300f + Up * 60f, -Ahead, 1, 7),
            };
            var flying = new List<Bolt>();
            var rnd = new Random(7);
            Combat.Fire(Combat.Guns[1], From, Ahead, float3.zero, ref rnd, flying);
            flying[0] = new Bolt { position = flying[0].position, velocity = flying[0].velocity, damage = flying[0].damage, life = flying[0].life, blast = flying[0].blast };
            var struck = new List<Strike>();
            for (int i = 0; i < 300 && flying.Count > 0; i++) Combat.Step(flying, foes, Body, From, float3.zero, 1f, World.Dt, struck);
            float full = Combat.Health(FoeKind.Drone, 1);
            Assert.Less(foes[0].health, 0f, "the one it hit");
            Assert.That(foes[1].health, Is.InRange(full - Combat.Guns[1].damage, full - 1f), "the one beside it");
            Assert.AreEqual(full, foes[2].health, "not the one well clear");
            Assert.AreEqual(Combat.Guns[1].blast, struck.Single().blast);
        }

        [Test]
        public void TheRaidersCitadelShootsAtWhateverComesNear()
        {
            var station = World.Solar.bodies.Single(body => body.kind == BodyKind.Station);
            var foe = Combat.Spawn(FoeKind.Base, float3.zero, new float3(0, 0, 1), 1, 5, station.radius * 0.6f);
            Assert.Greater(foe.health, 10f * Combat.Health(FoeKind.Raider, 1));
            var bolts = new List<Bolt>();
            float3 near = new float3(900, 0, 0), far = new float3(3000, 0, 0);
            float hurt = 0f;
            for (float t = 0; t < 20f; t += World.Dt)
            {
                Assert.IsFalse(Combat.Hunt(ref foe, station, far, float3.zero, 1, World.Dt, out _), "it leaves alone what keeps its distance");
            }
            for (float t = 0; t < 20f; t += World.Dt)
            {
                if (Combat.Hunt(ref foe, station, near, float3.zero, 1, World.Dt, out var shot))
                {
                    Assert.Greater(math.length(shot.position), station.GroundRadius(new float3(1, 0, 0)), "its guns are on its hull");
                    bolts.Add(shot);
                }
                hurt += Combat.Step(bolts, new List<Foe>(), station, near, float3.zero, Combat.ShipRadius, World.Dt, null);
            }
            Check.Near(float3.zero, foe.position, 1e-6f);
            Assert.Greater(hurt, 0f);
        }
    }
}
