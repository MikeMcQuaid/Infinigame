using System.Collections.Generic;
using Unity.Mathematics;

namespace Infinigame.Core
{
    public struct RelicSite
    {
        public int id, body;
        public float3 position; // relative to the body's centre
    }

    // The things worth finding in a system. Placement is a pure function of the descriptor, so the
    // same seed always hides the same relics in the same places.
    public static class Relics
    {
        public const float HoverHeight = 1.5f, WalkRange = 4f, ShipRange = 12f;

        public static List<RelicSite> Place(SolarSystemDescriptor system)
        {
            var sites = new List<RelicSite>();
            for (int b = 0; b < system.bodies.Length; b++)
            {
                var body = system.bodies[b];
                if (body.kind == BodyKind.Sun) continue;
                var rnd = new Random(math.hash(new uint2(system.seed, (uint)b)) | 1u);
                var candidates = new float3[600];
                var heights = new float[candidates.Length];
                for (int i = 0; i < candidates.Length; i++)
                {
                    candidates[i] = rnd.NextFloat3Direction();
                    heights[i] = body.GroundRadius(candidates[i]) - body.radius;
                }

                // Each target height is a kind of place: peaks, open sea, a shore and the plains on a
                // planet; a crater floor and a high rim on a moon. Sites keep well apart.
                const float highest = 1e4f, lowest = -1e4f;
                float[] targets = body.kind == BodyKind.Planet
                    ? new[] { highest, highest, lowest, 2f, body.terrain.plains }
                    : new[] { lowest, highest };
                var chosen = new List<float3>();
                foreach (float target in targets)
                {
                    int best = -1;
                    for (int i = 0; i < candidates.Length; i++)
                    {
                        bool apart = true;
                        foreach (float3 other in chosen) apart &= math.dot(other, candidates[i]) < 0.8f;
                        if (apart && (best < 0 || math.abs(heights[i] - target) < math.abs(heights[best] - target))) best = i;
                    }
                    chosen.Add(candidates[best]);
                    sites.Add(new RelicSite
                    {
                        id = b * 100 + chosen.Count, body = b,
                        position = candidates[best] * (math.max(body.radius + heights[best], body.SeaRadius) + HoverHeight),
                    });
                }
            }
            return sites;
        }
    }
}
