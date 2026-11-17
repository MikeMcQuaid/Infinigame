using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;

namespace Infinigame
{
    // A small surface flow: it spreads, follows the slope and settles without a fluid solver.
    public class IchorPool : MonoBehaviour
    {
        public BodyDescriptor body;
        public float radius;
        Mesh mesh;
        Vector3[] vertices;
        float age, next;
        Vector3 origin;
        void Start()
        {
            origin = transform.localPosition;
            mesh = new Mesh { name = "Liquid ichor" };
            vertices = new Vector3[1 + 4 * 48];
            var triangles = new int[48 * 3 + 3 * 48 * 6];
            int at = 0;
            for (int i = 0; i < 48; i++)
            {
                triangles[at++] = 0; triangles[at++] = 1 + (i + 1) % 48; triangles[at++] = 1 + i;
                for (int ring = 0; ring < 3; ring++)
                {
                    int a = 1 + ring * 48 + i, b = 1 + ring * 48 + (i + 1) % 48;
                    triangles[at++] = a; triangles[at++] = b; triangles[at++] = a + 48;
                    triangles[at++] = b; triangles[at++] = b + 48; triangles[at++] = a + 48;
                }
            }
            mesh.vertices = vertices; mesh.triangles = triangles;
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }
        void Update()
        {
            age += Time.deltaTime;
            if (age < next) return;
            next = age + 0.1f;
            float spread = radius * Mathf.Lerp(0.15f, 1, 1 - Mathf.Exp(-age * 0.65f));
            if (body.kind == BodyKind.Rock && age < 10)
            {
                Vector3 centre = transform.localPosition;
                Vector3 east = transform.localRotation * Vector3.right, north = transform.localRotation * Vector3.forward;
                float Gradient(Vector3 axis) => body.GroundRadius(math.normalizesafe((float3)(centre + axis))) - body.GroundRadius(math.normalizesafe((float3)(centre - axis)));
                Vector3 flow = Vector3.ClampMagnitude(-east * Gradient(east) - north * Gradient(north), 1) * 0.08f;
                if (Vector3.Distance(centre + flow, origin) < radius) centre += flow;
                transform.localPosition = Vector3.Normalize(centre) * (body.GroundRadius(math.normalizesafe((float3)centre)) + 0.045f);
            }
            vertices[0] = Vector3.zero;
            for (int ring = 0; ring < 4; ring++) for (int i = 0; i < 48; i++)
            {
                float angle = i * Mathf.PI * 2 / 48;
                float distance = spread * (ring + 1) / 4 * (0.94f + 0.06f * Mathf.Sin(angle * 5));
                Vector3 point = new Vector3(Mathf.Cos(angle) * distance, 0, Mathf.Sin(angle) * distance * 0.8f);
                if (body.kind == BodyKind.Rock)
                {
                    float3 on = transform.localPosition + transform.localRotation * point;
                    on = math.normalizesafe(on) * (body.GroundRadius(math.normalizesafe(on)) + 0.04f);
                    point = Quaternion.Inverse(transform.localRotation) * ((Vector3)on - transform.localPosition);
                }
                point.y += Mathf.Sin(distance * 7 - age * 3) * 0.012f * Mathf.Exp(-age * 0.2f);
                vertices[1 + ring * 48 + i] = point;
            }
            mesh.vertices = vertices; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            if (age >= 18) enabled = false;
        }
        void OnDestroy() { if (mesh != null) Destroy(mesh); }
    }
}
