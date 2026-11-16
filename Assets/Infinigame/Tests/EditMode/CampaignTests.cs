using System;
using System.Linq;
using Infinigame.Core;
using NUnit.Framework;
using Unity.Mathematics;

namespace Infinigame.Tests
{
    // The game is a string of missions, each won from the raiders and each opening the way to the next.
    public class CampaignTests
    {
        [Test]
        public void EveryPieceOfGearIsWonOnceAndTheCitadelComesLast()
        {
            var won = Campaign.Missions.SelectMany(mission => mission.wins).Prepend(Gear.Suit).ToArray();
            CollectionAssert.AreEquivalent(Enum.GetValues(typeof(Gear)), won, "everything is won somewhere, and nowhere twice");
            Assert.AreEqual(Campaign.Won, Campaign.Missions.Length);
            Assert.AreEqual(0, Campaign.Missions[Campaign.Buried].wins.Length, "digging yourself out wins nothing but daylight");
            Assert.IsEmpty(Campaign.Missions[Campaign.Stranger].wins, "the traveller already wears the suit");
            CollectionAssert.AreEqual(new[] { Gear.GateParts }, Campaign.Missions[Campaign.Citadel].wins);
            Assert.AreEqual(FoeKind.Base, Campaign.Missions[Campaign.Citadel].guards);
            foreach (var mission in Campaign.Missions.Skip(5)) Assert.Greater(mission.count, 0, mission.name);
        }

        [Test]
        public void WhatHasBeenWonGrowsMissionByMission()
        {
            Assert.IsTrue(Campaign.Has(Campaign.Kit(0), Gear.Suit));
            for (int stage = 1; stage <= Campaign.Won; stage++)
                Assert.AreEqual(Campaign.Kit(stage - 1), Campaign.Kit(stage) & Campaign.Kit(stage - 1));
            Assert.IsFalse(Campaign.Has(Campaign.Kit(2), Gear.Thrusters));
            Assert.IsTrue(Campaign.Has(Campaign.Kit(3), Gear.Thrusters));
            Assert.IsFalse(Campaign.Has(Campaign.Kit(4), Gear.Ship));
            Assert.IsTrue(Campaign.Has(Campaign.Kit(5), Gear.Ship));
            Assert.IsFalse(Campaign.Has(Campaign.Kit(Campaign.Citadel), Gear.GateParts));
            Assert.IsTrue(Campaign.Has(Campaign.Kit(Campaign.Citadel), Gear.Nukes));
        }

        [Test]
        public void EachWeaponIsWonBeforeItCanBeUsed()
        {
            foreach (var weapon in Combat.Guns.Concat(Combat.Arms))
                Assert.IsTrue(Campaign.Has(Campaign.Kit(Campaign.Won), weapon.needs), weapon.name);
            Assert.AreEqual(Gear.Suit, Combat.Arms[0].needs);
            Assert.AreEqual(Gear.Ship, Combat.Guns[0].needs, "the ship comes with its cannons");
        }

        [Test]
        public void MissionsUseDrySitesAndTheFirstBurrowIsNearby()
        {
            for (uint seed = 1; seed <= 25; seed++)
            {
                var system = SolarSystemDescriptor.Generate(seed);
                var home = system.bodies[SolarSystemDescriptor.Home];
                float3 woke = Campaign.Site(system, Campaign.Buried);
                Assert.AreEqual(home.FindLanding(seed + 1), woke);
                float last = 0f;
                for (int stage = Campaign.Stranger; stage < Campaign.Tower; stage++)
                {
                    Assert.AreEqual(SolarSystemDescriptor.Home, Campaign.World(system, stage));
                    float3 site = Campaign.Site(system, stage);
                    float walk = math.acos(math.clamp(math.dot(site, woke), -1f, 1f)) * home.radius;
                    Assert.Greater(math.distance(site, Campaign.Site(system, stage - 1)) * home.radius, stage == 1 ? 5 : 15, $"seed {seed} stage {stage}: a distinct site");
                    Assert.Greater(home.GroundRadius(site) - home.radius, stage == 1 ? 1f : 2.5f, "on dry land");
                    Assert.Greater(math.dot(home.GroundNormal(site), site), 0.9f, "and level enough to build on");
                    last = walk;
                }
                Assert.LessOrEqual(math.distance(Campaign.Site(system, 2), Campaign.Site(system, 1)) * home.radius, 300, "the first burrow is a short walk from the recovery robot");

            }
        }

        [Test]
        public void TheLaterMissionsAreOnTheOtherWorldsInTurn()
        {
            var system = World.Solar;
            string[] worlds = { "Luna", "Mars", "Venus", "Mercury", "Mercury", "Ganymede", "Titan", "Titania", "Triton" };
            for (int i = 0; i < worlds.Length; i++)
            {
                int stage = Campaign.Tower + i, world = Campaign.World(system, stage);
                Assert.AreEqual(worlds[i], system.bodies[world].name);
                Assert.AreEqual(BodyKind.Rock, system.bodies[world].kind, "somewhere with ground to hold");
                Assert.AreEqual(1f, math.length(Campaign.Site(system, stage)), 1e-4f);
                Assert.Greater(Campaign.Missions[stage].count, 0);
            }
            Assert.AreEqual(BodyKind.Station, system.bodies[Campaign.World(system, Campaign.Citadel)].kind);
        }
    }
}
