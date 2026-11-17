using System;
using System.Collections.Generic;
using System.Linq;
using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;

namespace Infinigame
{
    public class Corpses : MonoBehaviour
    {
        public Game game;
        sealed class Body
        {
            public int world;
            public Ragdoll joints;
            public Transform[] parts;
            public Quaternion rotation;
            public float radius, age;
        }
        readonly List<Body> bodies = new List<Body>();
        readonly Dictionary<Mesh, (Mesh mesh, Vector3 centre)[]> pieces = new Dictionary<Mesh, (Mesh, Vector3)[]>();
        readonly Dictionary<string, Mesh> friendly = new Dictionary<string, Mesh>();
        public int Count => bodies.Count;
        public void AddModel(Transform model, float3 at, float3 velocity, bool ship)
        {
            string key = ship ? "ship" : "robot";
            if (!friendly.TryGetValue(key, out var mesh))
            {
                mesh = new Mesh { name = "Friendly wreck", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.CombineMeshes(model.GetComponentsInChildren<MeshFilter>().Select(filter => new CombineInstance { mesh = filter.sharedMesh, transform = model.worldToLocalMatrix * filter.transform.localToWorldMatrix }).ToArray());
                friendly[key] = mesh;
            }
            Add(mesh, model.GetComponentInChildren<MeshRenderer>().sharedMaterial, at, model.rotation, velocity, ship ? 0.8f : 0.25f);
        }
        public void Prepare(Mesh source)
        {
            if (pieces.ContainsKey(source)) return;
            var vertices = source.vertices; var normals = source.normals; var uv = source.uv; var triangles = source.triangles;
            var groups = Enumerable.Range(0, 8).Select(_ => new List<int>()).ToArray();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 at = (vertices[triangles[i]] + vertices[triangles[i + 1]] + vertices[triangles[i + 2]]) / 3 - source.bounds.center;
                int group = (at.x > 0 ? 1 : 0) + (at.y > 0 ? 2 : 0) + (at.z > 0 ? 4 : 0);
                groups[group].AddRange(new[] { triangles[i], triangles[i + 1], triangles[i + 2] });
            }
            pieces[source] = groups.Where(group => group.Count > 0).Select(group =>
            {
                var indices = group.Distinct().ToArray();
                var map = indices.Select((old, index) => (old, index)).ToDictionary(pair => pair.old, pair => pair.index);
                var points = indices.Select(index => vertices[index]).ToArray();
                Vector3 centre = points.Aggregate(Vector3.zero, (sum, point) => sum + point) / points.Length;
                var mesh = new Mesh { name = source.name + " wreck section", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.vertices = points.Select(point => point - centre).ToArray();
                if (normals.Length == vertices.Length) mesh.normals = indices.Select(index => normals[index]).ToArray();
                if (uv.Length == vertices.Length) mesh.uv = indices.Select(index => uv[index]).ToArray();
                mesh.triangles = group.Select(index => map[index]).ToArray(); mesh.RecalculateBounds();
                return (mesh, centre);
            }).ToArray();
        }
        public void Add(Mesh mesh, Material material, float3 at, Quaternion rotation, float3 velocity, float radius, float scale = 1, bool crumble = false)
        {
            Prepare(mesh);
            while (bodies.Count >= (LodSettings.Tier == 0 ? 16 : 40)) Remove(0);
            var sections = pieces[mesh];
            var points = sections.Select(section => at + (float3)(rotation * section.centre * scale)).ToArray();
            var parts = sections.Select(section => Shapes.Part(transform, "Wreck / remains", section.mesh, material, Vector3.zero, Vector3.one * scale).transform).ToArray();
            foreach (var part in parts)
            {
                part.rotation = rotation;
                if (material.HasProperty("_Organic") && material.GetFloat("_Organic") > 0)
                { var stain = new MaterialPropertyBlock(); stain.SetFloat("_Damage", 0.28f); part.GetComponent<Renderer>().SetPropertyBlock(stain); }
            }
            var joints = new Ragdoll(points, crumble ? float3.zero : velocity);
            if (crumble) for (int i = 0; i < joints.joined.Length; i++) joints.joined[i] = false;
            bodies.Add(new Body { world = game.System.Reference, joints = joints, parts = parts, rotation = rotation, radius = radius });
        }
        public void Strike(float3 from, float3 step, float damage, float radius = 0.7f)
        {
            foreach (var body in bodies)
                if (body.world == game.System.Reference) { body.joints.Strike(from, step, damage, radius); body.age = 0; }
        }
        public void Clear() { while (bodies.Count > 0) Remove(bodies.Count - 1); }
        void Remove(int index)
        {
            foreach (var part in bodies[index].parts) if (part != null) Destroy(part.gameObject);
            bodies.RemoveAt(index);
        }
        void LateUpdate()
        {
            float dt = math.min(Time.deltaTime, 0.05f);
            foreach (var body in bodies)
            {
                bool local = body.world == game.System.Reference;
                foreach (var part in body.parts) part.gameObject.SetActive(local);
                if (!local) continue;
                if (body.age < 12 && dt > 0)
                {
                    for (int step = 0; step < 3; step++)
                    {
                        var before = (float3[])body.joints.positions.Clone();
                        body.joints.Step(game.System.ReferenceBody, dt / 3, body.radius);
                        for (int i = 0; i < body.parts.Length; i++)
                            body.joints.positions[i] = game.Outpost.Resolve(before[i], body.joints.positions[i], body.radius, out _);
                    }
                    body.age += dt;
                }
                for (int i = 0; i < body.parts.Length; i++)
                {
                    var part = body.parts[i];
                    part.position = body.joints.positions[i] - game.System.Anchor;
                    float speed = math.length(body.joints.velocities[i]);
                    if (body.age < 12 && speed > 0.4f) part.rotation = body.rotation * Quaternion.Euler(body.age * 25, i * body.age * 8, body.age * 35);
                }
            }
        }
        void OnDestroy()
        {
            Clear();
            foreach (var mesh in friendly.Values) Destroy(mesh);
            foreach (var set in pieces.Values) foreach (var part in set) Destroy(part.mesh);
        }
    }
}
