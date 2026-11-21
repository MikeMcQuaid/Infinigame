using System;
using Unity.Mathematics;

namespace Infinigame.Core
{
    public struct WalkerInput
    {
        public float2 move; // x strafe, y forward
        public float yaw; // radians to turn this step, positive to the right
        public bool jump, sprint; // holding jump in the air fires the jetpack
        public bool space;
        public float3 gaze; // tracked headset direction in the body's frame, when available
        public float lift;
        public bool bare; // with nothing on their back, so that holding jump in the air does nothing
        public bool riding; // on the bike: far faster, and it goes only where it points
        public float pitch; // radians the player is looking above the level, for where the pack pushes once well off the ground
    }

    // Position is the feet, relative to the body's centre.
    [Serializable]
    public struct WalkerState
    {
        public float3 position, velocity, forward;
        public float3 ground; // normal of the ground being touched, zero in the air
        public float fuel, height; // metres clear of the ground
        // The hardest landing since this was last cleared: speed into the ground, m/s.
        public float impact;
        public bool grounded, swimming;
    }

    // First-person movement on a sphere: the ground is the terrain height function itself,
    // so there is nothing to fall through and nothing to stream in.
    public static class Walker
    {
        public const float EyeHeight = 1.7f, JumpSpeed = 6f, Acceleration = 60f, AirAcceleration = 10f, BikeAcceleration = 12f;
        public const float JetpackFuel = 1.5f, JetpackRecharge = 0.75f, JetpackSpeed = 14f;
        // How far off the ground the pack starts to fly its wearer wherever they look, how hard, and how fast it will fly them straight down.
        public const float Adrift = 5f, AdriftThrust = 20f, SoftLanding = 8f;
        public const float SwimSpeed = 3f, FloatDepth = 1.3f, StepDown = 0.4f;
        const float WalkableSlope = 0.42f; // cosine of 65 degrees: anything gentler can be scrambled up

        public static WalkerState Spawn(BodyDescriptor body, float3 direction, float3 facing)
        {
            direction = math.normalize(direction);
            return new WalkerState
            {
                position = direction * body.GroundRadius(direction),
                forward = math.normalize(Project(facing, direction)), fuel = JetpackFuel, grounded = true,
            };
        }

