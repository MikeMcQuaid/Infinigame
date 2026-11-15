using System.IO;
using Infinigame.Core;
using NUnit.Framework;

namespace Infinigame.Tests
{
    [SetUpFixture]
    public class AuthoredContent
    {
        [OneTimeSetUp]
        public void ReadTheGameSources() => Narrative.Load(File.ReadAllText("STORY.md"), File.ReadAllText("MISSIONS.md"), File.ReadAllText("FACTIONS.md"), File.ReadAllText("COLONIES.md"));
    }

    public class NarrativeTests
    {
        [Test]
        public void StoryRevelationsFollowProgressAndLeaveThePlayersOriginOpen()
        {
            string early = Narrative.Context(2, false), late = Narrative.Context(14, false);
            StringAssert.Contains("whether you began as a human", early);
            StringAssert.DoesNotContain("radiation boundary is a prison", early);
            StringAssert.Contains("radiation boundary is a prison", late);
            StringAssert.DoesNotContain("moon impact has destroyed", late);
            StringAssert.Contains("moon impact has destroyed", Narrative.Context(15, true));
            StringAssert.Contains("preserve", early);
        }

        [Test]
        public void EveryBuildingHasAnAuthoredCostAndPurpose()
        {
            for (int i = 0; i < 7; i++) Assert.Greater(Narrative.Explanations[i].Length, 30);
            Assert.AreEqual(25, Colony.Cost(Building.Workshop).x);
            Assert.AreEqual("The recovery machine", Campaign.Missions[1].name);
        }
    }
}
