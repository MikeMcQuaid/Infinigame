using System.Collections.Generic;
using System.Linq;
using Infinigame.Core;
using NUnit.Framework;
using Unity.Mathematics;

namespace Infinigame.Tests
{
    public class DamageTests
    {
        // Drops someone from a height above level ground and says how hard they land.
        static float Landing(BodyDescriptor body, float height)
        {
            float3 direction = World.Land(body);
            var s = Walker.Spawn(body, direction, World.Tangent(direction));
            s.position += direction * height;
            s.grounded = false;
            for (float t = 0; t < 60f && !s.grounded; t += World.Dt) Walker.Step(ref s, default, body, World.Dt);
            Assert.IsTrue(s.grounded);
            return s.impact;
        }

        [Test] public void ShortAndLongDropsRegisterTheirLandingSpeed()
        {
            Assert.Less(Landing(World.Planet, 1.3f), Landing(World.Planet, 14f));
            Assert.Less(Landing(World.Moon, 14f), Landing(World.Planet, 60f));
        }

        [Test]
        public void SprintingOverGroundStaysWithinSoftLandingSpeed()
        {
            float3 direction = World.Land(World.Planet);
            var s = Walker.Spawn(World.Planet, direction, World.Tangent(direction));
            for (float t = 0; t < 60f; t += World.Dt) Walker.Step(ref s, new WalkerInput { move = new float2(0, 1), sprint = true }, World.Planet, World.Dt);
            Assert.LessOrEqual(s.impact, Walker.SoftLanding);
        }

        [Test]
        public void ALandingIsSafeAndAHillsideIsNot()
        {
            float3 direction = World.Land(World.Planet);
            var s = Ship.Spawn(World.Planet, direction, World.Tangent(direction));
            s.position += direction * 60f;
            s.landed = false;
            for (float t = 0; t < 60f && !s.landed; t += World.Dt) Ship.Step(ref s, new ShipInput { land = true }, World.Planet, true, World.Dt);
            Assert.IsTrue(s.landed);
            Assert.AreEqual(0f, Combat.Crash(s.impact), $"touched down at {s.impact} m/s");

            s.position += direction * 300f;
            s.velocity = -direction * 120f;
            s.landed = false;
            for (float t = 0; t < 5f; t += World.Dt) Ship.Step(ref s, default, World.Planet, false, World.Dt);
            Assert.GreaterOrEqual(Combat.Crash(s.impact), 100f);
        }

        [Test]
        public void ARockInABeltDentsAHullAndNeverFinishesIt()
        {
            Assert.That(Combat.Graze(5f, 100f), Is.InRange(1f, 5f));
            Assert.LessOrEqual(Combat.Graze(600f, 100f), 10f, "however fast it was struck");
            float hull = 100f;
            for (int i = 0; i < 200; i++) hull -= Combat.Graze(600f, hull);
            Assert.Greater(hull, 0f);
        }

        [Test]
        public void HeatHurtsOnlyPastWhatAHullOrASuitCanTake()
        {
            Assert.AreEqual(0f, Combat.Scorch(300f, Climate.SuitLimit));
            Assert.AreEqual(0f, Combat.Scorch(Climate.HullLimit, Climate.HullLimit));
            Assert.Greater(Combat.Scorch(737f, Climate.SuitLimit), 5f, "a furnace world is no place for a walk");
            Assert.AreEqual(0f, Combat.Scorch(737f, Climate.HullLimit), "though a ship can sit there");
            Assert.Greater(Combat.Scorch(4000f, Climate.HullLimit), 100f, "and nothing lasts beside a star");
        }
    }

    public class CombatTests
    {
        static readonly BodyDescriptor Body = World.Planet;
        static readonly float3 Up = World.Land(Body);

        [Test]
        public void ABoltHitsWhatItsPathCrosses()
        {
            Assert.IsTrue(Combat.Crosses(float3.zero, new float3(0, 0, 10), new float3(0.5f, 0, 5), 1f));
            Assert.IsFalse(Combat.Crosses(float3.zero, new float3(0, 0, 10), new float3(2f, 0, 5), 1f));
            Assert.IsFalse(Combat.Crosses(float3.zero, new float3(0, 0, 10), new float3(0, 0, 14), 1f), "beyond the end of the step");
        }

