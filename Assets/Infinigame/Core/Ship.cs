using System;
using Unity.Mathematics;

namespace Infinigame.Core
{
    public struct ShipInput
    {
        public float3 thrust; // x strafe, y lift, z forward, each -1 to 1
        public float3 turn; // rad/s: x nose up, y nose right, z roll right
        public bool boost, land;
    }

    // Position is the ship's centre, relative to the body's centre.
    [Serializable]
    public struct ShipState
    {
        public float3 position, velocity, angularVelocity;
        public quaternion rotation;
        public bool landed;
    }

    // A simulated ship: thrust, inertia and gravity integrated every step. With flight assist the
    // engines chase a target velocity, cancel gravity and follow the horizon; without it the ship
    // is purely Newtonian and will happily orbit.
    public static class Ship
    {
        public const float Clearance = 1.2f, Acceleration = 60f, SideSpeed = 40f, HoverHeight = 8f;
        const float TurnResponse = 8f, RollLevelling = 2f, Friction = 4f;

        // Stands in for air resistance: slow near the ground, fast in open space.
        public static float MaxSpeed(BodyDescriptor body, float altitude) => math.lerp(Tuning.ShipCruiseSpeed, Tuning.ShipSpaceSpeed,
            math.smoothstep(0.3f * body.radius, 2f * body.radius, altitude));

        public static ShipState Spawn(BodyDescriptor body, float3 direction, float3 facing)
        {
            direction = math.normalize(direction);
            return new ShipState
            {
                position = direction * (Floor(body, direction) + Clearance),
                rotation = quaternion.LookRotationSafe(Walker.Project(facing, direction), direction), landed = true,
            };
        }

        public static void Step(ref ShipState s, in ShipInput input, BodyDescriptor body, bool assist, float dt)
        {
            float3 thrust = math.clamp(input.thrust, -1f, 1f);
            if (s.landed)
            {
                if (input.land || (thrust.y <= 0f && thrust.z <= 0f)) return;
                s.landed = false;
            }

            float3 up = math.normalize(s.position);
            float altitude = math.length(s.position) - Floor(body, up);
            float boost = input.boost ? 2f : 1f, nearness = 1f - math.smoothstep(0.5f * body.radius, 1.5f * body.radius, altitude);

            // rotation: rate commands, eased so the ship has some weight
            float3 turn = input.turn;
            if (assist && math.abs(turn.z) < 0.01f) turn.z += nearness * RollLevelling * math.dot(math.rotate(s.rotation, new float3(1, 0, 0)), up);
            s.angularVelocity = math.lerp(s.angularVelocity, new float3(-turn.x, turn.y, -turn.z), math.saturate(TurnResponse * dt));
            s.rotation = math.normalize(math.mul(s.rotation, quaternion.EulerXYZ(s.angularVelocity * dt)));
            if (input.land) s.rotation = math.slerp(s.rotation, Upright(s.rotation, up), math.saturate(2f * dt));

            if (input.land)
                s.velocity = Walker.MoveTowards(s.velocity, -up * math.clamp(altitude * 0.5f, 2f, 20f), Acceleration * dt);
            else if (assist)
            {
                float3 target = math.rotate(s.rotation, thrust * new float3(SideSpeed, SideSpeed, MaxSpeed(body, altitude) * boost * (thrust.z < 0f ? 0.3f : 1f)));
                s.velocity = Walker.MoveTowards(s.velocity, target, Acceleration * boost * dt);
                // terrain following: the hover engines lift the ship over whatever lies under or ahead of it
                if (thrust.y >= 0f)
                {
                    float clear = altitude;
                    for (float t = 0.25f; t < 0.8f; t += 0.25f)
                        clear = math.min(clear, math.length(s.position) - Floor(body, math.normalize(s.position + s.velocity * t)));
                    s.velocity += up * math.max(0f, math.min((HoverHeight - clear) * 6f, 250f) - math.dot(s.velocity, up));
                }
            }
            else
                s.velocity += (math.rotate(s.rotation, thrust * new float3(SideSpeed, SideSpeed, Acceleration * boost)) + body.GravityAt(s.position)) * dt;

            s.position += s.velocity * dt;
            float3 newUp = math.normalize(s.position);
            if (assist)
            {
                // horizon lock: carry heading and velocity round the curve so level flight laps the body
                quaternion follow = math.slerp(quaternion.identity, FromTo(up, newUp), nearness);
                s.rotation = math.normalize(math.mul(follow, s.rotation));
                s.velocity = math.rotate(follow, s.velocity);
            }

            float floor = Floor(body, newUp) + Clearance;
            if (math.length(s.position) >= floor) return;
            float3 normal = body.GroundRadius(newUp) < body.SeaRadius ? newUp : body.GroundNormal(newUp);
            s.position = newUp * floor;
            float into = math.dot(s.velocity, normal);
            if (into < 0f) s.velocity -= normal * (into * 1.2f);
            s.velocity *= math.max(0f, 1f - Friction * dt);
            s.rotation = math.slerp(s.rotation, Upright(s.rotation, normal), math.saturate(6f * dt));
            if (math.lengthsq(s.velocity) < 1f && (input.land || (thrust.y <= 0f && thrust.z <= 0f)))
            {
                s.landed = true;
                s.velocity = s.angularVelocity = float3.zero;
                s.rotation = Upright(s.rotation, normal);
            }
        }

        // Ships sit on the ground or float on the sea.
        static float Floor(BodyDescriptor body, float3 direction) => math.max(body.GroundRadius(direction), body.SeaRadius);

        static quaternion Upright(quaternion rotation, float3 up) =>
            quaternion.LookRotationSafe(Walker.Project(math.rotate(rotation, new float3(0, 0, 1)), up), up);

        static quaternion FromTo(float3 from, float3 to)
        {
            float3 axis = math.cross(from, to);
            float sine = math.length(axis);
            return sine < 1e-7f ? quaternion.identity : quaternion.AxisAngle(axis / sine, math.asin(math.min(1f, sine)));
        }
    }
}
