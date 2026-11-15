using System.Collections.Generic;
using Unity.Mathematics;

namespace Infinigame.Core
{
    // Flies the ship to another world, far faster than a pilot may: it swings round anything in
    // the way, slows near worlds and stops a short way off its target, moving as the target moves.
    public static class Autopilot
    {
        public const float Speed = 60000f, Acceleration = 5000f;

        // How far from a world's centre the ship is brought to rest: near enough to fill the
        // view, clear of any rings and inside the world's own influence. A star is kept at arm's
        // length, where its heat is bearable.
        public static float Standoff(BodyDescriptor body) => body.kind == BodyKind.Sun ? 4.2f * body.radius
            : math.min(math.max(2.2f * body.radius, 1.12f * body.ringOuter), 0.9f * body.soiRadius);

        // How close the autopilot will pass a world that is merely in the way: clear of its
        // highest ground and its rings, or of a star's heat.
        public static float Berth(BodyDescriptor body) => body.kind == BodyKind.Sun ? 4f * body.radius
            : 1.15f * math.max(body.radius + body.terrain.MaxHeight, body.ringOuter);

        // One step towards a target whose centre is at centre, moving at drift, both in the frame
        // the ship's state is in. worlds lists everything else to steer round as (centre, berth).
        // Returns true once the ship has arrived.
        public static bool Step(ref ShipState s, float3 centre, float3 drift, float standoff, IReadOnlyList<float4> worlds, float dt)
        {
            float3 to = centre - s.position;
            float remaining = math.length(to) - standoff;
            float3 heading = math.normalizesafe(to, math.rotate(s.rotation, new float3(0, 0, 1)));
            if (remaining < 0f) heading = -heading;
            // no faster than it can stop from, easing in over the last stretch, and gently when close to anything
            float speed = math.min(math.min(Speed, math.sqrt(1.6f * Acceleration * math.abs(remaining))), 2f * math.abs(remaining) + 1f);
            for (int i = 0; worlds != null && i < worlds.Count; i++)
            {
                float distance = math.max(math.distance(s.position, worlds[i].xyz), 1e-3f), clear = distance - worlds[i].w;
                float3 inward = (worlds[i].xyz - s.position) / distance;
                speed = math.min(speed, 120f + 4f * math.max(clear, 0f));
                // It lies ahead: if the course cuts through the space kept round it, turn only as far
                // as it takes to graze that space, which close in means flying round it. The turn
                // grows and fades smoothly as the ship moves, so the ship never shakes.
                float graze = math.asin(math.min(1f, 1.15f * worlds[i].w / distance)), towards = math.dot(inward, heading);
                if (towards > 0f && towards * distance < math.length(to) && math.acos(math.min(towards, 1f)) < graze)
                    heading = inward * math.cos(graze)
                        + math.normalizesafe(heading - inward * towards, math.normalize(math.cross(inward, new float3(0.3f, 0.9f, 0.1f)))) * math.sin(graze);
                // inside its berth: climb out as well, the harder the deeper in
                if (clear < 0f) heading = math.normalize(heading - inward * (3f * math.saturate(-clear / (0.1f * worlds[i].w))));
            }

            if (math.abs(remaining) < 3f && math.distance(s.velocity, drift) < 10f)
            {
                s.velocity = drift;
                return true;
            }
            s.velocity = Walker.MoveTowards(s.velocity, drift + heading * speed, Acceleration * dt);
            s.position += s.velocity * dt;
            s.landed = false;
            s.angularVelocity = float3.zero;
            // the nose follows the course, keeping whatever way up it was
            s.rotation = math.slerp(s.rotation, quaternion.LookRotationSafe(heading, math.rotate(s.rotation, new float3(0, 1, 0))), math.saturate(3f * dt));
            return false;
        }
    }
}
