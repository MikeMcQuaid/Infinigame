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
            var target = Combat.Spawn(FoeKind.Sentry, at + new float3(20, 0, 100), new float3(0, 0, -1), 1, 1);
            target.stationary = turret;
            var bolts = new List<Bolt>();
            var random = new Unity.Mathematics.Random(1);
            Combat.Fire(ship ? Combat.Guns[1] : Combat.Arms[2], at, new float3(0, 0, 1), float3.zero, ref random, bolts);
            Combat.Step(bolts, new List<Foe> { target }, body, at, float3.zero, 1, 0.05f, null);
            if (turret) Assert.Greater(bolts[0].velocity.x, 0);
            else Assert.AreEqual(0, bolts[0].velocity.x, 0.001f);
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
        [Test] public void MoonWaitsForEngineAssemblyAndIgnition()
        {
            var system = SolarSystemDescriptor.Generate(1);
            var fall = new Moonfall { moon = 4, world = 3, began = 10, duration = 60, active = true };
            Assert.Less(math.distance(system.OrbitalPosition(4, 16), fall.Position(system, 16)), 0.01);
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
