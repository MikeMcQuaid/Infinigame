using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

namespace Infinigame.Core
{
    // Storms that wander a world with seas. A pure function of time, so the clouds the shader draws
    // and the rain that falls on the player always agree.
    public static class Weather
    {
        public const int Storms = 4;

        // One storm: xyz is its centre as a direction in the body's frame, w its radius in radians.
        // Each follows its own great circle and swells and fades as it goes.
        public static float4 Storm(BodyDescriptor body, int index, double time)
        {
            var rnd = new Random(math.hash(new float4(body.terrain.offset, index)) | 1u);
            float3 axis = rnd.NextFloat3Direction(), start = math.normalize(math.cross(axis, rnd.NextFloat3Direction()));
            double lap = rnd.NextDouble(900, 1800), breath = rnd.NextDouble(150, 300);
            float size = rnd.NextFloat(0.22f, 0.4f) * (0.7f + 0.3f * (float)math.sin(2 * math.PI_DBL * time / breath));
            return new float4(math.rotate(quaternion.AxisAngle(axis, (float)(2 * math.PI_DBL * math.frac(time / lap))), start), size);
        }

        // How hard it is raining at a spot, from 0 to 1: nothing at a storm's edge, pouring at its heart.
        public static float Rain(BodyDescriptor body, float3 direction, double time)
        {
            float rain = 0f;
            for (int i = 0; body.SeaRadius > 0f && i < Storms; i++)
            {
                float4 storm = Storm(body, i, time);
                rain = math.max(rain, 1f - math.smoothstep(0.3f, 0.85f, math.acos(math.clamp(math.dot(direction, storm.xyz), -1f, 1f)) / storm.w));
            }
            return rain;
        }
    }
}