        public static void Step(ref WalkerState s, in WalkerInput input, BodyDescriptor body, float dt)
        {
            float3 up = math.normalize(s.position);
            s.forward = math.normalize(Project(math.rotate(quaternion.AxisAngle(up, input.yaw), s.forward), up));
            float2 move = input.move / math.max(1f, math.length(input.move));
            // a bike has no sideways, and backs up only slowly
            if (input.riding) move = new float2(0f, math.max(move.y, -0.25f));
            float3 heading = s.forward * move.y + math.cross(up, s.forward) * move.x;
            float3 level = Project(s.velocity, up);
            float r = math.length(s.position);
            bool jumped = false;
            float3 aimForward = s.forward * math.cos(input.pitch) + up * math.sin(input.pitch);
            float3 sight = math.normalizesafe(input.gaze, aimForward);
            if (input.space && !input.bare && !input.riding)
            {
                float3 wish = (math.lengthsq(input.gaze) > 0 ? math.normalizesafe(Project(sight, up), s.forward) : aimForward) * move.y + math.cross(up, s.forward) * move.x;
                wish += input.jump ? sight : up * input.lift;
                wish /= math.max(1, math.length(wish));
                s.velocity = MoveTowards(s.velocity, wish * (input.sprint ? 65 : 28), 22 * dt);
                s.position += s.velocity * dt;
                s.grounded = false; s.swimming = false;
                up = math.normalize(s.position);
                s.height = math.length(s.position) - body.GroundRadius(up);
                if (s.height <= 0)
                {
                    s.position -= up * s.height; s.height = 0;
                    s.velocity -= up * math.min(0, math.dot(s.velocity, up));
                    s.ground = body.GroundNormal(up); s.grounded = true;
                }
                return;
            }

            bool directedBurn = !s.grounded && s.height > Adrift && input.jump && !input.bare && s.fuel > 0 && (math.lengthsq(input.gaze) > 0 || math.dot(sight, up) < -0.15f);
            s.swimming = body.SeaRadius > 0 && r + EyeHeight < body.SeaRadius && !body.Dry(s.position);
            if (s.grounded)
            {
                // climbing costs up to two fifths of your pace, going down costs nothing
                float pace = (input.riding ? Tuning.BikeSpeed : input.sprint ? Tuning.SprintSpeed : Tuning.WalkSpeed) * (1f - 0.4f * math.saturate(math.dot(heading, -s.ground) / 0.9f));
                if (s.swimming) pace *= 0.65f;
                // a jump taken the moment the ground is touched keeps all the speed the leap before it had
                if (!input.jump) s.velocity = MoveTowards(level, heading * pace, (input.riding ? BikeAcceleration : Acceleration) * dt);
                else
                {
                    s.velocity = level + up * JumpSpeed;
                    s.grounded = false;
                    jumped = true;
                }
            }
            else
            {
                s.velocity += body.GravityAt(s.position) * dt;
                // In the air a push only ever adds speed the way it points, up to a walk's worth, and
                // takes none away: so a leap can be steered, and one pushed across its own path gets
                // faster. The jetpack, while it burns, pushes harder and further. Neither pushes past
                // twice that, nor anywhere near the speed that would carry a leap right round a
                // small moon and never bring it down.
                bool burning = input.jump && !input.bare && s.fuel > 0f;
                float wish = burning ? JetpackSpeed : Tuning.WalkSpeed, room = wish - math.dot(level, heading), pull = math.length(body.GravityAt(s.position));
                if (!input.bare && !input.riding && s.height > Adrift)
                {
                    // Above the ground, a tracked burn follows the headset and counters local gravity.
                    float3 aim = directedBurn ? sight : math.normalizesafe(sight * move.y + math.cross(up, s.forward) * move.x);
                    float most = math.lerp(math.min(JetpackSpeed, 0.6f * math.sqrt(pull * r)), SoftLanding, math.saturate(-math.dot(aim, up)));
                    float3 push = aim * math.clamp(most - math.dot(s.velocity, aim), 0f, AdriftThrust * dt);
                    if (directedBurn)
                    {
                        s.velocity -= body.GravityAt(s.position) * dt;
                        s.velocity += push;
                        s.fuel -= dt;
                    }
                    else s.velocity += push - up * math.max(0f, math.dot(push, up) - 0.6f * pull * dt);
                }
                else if (math.lengthsq(move) > 0f && room > 0f && !input.riding && math.length(s.velocity) < math.min(2f * wish, 0.6f * math.sqrt(pull * r)))
                    s.velocity += heading * math.min(room, (burning ? 2f : 1f) * AirAcceleration * dt);
            }

            if (!s.grounded && input.jump && !jumped && !directedBurn && !input.bare && s.fuel > 0f)
            {
                // thrust scales with local gravity: a strong hop anywhere, never an escape from a moon
                s.velocity += up * ((math.length(body.GravityAt(s.position)) + 4f) * dt);
                s.fuel -= dt;
            }

            // Water slows motion but supplies no flotation. Thrusters still work under pressure.
            if (s.swimming && !s.grounded) s.velocity *= math.exp(-0.6f * dt);
            s.position += s.velocity * dt;
            up = math.normalize(s.position);
            float gap = math.length(s.position) - body.GroundRadius(up);
            bool touching = gap <= 0f || (s.grounded && !jumped && gap < StepDown);
            s.height = touching ? 0f : gap;
            if (touching) s.fuel = math.min(JetpackFuel, s.fuel + JetpackRecharge * dt);
            if (touching)
            {
                s.position = up * body.GroundRadius(up);
                s.ground = body.GroundNormal(up);
                // too steep to stand on: stay in contact but keep sliding, except in the shallows
                // where you can always clamber out of the water
                s.grounded = math.dot(s.ground, up) >= WalkableSlope || math.length(s.position) < body.SeaRadius + 0.5f;
                float into = math.dot(s.velocity, s.ground);
                s.impact = math.max(s.impact, s.swimming ? math.min(-into, SoftLanding) : -into);
                if (s.grounded) s.velocity = Project(s.velocity, up);
                else if (into < 0f) s.velocity -= s.ground * into;
            }
            else
            {
                s.grounded = false;
                s.ground = float3.zero;
            }
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
