using System;
using System.Collections.Generic;
using System.Linq;
using Infinigame.Core;
using NUnit.Framework;
using Unity.Mathematics;

namespace Infinigame.Tests
{
    public class ExpansionTests
    {
        [Test]
        public void LoadoutsRequireOwnershipSlotsAndPower()
        {
            int owned = Campaign.Kit(Campaign.Won);
            Assert.IsTrue(Suit.Fits(owned, Suit.Default(owned)));
            Assert.IsFalse(Suit.Fits(0, 1 << (int)Gear.Beams));
            Assert.IsTrue(Suit.Fits(owned, Suit.Modules.Aggregate(0, (bits, gear) => bits | 1 << (int)gear)));
            foreach (var module in Suit.Modules)
                Assert.IsTrue(Suit.Fits(owned, 1 << (int)module), module.ToString());
            float energy = 10f;
            Assert.IsTrue(Suit.Spend(ref energy, 8f));
            Assert.IsFalse(Suit.Spend(ref energy, 8f));
            Assert.AreEqual(2f, energy);
            Assert.AreEqual(0f, Combat.Scorch(737f, Suit.HeatLimit));
        }

        [Test]
        public void PresenceSpreadsWithPlayAndLiberationPushesItBack()
        {
            var presence = new Presence();
            float before = presence.Reach(2);
            presence.Step(3600f);
            Assert.Greater(presence.Reach(2), before);
            presence.Liberate(2);
            Assert.AreEqual(0f, presence.Reach(2));
            presence.Step(3600f);
            Assert.Greater(presence.Reach(2), 0f);
            Assert.LessOrEqual(presence.Reach(2), 1f);
        }

        [Test]
        public void ThePresenceCopiesOnlyRecoveredShipWeapons()
        {
            int kit = 1 << (int)Gear.Ship;
            for (uint i = 0; i < 8; i++) Assert.AreEqual(Gear.Ship, Combat.CapturedWeapon(kit, i).needs);
            var learned = new HashSet<Gear>();
            kit = Campaign.Kit(Campaign.Citadel);
            for (uint i = 0; i < Combat.Guns.Length; i++) learned.Add(Combat.CapturedWeapon(kit, i).needs);
            CollectionAssert.AreEquivalent(Combat.Guns.Where(weapon => weapon.blast < Combat.Nuclear).Select(weapon => weapon.needs), learned);
        }

        [Test]
        public void ColonySpendsResourcesProducesAndCanBeRaided()
        {
            var colony = new Colony { world = 3, ore = 100, ice = 100 };
            Assert.IsTrue(colony.Build(Building.Depot));
            Assert.IsTrue(colony.Build(Building.Mine));
            Assert.IsTrue(colony.Build(Building.Drill));
            Assert.IsTrue(colony.Build(Building.Robot));
            float ore = colony.ore, ice = colony.ice;
            colony.Step(60f, 0f);
            Assert.Greater(colony.ore, ore);
            Assert.Greater(colony.ice, ice);
            int robots = colony.robots;
            colony.Step(600f, 1f);
            Assert.Less(colony.robots, robots);
            Assert.IsFalse(new Colony().Build(Building.Workshop));
            Assert.GreaterOrEqual(colony.ore, 0f);
        }

        [Test]
        public void CitadelMantleStopsShipsOutsideThePassages()
        {
            var citadel = Interior.Citadel();
            var at = citadel.Move(new float3(130, 240, 280), new float3(0, -200, 0), 4, 1, false, out bool hit);
            Assert.IsTrue(hit);
            Assert.Greater(at.y, 115);
            Assert.IsTrue(citadel.Visible(new float3(0, 0, -400), citadel.entrance, false));
        }

        [Test]
        public void RoomsStopFastMoversButAnOpenDoorLetsThemThrough()
        {
            var rooms = Interior.Outpost();
            float3 start = new float3(0, 2, -5), velocity = new float3(0, 0, 300);
            float3 at = rooms.Move(start, velocity, 0.9f, 0.1f, false, out bool hit);
            Assert.IsTrue(hit);
            Assert.Less(at.z, 6f);
            at = rooms.Move(start, velocity, 0.9f, 0.1f, true, out hit);
            Assert.Greater(at.z, 6f);
            Assert.IsFalse(rooms.Visible(start, new float3(0, 2, 15), false));
            Assert.IsTrue(rooms.Visible(start, new float3(0, 2, 15), true));
        }

        [Test]
        public void InteriorStepsCanBeWalkedAndFoundationsBreached()
        {
            var rooms = Interior.Outpost();
            float3 at = rooms.Move(new float3(0, 0.9f, 8), new float3(0, 0, 10), 0.9f, 1, true, out _, 0.4f);
            Assert.Greater(at.z, 17);
            Assert.Greater(at.y, 2);
            Assert.IsFalse(rooms.Visible(new float3(0, -2, 0), new float3(0, 2, 0), true));
            Assert.IsFalse(rooms.Breach(new float3(0, -0.8f, 0), 70));
            Assert.IsTrue(rooms.Breach(new float3(0, -0.8f, 0), 70));
            Assert.IsTrue(rooms.Visible(new float3(0, -2, 0), new float3(0, 2, 0), true));
        }