        [Test]
        public void ThePlayersBoltsWearAFoeDown()
        {
            float3 from = Up * (Body.radius + 500f), to = World.Tangent(Up);
            var foes = new List<Foe> { Combat.Spawn(FoeKind.Raider, from + to * 400f, -to, 1, 5) };
            var bolts = new List<Bolt>();
            var struck = new List<Strike>();
            var cannon = Combat.Guns[0];
            var rnd = new Random(1);
            int shots = 0;
            for (; foes[0].health > 0f && shots < 50; shots++)
            {
                Combat.Fire(cannon, from, to, float3.zero, ref rnd, bolts);
                for (int i = 0; i < 60; i++) Assert.AreEqual(0f, Combat.Step(bolts, foes, Body, from, float3.zero, Combat.ShipRadius, World.Dt, struck), "your own bolts cannot hurt you");
            }
            Assert.AreEqual((int)math.ceil(Combat.Health(FoeKind.Raider, 1) / cannon.damage), shots);
            Assert.AreEqual(shots, struck.Count);
            Assert.AreEqual(0, bolts.Count, "a bolt that hits is spent");
        }

        [Test]
        public void HostileBoltsHurtOnlyWhenTheyHit()
        {
            float3 player = Up * (Body.radius + 500f), to = World.Tangent(Up);
            float Shoot(float3 aim)
            {
                var bolts = new List<Bolt> { Combat.Fire(player - to * 200f, math.normalize(aim), float3.zero, 400f, 7f, true) };
                float hurt = 0f;
                for (int i = 0; i < 240; i++) hurt += Combat.Step(bolts, new List<Foe>(), Body, player, float3.zero, Combat.ShipRadius, World.Dt, null);
                Assert.AreEqual(0, bolts.Count, "bolts do not fly for ever");
                return hurt;
            }
            Assert.AreEqual(7f, Shoot(to));
            Assert.AreEqual(0f, Shoot(to + Up * 0.1f));
        }

        [Test]
        public void TheGroundStopsBolts()
        {
            var bolts = new List<Bolt> { Combat.Fire(Up * (Body.GroundRadius(Up) + 30f), -Up, float3.zero, 100f, 5f, false) };
            var struck = new List<Strike>();
            for (int i = 0; i < 60; i++) Combat.Step(bolts, new List<Foe>(), Body, float3.zero, float3.zero, 1f, World.Dt, struck);
            Assert.AreEqual(0, bolts.Count);
            Assert.AreEqual(1, struck.Count);
            Assert.IsTrue(struck[0].ground, "and the strike is known to be on the ground, where it can leave a crater");
        }

        [Test]
        public void BackMissilesClearThePackBeforeTurningTowardsTheAim()
        {
            var bolts = new List<Bolt>();
            var rng = new Random(2);
            float3 up = World.Land(World.Planet), aim = World.Tangent(up);
            float3 pack = up * (World.Planet.GroundRadius(up) + 2);
            Combat.Fire(Combat.Arms[2], pack, aim, float3.zero, ref rng, bolts, launchUp: up);
            Assert.Greater(math.dot(math.normalize(bolts[0].velocity), up), 0.99f);
            for (int i = 0; i < 45; i++)
                Combat.Step(bolts, new List<Foe>(), World.Planet, pack, float3.zero, 1, 1f / 60, null);
            Assert.AreEqual(1, bolts.Count);
            Assert.That(math.dot(bolts[0].position - pack, up), Is.InRange(0.4f, 0.8f));
            Assert.Greater(math.dot(math.normalize(bolts[0].velocity), aim), 0.99f);
        }

