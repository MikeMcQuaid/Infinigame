using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Infinigame.Core
{
    public struct Wall
    {
        public float3 centre, half;
        public bool door, broken;
        public float damage;
        public Wall(float3 centre, float3 half, bool door = false) { this.centre = centre; this.half = half; this.door = door; broken = false; damage = 0; }
    }

    // Collision belongs to the built rooms only. Outside these boxes the terrain stays authoritative.
    public class Interior
    {
        public Wall[] walls;
        public float3 entrance, core, console;
        public static Interior Outpost() => new Interior
        {
            entrance = new float3(0, 1, -12), core = new float3(0, 2, 19), console = new float3(4, 1, -9),
            walls = new[]
            {
                new Wall(new float3(-8, 3, 6), new float3(0.5f, 3, 18)), new Wall(new float3(8, 3, 6), new float3(0.5f, 3, 18)),
                new Wall(new float3(0, 6, 6), new float3(8, 0.4f, 18)), new Wall(new float3(0, -0.4f, 6), new float3(8, 0.4f, 18)),
                new Wall(new float3(0, 3, 24), new float3(8, 3, 0.5f)),
                new Wall(new float3(-5, 3, 6), new float3(3, 3, 0.5f)), new Wall(new float3(5, 3, 6), new float3(3, 3, 0.5f)),
                new Wall(new float3(0, 3, 6), new float3(2, 3, 0.5f), true),
                // A ramp to the raised rear room, expressed as shallow steps.
                new Wall(new float3(0, 0.15f, 10), new float3(7.5f, 0.15f, 1)),
                new Wall(new float3(0, 0.3f, 12), new float3(7.5f, 0.3f, 1)),
                new Wall(new float3(0, 0.45f, 14), new float3(7.5f, 0.45f, 1)),
                new Wall(new float3(0, 0.6f, 19), new float3(7.5f, 0.6f, 4)),
            },
        };
        public static Interior Citadel()
        {
            return new Interior
            {
                entrance = new float3(0, 0, -260), core = new float3(160, 0, 150), console = new float3(160, 0, 150),
                walls = new[]
                {
                    // The boundary of three joined corridors, with just one entrance.
                    new Wall(new float3(-31, 0, -135), new float3(1, 30, 125)),
                    new Wall(new float3(31, 0, -165), new float3(1, 30, 95)),
                    new Wall(new float3(80, 0, -71), new float3(50, 30, 1)),
                    new Wall(new float3(50, 0, -9), new float3(80, 30, 1)),
                    new Wall(new float3(129, 0, 85), new float3(1, 30, 95)),
                    new Wall(new float3(191, 0, 55), new float3(1, 30, 125)),
                    new Wall(new float3(160, 0, 181), new float3(30, 30, 1)),
                    new Wall(new float3(0, 31, -165), new float3(30, 1, 95)),
                    new Wall(new float3(0, -31, -165), new float3(30, 1, 95)),
                    new Wall(new float3(80, 31, -40), new float3(110, 1, 30)),
                    new Wall(new float3(80, -31, -40), new float3(110, 1, 30)),
                    new Wall(new float3(160, 31, 85), new float3(30, 1, 95)),
                    new Wall(new float3(160, -31, 85), new float3(30, 1, 95)),
                },
            };
        }
        public float3 Move(float3 from, float3 velocity, float radius, float dt, bool open, out bool hit, float stepHeight = 0)
        {
            hit = false;
            int steps = math.max(1, (int)math.ceil(math.length(velocity) * dt / math.max(0.2f, radius * 0.5f)));
            float3 at = from, delta = velocity * (dt / steps);
            for (int i = 0; i < steps; i++)
            {
                for (int axis = 0; axis < 3; axis++)
                {
                    float3 next = at; next[axis] += delta[axis];
                    bool blocked = false;
                    foreach (var wall in walls)
                    {
                        if (wall.broken || open && wall.door || !math.all(math.abs(next - wall.centre) < wall.half + radius)) continue;
                        float top = wall.centre.y + wall.half.y + radius;
                        if (axis != 1 && delta.y <= 0 && stepHeight > 0 && top - at.y > 0 && top - at.y <= stepHeight)
                        {
                            float3 raised = new float3(next.x, top + 0.001f, next.z);
                            bool clear = true;
                            foreach (var ceiling in walls)
                                if (!ceiling.broken && !(open && ceiling.door) && math.all(math.abs(raised - ceiling.centre) < ceiling.half + radius)) { clear = false; break; }
                            if (clear) { next = raised; continue; }
                        }
                        blocked = true;
                        break;
                    }
                    if (blocked) hit = true;
                    else at = next;
                }
            }
            return at;
        }
        public bool Breach(float3 point, float damage)
        {
            for (int i = 0; i < walls.Length; i++)
            {
                var wall = walls[i];
                // The reinforced shell stays intact. Doors and foundations can be punched through.
                if (wall.broken || !wall.door && wall.centre.y >= 0 || !math.all(math.abs(point - wall.centre) < wall.half + 0.15f)) continue;
                wall.damage += damage;
                wall.broken = wall.damage >= 100;
                walls[i] = wall;
                return wall.broken;
            }
            return false;
        }
        public bool Visible(float3 from, float3 to, bool open)
        {
            foreach (var wall in walls)
            {
                if (wall.broken || open && wall.door) continue;
                float3 delta = to - from;
                float near = 0f, far = 1f;
                for (int axis = 0; axis < 3 && near <= far; axis++)
                {
                    if (math.abs(delta[axis]) < 1e-6f)
                    {
                        if (math.abs(from[axis] - wall.centre[axis]) > wall.half[axis]) near = 2;
                        continue;
                    }
                    float a = (wall.centre[axis] - wall.half[axis] - from[axis]) / delta[axis], b = (wall.centre[axis] + wall.half[axis] - from[axis]) / delta[axis];
                    near = math.max(near, math.min(a, b)); far = math.min(far, math.max(a, b));
                }
                if (near <= far) return false;
            }
            return true;
        }
    }

    public struct Shield
    {
        public float3 centre;
        public float radius;
        public bool active;
        public bool Blocks(float3 from, float3 step, out float3 at)
        {
            at = from + step;
            if (!active || math.distancesq(from, centre) < radius * radius || !Combat.Crosses(from, step, centre, radius)) return false;
            float3 direction = math.normalizesafe(step), offset = from - centre;
            float b = math.dot(offset, direction), d = b * b - math.lengthsq(offset) + radius * radius;
            at = from + direction * math.max(0f, -b - math.sqrt(math.max(0f, d)));
            return true;
        }
    }
}
