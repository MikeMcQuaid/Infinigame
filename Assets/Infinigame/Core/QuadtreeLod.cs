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

        // The depth whose nodes are closest to a size in metres.
        public static int DepthFor(float radius, float size) => math.max(0, (int)math.round(math.log2(NodeSize(radius, 0) / size)));

        // Every node at one depth whose centre is within range of a point, and perhaps a few just
        // beyond: the grid that plants, rocks and animals are dealt onto. Found by walking down the
        // tree, so no cell is ever skipped however its face is turned.
        public static void Near(float3 point, float radius, int depth, float range, HashSet<NodeId> into)
        {
            into.Clear();
            for (int face = 0; face < CubeSphere.Faces; face++) Visit(new NodeId(face, 0, 0, 0));

            void Visit(NodeId node)
            {
                // no part of a node is further from its centre than the length of its edge
                if (math.distance(point, node.Centre * radius) > range + NodeSize(radius, node.depth)) return;
                if (node.depth == depth) into.Add(node);
                else for (int i = 0; i < 4; i++) Visit(node.Child(i));
            }
        }

        // Detail that is already drawn is kept until the camera is this much further off than where
        // it first appeared, so hovering at the boundary never makes it flicker.
        public const float Linger = 1.25f;

        public static bool WantsSplit(NodeId node, float3 camera, float radius, int maxDepth, float splitFactor)
        {
            float size = NodeSize(radius, node.depth);
            return node.depth < maxDepth && math.distance(camera, node.Centre * radius) < size * (splitFactor + 0.5f);
        }

        // Fills draw with nodes that tile the sphere exactly once and wanted with every node
        // that should have a mesh. A node is only replaced by its children once all four are
        // ready, so the surface never shows a hole while detail streams in. shown is the nodes
        // whose children were drawn last time, which linger; showing is filled with this time's.
        public static void Select(float3 camera, float radius, int maxDepth, float splitFactor,
            Func<NodeId, bool> ready, List<NodeId> draw, List<NodeId> wanted, HashSet<NodeId> shown = null, HashSet<NodeId> showing = null)
        {
            draw.Clear();
            wanted.Clear();
            showing?.Clear();
            for (int face = 0; face < CubeSphere.Faces; face++)
            {
                var root = new NodeId(face, 0, 0, 0);
                wanted.Add(root);
                Visit(root);
            }

            void Visit(NodeId node)
            {
                if (WantsSplit(node, camera, radius, maxDepth, shown != null && shown.Contains(node) ? splitFactor * Linger : splitFactor))
                {
                    bool allReady = true;
                    for (int i = 0; i < 4; i++)
                    {
                        wanted.Add(node.Child(i));
                        allReady &= ready == null || ready(node.Child(i));
                    }
                    if (allReady)
                    {
                        showing?.Add(node);
                        for (int i = 0; i < 4; i++) Visit(node.Child(i));
                        return;
                    }
                }
                draw.Add(node);
            }
        }
    }

    // How far off things are worth drawing. Nothing may flicker: a thing appears once it is big
    // enough to see, and goes only when it is out of sight or too small.
    public static class Detail
    {
        // The distance at which something this tall covers only this many pixels, on a screen so
        // many pixels high with this vertical field of view in degrees.
        public static float Range(float height, float pixels, float screen, float fov) => height * screen / (2f * math.tan(math.radians(fov) / 2f) * pixels);

        // The size to draw something at: whole until the last fifth of its range, where it shrinks away.
        public static float Fade(float distance, float range) => math.saturate((range - distance) / (0.2f * range));

        // Whether something reaching this far from a body's centre, in this direction, shows over
        // the horizon of an eye above ground that is nowhere lower than floor.
        public static bool OverHorizon(float3 eye, float3 direction, float top, float floor)
        {
            float height = math.length(eye);
            return height <= floor || math.dot(eye / height, direction) > math.cos(math.min(math.PI, math.acos(floor / height) + math.acos(math.min(1f, floor / top))));
        }
    }
}
