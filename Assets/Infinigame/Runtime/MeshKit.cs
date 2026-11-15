using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Infinigame
{
    // The mesh-building kit behind Models. One kit collects the triangles of one mesh from skins,
    // lathes, swept sections, plates, panelled surfaces and blobs, then works out normals and UVs.
    // Each shape added is smoothed on its own: its faces share a normal where they meet at less
    // than the crease angle and stay crisp where they meet more sharply.
    // A face is seen from the side where its corners run clockwise, as Unity has it, so for a
    // quad a, b, c, d the outside is the way cross(b - a, d - a) points.
    sealed class MeshKit
    {
        public delegate Vector3 Sheet(float round, float along);

        public float crease = 40f;
        // Texture repeats per metre of the box-mapped UVs.
        public float tiling = 1f;
        // Turns what is added inside out, for the insides of hollow things.
        public bool flip;

        // Where shapes are being built. The kits of one model share it, so placing or mirroring
        // through any of them moves what is added to all of them.
        sealed class Space
        {
            public Matrix4x4 at = Matrix4x4.identity, normalAt = Matrix4x4.identity;
            public bool mirrored;
        }

        readonly Space space;
        readonly List<Vector3> corners = new List<Vector3>(), given = new List<Vector3>();
        // per triangle: the shape it belongs to and the cosine of its crease angle
        readonly List<int> shapes = new List<int>();
        readonly List<float> creases = new List<float>();
        int shape;

        public MeshKit(MeshKit sharing = null) => space = sharing == null ? new Space() : sharing.space;

        public int Triangles => shapes.Count;

        // Builds with its coordinates carried somewhere else.
        public void Place(Matrix4x4 local, Action build)
        {
            var keep = space.at;
            Set(keep * local);
            build();
            Set(keep);
        }

        // Builds twice: as given, and reflected to the other side of the model (x to -x).
        public void Mirror(Action build)
        {
            build();
            var keep = space.at;
            Set(Matrix4x4.Scale(new Vector3(-1f, 1f, 1f)) * keep);
            build();
            Set(keep);
        }

        void Set(Matrix4x4 matrix)
        {
            space.at = matrix;
            space.normalAt = matrix.inverse.transpose;
            space.mirrored = matrix.determinant < 0f;
        }

        void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 na = default, Vector3 nb = default, Vector3 nc = default)
        {
            a = space.at.MultiplyPoint3x4(a);
            b = space.at.MultiplyPoint3x4(b);
            c = space.at.MultiplyPoint3x4(c);
            // quads that close to a point leave triangles with no area
            if (Vector3.Cross(b - a, c - a).sqrMagnitude < 1e-16f) return;
            if (space.mirrored != flip) (b, c, nb, nc) = (c, b, nc, nb);
            Vector3 Given(Vector3 normal) => normal == default ? normal : space.normalAt.MultiplyVector(normal).normalized * (flip ? -1f : 1f);
            corners.Add(a);
            corners.Add(b);
            corners.Add(c);
            given.Add(Given(na));
            given.Add(Given(nb));
            given.Add(Given(nc));
            shapes.Add(shape);
            creases.Add(Mathf.Cos(crease * Mathf.Deg2Rad) - 1e-4f);
        }

        public void Triangle(Vector3 a, Vector3 b, Vector3 c)
        {
            shape++;
            Face(a, b, c);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            shape++;
            Face(a, b, c);
            Face(a, c, d);
        }

        // A skin over a row of rings, each the same number of points. Its outside is the way
        // cross(next point - point, next ring - ring) points. Ends can be capped flat.
        public void Skin(IReadOnlyList<Vector3[]> rings, bool closed = true, bool capStart = false, bool capEnd = false)
        {
            shape++;
            int n = rings[0].Length;
            for (int i = 0; i + 1 < rings.Count; i++)
            for (int j = 0; j < (closed ? n : n - 1); j++)
            {
                int k = (j + 1) % n;
                Vector3 a = rings[i][j], b = rings[i][k], c = rings[i + 1][k], d = rings[i + 1][j];
                // split along the shorter diagonal, which folds a twisted quad the gentler way
                if ((a - c).sqrMagnitude <= (b - d).sqrMagnitude)
                {
                    Face(a, b, c);
                    Face(a, c, d);
                }
                else
                {
                    Face(a, b, d);
                    Face(b, c, d);
                }
            }
            if (capStart) Cap(rings[0], true);
            if (capEnd) Cap(rings[rings.Count - 1], false);
        }

        // Fills a flat outline of any shape by clipping ears off it.
        void Cap(Vector3[] ring, bool reversed)
        {
            int n = ring.Length;
            Vector3 normal = default;
            for (int i = 0; i < n; i++) normal += Vector3.Cross(ring[i], ring[(i + 1) % n]);
            if (normal.sqrMagnitude < 1e-16f) return;
            Vector3 x = Vector3.Cross(normal, Mathf.Abs(normal.normalized.y) < 0.9f ? Vector3.up : Vector3.right).normalized, y = Vector3.Cross(normal.normalized, x);
            var flat = new Vector2[n];
            var left = new List<int>(n);
            for (int i = 0; i < n; i++)
            {
                flat[i] = new Vector2(Vector3.Dot(ring[i], x), Vector3.Dot(ring[i], y));
                left.Add(i);
            }
            float Turn(Vector2 a, Vector2 b, Vector2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            while (left.Count >= 3)
            {
                // the first corner that bulges outwards with no other point inside it; a tangled outline just loses a corner
                int ear = 0;
                for (int i = 0; i < left.Count && left.Count > 3; i++)
                {
                    Vector2 a = flat[left[(i + left.Count - 1) % left.Count]], b = flat[left[i]], c = flat[left[(i + 1) % left.Count]];
                    if (Turn(a, b, c) <= 1e-12f) continue;
                    bool empty = true;
                    foreach (int other in left)
                    {
                        Vector2 p = flat[other];
                        if (p == a || p == b || p == c) continue;
                        if (Turn(a, b, p) >= 0f && Turn(b, c, p) >= 0f && Turn(c, a, p) >= 0f) empty = false;
                    }
                    if (!empty) continue;
                    ear = i;
                    break;
                }
                Vector3 before = ring[left[(ear + left.Count - 1) % left.Count]], after = ring[left[(ear + 1) % left.Count]];
                if (reversed) Face(before, after, ring[left[ear]]);
                else Face(before, ring[left[ear]], after);
                left.RemoveAt(ear);
            }
        }

        // A section carried along a path, turning with it. section(i) is the outline at the i-th
        // point of the path, x to the right and y up, running anticlockwise.
        public void Sweep(Vector3[] path, Func<int, Vector2[]> section, Vector3 up, bool capStart = true, bool capEnd = true, bool loop = false)
        {
            var rings = new List<Vector3[]>();
            Vector3 before = default;
            for (int i = 0; i < path.Length; i++)
            {
                Vector3 next = path[i + 1 < path.Length ? i + 1 : loop ? 1 : i], last = path[i > 0 ? i - 1 : loop ? path.Length - 2 : 0];
                Vector3 along = (next - last).normalized;
                if (i > 0) up = Quaternion.FromToRotation(before, along) * up;
                before = along;
                Vector3 right = Vector3.Cross(up, along);
                if (right.sqrMagnitude < 1e-6f) right = Vector3.Cross(Mathf.Abs(along.x) < 0.9f ? Vector3.right : Vector3.forward, along);
                right.Normalize();
                up = Vector3.Cross(along, right);
                var outline = section(i);
                var ring = new Vector3[outline.Length];
                for (int j = 0; j < outline.Length; j++) ring[j] = path[i] + right * outline[j].x + up * outline[j].y;
                rings.Add(ring);
            }
            // a path that ends where it began joins up with itself
            if (loop) rings[rings.Count - 1] = rings[0];
            Skin(rings, true, capStart && !loop, capEnd && !loop);
        }

        // A round tube along a path, its radius running from one value to another.
        public void Tube(Vector3[] path, float radius, int sides = 8, float endRadius = -1f, bool caps = true, bool loop = false)
        {
            if (endRadius < 0f) endRadius = radius;
            Sweep(path, i => Oval(sides, Mathf.Lerp(radius, endRadius, i / (path.Length - 1f))), Vector3.up, caps, caps, loop);
        }

        public void Rod(Vector3 from, Vector3 to, float radius, int sides = 8, float endRadius = -1f) => Tube(new[] { from, to }, radius, sides, endRadius);

        // A spike with flat forged sides.
        public void Spike(Vector3 from, Vector3 tip, float radius, int sides = 4) => Tube(new[] { from, tip }, radius, sides, 0f, false);

        // A profile of (radius, height) pairs turned about the y axis. Run it upwards for a surface
        // that faces outwards. lobes(angle, i) scales the radius of the i-th ring at an angle round.
        public void Lathe(float[] profile, int sides, Func<float, int, float> lobes = null, float arc = 360f)
        {
            bool whole = arc >= 360f;
            var rings = new Vector3[profile.Length / 2][];
            for (int i = 0; i < rings.Length; i++)
            {
                rings[i] = new Vector3[whole ? sides : sides + 1];
                for (int j = 0; j < rings[i].Length; j++)
                {
                    float angle = arc * Mathf.Deg2Rad * j / sides, radius = profile[2 * i] * (lobes == null ? 1f : lobes(angle, i));
                    rings[i][j] = new Vector3(radius * Mathf.Sin(angle), profile[2 * i + 1], radius * Mathf.Cos(angle));
                }
            }
            Skin(rings, whole);
        }

        // A ring of round section lying in the xz plane.
        public void Torus(float radius, float thickness, int segments, int sides)
        {
            var profile = new float[2 * sides + 2];
            for (int i = 0; i <= sides; i++)
            {
                float angle = 2f * Mathf.PI * (i % sides) / sides;
                profile[2 * i] = radius + thickness * Mathf.Cos(angle);
                profile[2 * i + 1] = thickness * Mathf.Sin(angle);
            }
            Lathe(profile, segments);
        }

        public void Ball(Vector3 centre, Vector3 radii, int sides = 16)
        {
            var rings = new Vector3[sides / 2 + 1][];
            for (int i = 0; i < rings.Length; i++)
            {
                // the poles close to exact points
                float height = Mathf.PI * (i / (rings.Length - 1f) - 0.5f), wide = i == 0 || i == rings.Length - 1 ? 0f : Mathf.Cos(height);
                rings[i] = new Vector3[sides];
                for (int j = 0; j < sides; j++)
                {
                    float angle = 2f * Mathf.PI * j / sides;
                    rings[i][j] = centre + Vector3.Scale(radii, new Vector3(wide * Mathf.Sin(angle), Mathf.Sin(height), wide * Mathf.Cos(angle)));
                }
            }
            Skin(rings);
        }

        public void Ball(Vector3 centre, float radius, int sides = 16) => Ball(centre, Vector3.one * radius, sides);

        // A box with its edges and corners chamfered off.
        public void Box(Vector3 centre, Vector3 size, float bevel = 0f)
        {
            shape++;
            Vector3 half = size / 2f;
            // the corner of the box in an octant, as it lies on the face across one axis: pulled in along the other two
            Vector3 Corner(int axis, int x, int y, int z)
            {
                var sign = new Vector3(x, y, z);
                var corner = Vector3.Scale(half, sign);
                for (int other = 0; other < 3; other++)
                    if (other != axis)
                        corner[other] -= sign[other] * bevel;
                return centre + corner;
            }
            void Outward(params Vector3[] polygon)
            {
                bool turned = Vector3.Dot(Vector3.Cross(polygon[1] - polygon[0], polygon[2] - polygon[0]), polygon[0] + polygon[2] - 2f * centre) < 0f;
                for (int i = 1; i + 1 < polygon.Length; i++)
                    if (turned) Face(polygon[0], polygon[i + 1], polygon[i]);
                    else Face(polygon[0], polygon[i], polygon[i + 1]);
            }
            for (int axis = 0; axis < 3; axis++)
            {
                int b = (axis + 1) % 3, c = (axis + 2) % 3;
                Vector3 At(int own, int sa, int sb, int sc)
                {
                    var sign = new Vector3Int();
                    sign[axis] = sa;
                    sign[b] = sb;
                    sign[c] = sc;
                    return Corner(own, sign.x, sign.y, sign.z);
                }
                foreach (int s in new[] { -1, 1 })
                {
                    Outward(At(axis, s, -1, -1), At(axis, s, 1, -1), At(axis, s, 1, 1), At(axis, s, -1, 1));
                    if (bevel <= 0f) continue;
                    // the chamfer between this face and the next axis's, along the third
                    foreach (int t in new[] { -1, 1 })
                        Outward(At(axis, s, t, -1), At(axis, s, t, 1), At(b, s, t, 1), At(b, s, t, -1));
                }
            }
            if (bevel <= 0f) return;
            foreach (int x in new[] { -1, 1 })
            foreach (int y in new[] { -1, 1 })
            foreach (int z in new[] { -1, 1 })
                Outward(Corner(0, x, y, z), Corner(1, x, y, z), Corner(2, x, y, z));
        }

        // A plate cut to an outline in the xy plane, this thick along z and centred on z = 0. Its
        // edges are chamfered `bevel` deep and `inset` wide; a bevel of half the thickness makes a blade.
        public void Plate(Vector2[] outline, float thickness, float bevel = 0f, float inset = -1f)
        {
            float area = 0f, half = thickness / 2f;
            for (int i = 0; i < outline.Length; i++)
            {
                Vector2 a = outline[i], b = outline[(i + 1) % outline.Length];
                area += a.x * b.y - b.x * a.y;
            }
            if (area < 0f)
            {
                outline = (Vector2[])outline.Clone();
                Array.Reverse(outline);
            }
            Vector3[] Ring(Vector2[] points, float z) => Array.ConvertAll(points, p => new Vector3(p.x, p.y, z));
            if (bevel <= 0f)
            {
                Skin(new[] { Ring(outline, -half), Ring(outline, half) }, true, true, true);
                return;
            }
            var inner = Inset(outline, inset < 0f ? bevel : inset);
            Skin(new[] { Ring(inner, -half), Ring(outline, -half + bevel), Ring(outline, half - bevel), Ring(inner, half) }, true, true, true);
        }

        // An anticlockwise outline moved inwards by a distance.
        public static Vector2[] Inset(Vector2[] outline, float distance)
        {
            int n = outline.Length;
            var inner = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 before = (outline[i] - outline[(i + n - 1) % n]).normalized, after = (outline[(i + 1) % n] - outline[i]).normalized;
                Vector2 a = new Vector2(-before.y, before.x), b = new Vector2(-after.y, after.x);
                // sharp points would otherwise pull back a long way
                inner[i] = outline[i] + (a + b) * (distance / Mathf.Max(1f + Vector2.Dot(a, b), 0.35f));
            }
            return inner;
        }

        // A blade: a diamond section along a spine, with a ridge down the middle of each face. The
        // edges lie along `across`, this far ahead of and behind the spine at each point.
        public void Blade(Vector3[] spine, Vector3 across, Func<float, float> ahead, Func<float, float> behind, Func<float, float> thickness)
        {
            var rings = new Vector3[spine.Length][];
            for (int i = 0; i < spine.Length; i++)
            {
                float t = i / (spine.Length - 1f);
                Vector3 along = (spine[Mathf.Min(i + 1, spine.Length - 1)] - spine[Mathf.Max(i - 1, 0)]).normalized;
                Vector3 edge = (across - along * Vector3.Dot(across, along)).normalized, face = Vector3.Cross(along, edge);
                rings[i] = new[] { spine[i] + edge * ahead(t), spine[i] + face * (thickness(t) / 2f), spine[i] - edge * behind(t), spine[i] - face * (thickness(t) / 2f) };
            }
            Skin(rings, true, true, true);
        }

        // Covers a surface with panels that have real gaps between them. The surface takes how far
        // round and how far along it a point is, and cross(d/dround, d/dalong) must point out of it.
        // rounds and alongs are where panels meet. into(i, j) is the mesh panel (i, j) joins, or null
        // to leave a hole. The sloping sides of the gaps, `depth` deep, go into `walls`.
        public static void Panels(Sheet surface, float[] rounds, float[] alongs, float cell, float gap, float depth, Func<int, int, MeshKit> into, MeshKit walls = null)
        {
            for (int i = 0; i + 1 < rounds.Length; i++)
            for (int j = 0; j + 1 < alongs.Length; j++)
            {
                var kit = into(i, j);
                if (kit == null) continue;
                float u0 = rounds[i], u1 = rounds[i + 1], v0 = alongs[j], v1 = alongs[j + 1], width = 0f, length = 0f;
                for (int k = 0; k < 4; k++)
                {
                    width += Vector3.Distance(surface(Mathf.Lerp(u0, u1, k / 4f), (v0 + v1) / 2f), surface(Mathf.Lerp(u0, u1, (k + 1) / 4f), (v0 + v1) / 2f));
                    length += Vector3.Distance(surface((u0 + u1) / 2f, Mathf.Lerp(v0, v1, k / 4f)), surface((u0 + u1) / 2f, Mathf.Lerp(v0, v1, (k + 1) / 4f)));
                }
                int nu = Mathf.Max(1, Mathf.CeilToInt(width / cell)), nv = Mathf.Max(1, Mathf.CeilToInt(length / cell));
                float gu = Mathf.Min(gap / 2f / Mathf.Max(width, 1e-4f), 0.3f) * (u1 - u0), gv = Mathf.Min(gap / 2f / Mathf.Max(length, 1e-4f), 0.3f) * (v1 - v0);
                float eu = (u1 - u0) * 1e-3f, ev = (v1 - v0) * 1e-3f;
                var points = new Vector3[nu + 1, nv + 1];
                var normals = new Vector3[nu + 1, nv + 1];
                for (int a = 0; a <= nu; a++)
                for (int b = 0; b <= nv; b++)
                {
                    float u = Mathf.Lerp(u0 + gu, u1 - gu, a / (float)nu), v = Mathf.Lerp(v0 + gv, v1 - gv, b / (float)nv);
                    points[a, b] = surface(u, v);
                    // slopes are measured inside the panel, so a panel that ends at a sharp edge keeps its own normal there
                    normals[a, b] = Vector3.Cross(surface(Mathf.Min(u + eu, u1), v) - surface(Mathf.Max(u - eu, u0), v),
                        surface(u, Mathf.Min(v + ev, v1)) - surface(u, Mathf.Max(v - ev, v0))).normalized;
                }
                kit.shape++;
                for (int a = 0; a < nu; a++)
                for (int b = 0; b < nv; b++)
                {
                    kit.Face(points[a, b], points[a + 1, b], points[a + 1, b + 1], normals[a, b], normals[a + 1, b], normals[a + 1, b + 1]);
                    kit.Face(points[a, b], points[a + 1, b + 1], points[a, b + 1], normals[a, b], normals[a + 1, b + 1], normals[a, b + 1]);
                }
                if (gap <= 0f || walls == null) continue;

                // the foot of the wall under an edge point: on the line where the panels meet, sunk into the surface
                Vector3 Foot(int a, int b) =>
                    surface(a == 0 ? u0 : a == nu ? u1 : Mathf.Lerp(u0 + gu, u1 - gu, a / (float)nu), b == 0 ? v0 : b == nv ? v1 : Mathf.Lerp(v0 + gv, v1 - gv, b / (float)nv)) - normals[a, b] * depth;
                walls.shape++;
                for (int b = 0; b < nv; b++)
                {
                    walls.Face(points[0, b], points[0, b + 1], Foot(0, b + 1));
                    walls.Face(points[0, b], Foot(0, b + 1), Foot(0, b));
                    walls.Face(points[nu, b + 1], points[nu, b], Foot(nu, b));
                    walls.Face(points[nu, b + 1], Foot(nu, b), Foot(nu, b + 1));
                }
                for (int a = 0; a < nu; a++)
                {
                    walls.Face(points[a + 1, 0], points[a, 0], Foot(a, 0));
                    walls.Face(points[a + 1, 0], Foot(a, 0), Foot(a + 1, 0));
                    walls.Face(points[a, nv], points[a + 1, nv], Foot(a + 1, nv));
                    walls.Face(points[a, nv], Foot(a + 1, nv), Foot(a, nv));
                }
            }
        }

        // The same surface lifted off itself by a distance, for raised frames and sunken bays.
        public static Sheet Lifted(Sheet surface, float distance) => (u, v) =>
        {
            const float e = 1e-3f;
            return surface(u, v) + Vector3.Cross(surface(u + e, v) - surface(u - e, v), surface(u, v + e) - surface(u, v - e)).normalized * distance;
        };

        // The surface where a field crosses zero, negative inside, found on a grid of cells by
        // surface nets: one point in every cell the surface passes through, joined across the grid's
        // edges. Smooth all over, for skulls, heads and hands.
        public void Blob(Func<Vector3, float> field, Vector3 min, Vector3 max, float cell)
        {
            shape++;
            int nx = Mathf.CeilToInt((max.x - min.x) / cell) + 1, ny = Mathf.CeilToInt((max.y - min.y) / cell) + 1, nz = Mathf.CeilToInt((max.z - min.z) / cell) + 1;
            var values = new float[nx, ny, nz];
            for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            for (int k = 0; k < nz; k++)
                values[i, j, k] = field(min + new Vector3(i, j, k) * cell);

            var points = new Vector3[nx, ny, nz];
            var normals = new Vector3[nx, ny, nz];
            for (int i = 0; i + 1 < nx; i++)
            for (int j = 0; j + 1 < ny; j++)
            for (int k = 0; k + 1 < nz; k++)
            {
                // the middle of where the surface cuts the cell's twelve edges
                Vector3 sum = default;
                int cuts = 0;
                for (int corner = 0; corner < 8; corner++)
                for (int axis = 0; axis < 3; axis++)
                {
                    if ((corner >> axis & 1) != 0) continue;
                    int other = corner | 1 << axis;
                    float a = values[i + (corner & 1), j + (corner >> 1 & 1), k + (corner >> 2 & 1)], b = values[i + (other & 1), j + (other >> 1 & 1), k + (other >> 2 & 1)];
                    if (a < 0f == b < 0f) continue;
                    sum += Vector3.Lerp(new Vector3(corner & 1, corner >> 1 & 1, corner >> 2 & 1), new Vector3(other & 1, other >> 1 & 1, other >> 2 & 1), a / (a - b));
                    cuts++;
                }
                if (cuts == 0) continue;
                Vector3 p = min + (new Vector3(i, j, k) + sum / cuts) * cell;
                float e = cell * 0.3f;
                points[i, j, k] = p;
                normals[i, j, k] = new Vector3(field(p + Vector3.right * e) - field(p - Vector3.right * e), field(p + Vector3.up * e) - field(p - Vector3.up * e),
                    field(p + Vector3.forward * e) - field(p - Vector3.forward * e)).normalized;
            }

            // every grid edge the surface cuts is crossed by a quad through the four cells round it
            void Join(bool outward, int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l)
            {
                Vector3 p0 = points[a, b, c], p1 = points[d, e, f], p2 = points[g, h, i], p3 = points[j, k, l];
                Vector3 n0 = normals[a, b, c], n1 = normals[d, e, f], n2 = normals[g, h, i], n3 = normals[j, k, l];
                if (outward)
                {
                    Face(p0, p1, p2, n0, n1, n2);
                    Face(p0, p2, p3, n0, n2, n3);
                }
                else
                {
                    Face(p0, p2, p1, n0, n2, n1);
                    Face(p0, p3, p2, n0, n3, n2);
                }
            }
            for (int i = 1; i + 1 < nx; i++)
            for (int j = 1; j + 1 < ny; j++)
            for (int k = 1; k + 1 < nz; k++)
            {
                bool inside = values[i, j, k] < 0f;
                if (inside != values[i + 1, j, k] < 0f) Join(inside, i, j - 1, k - 1, i, j, k - 1, i, j, k, i, j - 1, k);
                if (inside != values[i, j + 1, k] < 0f) Join(inside, i - 1, j, k - 1, i - 1, j, k, i, j, k, i, j, k - 1);
                if (inside != values[i, j, k + 1] < 0f) Join(inside, i - 1, j - 1, k, i, j - 1, k, i, j, k, i - 1, j, k);
            }
        }

        // The finished mesh. Normals come from the faces round each point of each shape, and UVs from
        // laying the texture flat along whichever axis a face mostly looks down.
        public Mesh Mesh(string name)
        {
            int count = corners.Count;
            var faces = new Vector3[count / 3];
            for (int t = 0; t < faces.Length; t++)
                faces[t] = Vector3.Cross(corners[3 * t + 1] - corners[3 * t], corners[3 * t + 2] - corners[3 * t]).normalized;

            // corners of one shape that sit at one point are chained together
            (int, int, int, int) Spot(int corner) => (shapes[corner / 3], Mathf.RoundToInt(corners[corner].x * 1e4f), Mathf.RoundToInt(corners[corner].y * 1e4f), Mathf.RoundToInt(corners[corner].z * 1e4f));
            var heads = new Dictionary<(int, int, int, int), int>(count / 2);
            var next = new int[count];
            for (int c = 0; c < count; c++)
            {
                var spot = Spot(c);
                next[c] = heads.TryGetValue(spot, out int head) ? head : -1;
                heads[spot] = c;
            }

            var vertices = new List<Vector3>(count / 3);
            var normals = new List<Vector3>(count / 3);
            var uvs = new List<Vector2>(count / 3);
            var indices = new int[count];
            for (int c = 0; c < count; c++)
            {
                int t = c / 3, first = heads[Spot(c)];
                Vector3 normal = given[c], face = faces[t], p = corners[c];
                if (normal == default)
                {
                    for (int k = first; k >= 0; k = next[k])
                    {
                        if (Vector3.Dot(faces[k / 3], face) < creases[t]) continue;
                        // each face counts by the angle it fills at the point
                        int tri = k - k % 3;
                        normal += faces[k / 3] * Vector3.Angle(corners[tri + (k + 1) % 3] - corners[k], corners[tri + (k + 2) % 3] - corners[k]);
                    }
                    normal.Normalize();
                }
                float x = Mathf.Abs(face.x), y = Mathf.Abs(face.y), z = Mathf.Abs(face.z);
                Vector2 uv = (x > y && x > z ? new Vector2(p.z, p.y) : y > z ? new Vector2(p.x, p.z) : new Vector2(p.x, p.y)) * tiling;
                indices[c] = -1;
                for (int k = first; k >= 0 && indices[c] < 0; k = next[k])
                    if (k < c && (normals[indices[k]] - normal).sqrMagnitude < 1e-6f && (uvs[indices[k]] - uv).sqrMagnitude < 1e-8f)
                        indices[c] = indices[k];
                if (indices[c] >= 0) continue;
                indices[c] = vertices.Count;
                vertices.Add(p);
                normals.Add(normal);
                uvs.Add(uv);
            }

            var mesh = new Mesh { name = name, indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(indices, 0);
            return mesh;
        }

        // A closed outline round the origin: a circle or an oval, squarer as `squareness` rises above 2.
        public static Vector2[] Oval(int sides, float x, float y = -1f, float squareness = 2f)
        {
            if (y < 0f) y = x;
            var points = new Vector2[sides];
            for (int i = 0; i < sides; i++)
            {
                float angle = 2f * Mathf.PI * i / sides, c = Mathf.Cos(angle), s = Mathf.Sin(angle);
                points[i] = new Vector2(x * Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 2f / squareness), y * Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), 2f / squareness));
            }
            return points;
        }

        public static Vector2[] Outline(params float[] xy)
        {
            var points = new Vector2[xy.Length / 2];
            for (int i = 0; i < points.Length; i++) points[i] = new Vector2(xy[2 * i], xy[2 * i + 1]);
            return points;
        }

        // A smooth curve through keys given as (where, value) pairs in order, flat beyond its ends
        // and never overshooting a key.
        public static float Key(float t, params float[] keys)
        {
            int last = keys.Length - 2;
            if (t <= keys[0]) return keys[1];
            if (t >= keys[last]) return keys[last + 1];
            int i = 0;
            while (keys[i + 2] < t) i += 2;
            float Slope(int k) => (keys[k + 3] - keys[k + 1]) / (keys[k + 2] - keys[k]);
            float Tangent(int k)
            {
                if (k == 0) return Slope(0);
                if (k == last) return Slope(last - 2);
                float before = Slope(k - 2), after = Slope(k);
                return before * after <= 0f ? 0f : 2f * before * after / (before + after);
            }
            float span = keys[i + 2] - keys[i], s = (t - keys[i]) / span, s2 = s * s, s3 = s2 * s;
            return (2f * s3 - 3f * s2 + 1f) * keys[i + 1] + (s3 - 2f * s2 + s) * span * Tangent(i) + (-2f * s3 + 3f * s2) * keys[i + 3] + (s3 - s2) * span * Tangent(i + 2);
        }

        // A smooth path through the given points, with this many steps between each pair.
        public static Vector3[] Path(int steps, params Vector3[] through)
        {
            var path = new Vector3[(through.Length - 1) * steps + 1];
            for (int i = 0; i < path.Length; i++)
            {
                int k = Mathf.Min(i / steps, through.Length - 2);
                float t = i / (float)steps - k;
                Vector3 a = through[Mathf.Max(k - 1, 0)], b = through[k], c = through[k + 1], d = through[Mathf.Min(k + 2, through.Length - 1)];
                path[i] = 0.5f * (2f * b + (c - a) * t + (2f * a - 5f * b + 4f * c - d) * (t * t) + (3f * b - a - 3f * c + d) * (t * t * t));
            }
            return path;
        }

        public static Matrix4x4 Move(float x, float y, float z) => Matrix4x4.Translate(new Vector3(x, y, z));
        public static Matrix4x4 Move(Vector3 to) => Matrix4x4.Translate(to);
        public static Matrix4x4 Turn(float x, float y, float z) => Matrix4x4.Rotate(Quaternion.Euler(x, y, z));
        public static Matrix4x4 Size(float x, float y, float z) => Matrix4x4.Scale(new Vector3(x, y, z));
        public static Matrix4x4 Size(float all) => Matrix4x4.Scale(Vector3.one * all);

        // Somewhere to build with z pointing from one point at another.
        public static Matrix4x4 Aim(Vector3 from, Vector3 to, Vector3 up) => Matrix4x4.TRS(from, Quaternion.LookRotation(to - from, up), Vector3.one);

        // Fields for Blob: distances to simple solids, and ways of melting them together.
        public static float Capsule(Vector3 p, Vector3 a, Vector3 b, float radiusA, float radiusB)
        {
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - a - ab * t).magnitude - Mathf.Lerp(radiusA, radiusB, t);
        }

        public static float Ellipsoid(Vector3 p, Vector3 centre, Vector3 radii)
        {
            Vector3 q = p - centre;
            float k0 = new Vector3(q.x / radii.x, q.y / radii.y, q.z / radii.z).magnitude;
            float k1 = new Vector3(q.x / (radii.x * radii.x), q.y / (radii.y * radii.y), q.z / (radii.z * radii.z)).magnitude;
            return k1 < 1e-6f ? -Mathf.Min(radii.x, Mathf.Min(radii.y, radii.z)) : k0 * (k0 - 1f) / k1;
        }

        // Either of two solids, run together over a distance k where they meet.
        public static float Melt(float a, float b, float k)
        {
            float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(b, a, h) - k * h * (1f - h);
        }

        // The first solid with the second scooped out of it.
        public static float Carve(float a, float b, float k) => -Melt(-a, b, k);
    }
}
