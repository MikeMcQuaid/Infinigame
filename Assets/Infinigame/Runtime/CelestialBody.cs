using System;
using System.Collections.Generic;
using Infinigame.Core;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Infinigame
{
    // Draws one body. Terrain is a quadtree of patches on each cube face, built in jobs and swapped
    // in as the camera moves. The sun is a plain glowing sphere.
    public class CelestialBody : MonoBehaviour
    {
        class Patch
        {
            public MeshRenderer renderer;
            public Mesh mesh;
            public Mesh.MeshDataArray data;
            public JobHandle job;
            public bool building, ready;
        }

        public BodyDescriptor Descriptor { get; private set; }
        public int PatchCount => patches.Count;

        readonly Dictionary<NodeId, Patch> patches = new Dictionary<NodeId, Patch>();
        readonly List<NodeId> draw = new List<NodeId>(), wanted = new List<NodeId>(), scratch = new List<NodeId>();
        readonly HashSet<NodeId> drawSet = new HashSet<NodeId>(), wantedSet = new HashSet<NodeId>();
        Func<NodeId, bool> isReady;
        Material terrain;
        MeshFilter sea;
        LodSettings lod;
        int tier = -1, maxDepth;

        public void Init(BodyDescriptor descriptor)
        {
            Descriptor = descriptor;
            name = descriptor.name;
            isReady = id => patches.TryGetValue(id, out var patch) && patch.ready;
            if (descriptor.kind == BodyKind.Sun)
            {
                Shapes.Part(transform, "Surface", Shapes.Sphere(descriptor.radius, 24), Shapes.Material("Glow", Shapes.SunColour(descriptor) * 6f), Vector3.zero, Vector3.one, false);
                return;
            }
            terrain = Shapes.Material("Lit", Color.white);
            terrain.mainTexture = Shapes.Palette(descriptor);
            gameObject.AddComponent<Scatter>().Init(descriptor);
            if (descriptor.SeaRadius == 0f) return;
            sea = Shapes.Part(transform, "Sea", null, Shapes.Material("Water", Shapes.SeaColour(descriptor)), Vector3.zero, Vector3.one, false).GetComponent<MeshFilter>();

            // worlds with seas have air; a shader that fails to compile just leaves the sky black
            var shader = Resources.Load<Shader>("Atmosphere");
            if (shader == null || !shader.isSupported) return;
            var air = new Material(shader);
            air.SetColor("_SkyColour", Shapes.SkyColour(descriptor, false));
            air.SetColor("_DuskColour", Shapes.SkyColour(descriptor, true));
            air.SetFloat("_PlanetRadius", descriptor.radius);
            air.SetFloat("_AtmosphereRadius", descriptor.radius * 1.25f);
            Shapes.Part(transform, "Atmosphere", Shapes.Sphere(descriptor.radius * 1.25f, 24), air, Vector3.zero, Vector3.one, false);
        }

        void Update()
        {
            if (terrain == null) return;
            if (tier != LodSettings.Tier)
            {
                // a new quality level changes patch resolution, so start again from the roots
                Clear();
                tier = LodSettings.Tier;
                lod = LodSettings.Current;
                maxDepth = QuadtreeLod.MaxDepth(Descriptor.radius, lod.quads, lod.finestQuad);
                if (sea != null)
                {
                    if (sea.sharedMesh != null) Destroy(sea.sharedMesh);
                    sea.sharedMesh = Shapes.Sphere(Descriptor.SeaRadius, lod.waterQuads);
                }
            }

            float3 camera = transform.InverseTransformPoint(Camera.main.transform.position);
            QuadtreeLod.Select(camera, Descriptor.radius, maxDepth, lod.splitFactor, isReady, draw, wanted);
            drawSet.Clear();
            drawSet.UnionWith(draw);
            wantedSet.Clear();
            wantedSet.UnionWith(wanted);

            // finish builds within the frame budget, drop patches nobody wants, show the drawn set
            int budget = lod.buildsPerFrame, inFlight = 0;
            scratch.Clear();
            foreach (var pair in patches)
            {
                var patch = pair.Value;
                if (!wantedSet.Contains(pair.Key))
                {
                    scratch.Add(pair.Key);
                    continue;
                }
                if (patch.building && patch.job.IsCompleted && budget-- > 0)
                {
                    patch.job.Complete();
                    Mesh.ApplyAndDisposeWritableMeshData(patch.data, patch.mesh, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                    patch.mesh.bounds = patch.mesh.GetSubMesh(0).bounds;
                    patch.building = false;
                    patch.ready = true;
                }
                if (patch.building) inFlight++;
                patch.renderer.enabled = patch.ready && drawSet.Contains(pair.Key);
            }
            foreach (var id in scratch)
            {
                Release(patches[id]);
                patches.Remove(id);
            }

            // start the nearest missing patches
            scratch.Clear();
            foreach (var id in wanted)
                if (!patches.ContainsKey(id))
                    scratch.Add(id);
            scratch.Sort((a, b) => math.distancesq(a.Centre * Descriptor.radius, camera).CompareTo(math.distancesq(b.Centre * Descriptor.radius, camera)));
            for (int i = 0; i < scratch.Count && inFlight < lod.buildsPerFrame * 4; i++, inFlight++)
                patches[scratch[i]] = Build(scratch[i]);
        }

        Patch Build(NodeId id)
        {
            var mesh = new Mesh();
            var data = Mesh.AllocateWritableMeshData(1);
            return new Patch
            {
                mesh = mesh, data = data, building = true,
                renderer = Shapes.Part(transform, "Patch", mesh, terrain, Vector3.zero, Vector3.one).GetComponent<MeshRenderer>(),
                job = new PatchMeshJob
                {
                    face = id.face, quads = lod.quads, uvMin = id.UvMin, uvSize = id.UvSize, radius = Descriptor.radius,
                    skirt = 2f * QuadtreeLod.NodeSize(Descriptor.radius, id.depth) / lod.quads, terrain = Descriptor.terrain, mesh = data[0],
                }.Schedule(),
            };
        }

        void Release(Patch patch)
        {
            if (patch.building)
            {
                patch.job.Complete();
                patch.data.Dispose();
            }
            Destroy(patch.mesh);
            Destroy(patch.renderer.gameObject);
        }

        void Clear()
        {
            foreach (var patch in patches.Values) Release(patch);
            patches.Clear();
        }

        void OnDestroy() => Clear();
    }
}
