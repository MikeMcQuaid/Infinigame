using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Infinigame.Core
{
    // One square of a cube face: depth 0 is the whole face, each level halves the edge.
    public readonly struct NodeId : IEquatable<NodeId>
    {
        public readonly int face, depth, x, y;

        public NodeId(int face, int depth, int x, int y)
        {
            this.face = face;
            this.depth = depth;
            this.x = x;
            this.y = y;
        }

        public float UvSize => 2f / (1 << depth);
        public float2 UvMin => new float2(x, y) * UvSize - 1f;
        public float3 Centre => CubeSphere.Direction(face, UvMin + UvSize / 2f);
        public NodeId Child(int i) => new NodeId(face, depth + 1, x * 2 + (i & 1), y * 2 + (i >> 1));

        public bool Equals(NodeId o) => face == o.face && depth == o.depth && x == o.x && y == o.y;
        public override bool Equals(object o) => o is NodeId n && Equals(n);
        public override int GetHashCode() => (int)math.hash(new int4(face, depth, x, y));
    }

    public static class QuadtreeLod
    {
        // Edge length in metres of a node on a sphere of this radius.
        public static float NodeSize(float radius, int depth) => radius * (math.PI / 2f) / (1 << depth);

        // Deepest level needed for quads no larger than finestQuad metres.
        public static int MaxDepth(float radius, int quads, float finestQuad) =>
            math.max(0, (int)math.ceil(math.log2(NodeSize(radius, 0) / (quads * finestQuad))));

        public static bool WantsSplit(NodeId node, float3 camera, float radius, int maxDepth, float splitFactor)
        {
            float size = NodeSize(radius, node.depth);
            return node.depth < maxDepth && math.distance(camera, node.Centre * radius) < size * (splitFactor + 0.5f);
        }

        // Fills draw with nodes that tile the sphere exactly once and wanted with every node
        // that should have a mesh. A node is only replaced by its children once all four are
        // ready, so the surface never shows a hole while detail streams in.
        public static void Select(float3 camera, float radius, int maxDepth, float splitFactor,
            Func<NodeId, bool> ready, List<NodeId> draw, List<NodeId> wanted)
        {
            draw.Clear();
            wanted.Clear();
            for (int face = 0; face < CubeSphere.Faces; face++)
            {
                var root = new NodeId(face, 0, 0, 0);
                wanted.Add(root);
                Visit(root);
            }

            void Visit(NodeId node)
            {
                if (WantsSplit(node, camera, radius, maxDepth, splitFactor))
                {
                    bool allReady = true;
                    for (int i = 0; i < 4; i++)
                    {
                        wanted.Add(node.Child(i));
                        allReady &= ready == null || ready(node.Child(i));
                    }
                    if (allReady)
                    {
                        for (int i = 0; i < 4; i++) Visit(node.Child(i));
                        return;
                    }
                }
                draw.Add(node);
            }
        }
    }
}