        [Test]
        public void AMissileFromTheLauncherFindsARaiderOverhead()
        {
            float3 up = World.Land(World.Planet), along = World.Tangent(up);
            float ground = World.Planet.GroundRadius(up);
            var launcher = Combat.Arms.Single(weapon => weapon.needs == Gear.BackMissiles);
            var foes = new List<Foe> { Combat.Spawn(FoeKind.Raider, up * (ground + 400f) + along * 250f, along, 1, 7u) };
            var bolts = new List<Bolt>();
            var rnd = new Random(1);
            // fired upwards and well wide of it, from someone standing on the ground
            Combat.Fire(launcher, up * (ground + 2f), up, float3.zero, ref rnd, bolts);
            for (float t = 0; t < launcher.life && bolts.Count > 0; t += World.Dt) Combat.Step(bolts, foes, World.Planet, up * ground, float3.zero, Combat.PersonRadius, World.Dt, null);
            Assert.Less(foes[0].health, Combat.Health(FoeKind.Raider, 1));
        }

        [Test]
        public void ALandingChargeMarksTheGroundItStrikesForLevelling()
        {
            float3 up = World.Land(World.Planet);
            var bolts = new List<Bolt> { new Bolt { position = up * (World.Planet.GroundRadius(up) + 40f), velocity = -up * 150f, life = 5f, level = true } };
            var struck = new List<Strike>();
            for (int i = 0; i < 120 && struck.Count == 0; i++) Combat.Step(bolts, new List<Foe>(), World.Planet, up * 1e6f, float3.zero, 1f, World.Dt, struck);
            Assert.IsTrue(struck[0].ground && struck[0].level);
            Assert.AreEqual(-1, struck[0].foe);
        }

        [Test]
        public void AStrikeSaysWhichFoeItFell()
        {
            var foes = new List<Foe> { Combat.Spawn(FoeKind.Drone, new float3(0, 2000f, 0), new float3(0, 0, 1), 1, 3u), Combat.Spawn(FoeKind.Drone, new float3(40f, 2000f, 0), new float3(0, 0, 1), 1, 5u) };
            var bolts = new List<Bolt> { Combat.Fire(new float3(40f, 2000f, -20f), new float3(0, 0, 1), float3.zero, 400f, 5f, false) };
            var struck = new List<Strike>();
            for (int i = 0; i < 30 && struck.Count == 0; i++) Combat.Step(bolts, foes, World.Planet, float3.zero, float3.zero, 1f, World.Dt, struck);
            Assert.AreEqual(1, struck[0].foe);
            Assert.IsFalse(struck[0].ground);
        }

        [Test]
        public void OnlyBrokenGroundIsTooRoughToLandOn()
        {
            Assert.IsFalse(Ship.Rough(World.Planet, World.Land(World.Planet)), "a landing site");
            Assert.IsFalse(Ship.Rough(World.Planet, World.Sea(World.Planet)), "the sea");
            // the steepest of a few thousand places
            float3 steep = Check.Directions(4000, 9).Where(direction => World.Planet.GroundRadius(direction) > World.Planet.SeaRadius + 5f)
                .OrderBy(direction => math.dot(World.Planet.GroundNormal(direction), direction)).First();
            Assert.Less(math.dot(World.Planet.GroundNormal(steep), steep), 0.9f);
            Assert.IsTrue(Ship.Rough(World.Planet, steep), "a mountainside");
            Assert.IsFalse(Ship.Rough(World.Solar.bodies.First(body => body.kind == BodyKind.Gas), new float3(0, 1, 0)), "a giant has no ground at all");
        }

        [Test]
        public void DronesCloseInHoverAndShoot()
        {
            float3 chest = Up * (Body.GroundRadius(Up) + 1.2f);
            float3 start = math.normalize(Up + World.Tangent(Up) * (60f / Body.radius));
            var foe = Combat.Spawn(FoeKind.Drone, start * (Body.GroundRadius(start) + 2f), -World.Tangent(Up), 1, 9);
            var bolts = new List<Bolt>();
            float hurt = 0f, lowest = float.MaxValue;
            for (float t = 0; t < 30f; t += World.Dt)
            {
                if (Combat.Hunt(ref foe, Body, chest, float3.zero, 1, World.Dt, out var shot)) bolts.Add(shot);
                hurt += Combat.Step(bolts, new List<Foe>(), Body, chest, float3.zero, Combat.PersonRadius, World.Dt, null);
                lowest = math.min(lowest, math.length(foe.position) - Ship.Floor(Body, math.normalize(foe.position)));
            }
            Assert.Less(math.distance(foe.position, chest), 20f, "it closes in");
            Assert.Greater(math.distance(foe.position, chest), 6f, "but keeps its distance");
            Assert.Greater(lowest, 0.7f, "and never touches the ground");
            Assert.Greater(hurt, 0f, "someone who stands still gets shot");
        }

