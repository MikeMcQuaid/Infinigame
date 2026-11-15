using System.Collections.Generic;
using System.Linq;
using Infinigame.Core;
using NUnit.Framework;
using Unity.Mathematics;

namespace Infinigame.Tests
{
    public class SystemTests
    {
        static IEnumerable<BodyDescriptor> Planets(SolarSystemDescriptor system) => system.Planets.Select(planet => system.bodies[planet]);

        [Test]
        public void IsASmallCopyOfOurOwn()
        {
            var system = SolarSystemDescriptor.Generate(1);
            var planets = Planets(system).ToArray();
            Assert.AreEqual(BodyKind.Sun, system.bodies[0].kind);
            Assert.AreEqual(8, planets.Length);
            CollectionAssert.AreEqual(Enumerable.Repeat(BodyKind.Rock, 4).Concat(Enumerable.Repeat(BodyKind.Gas, 4)), planets.Select(planet => planet.kind),
                "four rocky worlds, then four giants");
            Assert.AreEqual(2, planets.Count(planet => planet.ringOuter > 0f), "two of the giants have rings");
            Assert.AreEqual(1, system.bodies.Count(body => body.SeaRadius > 0f), "one world has seas");
            Assert.Greater(system.bodies[SolarSystemDescriptor.Home].SeaRadius, 0f);
            Assert.AreEqual(SolarSystemDescriptor.Home, system.bodies[SolarSystemDescriptor.Moon].orbit.parent);
        }

        [Test]
        public void NoTwoSpheresOfInfluenceEverTouch()
        {
            for (uint seed = 0; seed < 200; seed++)
            {
                var system = SolarSystemDescriptor.Generate(seed);
                double reach = system.bodies[0].radius * 2;
                foreach (var planet in Planets(system))
                {
                    // the tilt of an orbit moves a world up and down, never nearer or further than its ellipse does
                    Assert.Greater(planet.orbit.Nearest - planet.soiRadius, reach, $"seed {seed} {planet.summary}");
                    reach = planet.orbit.Furthest + planet.soiRadius;
                }
            }
        }

        [Test]
        public void YearsGetLongerFurtherOut()
        {
            var planets = Planets(SolarSystemDescriptor.Generate(3)).ToArray();
            for (int i = 1; i < planets.Length; i++) Assert.Greater(planets[i].orbit.period, planets[i - 1].orbit.period);
        }

        [Test]
        public void RingsLieOutsideTheirPlanetAndInsideItsInfluence()
        {
            for (uint seed = 0; seed < 50; seed++)
                foreach (var body in SolarSystemDescriptor.Generate(seed).bodies.Where(body => body.ringOuter > 0f && body.kind != BodyKind.Sun))
                {
                    Assert.AreEqual(BodyKind.Gas, body.kind);
                    Assert.Greater(body.ringInner, body.radius * 1.1f);
                    Assert.Greater(body.ringOuter, body.ringInner);
                    Assert.Less(body.ringOuter, body.soiRadius * 0.9f);
                }
        }

        [Test]
        public void TheSunIsTheBiggestAndGiantsOutsizeTheHomeWorld()
        {
            for (uint seed = 0; seed < 50; seed++)
            {
                var system = SolarSystemDescriptor.Generate(seed);
                foreach (var planet in Planets(system))
                {
                    Assert.Less(planet.radius, system.bodies[0].radius);
                    if (planet.kind == BodyKind.Gas) Assert.Greater(planet.radius, system.bodies[SolarSystemDescriptor.Home].radius);
                }
            }
        }

