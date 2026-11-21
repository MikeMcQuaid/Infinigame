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
            StringAssert.Contains("Whether you began as a human", early);
            StringAssert.DoesNotContain("impact will collapse the mind", early);
            StringAssert.Contains("impact will collapse the mind", late);
            StringAssert.DoesNotContain("The moon has struck", late);
            StringAssert.Contains("The moon has struck", Narrative.Context(15, true));
            StringAssert.Contains("preserve", early);
            StringAssert.DoesNotContain("factory", early.ToLowerInvariant());
            StringAssert.Contains("factory", Narrative.Context(Campaign.SecondBurrow, false));
        }

        [Test]
        public void EveryBuildingHasAnAuthoredCostAndPurpose()
        {
            for (int i = 7; i < 9; i++) Assert.Greater(Narrative.Explanations[i].Length, 30);
            Assert.AreEqual(0, Colony.Cost(Building.Factory).x);
            Assert.AreEqual("The recovery machine", Campaign.Missions[1].name);
        }
    }
}