        [Test]
        public void RaidersMakeFiringPassesAndPullUpInTime()
        {
            float3 ship = Up * (Body.radius + 500f);
            var foe = Combat.Spawn(FoeKind.Raider, ship + World.Tangent(Up) * 1500f, -World.Tangent(Up), 1, 3);
            var bolts = new List<Bolt>();
            float hurt = 0f, lowest = float.MaxValue, nearest = float.MaxValue, furthest = 0f;
            int shots = 0;
            for (float t = 0; t < 90f; t += World.Dt)
            {
                if (Combat.Hunt(ref foe, Body, ship, float3.zero, 1, World.Dt, out var shot))
                {
                    bolts.Add(shot);
                    shots++;
                }
                hurt += Combat.Step(bolts, new List<Foe>(), Body, ship, float3.zero, Combat.ShipRadius, World.Dt, null);
                lowest = math.min(lowest, math.length(foe.position) - Ship.Floor(Body, math.normalize(foe.position)));
                nearest = math.min(nearest, math.distance(foe.position, ship));
                if (t > 20f) furthest = math.max(furthest, math.distance(foe.position, ship));
            }
            Assert.Greater(shots, 5);
            Assert.Greater(hurt, 0f, "a ship that sits still gets hit");
            Assert.Less(nearest, 200f, "it comes in close");
            Assert.That(furthest, Is.InRange(400f, 2500f), "then breaks away and comes round again");
            Assert.Greater(lowest, 8f, "without flying into the ground");
        }

        [Test]
        public void IndividualFoesKeepTheirHealthAndDamageAsSwarmsGrow()
        {
            foreach (FoeKind kind in new[] { FoeKind.Raider, FoeKind.Drone })
            {
                Assert.AreEqual(Combat.Health(kind, 4), Combat.Health(kind, 1));
                Assert.AreEqual(Combat.Damage(kind, 4), Combat.Damage(kind, 1));
            }
        }
    }

    public class RingTests
    {
        static readonly BodyDescriptor Ringed = World.Solar.bodies.First(body => body.ringDensity > 0.5f);

        static List<Rock> Cell(int size, int band, int sector)
        {
            var rocks = new List<Rock>();
            Rings.Rocks(Ringed, size, band, sector, rocks);
            return rocks;
        }

        // Every rock of one class, all the way round.
        static List<Rock> All(int size)
        {
            var rocks = new List<Rock>();
            for (int band = 0; band < Rings.Bands(Ringed, size); band++)
            for (int sector = 0; sector < Rings.Sectors(Ringed, size, band); sector++)
                Rings.Rocks(Ringed, size, band, sector, rocks);
            return rocks;
        }

        [Test]
        public void RocksLieInAThinSheetBetweenTheRingsEdges()
        {
            var rocks = All(0);
            Assert.Greater(rocks.Count, 500);
            foreach (var rock in rocks)
            {
                Assert.That(math.length(rock.position.xz), Is.InRange(Ringed.ringInner, Ringed.ringOuter + Rings.Cell(Ringed, 0)));
                Assert.LessOrEqual(math.abs(rock.position.y), Ringed.ringThick);
                Assert.That(rock.radius, Is.InRange(Rings.Size(Ringed, 0) / 2f, Rings.Size(Ringed, 0)));
            }
        }

        [Test]
        public void TheSameCellAlwaysHoldsTheSameRocks()
        {
            var once = Cell(1, 2, 7);
            var again = Cell(1, 2, 7);
            Assert.Greater(once.Count, 0);
            Assert.AreEqual(once.Count, again.Count);
            for (int i = 0; i < once.Count; i++) Check.Near(once[i].position, again[i].position, 1e-6f);
            Assert.AreNotEqual(once[0].position, Cell(1, 2, 8)[0].position);
        }

