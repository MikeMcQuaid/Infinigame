using Unity.Mathematics;

namespace Infinigame.Core
{
    // Small articulated bodies use the same height field as living bodies.
    public class Ragdoll
    {
        public float3[] positions, velocities;
        public float[] lengths;
        public bool[] joined;
        public Ragdoll(float3[] points, float3 velocity)
        {
            positions = points; velocities = new float3[points.Length];
            lengths = new float[points.Length]; joined = new bool[points.Length];
            for (int i = 0; i < points.Length; i++)
            { velocities[i] = velocity; lengths[i] = math.distance(points[0], points[i]); joined[i] = i > 0; }
        }
        public void Strike(float3 from, float3 step, float force, float radius = 0.7f)
        {
            for (int i = 0; i < positions.Length; i++)
                if (Combat.Crosses(from, step, positions[i], radius))
                { joined[i] = false; velocities[i] += math.normalizesafe(step) * math.min(25, force * 0.12f); }
        }
        public void Step(BodyDescriptor body, float dt, float radius)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                velocities[i] += body.GravityAt(positions[i]) * dt;
                positions[i] += velocities[i] * dt;
            }
            for (int pass = 0; pass < 4; pass++)
            {
                for (int i = 1; i < positions.Length; i++)
                {
                    if (!joined[i]) continue;
                    float3 delta = positions[i] - positions[0];
                    float3 correction = math.normalizesafe(delta) * (math.length(delta) - lengths[i]) * 0.5f;
                    positions[i] -= correction; positions[0] += correction;
                }
                for (int i = 0; i < positions.Length; i++)
                {
                    float3 up = math.normalizesafe(positions[i], new float3(0, 1, 0));
                    float floor = body.GroundRadius(up) + radius;
                    if (body.kind == BodyKind.Station || math.length(positions[i]) >= floor) continue;
                    positions[i] = up * floor;
                    velocities[i] = Walker.Project(velocities[i], up) * math.exp(-8 * dt);
                }
            }
        }
    }
}