        [Test]
        public void EveryBodyHasItsOwnName()
        {
            for (uint seed = 0; seed < 50; seed++)
            {
                var system = SolarSystemDescriptor.Generate(seed);
                var names = system.bodies.Select(body => body.name).ToArray();
                CollectionAssert.AllItemsAreUnique(names);
                foreach (string name in names) Assert.That(name.Length, Is.InRange(2, 12));
                // the worlds are named for the ones they are copies of, and only the raiders' base has a name of its own
                Assert.AreEqual("Sol", names[0]);
                CollectionAssert.AreEqual(new[] { "Mercury", "Venus", "Earth", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune" }, system.Planets.Select(planet => names[planet]));
                Assert.AreEqual("Luna", names[SolarSystemDescriptor.Moon]);
                CollectionAssert.IsSubsetOf(new[] { "Phobos", "Io", "Europa", "Titan", "Miranda", "Triton" }, names);
            }
        }

        [Test]
        public void TheLandingSiteAndTheCampBesideItAreOutInTheOpen()
        {
            for (uint seed = 1; seed <= 60; seed++)
            {
                var home = SolarSystemDescriptor.Generate(seed).bodies[SolarSystemDescriptor.Home];
                float3 landing = home.FindLanding(seed + 1), camp = home.FindCamp(landing);
                // trees grow where this noise is above -0.15 (see Scatter), and their boughs reach a few paces further
                foreach (float3 spot in new[] { landing, camp })
                    Assert.Less(noise.snoise(spot * 9f + home.terrain.offset), -0.25f, $"seed {seed}: nobody lands or camps under the trees");
                Assert.That(math.distance(landing, camp) * home.radius, Is.InRange(20f, 150f), "the camp is a short walk from the ship");
                Assert.Greater(home.GroundRadius(camp) - home.radius, 2.5f, "and above the tide");
            }
        }

        [Test]
        public void GiantsHaveNoGroundOnlyDeeperAndHotterCloud()
        {
            var system = SolarSystemDescriptor.Generate(1);
            int index = system.Planets.First(planet => system.bodies[planet].kind == BodyKind.Gas);
            var giant = system.bodies[index];
            float3 direction = math.normalize(new float3(1, 2, 3));
            Assert.AreEqual(giant.radius, giant.GroundRadius(direction), 1e-3f, "what is seen is the cloud tops");
            Assert.Less(Ship.Floor(giant, direction), 0.8f * giant.radius, "but a ship can sink far into them");

            var ship = Ship.Spawn(giant, direction, World.Tangent(direction));
            ship.landed = false;
            ship.position = direction * (giant.radius + 40f);
            float tops = Climate.At(system, index, ship.position, 0), last = tops;
            for (int second = 0; second < 30; second++)
            {
                for (int i = 0; i < 60; i++) Ship.Step(ref ship, new ShipInput { thrust = new float3(0, -1, 0) }, giant, true, World.Dt);
                float now = Climate.At(system, index, ship.position, 0);
                Assert.GreaterOrEqual(now, last - 0.01f, "it only gets hotter on the way down");
                last = now;
            }
            Assert.IsFalse(ship.landed);
            Assert.Less(math.length(ship.position), giant.radius - 500f, "it sank into the giant");
            Assert.AreEqual(giant.heat, tops, 5f, "bitterly cold at the cloud tops");
            Assert.Greater(last, Climate.HullLimit * 2f, "and no hull lasts down here");
        }
    }

    public class VolcanoTests
    {
        static BodyDescriptor Named(SolarSystemDescriptor system, string name) => system.bodies.First(body => body.name == name);
        static readonly string[] Worlds = { "Mars", "Io", "Venus" };

        // Directions in a ring round a volcano, this far out as a share of its radius.
        static IEnumerable<float3> Ring(BodyDescriptor body, Volcano volcano, float across)
        {
            for (float angle = 0f; angle < 6.2f; angle += 0.4f)
                yield return math.normalize(volcano.direction
                    + math.rotate(quaternion.AxisAngle(volcano.direction, angle), World.Tangent(volcano.direction)) * (across * volcano.radius / body.radius));
        }

        [Test]
        public void TheWorldsSaidToHaveVolcanoesHaveThemAndNoOthersDo()
        {
            for (uint seed = 0; seed < 20; seed++)
            {
                var system = SolarSystemDescriptor.Generate(seed);
                BodyDescriptor mars = Named(system, "Mars"), io = Named(system, "Io"), venus = Named(system, "Venus");
                CollectionAssert.AreEquivalent(new[] { mars, io, venus }, system.bodies.Where(body => body.volcanoes.Length > 0));
                Assert.AreEqual(4, mars.volcanoes.Length, "a few vast ones");
                Assert.Greater(mars.volcanoes.Max(volcano => volcano.radius), 0.4f * mars.radius);
                Assert.Greater(mars.volcanoes.Max(volcano => volcano.height), 0.14f * mars.radius, "which stand out from orbit");
                Assert.IsTrue(mars.volcanoes.All(volcano => volcano.lava == 0f), "long dead");
                Assert.GreaterOrEqual(io.volcanoes.Length, 12, "many small ones");
                Assert.Less(io.volcanoes.Max(volcano => volcano.radius), 0.21f * io.radius);
                Assert.That(io.volcanoes.Count(volcano => volcano.lava > 0f), Is.InRange(4, io.volcanoes.Length - 1), "some of them alight");
                Assert.GreaterOrEqual(venus.volcanoes.Length, 5, "broad low ones");
                Assert.Less(venus.volcanoes.Max(volcano => volcano.height / volcano.radius), 0.25f);
                foreach (var body in new[] { mars, io, venus })
                foreach (var volcano in body.volcanoes)
                {
                    Assert.AreEqual(1f, math.length(volcano.direction), 1e-4f);
                    Assert.LessOrEqual(volcano.height, 0.9f * body.terrain.volcanoHeight + 1e-3f, body.name);
                    foreach (var other in body.volcanoes.Where(other => !other.Equals(volcano)))
                        Assert.Greater(math.distance(volcano.direction, other.direction) * body.radius, 0.9f * (volcano.radius + other.radius), "they keep clear of each other");
                }
            }
        }

        [Test]
        public void EveryVolcanoIsAConeWithACalderaSunkInItsSummit()
        {
            for (uint seed = 1; seed <= 6; seed++)
            foreach (var body in Worlds.Select(name => Named(SolarSystemDescriptor.Generate(seed), name)))
            foreach (var volcano in body.volcanoes)
            {
                float floor = body.GroundRadius(volcano.direction) - body.radius, rim = Ring(body, volcano, volcano.caldera).Min(direction => body.GroundRadius(direction)) - body.radius;
                float foot = Ring(body, volcano, 1.3f).Average(direction => body.GroundRadius(direction)) - body.radius;
                Assert.AreEqual(0.78f * volcano.height, floor, 0.02f, "the floor of its caldera, with the land it stands on smoothed away");
                Assert.Greater(rim, floor + 0.05f * volcano.height, $"{body.name}: the rim stands over the floor all the way round");
                foreach (float3 direction in Ring(body, volcano, 0.3f * volcano.caldera))
                    Assert.AreEqual(floor, body.GroundRadius(direction) - body.radius, 0.02f, "the floor is flat");
                Assert.Greater(rim, foot + 0.3f * volcano.height, $"{body.name}: and the cone over the country round it");
                // the flanks fall away from the rim, as far as where the land round the foot begins to show through
                foreach (float3 direction in Ring(body, volcano, 1.25f * volcano.caldera))
                {
                    float3 outward = math.normalize(direction - volcano.direction * math.dot(direction, volcano.direction));
                    float last = float.MaxValue;
                    for (float across = 1.25f * volcano.caldera; across < 0.62f; across += 0.1f)
                    {
                        float here = body.GroundRadius(math.normalize(volcano.direction + outward * (across * volcano.radius / body.radius)));
                        Assert.Less(here, last);
                        last = here;
                    }
                }
            }
        }

        [Test]
        public void VolcanoWorldsKeepWithinTheirBoundsAndHaveNoSteps()
        {
            foreach (var body in Worlds.Select(name => Named(World.Solar, name)))
            foreach (float3 direction in Check.Directions(6000))
            {
                float height = body.GroundRadius(direction) - body.radius;
                Assert.That(height, Is.InRange(body.terrain.MinHeight, body.terrain.MaxHeight), body.name);
                float3 nearby = math.normalize(direction + math.cross(direction, new float3(0.3f, 0.5f, 0.8f)) * 1e-4f);
                Assert.Less(math.abs(height - (body.GroundRadius(nearby) - body.radius)), 1f, body.name);
            }
        }

        [Test]
        public void TheirGroundIsKnownForWhatItIs()
        {
            foreach (var body in Worlds.Select(name => Named(World.Solar, name)))
            foreach (var volcano in body.volcanoes)
            {
                Assert.AreEqual(volcano.lava > 0f ? "lava lake" : "caldera floor", Scan.Terrain(body, volcano.direction), body.name);
                foreach (float3 flank in Ring(body, volcano, 0.6f)) Assert.AreEqual("volcano flank", Scan.Terrain(body, flank), body.name);
                Assert.AreEqual(Scar.Lava, body.Blasted(volcano.direction));
                Assert.AreEqual(Scar.Lava, body.Blasted(Ring(body, volcano, 0.6f).First()), "its rock melts again");
                // what the patches are painted with: old flows all over the summit, and a lake that glows only where there is one
                TerrainNoise.Cone(volcano.direction, volcano, body.radius, out float across);
                float2 flows = TerrainNoise.Flows(volcano.direction, volcano, across);
                Assert.AreEqual(volcano.lava, flows.x);
                Assert.Greater(flows.y, 0.39f);
                Assert.AreEqual(0f, Ring(body, volcano, 0.8f).Min(direction => TerrainNoise.Cone(direction, volcano, body.radius, out across).y > 0f ? TerrainNoise.Flows(direction, volcano, across).y : 0f),
                    "the flows run out in fingers, and not all of them reach the foot");
            }
            Assert.IsTrue(Check.Directions(3000).Any(direction => Scan.Terrain(Named(World.Solar, "Mars"), direction) == "polar ice"), "the desert keeps its ice caps");
        }

        [Test]
        public void TheyAreGroundToWalkAndLandOn()
        {
            foreach (var body in Worlds.Select(name => Named(World.Solar, name)))
            {
                // a caldera floor is level enough to set a ship down on, and all of a shield's flank can be walked
                foreach (var volcano in body.volcanoes)
                {
                    Assert.Greater(math.dot(body.GroundNormal(volcano.direction), volcano.direction), 0.98f, body.name);
                    if (body.name != "Io")
                        foreach (float3 flank in Ring(body, volcano, 0.6f))
                            Assert.Greater(math.dot(body.GroundNormal(flank), flank), 0.85f, body.name);
                }
                Assert.AreNotEqual(new float3(0, 1, 0), body.FindLanding(3), "and there is still open ground between them");
                // walking straight over the biggest one, a walker is on the ground all the way and gets there
                var big = body.volcanoes.OrderByDescending(volcano => volcano.radius).First();
                float3 start = Ring(body, big, 1.2f).First(), heading = math.normalize(Walker.Project(big.direction - start, start));
                var walker = Walker.Spawn(body, start, heading);
                float nearest = float.MaxValue;
                for (float t = 0f; t < 2.4f * big.radius / Tuning.WalkSpeed * 2f && nearest > 0.3f * big.caldera * big.radius; t += World.Dt)
                {
                    Walker.Step(ref walker, new WalkerInput { move = new float2(0f, 1f) }, body, World.Dt);
                    nearest = math.min(nearest, math.distance(math.normalize(walker.position), big.direction) * body.radius);
                }
                Assert.Less(nearest, 0.3f * big.caldera * big.radius + 0.5f, $"{body.name}: up the flank, over the rim and down onto the floor");
            }
        }

        [Test]
        public void TheSameSeedDealsTheSameVolcanoesAndTheHomeWorldIsLeftAsItWas()
        {
            var a = SolarSystemDescriptor.Generate(7);
            var b = SolarSystemDescriptor.Generate(7);
            CollectionAssert.AreEqual(Named(a, "Io").volcanoes, Named(b, "Io").volcanoes);
            CollectionAssert.AreNotEqual(Named(a, "Io").volcanoes.Select(volcano => volcano.direction), Named(SolarSystemDescriptor.Generate(8), "Io").volcanoes.Select(volcano => volcano.direction));
            var home = a.bodies[SolarSystemDescriptor.Home];
            foreach (float3 direction in Check.Directions(200))
                Assert.AreEqual(home.radius + TerrainNoise.Height(direction, home.terrain), home.GroundRadius(direction));
        }
    }

    public class WeatherTests
    {
        static readonly SolarSystemDescriptor Solar = SolarSystemDescriptor.Generate(1);
        static BodyDescriptor Planet => Solar.bodies[SolarSystemDescriptor.Home];

        [Test]
        public void ItPoursAtTheHeartOfAStorm()
        {
            for (int storm = 0; storm < Weather.Storms; storm++)
            {
                float4 at = Weather.Storm(Planet, storm, 500);
                Assert.AreEqual(1f, math.length(at.xyz), 1e-4f);
                Assert.AreEqual(1f, Weather.Rain(Planet, at.xyz, 500), 1e-4f);
            }
        }

        [Test]
        public void MostOfTheWorldIsDryAtAnyMoment()
        {
            foreach (double time in new[] { 0.0, 777, 5000 })
            {
                int wet = Check.Directions(2000).Count(direction => Weather.Rain(Planet, direction, time) > 0f);
                Assert.That(wet, Is.InRange(10, 600), $"time {time}");
            }
        }

        [Test]
        public void StormsWander() =>
            Assert.Less(math.dot(Weather.Storm(Planet, 0, 0).xyz, Weather.Storm(Planet, 0, 300).xyz), 0.99f);

        [Test]
        public void ItNeverRainsOnAWorldWithoutSeas()
        {
            foreach (var body in Solar.bodies.Where(body => body.SeaRadius == 0f))
                foreach (float3 direction in Check.Directions(50))
                    Assert.AreEqual(0f, Weather.Rain(body, direction, 123));
        }
    }

    public class WildlifeTests
    {
        static readonly SolarSystemDescriptor Solar = SolarSystemDescriptor.Generate(1);
        static BodyDescriptor Planet => Solar.bodies[SolarSystemDescriptor.Home];

        static List<Troop> Troops(BodyDescriptor body, Creature kind)
        {
            var troops = new List<Troop>();
            int depth = QuadtreeLod.DepthFor(body.radius, Wildlife.CellSize(kind));
            for (int face = 0; face < CubeSphere.Faces; face++)
            for (int y = 0; y < 1 << depth; y++)
            for (int x = 0; x < 1 << depth; x++)
                if (Wildlife.Find(body, kind, new NodeId(face, depth, x, y), out var troop))
                    troops.Add(troop);
            return troops;
        }

        [Test]
        public void TheHomeWorldIsAliveAndTheOthersAreNot()
        {
            foreach (Creature kind in System.Enum.GetValues(typeof(Creature)))
            {
                Assert.AreEqual(kind == Creature.Grazer || kind == Creature.Bird || kind == Creature.Fish, Troops(Planet, kind).Count > 10, $"{kind}");
                Assert.AreEqual(0, Troops(Solar.bodies[SolarSystemDescriptor.Moon], kind).Count);
            }
        }

        [Test]
        public void StrangerThingsLiveOnStrangerWorlds()
        {
            var giant = Solar.bodies.First(body => body.kind == BodyKind.Gas);
            var desert = Solar.bodies.First(body => body.summary.Contains("desert"));
            var drifters = Troops(giant, Creature.Drifter);
            Assert.Greater(drifters.Count, 10, "sky-life in a giant's cloud tops");
            foreach (var troop in drifters.Take(20))
            for (int member = 0; member < troop.count; member++)
            {
                Wildlife.Member(giant, troop, member, 77, out float3 position, out float3 forward);
                Assert.That(math.length(position) - giant.radius, Is.InRange(-40f, 700f), "they hang about the cloud tops");
                Assert.AreEqual(1f, math.length(forward), 1e-3f);
            }
            var striders = Troops(desert, Creature.Strider);
            Assert.Greater(striders.Count, 5, "walkers on a cold desert");
            foreach (var troop in striders)
            for (int member = 0; member < troop.count; member++)
            {
                Wildlife.Member(desert, troop, member, 77, out float3 position, out _);
                Assert.AreEqual(desert.GroundRadius(math.normalize(position)), math.length(position), 0.01f, "they walk the ground");
            }
            Assert.AreEqual(0, Troops(Planet, Creature.Drifter).Count + Troops(Planet, Creature.Strider).Count, "neither lives on the home world");
        }

        [Test]
        public void TheSameTroopLivesInTheSameCellEveryTime()
        {
            var a = Troops(Planet, Creature.Grazer);
            var b = Troops(Planet, Creature.Grazer);
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].centre, b[i].centre);
                Assert.AreEqual(a[i].seed, b[i].seed);
            }
        }

