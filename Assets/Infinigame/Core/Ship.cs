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
        // The hardest knock since this was last cleared: speed into whatever was hit, m/s.
        public float impact;
        public bool landed;
    }

    // A simulated ship: thrust, inertia and gravity integrated every step. With flight assist the
    // engines chase a target velocity, cancel gravity and follow the horizon; without it the ship
    // is purely Newtonian and will happily orbit.
    public static class Ship
    {
        public const float Clearance = 1.2f, Acceleration = 60f, SideSpeed = 40f, HoverHeight = 8f;
        // How wide a patch of level ground a ship needs under it to set down without burying a wing, as a radius.
        public const float Pad = 12f;
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
            // height above what can be seen, which for a giant is its cloud tops, though a ship can sink far into those
            float altitude = math.max(0f, math.length(s.position) - Surface(body, up));
            float boost = input.boost ? 2f : 1f, nearness = 1f - math.smoothstep(0.5f * body.radius, 1.5f * body.radius, altitude);

            // rotation: rate commands, eased so the ship has some weight
            float3 turn = input.turn;
            if (assist && body.kind != BodyKind.Station && math.abs(turn.z) < 0.01f) turn.z += nearness * RollLevelling * math.dot(math.rotate(s.rotation, new float3(1, 0, 0)), up);
            if (input.turn.z != 0 && turn.x == 0 && turn.y == 0) s.angularVelocity.xy = 0;
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
                if (body.kind != BodyKind.Station && thrust.y >= 0f)
                {
                    float clear = math.length(s.position) - Floor(body, up);
                    for (float t = 0.25f; t < 0.8f; t += 0.25f)
                        clear = math.min(clear, math.length(s.position) - Floor(body, math.normalize(s.position + s.velocity * t)));
                    s.velocity += up * math.max(0f, math.min((HoverHeight - clear) * 6f, 250f) - math.dot(s.velocity, up));
                }
            }
            else
                s.velocity += (math.rotate(s.rotation, thrust * new float3(SideSpeed, SideSpeed, Acceleration * boost)) + body.GravityAt(s.position)) * dt;

            s.position += s.velocity * dt;
            float3 newUp = math.normalize(s.position);
            if (body.kind == BodyKind.Station) return;
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
            if (into < 0f)
            {
                s.impact = math.max(s.impact, -into);
                s.velocity -= normal * (into * 1.2f);
            }
            s.velocity *= math.max(0f, 1f - Friction * dt);
            s.rotation = math.slerp(s.rotation, Upright(s.rotation, normal), math.saturate(6f * dt));
            // only ground can be settled on: not a giant's depths, a star or a station's hull
            if (body.kind == BodyKind.Rock && math.lengthsq(s.velocity) < 1f && (input.land || (thrust.y <= 0f && thrust.z <= 0f)))
            {
                s.landed = true;
                s.velocity = s.angularVelocity = float3.zero;
                s.rotation = Upright(s.rotation, normal);
            }
        }

        // Whether the ground straight below is no place to set down: a slope steeper than a ship
        // can sit on, or a hollow whose sides would stand a metre into its wings. A ship perches
        // on a rise happily enough, and the sea is flat.
        public static bool Rough(BodyDescriptor body, float3 direction)
        {
            direction = math.normalize(direction);
            float middle = body.GroundRadius(direction), span = 0.7f * Pad;
            if (body.kind != BodyKind.Rock || middle < body.SeaRadius) return false;
            float3 along = math.normalize(math.cross(direction, new float3(0.2f, 0.9f, 0.4f))), across = math.cross(direction, along);
            for (int i = 0; i < 4; i++)
            {
                float3 reach = (along * math.cos(i * math.PI / 4f) + across * math.sin(i * math.PI / 4f)) * span;
                float one = body.GroundRadius(math.normalize(direction * middle + reach)), other = body.GroundRadius(math.normalize(direction * middle - reach));
                if (math.abs(one - other) > 0.8f * span || one + other - 2f * middle > 2f) return true;
            }
            return false;
        }

        // What is seen from above: the ground, the sea or a giant's cloud tops.
        public static float Surface(BodyDescriptor body, float3 direction) => math.max(body.GroundRadius(direction), body.SeaRadius);

        // How low a ship can go. It sits on the ground and floats on the sea, but nothing holds it up
        // in a giant's clouds: it can sink a quarter of the way to the centre, if the heat lets it.
        public static float Floor(BodyDescriptor body, float3 direction) => body.kind == BodyKind.Gas ? 0.75f * body.radius : Surface(body, direction);

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
