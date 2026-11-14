using System;
using Unity.Mathematics;

namespace Infinigame.Core
{
    public struct WalkerInput
    {
        public float2 move; // x strafe, y forward
        public float yaw; // radians to turn this step, positive to the right
        public bool jump, sprint; // holding jump in the air fires the jetpack
    }

    // Position is the feet, relative to the body's centre.
    [Serializable]
    public struct WalkerState
    {
        public float3 position, velocity, forward;
        public float fuel;
        public bool grounded, swimming;
    }

    // First-person movement on a sphere: the ground is the terrain height function itself,
    // so there is nothing to fall through and nothing to stream in.
    public static class Walker
    {
        public const float EyeHeight = 1.7f, JumpSpeed = 5f, Acceleration = 40f, AirAcceleration = 8f;
        public const float JetpackFuel = 1.5f, JetpackRecharge = 0.75f;
        public const float SwimSpeed = 3f, FloatDepth = 1.3f, StepDown = 0.4f;
        const float WalkableSlope = 0.643f; // cosine of 50 degrees

        public static WalkerState Spawn(BodyDescriptor body, float3 direction, float3 facing)
        {
            direction = math.normalize(direction);
            return new WalkerState
            {
                position = direction * math.max(body.GroundRadius(direction), body.SeaRadius - FloatDepth),
                forward = math.normalize(Project(facing, direction)), fuel = JetpackFuel, grounded = true,
            };
        }

        public static void Step(ref WalkerState s, in WalkerInput input, BodyDescriptor body, float dt)
        {
            float3 up = math.normalize(s.position);
            s.forward = math.normalize(Project(math.rotate(quaternion.AxisAngle(up, input.yaw), s.forward), up));
            float2 move = input.move / math.max(1f, math.length(input.move));
            float3 heading = s.forward * move.y + math.cross(up, s.forward) * move.x;
            float3 level = Project(s.velocity, up);
            float climb = math.dot(s.velocity, up), r = math.length(s.position), floating = body.SeaRadius - FloatDepth;
            bool jumped = false;

            s.swimming = body.GroundRadius(up) < floating - 0.2f && r < floating + 0.5f;
            if (s.swimming)
            {
                // buoyancy is a damped spring that holds the head above the waves
                climb += ((floating - r) * 6f - climb * 4f) * dt;
                s.velocity = MoveTowards(level, heading * SwimSpeed, Acceleration * 0.3f * dt) + up * climb;
                s.grounded = false;
            }
            else if (s.grounded)
            {
                s.velocity = MoveTowards(level, heading * (input.sprint ? Tuning.SprintSpeed : Tuning.WalkSpeed), Acceleration * dt);
                if (input.jump)
                {
                    s.velocity += up * JumpSpeed;
                    s.grounded = false;
                    jumped = true;
                }
            }
            else
            {
                s.velocity += body.GravityAt(s.position) * dt;
                if (math.lengthsq(move) > 0f)
                    s.velocity = MoveTowards(level, heading * Tuning.WalkSpeed, AirAcceleration * dt) + up * math.dot(s.velocity, up);
            }

            if (!s.grounded && input.jump && !jumped && s.fuel > 0f)
            {
                // thrust scales with local gravity: a strong hop anywhere, never an escape from a moon
                s.velocity += up * ((1.5f * math.length(body.GravityAt(s.position)) + 3f) * dt);
                s.fuel -= dt;
            }

            s.position += s.velocity * dt;
            up = math.normalize(s.position);
            float gap = math.length(s.position) - body.GroundRadius(up);
            bool touching = gap <= 0f || (s.grounded && !jumped && gap < StepDown);
            if (touching || s.swimming) s.fuel = math.min(JetpackFuel, s.fuel + JetpackRecharge * dt);
            if (touching)
            {
                s.position = up * body.GroundRadius(up);
                float3 normal = body.GroundNormal(up);
                float into = math.dot(s.velocity, normal);
                if (into < 0f) s.velocity -= normal * into;
                // too steep to stand on: stay in contact but keep sliding, except in the shallows
                // where you can always clamber out of the water
                s.grounded = math.dot(normal, up) >= WalkableSlope || math.length(s.position) < body.SeaRadius + 0.5f;
            }
            else s.grounded = false;
            s.forward = math.normalize(Project(s.forward, up));
        }

        public static float3 Project(float3 v, float3 normal) => v - normal * math.dot(v, normal);

        public static float3 MoveTowards(float3 from, float3 to, float maxDelta)
        {
            float3 delta = to - from;
            float distance = math.length(delta);
            return distance <= maxDelta ? to : from + delta * (maxDelta / distance);
        }
    }
}
