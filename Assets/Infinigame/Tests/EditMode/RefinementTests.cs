using System.Collections.Generic;
using Infinigame.Core;
using NUnit.Framework;
using Unity.Mathematics;

namespace Infinigame.Tests
{
    public class RefinementTests
    {
        [TestCase(false, false)] [TestCase(true, false)]
        [TestCase(false, true)] [TestCase(true, true)]
        public void MissilesOnlyGuideToShipsAndTurrets(bool ship, bool turret)
        {
            var body = SolarSystemDescriptor.Generate(1).bodies[SolarSystemDescriptor.Home];
            float3 at = new float3(0, body.radius + 1000, 0);
            var target = Combat.Spawn(FoeKind.Sentry, at + new float3(45, 0, 300), new float3(0, 0, -1), 1, 1);
            target.stationary = turret;
            var bolts = new List<Bolt>();
            var random = new Unity.Mathematics.Random(1);
            Combat.Fire(ship ? Combat.Guns[1] : Combat.Arms[2], at, new float3(0, 0, 1), float3.zero, ref random, bolts, launchUp: ship ? float3.zero : new float3(0, 1, 0));
            var foes = new List<Foe> { target };
            for (int i = 0; i < 40; i++) Combat.Step(bolts, foes, body, at, float3.zero, 1, 1f / 60, null);
            if (turret) Assert.Greater(bolts[0].velocity.x, 0);
            else Assert.AreEqual(0, bolts[0].velocity.x, 0.001f);
        }
        [Test] public void BeamMeetsTheSameGroundAsWalkingAndFlight()
        {
            var system = SolarSystemDescriptor.Generate(91);
            var random = new Unity.Mathematics.Random(42);
            var foes = new List<Foe>();
            foreach (int world in new[] { SolarSystemDescriptor.Home, SolarSystemDescriptor.Moon })
            {
                var body = system.bodies[world];
                for (int i = 0; i < 16; i++)
                {
                    float3 up = random.NextFloat3Direction(), from = up * (Combat.Surface(body, up * body.radius) + random.NextFloat(5, 180));
                    float3 direction = math.normalizesafe(Walker.Project(random.NextFloat3Direction(), up) - up * random.NextFloat(0.25f, 2));
                    const int range = 450;
                    float distance = 0.5f;
                    for (; distance < range; distance += 0.5f)
                    {
                        float3 point = from + direction * distance;
                        if (math.length(point) < Combat.Surface(body, point)) break;
                    }
                    float3 end = Combat.Ray(foes, body, from, direction, range, 0, false, default, null);
                    Assert.That(math.distance(end, from), Is.EqualTo(distance).Within(0.51f));
                }
            }
        }

        [Test] public void BackMissileClearsTheHelmetAndFliesAlongTheSightLine()
        {
            var body = new BodyDescriptor { kind = BodyKind.Station };
            var origin = new float3(0, 100, 0);
            var bolts = new List<Bolt>();
            var random = new Unity.Mathematics.Random(3);
            Combat.Fire(Combat.Arms[2], origin, new float3(0, 0, 1), float3.zero, ref random, bolts, launchUp: new float3(0, 1, 0));
            var foes = new List<Foe>();
            for (int i = 0; i < 12; i++) Combat.Step(bolts, foes, body, origin, float3.zero, 1, 1f / 60, null);
            Assert.That(bolts[0].position.y - origin.y, Is.InRange(0.2f, 1f));
            Assert.Greater(bolts[0].velocity.z, 200);
        }

        [Test] public void FactoryProjectilesSpareMindsEvenInSplashRange()
        {
            var body = new BodyDescriptor { kind = BodyKind.Station };
            var at = new float3(0, 100, 5);
            var mind = Combat.Spawn(FoeKind.Mind, at, new float3(0, 0, -1), 1, 1);
            var foes = new List<Foe> { mind, Combat.Spawn(FoeKind.Sentry, at, new float3(0, 0, -1), 1, 2),
                Combat.Spawn(FoeKind.Mind, at + new float3(5, 0, 0), new float3(0, 0, -1), 1, 3) };
            var bolts = new List<Bolt> { new Bolt { robot = true, position = at - new float3(0, 0, 5), velocity = new float3(0, 0, 50), life = 1, blast = 12, damage = 500 } };
            Combat.Step(bolts, foes, body, float3.zero, float3.zero, 1, 0.1f, null);
            Assert.AreEqual(mind.health, foes[0].health);
            Assert.AreEqual(mind.health, foes[2].health);
            Assert.LessOrEqual(foes[1].health, 0);
        }