        [Test]
        public void HerdsWalkDryGroundBirdsFlyAndFishStayUnderWater()
        {
            foreach (Creature kind in new[] { Creature.Grazer, Creature.Bird, Creature.Fish })
            foreach (var troop in Troops(Planet, kind))
            foreach (double time in new[] { 0.0, 41, 900 })
            for (int member = 0; member < troop.count; member++)
            {
                Wildlife.Member(Planet, troop, member, time, out float3 position, out float3 forward);
                float3 up = math.normalize(position);
                float distance = math.length(position), ground = Planet.GroundRadius(up);
                Assert.AreEqual(1f, math.length(forward), 1e-3f);
                Assert.AreEqual(0f, math.dot(forward, up), 1e-3f, "everyone faces along the ground");
                Assert.Less(math.distance(position, troop.centre * distance), 80f, "nobody strays from the troop");
                if (kind == Creature.Grazer)
                {
                    Assert.AreEqual(ground, distance, 0.01f);
                    Assert.Greater(distance, Planet.SeaRadius + 1f, "herds keep out of the sea");
                    Assert.Less(math.abs(distance - troop.level), 7f, "and off the steep ground beside their pasture");
                }
                else if (kind == Creature.Bird) Assert.Greater(distance, math.max(ground, Planet.SeaRadius) + 9f);
                else
                {
                    Assert.Less(distance, Planet.SeaRadius - 0.4f);
                    Assert.Greater(distance, ground);
                }
            }
        }