        [Test]
        public void TheCitadelHasNoRearEntranceOrStraightShotToItsCore()
        {
            var rooms = Interior.Citadel();
            Assert.IsFalse(rooms.Visible(new float3(160, 0, 400), rooms.core, true));
            Assert.IsFalse(rooms.Visible(rooms.entrance, rooms.core, true));
            for (int i = 0; i < rooms.walls.Length; i++)
                if (rooms.walls[i].section != 0) rooms.walls[i].broken = true;
            float3 scar = rooms.walls[rooms.escapeWall].centre;
            Assert.IsFalse(rooms.Breach(scar, 1000));
            rooms.coreDestroyed = true;
            Assert.IsTrue(rooms.Breach(scar, 12));
            Assert.IsTrue(rooms.Visible(rooms.core, rooms.core + new float3(0, 0, 150), false));
            var route = rooms.route;
            Assert.GreaterOrEqual(route.Length - 2, 6);
            for (int i = 1; i < route.Length; i++)
            {
                Assert.IsTrue(rooms.Visible(route[i - 1], route[i], true), $"corridor {i}");
                float3 at = rooms.Move(route[i - 1], route[i] - route[i - 1], 4, 1, true, out bool hit);
                Assert.IsFalse(hit, $"ship fits through corridor {i}");
                Assert.Less(math.distance(at, route[i]), 0.01f);
            }
        }

        [Test]
        public void ShieldStopsShotsInBothDirections()
        {
            var shield = new Shield { centre = new float3(0, 100, 0), radius = 30f, active = true };
            Assert.IsTrue(shield.Blocks(new float3(0, 100, -50), new float3(0, 0, 100), out _));
            Assert.IsTrue(shield.Blocks(shield.centre, new float3(0, 0, 100), out _));
            var foes = new List<Foe> { Combat.Spawn(FoeKind.Mind, shield.centre, new float3(0, 0, -1), 1, 1) };
            var bolts = new List<Bolt> { Combat.Fire(new float3(0, 100, -50), new float3(0, 0, 1), float3.zero, 1000, 800, false, 60) };
            var struck = new List<Strike>();
            Combat.Step(bolts, foes, new BodyDescriptor { radius = 1 }, float3.zero, float3.zero, 1, 0.1f, struck, shield);
            Assert.AreEqual(Combat.Health(FoeKind.Mind, 1), foes[0].health);
            Assert.IsTrue(struck.Single().shield);
        }

        [Test]
        public void AnExplosionOutsideTheShieldCannotSplashTheMindInside()
        {
            var shield = new Shield { centre = new float3(0, 100, 0), radius = 30, active = true };
            var foes = new List<Foe>
            {
                Combat.Spawn(FoeKind.Mind, shield.centre, new float3(0, 0, 1), 1, 1),
                Combat.Spawn(FoeKind.Drone, shield.centre + new float3(0, 0, 40), new float3(0, 0, 1), 1, 2),
            };
            var bolts = new List<Bolt> { Combat.Fire(shield.centre + new float3(5, 0, 40), new float3(-1, 0, 0), float3.zero, 100, 800, false, 60) };
            Combat.Step(bolts, foes, new BodyDescriptor { radius = 1 }, float3.zero, float3.zero, 1, 0.05f, new List<Strike>(), shield);
            Assert.AreEqual(Combat.Health(FoeKind.Mind, 1), foes[0].health);
            Assert.Less(foes[1].health, 0);
        }

        [Test]
        public void RailPiercesAndBeamDamageDependsOnTime()
        {
            var body = new BodyDescriptor { radius = 1 };
            var foes = new List<Foe>
            {
                Combat.Spawn(FoeKind.Drone, new float3(0, 100, 10), new float3(0, 0, -1), 1, 1),
                Combat.Spawn(FoeKind.Drone, new float3(0, 100, 20), new float3(0, 0, -1), 1, 2),
            };
            Combat.Ray(foes, body, new float3(0, 100, 0), new float3(0, 0, 1), 100, 10, true, default, null);
            Assert.AreEqual(25, foes[0].health);
            Assert.AreEqual(25, foes[1].health);
            for (int i = 0; i < 60; i++) Combat.Ray(foes, body, new float3(0, 100, 0), new float3(0, 0, 1), 100, 6f / 60f, false, default, null);
            Assert.AreEqual(19f, foes[0].health, 0.001f);
            Assert.AreEqual(25, foes[1].health);
        }