        [Test]
        public void SmallerRocksAreFarMoreNumerous()
        {
            Assert.AreEqual(Rings.Size(Ringed, 0) / 2f, Rings.Size(Ringed, 1));
            Assert.That(All(1).Count / (float)All(0).Count, Is.InRange(3f, 5f), "half the size, four times as many");
        }

        [Test]
        public void TheDarkDivisionIsNearlyEmpty()
        {
            float width = Ringed.ringOuter - Ringed.ringInner, division = Ringed.ringInner + 0.62f * width;
            Assert.Less(Rings.Density(Ringed, division), 0.1f);
            Assert.AreEqual(0f, Rings.Density(Ringed, Ringed.ringInner - 1f));
            Assert.AreEqual(0f, Rings.Density(Ringed, Ringed.ringOuter + 1f));
            int inside = All(1).Count(rock => math.abs(math.length(rock.position.xz) - division) < 0.008f * width);
            int beside = All(1).Count(rock => math.abs(math.length(rock.position.xz) - division - 0.1f * width) < 0.008f * width);
            Assert.Less(inside * 4, beside);
        }

        [Test]
        public void EachBandCirclesAtItsOwnOrbitalSpeed()
        {
            int last = Rings.Bands(Ringed, 1) - 1;
            // seen from outside the planet's own turning, a band goes round as a moon there would
            double Rate(int band) => Rings.Turned(Ringed, 1, band, 1.0) - 2 * math.PI_DBL / Ringed.spinPeriod;
            double distance = Ringed.ringInner + 0.5 * Rings.Cell(Ringed, 1);
            Assert.AreEqual(math.sqrt(Ringed.surfaceGravity * Ringed.radius * Ringed.radius / (distance * distance * distance)), math.abs(Rate(0)), 1e-4);
            Assert.Greater(math.abs(Rate(0)), math.abs(Rate(last)), "the inner ring laps the outer");
            Assert.AreEqual(math.sign(Ringed.Spin.y), math.sign(Rate(0)), "the same way round as the planet turns");
        }

        [Test]
        public void AShipTouchesARockOnlyWhereOneIs()
        {
            const double time = 321.5;
            var scratch = new List<Rock>();
            var rock = Cell(0, 1, 3)[0];
            float3 now = Rings.Now(Ringed, 0, 1, time, rock.position), outward = math.normalize(new float3(now.x, 0, now.z));
            Assert.Greater(math.distance(now, rock.position), 10f, "it has moved on since time zero");

            Assert.IsTrue(Rings.Hit(Ringed, now + outward * (rock.radius + 1f), 2f, time, scratch, out float3 normal, out float3 velocity));
            Check.Near(outward, normal, 0.01f);
            Check.Near((Rings.Now(Ringed, 0, 1, time + 0.01, rock.position) - Rings.Now(Ringed, 0, 1, time - 0.01, rock.position)) / 0.02f, velocity, 0.5f);
            Assert.IsFalse(Rings.Hit(Ringed, now + new float3(0, 80f, 0), 2f, time, scratch, out _, out _), "above the sheet there is nothing");
            Assert.IsFalse(Rings.Hit(World.Planet, now, 2f, time, scratch, out _, out _), "nor round a world without rings");

            // flying through the sheet, sooner or later something is in the way
            int hits = 0;
            for (int i = 0; i < 4000; i++)
                if (Rings.Hit(Ringed, new float3(Ringed.ringInner + 200f + i * 0.5f, 0f, 0f), Combat.ShipRadius, time, scratch, out _, out _))
                    hits++;
            Assert.That(hits, Is.InRange(1, 2000));
        }
    }

    public class AutopilotTests
    {
        static readonly float3 Target = new float3(60000f, 0f, 0f), Drift = new float3(0f, 0f, 40f);
        const float Standoff = 2000f;