        [Test]
        public void AnimalsMoveAtAnAnimalsPace()
        {
            foreach (Creature kind in new[] { Creature.Grazer, Creature.Bird, Creature.Fish })
            {
                var troop = Troops(Planet, kind)[0];
                float limit = kind == Creature.Bird ? 14f : kind == Creature.Fish ? 2.5f : 1.2f, fastest = 0f;
                for (int member = 0; member < troop.count; member++)
                {
                    Wildlife.Member(Planet, troop, member, 100, out float3 before, out _);
                    Wildlife.Member(Planet, troop, member, 100.5, out float3 after, out _);
                    fastest = math.max(fastest, math.distance(before, after) / 0.5f);
                }
                Assert.Less(fastest, limit, $"{kind}");
                if (kind != Creature.Grazer) Assert.Greater(fastest, 0.3f, $"{kind} keep moving");
            }
        }
    }

    public class ScanTests
    {
        static readonly SolarSystemDescriptor Solar = SolarSystemDescriptor.Generate(1);
        static BodyDescriptor Planet => Solar.bodies[SolarSystemDescriptor.Home];
        static readonly float3 Eye = new float3(0, 1000, 0), Ahead = new float3(0, 0, 1);

        static Scannable Thing(string name, float3 foot, float height = 0f, float girth = 1f) => new Scannable { name = name, foot = foot, height = height, girth = girth };

        [Test]
        public void PicksTheNearestThingOnTheRay()
        {
            var things = new[] { Thing("far", Eye + Ahead * 40f), Thing("near", Eye + Ahead * 12f), Thing("aside", Eye + new float3(6, 0, 5)), Thing("behind", Eye - Ahead * 3f) };
            Assert.AreEqual(1, Scan.Pick(Eye, Ahead, things));
        }

        [Test]
        public void MissesWhatIsOffTheRayOrOutOfRange()
        {
            Assert.AreEqual(-1, Scan.Pick(Eye, Ahead, new[] { Thing("aside", Eye + new float3(3, 0, 10)), Thing("far", Eye + Ahead * (Scan.Range + 5f)) }));
            Assert.AreEqual(-1, Scan.Pick(Eye, Ahead, new Scannable[0]));
        }

        [Test]
        public void TallThingsCanBePickedAnywhereUpTheirHeight()
        {
            // a tree whose foot is ten metres below the line of sight
            var tree = Thing("tree", Eye + Ahead * 20f - new float3(0, 10, 0), 12f, 1.5f);
            Assert.AreEqual(0, Scan.Pick(Eye, Ahead, new[] { tree }));
            tree.height = 4f;
            Assert.AreEqual(-1, Scan.Pick(Eye, Ahead, new[] { tree }));
        }

        [Test]
        public void FindsTheGroundAtYourFeetButNotTheSky()
        {
            float3 up = World.Land(Planet), eye = up * (Planet.GroundRadius(up) + 1.7f);
            Assert.IsTrue(Scan.Ground(Planet, eye, math.normalize(World.Tangent(up) - up), out float3 hit));
            Assert.Less(math.distance(hit, eye), 8f);
            Assert.IsFalse(Scan.Ground(Planet, eye, up, out _));
        }

        [Test]
        public void AimingAtAWorldAcrossSpacePicksIt()
        {
            const double time = 300;
            float3 up = World.Land(Planet), eye = up * (Planet.radius + 400f);
            for (int body = 0; body < Solar.bodies.Length; body++)
            {
                if (body == SolarSystemDescriptor.Home) continue;
                float3 there = Solar.ToLocal(SolarSystemDescriptor.Home, time, Solar.PositionAt(body, time));
                // skip worlds that are behind the planet under our feet
                if (math.dot(math.normalize(there - eye), up) < 0.1f) continue;
                float3 ray = math.normalize(there - eye);
                int picked = Scan.Body(Solar, SolarSystemDescriptor.Home, time, eye, ray);
                if (picked == body) continue;
                // the only excuse is something nearer in the way, as when a moon passes behind its planet
                float3 other = Solar.ToLocal(SolarSystemDescriptor.Home, time, Solar.PositionAt(picked, time)) - eye;
                Assert.Less(math.dot(other, ray), math.distance(there, eye), Solar.bodies[body].summary);
                Assert.Less(math.length(other - ray * math.dot(other, ray)), Solar.bodies[picked].radius, Solar.bodies[body].summary);
            }
            Assert.AreEqual(SolarSystemDescriptor.Home, Scan.Body(Solar, SolarSystemDescriptor.Home, time, eye, -up));
        }

        [Test]
        public void DescribesAPlaceInPlainWords()
        {
            const double time = 300;
            float3 sun = math.normalize(Solar.ToLocal(SolarSystemDescriptor.Home, time, Solar.PositionAt(0, time)));
            string noon = Scan.Place(Solar, SolarSystemDescriptor.Home, sun * Planet.GroundRadius(sun), time);
            string midnight = Scan.Place(Solar, SolarSystemDescriptor.Home, -sun * Planet.GroundRadius(-sun), time);
            StringAssert.Contains(Planet.name, noon);
            StringAssert.Contains("around midday", noon);
            StringAssert.Contains("at night", midnight);
            StringAssert.Contains(sun.y < 0f ? "south" : "north", noon);
            StringAssert.Contains("sea", Scan.Place(Solar, SolarSystemDescriptor.Home, World.Sea(Planet) * Planet.radius, time));
            StringAssert.Contains("airless", Scan.Place(Solar, SolarSystemDescriptor.Moon, new float3(0, 130, 0), time));
        }

        [Test]
        public void NamesTheGroundUnderfoot()
        {
            Assert.AreEqual("sea", Scan.Terrain(Planet, World.Sea(Planet)));
            Assert.AreEqual("cloud tops", Scan.Terrain(Solar.bodies.First(body => body.kind == BodyKind.Gas), new float3(0, 1, 0)));
            var kinds = new HashSet<string>(Check.Directions(3000).Select(direction => Scan.Terrain(Planet, direction)));
            CollectionAssert.IsSupersetOf(kinds, new[] { "sea", "beach sand", "meadow grass", "cliff rock" });
            float3 summit = Check.Directions(3000).OrderByDescending(direction => Planet.GroundRadius(direction)).First();
            CollectionAssert.Contains(new[] { "cliff rock", "mountain rock", "snow" }, Scan.Terrain(Planet, summit));
            StringAssert.Contains(Planet.name, Scan.Describe(Planet));
            StringAssert.Contains("a day of", Scan.Describe(Planet));
        }
    }
}
