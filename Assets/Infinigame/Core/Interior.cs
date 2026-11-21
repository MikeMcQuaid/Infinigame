using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Infinigame.Core
{
    [Serializable]
    public class Ruin
    {
        public int stage;
        public bool open, destroyed;
        public double detonates;
        public List<int> broken = new List<int>();
    }

    public struct Burrow
    {
        public float3 origin;
        public quaternion rotation;
        public float3 Local(float3 point) => math.rotate(math.inverse(rotation), point - origin);
        public bool InPassage(float3 point)
        {
            float3 local = Local(point);
            float height = local.y - Interior.TunnelFloor(local.z);
            return math.abs(local.x) < 6 && local.z > -24 && local.z < 40 && height > -4 && height < 8;
        }
        public bool Contains(float3 point)
        {
            var local = Local(point);
            return math.abs(local.x) < 8 && local.z > -25 && local.z < 42 && local.y > -32 && local.y < 12;
        }
    }

    public struct Wall
    {
        public float3 centre, half;
        public bool door, broken, bedrock;
        public float damage;
        public Wall(float3 centre, float3 half, bool door = false) { this.centre = centre; this.half = half; this.door = door; broken = bedrock = false; damage = 0; }
    }

    // Collision belongs to the built rooms only. Outside these boxes the terrain stays authoritative.
    public class Interior
    {
        public Wall[] walls;
        public bool indestructible;
        HashSet<int2> corridors;
        public int escapeWall = -1;
        public bool coreDestroyed;
        public bool InsideCitadel(float3 p) => corridors != null && math.abs(p.y) < 30 && corridors.Contains((int2)math.round(new float2(p.x, p.z + 260) / 58));

        public static float2 MantleRadii(float t, float angle)
        {
            float belly = math.pow(math.max(0, math.sin(math.PI * t)), 0.57f);
            return (math.lerp(new float2(64, 59), new float2(26, 24), t) + new float2(218, 100) * belly)
                * (1 + 0.09f * math.cos(7 * angle + t * 5) + 0.047f * math.sin(14 * angle - t * 11));
        }

        bool InMantle(float3 p, float radius)
        {
            if (corridors == null || p.z < -329 - radius || p.z > 896 + radius || InsideCitadel(p)) return false;
            if (p.z < entrance.z + 4 && math.abs(p.x) + radius < 29 && math.abs(p.y) + radius < 29) return false;
            if (p.z > core.z && walls[escapeWall].broken && math.abs(p.x - core.x) + radius < 26 && math.abs(p.y) + radius < 26) return false;
            float t = math.saturate((p.z + 329) / 1225);
            float2 offset = p.xy - new float2(132 * math.smoothstep(0, 1, 3 * t), 0);
            float2 ellipse = MantleRadii(t, 0);
            float angle = math.atan2(offset.y / ellipse.y, offset.x / ellipse.x);
            return math.lengthsq(offset / (MantleRadii(t, angle) + radius)) <= 1;
        }

        Wall[] bounded;
        float3 minimum, maximum;

        bool Outside(float3 from, float3 to, float radius)
        {
            if (bounded != walls)
            {
                minimum = new float3(float.MaxValue); maximum = -minimum;
                foreach (var wall in walls)
                {
                    minimum = math.min(minimum, wall.centre - wall.half);
                    maximum = math.max(maximum, wall.centre + wall.half);
                }
                if (corridors != null) { minimum = new float3(-330, -200, -330); maximum = new float3(475, 200, 897); }
                bounded = walls;
            }
            return math.any(math.max(from, to) + radius < minimum) || math.any(math.min(from, to) - radius > maximum);
        }
        public float3 entrance, core, console;
        public float3[] route = Array.Empty<float3>();
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
        public static float TunnelFloor(float z) => -math.clamp((z + 24) * 0.42f, 0, 24);
        public static Interior Burrow(int variant = 0)
        {
            var walls = new List<Wall>();
            for (int i = 0; i < 10; i++)
            {
                float z = -21 + i * 6, floor = TunnelFloor(z);
                walls.Add(new Wall(new float3(-6, floor + (variant >= 2 ? 1 : 0), z), new float3(1.5f, variant >= 2 ? 8 : 7, 3.2f)));
                walls.Add(new Wall(new float3(6, floor + (variant >= 2 ? 1 : 0), z), new float3(1.5f, variant >= 2 ? 8 : 7, 3.2f)));
                if (i > 1) walls.Add(new Wall(new float3(0, floor + (variant >= 2 ? 9 : 7), z), new float3(7, 2, 3.2f)));
            }
            if (variant == 1)
                for (int i = 0; i < 4; i++)
                {
                    float z = -9 + i * 6;
                    walls.Add(new Wall(new float3(0, TunnelFloor(z) + 3, z), new float3(0.8f, 4, 3.2f)));
                }
            if (variant >= 2)
                foreach (float z in new[] { -8f, 14f })
                    walls.Add(new Wall(new float3(0, TunnelFloor(z) + 1.5f, z), new float3(5, 1.5f, 2)));
            walls.Add(new Wall(new float3(0, TunnelFloor(40) + 3, 40), new float3(8, 8, 3)));
            float3 entrance = new float3(0, 1, -26), core = new float3(0, TunnelFloor(32) + 1, 32);
            var route = new List<float3> { entrance };
            if (variant > 0)
            {
                // Leave the old wall indices intact for saved demolition damage.
                foreach (int z in new[] { -17, 18 })
                {
                    float side = z < 0 ? 1 : -1;
                    walls.Add(new Wall(new float3(-side * 1.65f, TunnelFloor(z) + 2, z), new float3(3.2f, 6, 1.4f)) { bedrock = true });
                    route.Add(new float3(side * 3.3f, TunnelFloor(z - 3) + (variant == 1 ? 2 : 4.3f), z - 3));
                    route.Add(new float3(side * 3.3f, TunnelFloor(z + 3) + (variant == 1 ? 2 : 4.3f), z + 3));
                    if (z < 0) route.Add(new float3(side * 3.3f, TunnelFloor(13) + (variant == 1 ? 2 : 4.3f), 13));
                }
            }
            route.Add(core);
            return new Interior { entrance = entrance, core = core, console = new float3(10000), route = route.ToArray(), walls = walls.ToArray() };
        }
        public static Interior Citadel()
        {
            var cells = new HashSet<int2>();
            int2[] turns = { new int2(0, 0), new int2(0, 3), new int2(1, 3), new int2(1, 7),
                new int2(4, 7), new int2(4, 12), new int2(2, 12), new int2(2, 18) };
            int exit = -1;
            for (int i = 1; i < turns.Length; i++)
            {
                int2 at = turns[i - 1], step = (int2)math.sign(turns[i] - at);
                cells.Add(at);
                while (!at.Equals(turns[i])) { at += step; cells.Add(at); }
            }
            var walls = new List<Wall>();
            foreach (var cell in cells)
            {
                float3 centre = new float3(cell.x * 58, 0, -260 + cell.y * 58);
                walls.Add(new Wall(centre + new float3(0, 30, 0), new float3(29, 1, 29)));
                walls.Add(new Wall(centre - new float3(0, 30, 0), new float3(29, 1, 29)));
                foreach (var side in new[] { new int2(-1, 0), new int2(1, 0), new int2(0, -1), new int2(0, 1) })
                {
                    if (cells.Contains(cell + side) || cell.Equals(int2.zero) && side.y == -1) continue;
                    if (cell.Equals(turns[^1]) && side.y == 1) exit = walls.Count;
                    walls.Add(new Wall(centre + new float3(side.x * 29, 0, side.y * 29), new float3(side.x != 0 ? 1 : 29, 30, side.y != 0 ? 1 : 29)));
                }
            }
            var route = new List<float3> { new float3(0, 0, -290) };
            foreach (var turn in turns) route.Add(new float3(turn.x * 58, 0, -260 + turn.y * 58));
            return new Interior { corridors = cells, escapeWall = exit, indestructible = true, entrance = route[0], core = route[^1], console = route[^1], route = route.ToArray(), walls = walls.ToArray() };
        }
        public bool Fits(float3 centre, float radius, bool open)
        {
            if (InMantle(centre, radius)) return false;
            foreach (var wall in walls)
                if (!wall.broken && !(wall.door && open) && math.all(math.abs(centre - wall.centre) < wall.half + radius)) return false;
            return true;
        }
        public float3 Move(float3 from, float3 velocity, float radius, float dt, bool open, out bool hit, float stepHeight = 0)
        {
            hit = false;
            if (Outside(from, from + velocity * dt, radius + stepHeight)) return from + velocity * dt;
            int steps = math.max(1, (int)math.ceil(math.length(velocity) * dt / math.max(0.2f, radius * 0.5f)));
            float3 at = from, delta = velocity * (dt / steps);
            for (int i = 0; i < steps; i++)
            {
                for (int axis = 0; axis < 3; axis++)
                {
                    if (delta[axis] == 0) continue;
                    float3 next = at; next[axis] += delta[axis];
                    if (InMantle(next, radius)) { hit = true; continue; }
                    bool blocked = false;
                    foreach (var wall in walls)
                    {
                        if (wall.broken || open && wall.door || !math.all(math.abs(next - wall.centre) < wall.half + radius)) continue;
                        float top = wall.centre.y + wall.half.y + radius;
                        if (axis != 1 && delta.y <= 0 && stepHeight > 0 && top - at.y > 0 && top - at.y <= stepHeight)
                        {
                            float3 raised = new float3(next.x, top + 0.001f, next.z);
                            if (Fits(raised, radius, open)) { next = raised; continue; }
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
        // Preserve the wall top and its save index when extending it below uneven terrain.
        public void SinkWall(int index, float bottom)
        {
            var wall = walls[index];
            float extra = math.max(0, wall.centre.y - wall.half.y - bottom) * 0.5f;
            wall.centre.y -= extra; wall.half.y += extra;
            walls[index] = wall; bounded = null;
        }

        public bool Breach(float3 point, float damage)
        {
            if (indestructible && !coreDestroyed) return false;
            for (int i = 0; i < walls.Length; i++)
            {
                var wall = walls[i];
                if (indestructible && i != escapeWall || wall.broken || wall.bedrock || !math.all(math.abs(point - wall.centre) < wall.half + 0.15f)) continue;
                wall.damage += damage;
                wall.broken = wall.damage >= (i == escapeWall ? 12 : wall.door || wall.centre.y < 0 ? 100 : 240);
                walls[i] = wall;
                return wall.broken;
            }
            return false;
        }
        public bool Visible(float3 from, float3 to, bool open)
        {
            if (Outside(from, to, 0)) return true;
            float3 delta = to - from;
            if (corridors != null)
            {
                float3 inverse = math.rcp(math.select(delta, new float3(0.00001f), math.abs(delta) < 0.00001f));
                float3 a = (minimum - from) * inverse, b = (maximum - from) * inverse;
                float start = math.max(0, math.cmax(math.min(a, b))), end = math.min(1, math.cmin(math.max(a, b)));
                if (start <= end)
                {
                    int steps = math.max(1, (int)math.ceil(math.length(delta) * (end - start) / 6));
                    for (int i = 0; i <= steps; i++)
                        if (InMantle(from + delta * math.lerp(start, end, (float)i / steps), 0)) return false;
                }
            }
            foreach (var wall in walls)
            {
                if (wall.broken || open && wall.door) continue;
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
        public bool Bounce(float3 from, ref float3 to, ref float3 velocity, float clearance)
        {
            var hull = this;
            bool inside = math.distance(from, centre) < radius;
            hull.radius = math.max(0.1f, radius + (inside ? -clearance : clearance));
            if (!hull.Blocks(from, to - from, out float3 hit)) return false;
            float3 normal = math.normalizesafe(hit - centre) * (inside ? -1 : 1);
            to = hit + normal * 0.05f;
            velocity = (velocity - 2 * math.dot(velocity, normal) * normal) * 0.55f;
            return true;
        }
        public bool Blocks(float3 from, float3 step, out float3 at)
        {
            at = from + step;
            if (!active || math.lengthsq(step) < 1e-10f) return false;
            float3 direction = math.normalize(step), offset = from - centre;
            float b = math.dot(offset, direction), d = b * b - math.lengthsq(offset) + radius * radius;
            if (d < 0) return false;
            float near = -b - math.sqrt(d), far = -b + math.sqrt(d);
            float distance = near > 0.001f ? near : far;
            if (distance < 0.001f || distance > math.length(step)) return false;
            at = from + direction * distance;
            return true;
        }
    }
}