        [Test] public void ShipBeamHasTwoEmittersAndAnAccurateName()
        {
            Assert.AreEqual("Beam", Combat.Guns[2].name);
            Assert.AreEqual(2, Combat.Guns[2].pellets);
        }
        [Test] public void CitadelHasALongerEnclosedRoute()
        {
            var rooms = Interior.Citadel();
            Assert.Greater(rooms.core.z - rooms.entrance.z, 800);
            Assert.Greater(rooms.walls.Length, 100);
            Assert.IsFalse(rooms.Visible(rooms.entrance, rooms.core, false));
        }
        [Test] public void MoonWaitsForEngineAssemblyBeforeIgnition()
        {
            var system = SolarSystemDescriptor.Generate(1);
            var fall = new Moonfall { moon = 4, world = 3, began = 10, duration = 60, active = true };
            double3 StagedOffset(double time) => fall.Position(system, time) - system.OrbitalPosition(3, time);
            Assert.Less(math.distance(StagedOffset(fall.began), StagedOffset(fall.began + Moonfall.Assembly)), 0.01);
            Assert.AreEqual(system.bodies[3].radius + system.bodies[4].radius, math.distance(system.OrbitalPosition(3, 70), fall.Position(system, 70)), 0.01);
        }
        [TestCase(false)] [TestCase(true)] public void ShieldReflectsShipsFromEitherSide(bool inside)
        {
            var shield = new Shield { centre = float3.zero, radius = 10, active = true };
            float3 from = new float3(inside ? 5 : 15, 0, 0), to = new float3(inside ? 15 : 5, 0, 0);
            float3 velocity = new float3(inside ? 30 : -30, 0, 0);
            Assert.IsTrue(shield.Bounce(from, ref to, ref velocity, 2));
            if (inside) { Assert.Less(to.x, 8); Assert.Less(velocity.x, 0); }
            else { Assert.Greater(to.x, 12); Assert.Greater(velocity.x, 0); }
        }
        [Test] public void FoundationsHoldUntilTheStructureIsDestroyed()
        {
            var body = SolarSystemDescriptor.Generate(1).bodies[SolarSystemDescriptor.Home];
            float3 up = math.normalize(new float3(1, 2, 3));
            float floor = body.GroundRadius(up);
            body.foundations.Add(new float4(up * floor, 4));
            body.Dig(up, 8);
            Assert.AreEqual(floor, body.GroundRadius(up));
            body.foundations.Clear(); body.Dig(up, 8);
            Assert.Less(body.GroundRadius(up), floor - 1);
        }
        [Test] public void NuclearBlastCannotReachAcrossAtmosphereOrBeyondItsRadius()
        {
            var body = SolarSystemDescriptor.Generate(1).bodies[SolarSystemDescriptor.Home];
            float air = body.radius + math.max(body.radius * 0.12f, body.terrain.MaxHeight * 1.45f);
            float3 at = new float3(0, air - 5, 0);
            Assert.AreEqual(0, Combat.NuclearDamage(body, at, at + new float3(0, 10, 0), 60));
            Assert.AreEqual(0, Combat.NuclearDamage(body, at, at + new float3(80, -30, 0), 60));
            Assert.Greater(Combat.NuclearDamage(body, at, at - new float3(0, 10, 0), 60), 0);
        }
        [Test] public void ChargedRailsAndAreaWeaponsHaveDifferentCosts()
        {
            Assert.Greater(Combat.Guns[3].charge, Combat.Arms[3].charge);
            Assert.Greater(Combat.Guns[3].damage, Combat.Guns[0].damage * 5);
            Assert.Greater(Combat.Guns[4].period, Combat.Guns[1].period);
            Assert.Greater(Combat.Arms[4].energy, 0);
        }
    }
}