        // Flies until arrival, calling watch every step. Returns the seconds taken.
        static float Fly(ref ShipState s, List<float4> worlds, System.Action<ShipState> watch = null)
        {
            float3 target = Target;
            for (float t = 0; t < 300f; t += World.Dt)
            {
                target += Drift * World.Dt;
                if (Autopilot.Step(ref s, target, Drift, Standoff, worlds, World.Dt))
                {
                    Assert.AreEqual(Standoff, math.distance(s.position, target), 5f);
                    Check.Near(Drift, s.velocity, 1e-3f);
                    return t;
                }
                watch?.Invoke(s);
            }
            Assert.Fail("never arrived");
            return 0f;
        }

        [Test]
        public void ArrivesBesideAMovingWorldFarSoonerThanByHand()
        {
            var s = new ShipState { rotation = quaternion.identity };
            float top = 0f;
            float taken = Fly(ref s, null, ship => top = math.max(top, math.length(ship.velocity)));
            Assert.Less(taken, 15f);
            Assert.Greater(60000f / Tuning.ShipSpaceSpeed, 5f * taken, "many times as quick as full thrust");
            Assert.LessOrEqual(top, Autopilot.Speed + 50f);
            Assert.Greater(math.dot(math.rotate(s.rotation, new float3(0, 0, 1)), math.normalize(Target)), 0.9f, "nose towards where it was going");
        }

        [Test]
        public void SteersRoundAWorldInTheWay()
        {
            var worlds = new List<float4> { new float4(30000f, 300f, 0f, 3000f) };
            var s = new ShipState { rotation = quaternion.identity };
            float nearest = float.MaxValue;
            Fly(ref s, worlds, ship => nearest = math.min(nearest, math.distance(ship.position, worlds[0].xyz)));
            Assert.Greater(nearest, 2950f);
            Assert.Less(nearest, 6000f, "without going far out of its way");
        }

        [Test]
        public void ClimbsAwayFromTheGroundBeforeItSpeedsUp()
        {
            // parked on a world whose surface is 2400 m from its centre, with the target on the far side
            var worlds = new List<float4> { new float4(0f, 0f, 0f, 3000f) };
            var s = new ShipState { rotation = quaternion.identity, position = new float3(-2410f, 0f, 0f), landed = true };
            float lowest = float.MaxValue;
            Fly(ref s, worlds, ship =>
            {
                float height = math.length(ship.position);
                lowest = math.min(lowest, height);
                if (height < 3000f) Assert.Less(math.distance(ship.velocity, Drift), 130f, "slow while close in");
            });
            Assert.Greater(lowest, 2405f, "it never dips towards the ground");
            Assert.IsFalse(s.landed);
        }

        [Test]
        public void HoldsASteadyCourseAllTheWayRoundAWorldInTheWay()
        {
            // from out in space with a world dead ahead, from close by it and from the ground on its far side
            foreach (float3 from in new[] { new float3(0f, 0f, 0f), new float3(26000f, 900f, 200f), new float3(27590f, 300f, 0f) })
            {
                var worlds = new List<float4> { new float4(30000f, 300f, 0f, 3000f) };
                var s = new ShipState { rotation = quaternion.identity, position = from };
                float3 last = float3.zero, turning = float3.zero;
                int reversals = 0;
                Fly(ref s, worlds, ship =>
                {
                    // which way the nose is swinging, as the axis it turns about
                    float3 nose = math.rotate(ship.rotation, new float3(0, 0, 1)), turn = math.cross(last, nose);
                    last = nose;
                    if (math.length(turn) < 0.0005f) return;
                    if (math.dot(turn, turning) < 0f) reversals++;
                    turning = turn;
                });
                Assert.Less(reversals, 6, $"from {from} it turns aside and turns back, and does not shake from side to side on the way");
            }
        }

        [Test]
        public void StandsOffClearOfRingsAndInsideTheWorldsInfluence()
        {
            foreach (var body in World.Solar.bodies.Where(body => body.orbit.parent >= 0))
            {
                float standoff = Autopilot.Standoff(body);
                Assert.Greater(standoff, body.radius * 1.5f, body.summary);
                Assert.Greater(standoff, body.ringOuter * 1.05f, body.summary);
                Assert.Less(standoff, body.soiRadius, body.summary);
            }
        }
    }
}
