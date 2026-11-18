using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;

namespace Infinigame
{
    public class IchorPool : MonoBehaviour
    {
        public BodyDescriptor body;
        public float radius;
        Mesh mesh;

        void Start()
        {
            const int rings = 4, sides = 32;
            var vertices = new Vector3[1 + rings * sides];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[sides * (3 + (rings - 1) * 6)];
            int at = 0;
            for (int ring = 0; ring < rings; ring++)
                for (int i = 0; i < sides; i++)
                {
                    int a = 1 + ring * sides + i, b = 1 + ring * sides + (i + 1) % sides;
                    float angle = i * Mathf.PI * 2 / sides;
                    uv[a] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * ((ring + 1f) / rings);
                    float reach = radius * (0.93f + 0.07f * Mathf.Sin(angle * 5));
                    Vector3 point = new Vector3(uv[a].x * reach, 0, uv[a].y * reach * 0.85f);
                    if (body.kind == BodyKind.Rock)
                    {
                        float3 up = math.normalizesafe((float3)(transform.localPosition + transform.localRotation * point));
                        Vector3 floor = up * (body.GroundRadius(up) + 0.045f);
                        point = Quaternion.Inverse(transform.localRotation) * (floor - transform.localPosition);
                    }
                    vertices[a] = point;
                    if (ring == 0)
                    {
                        triangles[at++] = 0; triangles[at++] = b; triangles[at++] = a;
                    }
                    else
                    {
                        triangles[at++] = a - sides; triangles[at++] = b - sides; triangles[at++] = a;
                        triangles[at++] = b - sides; triangles[at++] = b; triangles[at++] = a;
                    }
                }
            mesh = new Mesh { name = "Liquid ichor", vertices = vertices, uv = uv, triangles = triangles };
            mesh.RecalculateNormals();
            GetComponent<MeshFilter>().sharedMesh = mesh;
            var liquid = new MaterialPropertyBlock();
            liquid.SetFloat("_Born", Time.time);
            GetComponent<Renderer>().SetPropertyBlock(liquid);
        }

        void OnDestroy() { if (mesh != null) Destroy(mesh); }
    }
}
