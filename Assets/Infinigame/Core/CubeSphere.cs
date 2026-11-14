using Unity.Mathematics;

namespace Infinigame.Core
{
    // Six cube faces projected onto the unit sphere. Face coordinates run from -1 to 1.
    public static class CubeSphere
    {
        public const int Faces = 6;

        // right x up = normal, so triangles wound (origin, +right, +up) face outwards in Unity.
        public static void Axes(int face, out float3 right, out float3 up, out float3 normal)
        {
            switch (face)
            {
                case 0: right = new float3(0, 0, -1); up = new float3(0, 1, 0); break;
                case 1: right = new float3(0, 0, 1); up = new float3(0, 1, 0); break;
                case 2: right = new float3(1, 0, 0); up = new float3(0, 0, -1); break;
                case 3: right = new float3(1, 0, 0); up = new float3(0, 0, 1); break;
                case 4: right = new float3(1, 0, 0); up = new float3(0, 1, 0); break;
                default: right = new float3(-1, 0, 0); up = new float3(0, 1, 0); break;
            }
            normal = math.cross(right, up);
        }

        // The tangent warp keeps cells near face corners close in size to cells at face centres.
        public static float3 Direction(int face, float2 uv)
        {
            Axes(face, out var right, out var up, out var normal);
            float2 w = math.tan(uv * (math.PI / 4f));
            return math.normalize(normal + w.x * right + w.y * up);
        }

        public static int Face(float3 direction, out float2 uv)
        {
            float3 a = math.abs(direction);
            int face = a.x >= a.y && a.x >= a.z ? (direction.x > 0 ? 0 : 1)
                : a.y >= a.z ? (direction.y > 0 ? 2 : 3)
                : (direction.z > 0 ? 4 : 5);
            Axes(face, out var right, out var up, out var normal);
            float3 p = direction / math.dot(direction, normal);
            uv = math.atan(new float2(math.dot(p, right), math.dot(p, up))) * (4f / math.PI);
            return face;
        }
    }
}
