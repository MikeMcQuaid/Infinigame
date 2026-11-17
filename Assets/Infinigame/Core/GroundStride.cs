using Unity.Mathematics;

namespace Infinigame.Core
{
    public static class GroundStride
    {
        // Forward offset and lift. A foot spends three fifths of its cycle on the ground.
        public static float2 Foot(float cycle)
        {
            float t = math.frac(cycle);
            if (t < 0.6f) return new float2(0.5f - t / 0.6f, 0);
            float swing = (t - 0.6f) / 0.4f;
            return new float2(math.smoothstep(0, 1, swing) - 0.5f, math.sin(swing * math.PI));
        }
    }
}
