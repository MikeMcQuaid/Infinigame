using System.Collections.Generic;
using Infinigame.Core;
using NUnit.Framework;
using Unity.Mathematics;

namespace Infinigame.Tests
{
    // What is drawn must not flicker: a thing appears once it is big enough to see and stays until
    // it is too small or out of sight.
    public class DetailTests
    {
        const float Radius = 1000f, SplitFactor = 2.5f;

        [Test]
        public void EveryCellInRangeIsListedWhicheverWayItsGridFaces()
        {
            var near = new HashSet<NodeId>();
            int depth = QuadtreeLod.DepthFor(Radius, 45f), side = 1 << depth;
            foreach (float3 direction in Check.Directions(40))
            {
                float3 eye = direction * (Radius + 2f);
                QuadtreeLod.Near(eye, Radius, depth, 300f, near);
                for (int face = 0; face < CubeSphere.Faces; face++)
                for (int y = 0; y < side; y++)
                for (int x = 0; x < side; x++)
                {
                    var cell = new NodeId(face, depth, x, y);
                    if (math.distance(eye, cell.Centre * Radius) < 300f) Assert.IsTrue(near.Contains(cell), $"cell {face} {x} {y} is in range of {direction} but was left out");
                }
                Assert.Less(near.Count, 400, "and not much else is");
            }
        }

        [Test]
        public void DetailOnceShownStaysUntilWellOutOfRange()
        {
            float3 up = math.normalize(new float3(0.3f, 0.8f, -0.5f));
            HashSet<NodeId> shown = new HashSet<NodeId>(), showing = new HashSet<NodeId>();
            int Deepest(float height)
            {
                var draw = new List<NodeId>();
                QuadtreeLod.Select(up * (Radius + height), Radius, 6, SplitFactor, null, draw, new List<NodeId>(), shown, showing);
                (shown, showing) = (showing, shown);
                int deepest = 0;
                foreach (var node in draw) deepest = math.max(deepest, node.depth);
                return deepest;
            }

            // climb until the finest level is first given up, with nothing remembered
            float gone = 2f;
            for (; ; gone += 1f)
            {
                shown.Clear();
                if (Deepest(gone) < 6) break;
            }
            shown.Clear();
            Assert.AreEqual(6, Deepest(gone - 2f));
            Assert.AreEqual(6, Deepest(gone + 3f), "a step past where it would first appear, it is still there");
            Assert.AreEqual(6, Deepest(gone - 2f));
            Assert.Less(Deepest(gone * 2f), 6, "far enough away it goes");
            Assert.Less(Deepest(gone + 3f), 6, "and coming back, it has to earn its place again");
        }

        [Test]
        public void ThingsAreDrawnUntilTheyAreTooSmallToSee()
        {
            // ten metres tall, two pixels, a 1000 pixel screen and a right-angle view: 2.5 km
            Assert.AreEqual(2500f, Detail.Range(10f, 2f, 1000f, 90f), 1f);
            Assert.Greater(Detail.Range(10f, 2f, 2000f, 90f), Detail.Range(10f, 2f, 1000f, 90f), "a sharper screen shows them further off");
            Assert.AreEqual(1f, Detail.Fade(100f, 1000f));
            Assert.AreEqual(1f, Detail.Fade(790f, 1000f), 1e-3f);
            Assert.AreEqual(0.5f, Detail.Fade(900f, 1000f), 1e-3f);
            Assert.AreEqual(0f, Detail.Fade(1000f, 1000f));
        }

        [Test]
        public void TheHorizonHidesWhatIsBeyondIt()
        {
            float3 up = new float3(0, 1, 0), eye = up * (Radius + 2f);
            float3 At(float metres) => math.normalize(up + new float3(metres / Radius, 0, 0));
            Assert.IsTrue(Detail.OverHorizon(eye, At(100f), Radius + 10f, Radius), "a tree a hundred metres off");
            Assert.IsFalse(Detail.OverHorizon(eye, At(400f), Radius + 10f, Radius), "the same tree round the curve of a small world");
            Assert.IsTrue(Detail.OverHorizon(eye, At(400f), Radius + 120f, Radius), "a mountain there still shows");
            Assert.IsTrue(Detail.OverHorizon(up * (Radius + 500f), At(400f), Radius + 10f, Radius), "and from the air so does the tree");
            Assert.IsFalse(Detail.OverHorizon(up * (Radius * 5f), -up, Radius + 10f, Radius), "nothing on the far side ever does");
        }
    }
}
