using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Random = Unity.Mathematics.Random;

namespace Infinigame
{
    // Rain round the camera while a storm is overhead: streaks falling through a box that follows
    // the player. Each streak keeps its place in the world as the box moves, and wraps round to the
    // far side when the box leaves it behind.
    public class Rainfall : MonoBehaviour
    {
        const int Drops = 1800;
        const float Width = 18f, Height = 14f, Speed = 24f;

        SolarSystem system;
        Mesh streak;
        Material material;
        readonly Matrix4x4[] matrices = new Matrix4x4[Drops];
        readonly float3[] seeds = new float3[Drops];

        void Start()
        {
            system = GetComponent<SolarSystem>();
            streak = Shapes.Box();
            material = new Material(Resources.Load<Shader>("Rain")) { enableInstancing = true };
            var rnd = new Random(3);
            for (int i = 0; i < Drops; i++) seeds[i] = rnd.NextFloat3();
        }

        void Update()
        {
            int count = (int)(Drops * system.Rain);
            if (count == 0) return;
            float3 eye = Camera.main.transform.position, up = math.normalize(eye);
            float3 east = math.normalize(math.cross(up, math.abs(up.y) < 0.9f ? new float3(0, 1, 0) : new float3(1, 0, 0))), north = math.cross(up, east);
            float fallen = (float)(system.Clock % 1000.0) * Speed;
            // the wind leans the rain a little
            Quaternion lean = Quaternion.FromToRotation(Vector3.up, up + east * 0.12f);
            var scale = new Vector3(0.008f, 0.9f, 0.008f);
            for (int i = 0; i < count; i++)
            {
                float3 seed = seeds[i];
                float3 within = new float3(seed.x * Width - math.dot(eye, east), seed.y * Height - fallen * (0.8f + 0.4f * seed.z), seed.z * Width - math.dot(eye, north));
                within -= new float3(Width, Height, Width) * math.floor(within / new float3(Width, Height, Width));
                within -= new float3(Width, Height, Width) / 2f;
                // a drop right in front of the eye would be a grey bar across the view, so those are left out
                matrices[i] = Matrix4x4.TRS(eye + east * within.x + up * within.y + north * within.z, lean, math.lengthsq(within.xz) < 2.5f ? Vector3.zero : scale);
            }
            var parameters = new RenderParams(material) { worldBounds = new Bounds(eye, Vector3.one * 2f * Width), shadowCastingMode = ShadowCastingMode.Off };
            for (int start = 0; start < count; start += 1023) Graphics.RenderMeshInstanced(parameters, streak, 0, matrices, math.min(1023, count - start), start);
        }
    }
}
