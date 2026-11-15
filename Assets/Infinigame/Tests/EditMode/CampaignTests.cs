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
            var won = Campaign.Missions.SelectMany(mission => mission.wins).ToArray();
            CollectionAssert.AreEquivalent(Enum.GetValues(typeof(Gear)), won, "everything is won somewhere, and nowhere twice");
            Assert.AreEqual(Campaign.Won, Campaign.Missions.Length);
            Assert.AreEqual(0, Campaign.Missions[Campaign.Buried].wins.Length, "digging yourself out wins nothing but daylight");
            CollectionAssert.AreEqual(new[] { Gear.Suit }, Campaign.Missions[Campaign.Stranger].wins, "the hermit gives the first gun");
            CollectionAssert.AreEqual(new[] { Gear.GateParts }, Campaign.Missions[Campaign.Citadel].wins);
            Assert.AreEqual(FoeKind.Base, Campaign.Missions[Campaign.Citadel].guards);
            foreach (var mission in Campaign.Missions.Skip(2)) Assert.Greater(mission.count, 0, mission.name);
        }

        [Test]
        public void WhatHasBeenWonGrowsMissionByMission()
        {
            Assert.AreEqual(0, Campaign.Kit(Campaign.Stranger), "you begin with nothing");
            Gear[] order = { Gear.Suit, Gear.Thrusters, Gear.Stealth, Gear.Ship, Gear.Orbiter, Gear.Shield, Gear.Nukes, Gear.GateParts };
            int last = 0;
            foreach (var gear in order)
            {
                int stage = Enumerable.Range(0, Campaign.Won + 1).First(reached => Campaign.Has(Campaign.Kit(reached), gear));
                Assert.Greater(stage, last, $"{gear} comes after what came before it");
                last = stage;
            }
            Assert.IsFalse(Campaign.Has(Campaign.Kit(Campaign.Citadel), Gear.GateParts));
            Assert.IsTrue(Campaign.Has(Campaign.Kit(Campaign.Citadel), Gear.Nukes), "the citadel is faced with torpedoes in hand");
            Assert.AreEqual("GAUNTLET BEAMS AND A CLOAK", Campaign.Spoils(3));
            Assert.AreEqual("A CAPACITOR, BACK MISSILES AND YOUR SHIP", Campaign.Spoils(4), "the launcher comes with the ship it is for shooting at");
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
        public void MissionsUseDryBuildableSitesWithTheTowerBeyondWalkingRange()
        {
            for (uint seed = 1; seed <= 25; seed++)
            {
                var system = SolarSystemDescriptor.Generate(seed);
                var home = system.bodies[SolarSystemDescriptor.Home];
                float3 woke = Campaign.Site(system, Campaign.Buried);
                Assert.AreEqual(home.FindLanding(seed + 1), woke);
                float last = 0f;
                for (int stage = Campaign.Stranger; stage <= Campaign.Tower; stage++)
                {
                    Assert.AreEqual(SolarSystemDescriptor.Home, Campaign.World(system, stage));
                    float3 site = Campaign.Site(system, stage);
                    float walk = math.acos(math.clamp(math.dot(site, woke), -1f, 1f)) * home.radius;
                    Assert.Greater(math.distance(site, Campaign.Site(system, stage - 1)) * home.radius, 15, $"seed {seed} stage {stage}: a distinct site");
                    Assert.Greater(home.GroundRadius(site) - home.radius, 2.5f, "on dry land");
                    Assert.Greater(math.dot(home.GroundNormal(site), site), 0.9f, "and level enough to build on");
                    last = walk;
                }
                Assert.LessOrEqual(math.distance(Campaign.Site(system, 2), Campaign.Site(system, 1)) * home.radius, 300, "the first outpost is a short walk from the workshop");
                Assert.Greater(last, 0.5f * math.PI * home.radius, "and the tower is round the far side");
            }
        }

        [Test]
        public void TheLaterMissionsAreOnTheOtherWorldsInTurn()
        {
            var system = World.Solar;
            string[] worlds = { "Luna", "Mars", "Venus", "Mercury", "Ganymede", "Titan", "Titania", "Triton" };
            for (int i = 0; i < worlds.Length; i++)
            {
                int stage = Campaign.Tower + 1 + i, world = Campaign.World(system, stage);
                Assert.AreEqual(worlds[i], system.bodies[world].name);
                Assert.AreEqual(BodyKind.Rock, system.bodies[world].kind, "somewhere with ground to hold");
                Assert.AreEqual(1f, math.length(Campaign.Site(system, stage)), 1e-4f);
                Assert.AreEqual(FoeKind.Raider, Campaign.Missions[stage].guards);
            }
            Assert.AreEqual(BodyKind.Station, system.bodies[Campaign.World(system, Campaign.Citadel)].kind);
        }
    }
}