        [Test]
        public void MindLossScattersBodiesAndQuietDarkMovementIsUnseen()
        {
            var foe = Combat.Spawn(FoeKind.Drone, new float3(0, 100, 0), new float3(0, 0, 1), 1, 1);
            Assert.AreEqual(Alert.Calm, Combat.Observe(ref foe, new float3(0, 100, -20), 0, 0, true, 1));
            Assert.AreEqual(Alert.Alarmed, Combat.Observe(ref foe, new float3(0, 100, 10), 1, 1, true, 1));
            var mind = Combat.Spawn(FoeKind.Mind, new float3(0, 100, 0), new float3(0, 0, 1), 1, 1);
            Combat.Hunt(ref mind, new BodyDescriptor { radius = 1 }, new float3(0, 100, 10), float3.zero, 1, 1, out _);
            Assert.AreEqual(new float3(0, 100, 0), mind.position);
        }

        [Test]
        public void MoonUsesItsOrdinaryOrbitBeforeTheOpeningCutAndTouchesThePlanetAtImpact()
        {
            var system = SolarSystemDescriptor.Generate(1);
            var fall = new Moonfall { moon = 4, world = 3, began = 10, duration = 60, active = true };
            Assert.Less(math.distance(system.PositionAt(4, 9.99), fall.Position(system, 9.99)), 0.001);
            Assert.AreEqual(system.bodies[3].radius + system.bodies[4].radius, math.distance(system.PositionAt(3, 70), fall.Position(system, 70)), 0.001);
            Assert.IsFalse(fall.Finished(69));
            Assert.IsTrue(fall.Finished(70));
        }

        [Test]
        public void NewSystemsAreDeterministicVariedAndHaveMissionWorlds()
        {
            var counts = new HashSet<int>();
            bool binary = false, livingMoon = false;
            for (uint seed = 1; seed <= 12; seed++)
            {
                var a = SolarSystemDescriptor.Beyond(seed);
                var b = SolarSystemDescriptor.Beyond(seed);
                CollectionAssert.AreEqual(a.bodies.Select(x => x.name), b.bodies.Select(x => x.name));
                CollectionAssert.AreEqual(a.bodies.Select(x => x.radius), b.bodies.Select(x => x.radius));
                Assert.AreEqual(BodyKind.Rock, a.bodies[SolarSystemDescriptor.Home].kind);
                Assert.Greater(a.bodies[SolarSystemDescriptor.Home].SeaRadius, 0);
                Assert.Greater(Campaign.FinalMoon(a), 0);
                counts.Add(a.bodies.Length);
                binary |= a.bodies.Count(x => x.kind == BodyKind.Sun) > 1;
                livingMoon |= a.bodies.Any(x => x.orbit.parent > 0 && x.SeaRadius > 0);
                for (int stage = 0; stage < Campaign.Won; stage++) Assert.GreaterOrEqual(Campaign.World(a, stage), 0);
            }
            Assert.Greater(counts.Count, 1);
            Assert.IsTrue(binary);
            Assert.IsTrue(livingMoon);
        }
        [Test]
        public void ASecondStarAddsHeatAndBinaryStarsCircleTheirCentre()
        {
            SolarSystemDescriptor system = null;
            for (uint seed = 1; seed < 20 && system == null; seed++)
            {
                var next = SolarSystemDescriptor.Beyond(seed);
                if (next.bodies.Count(body => body.kind == BodyKind.Sun) == 2) system = next;
            }
            Assert.IsNotNull(system);
            int second = Array.FindLastIndex(system.bodies, body => body.kind == BodyKind.Sun);
            Assert.Less(math.length(system.PositionAt(0, 20) + system.PositionAt(second, 20)), 0.01);
            var home = system.bodies[SolarSystemDescriptor.Home];
            float3 position = new float3(0, home.radius, 0);
            float two = Climate.At(system, SolarSystemDescriptor.Home, position, 20);
            system.bodies[second].kind = BodyKind.Station;
            Assert.Greater(two, Climate.At(system, SolarSystemDescriptor.Home, position, 20));
        }

        [Test]
        public void SongsOverlapBeforeTheirEndsAndEveryTrackGetsATurn()
        {
            var list = new Playlist(new[] { 20f, 30f, 12f }, 0, 100);
            Assert.AreEqual(115, list.nextAt);
            Assert.AreEqual(new float2(1, 0), list.Gains(110));
            float2 middle = list.Gains(117.5);
            Assert.AreEqual(1, math.lengthsq(middle), 0.0001);
            Assert.AreEqual(middle.x, middle.y, 0.0001);
            Assert.IsFalse(list.Advance(119));
            Assert.IsTrue(list.Advance(120));
            Assert.AreEqual(1, list.index);
            Assert.AreEqual(115, list.start);
            Assert.AreEqual(140, list.nextAt);
            Assert.IsTrue(list.Advance(145));
            Assert.AreEqual(2, list.index);
            var shortSong = new Playlist(new[] { 2f }, 0, 0);
            Assert.AreEqual(1, shortSong.nextAt);
            Assert.IsTrue(shortSong.Advance(2));
            Assert.AreEqual(0, shortSong.index);
        }

    }
}
