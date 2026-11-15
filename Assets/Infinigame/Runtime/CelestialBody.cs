using System;
using System.Collections.Generic;
using Infinigame.Core;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Infinigame
{
    // Draws one body. Its surface is a quadtree of patches on each cube face, built in jobs and
    // swapped in as the camera moves: ground for a rocky world, cloud tops for a giant. Round that
    // go whatever the body has of sea, rings, air, cloud and polar lights. The sun is a glowing sphere.
    // From far off rings are a banded sheet of dust; RingField draws the rocks they are made of.
    public class CelestialBody : MonoBehaviour
    {
        class Patch
        {
            public MeshRenderer renderer;
            public Mesh mesh;
            public Mesh.MeshDataArray data;
            public JobHandle job;
            // stale: the ground under it has been blown about since it was last built
            public bool building, ready, stale;
        }

        public BodyDescriptor Descriptor { get; private set; }
        public int PatchCount => patches.Count;

        // Whether the player is in this body's frame. Set by SolarSystem. Only that body has haze,
        // plants and animals close enough to matter.
        [NonSerialized] public bool IsReference;

        readonly Dictionary<NodeId, Patch> patches = new Dictionary<NodeId, Patch>();
        readonly List<NodeId> draw = new List<NodeId>(), wanted = new List<NodeId>(), scratch = new List<NodeId>();
        readonly HashSet<NodeId> drawSet = new HashSet<NodeId>(), wantedSet = new HashSet<NodeId>();
        // the nodes whose finer children were drawn last frame and this: detail on show lingers
        HashSet<NodeId> shown = new HashSet<NodeId>(), showing = new HashSet<NodeId>();
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
            float radius = descriptor.radius;
            GameObject Shell(string part, float size, Material material) =>
                Shapes.Part(transform, part, Shapes.Sphere(radius * size, 24), material, Vector3.zero, Vector3.one, false);

            if (descriptor.kind == BodyKind.Sun)
            {
                var glow = new Material(Resources.Load<Shader>("Sun"));
                glow.SetColor("_Colour", Shapes.SunColour(descriptor) * 7f);
                Shell("Surface", 1f, glow);
                // a star's ring is its asteroid belt: rocks, with no sheet of dust between them
                if (descriptor.ringOuter > 0f) gameObject.AddComponent<RingField>().Init(descriptor);
                return;
            }
            if (descriptor.kind == BodyKind.Station)
            {
                Models.Station(radius).SetParent(transform, false);
                return;
            }
            terrain = Shapes.Ground(descriptor);
            if (descriptor.kind == BodyKind.Rock)
            {
                // rock that has run molten, from a blast or a volcano, is a layer of its own over the rest
                terrain.SetTexture("_Albedo4", Textures.Albedo(Surface.Lava));
                terrain.SetTexture("_Normal4", Textures.Normal(Surface.Lava));
                gameObject.AddComponent<Scatter>().Init(descriptor);
            }
            foreach (Creature kind in Enum.GetValues(typeof(Creature)))
                if (Wildlife.Lives(descriptor, kind) && GetComponent<Creatures>() == null)
                    gameObject.AddComponent<Creatures>().Init(descriptor);

            if (descriptor.ringOuter > 0f)
            {
                var rings = new Material(Resources.Load<Shader>("Rings"));
                rings.SetColor("_Colour", Color.Lerp(Shapes.Colour(descriptor.ground), Color.white, 0.5f));
                rings.SetFloat("_Density", descriptor.ringDensity);
                rings.SetFloat("_PlanetRadius", radius);
                rings.SetFloat("_Seed", descriptor.terrain.offset.x);
                rings.SetTexture("_Profile", Shapes.RingProfile(descriptor));
                Shapes.Part(transform, "Rings", Shapes.Ring(descriptor.ringInner, descriptor.ringOuter, 128), rings, Vector3.zero, Vector3.one, false);
                gameObject.AddComponent<RingField>().Init(descriptor);
            }
            if (descriptor.air > 0f)
            {
                var air = new Material(Resources.Load<Shader>("Atmosphere"));
                air.SetColor("_SkyColour", Shapes.Colour(descriptor.sky));
                air.SetColor("_DuskColour", Shapes.Colour(descriptor.dusk));
                air.SetFloat("_PlanetRadius", radius);
                air.SetFloat("_AtmosphereRadius", radius * Shapes.AirTop(descriptor));
                air.SetFloat("_Density", 2.2f * descriptor.air);
                Shell("Atmosphere", Shapes.AirTop(descriptor), air);
            }
            if (descriptor.cloud > 0f && descriptor.kind == BodyKind.Rock)
            {
                var clouds = new Material(Resources.Load<Shader>("Clouds"));
                clouds.SetFloat("_Cover", 1f - descriptor.cloud);
                // only a world with seas has weather; any other cloud takes the colour of its sky
                clouds.SetFloat("_Stormy", descriptor.SeaRadius > 0f ? 1f : 0f);
                clouds.SetColor("_Tint", descriptor.SeaRadius > 0f ? Color.white : Color.Lerp(Shapes.Colour(descriptor.sky), Color.white, 0.3f));
                Shell("Clouds", Shapes.CloudDeck(descriptor), clouds);
            }
            if (descriptor.SeaRadius == 0f) return;

            // a world with seas is alive, and its poles glow at night
            var water = new Material(Resources.Load<Shader>("Water"));
            water.SetColor("_Shallow", Shapes.SeaColour(0f));
            water.SetColor("_Deep", Shapes.SeaColour(1f));
            water.SetTexture("_Waves", Textures.Normal(Surface.Water));
            water.SetTexture("_Foam", Textures.Albedo(Surface.Water));
            Shapes.Air(water, descriptor);
            sea = Shapes.Part(transform, "Sea", null, water, Vector3.zero, Vector3.one, false).GetComponent<MeshFilter>();
            var aurora = new Material(Resources.Load<Shader>("Aurora"));
            aurora.SetFloat("_Inner", radius * (Shapes.AirTop(descriptor) - 0.15f));
            aurora.SetFloat("_Outer", radius * (Shapes.AirTop(descriptor) - 0.01f));
            Shell("Aurora", Shapes.AirTop(descriptor), aurora);
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
                // cloud tops are smooth, so a giant needs only enough patches to look round
                maxDepth = math.min(QuadtreeLod.MaxDepth(Descriptor.radius, lod.quads, lod.finestQuad), Descriptor.kind == BodyKind.Gas ? 3 : 99);
                if (sea != null)
                {
                    if (sea.sharedMesh != null) Destroy(sea.sharedMesh);
                    sea.sharedMesh = Shapes.Sphere(Descriptor.SeaRadius, lod.waterQuads);
                }
            }

            float3 camera = transform.InverseTransformPoint(Camera.main.transform.position);
            // The scene's light and haze belong to the body the player is in the frame of. They are
            // eased out well before that frame is left, so crossing the boundary shows nothing.
            terrain.SetFloat("_Haze", IsReference ? math.smoothstep(0.95f, 0.6f, math.length(camera) / Descriptor.soiRadius) : 0f);
            // the clock blasts are timed by: molten rock cools as it runs on
            terrain.SetFloat("_Clock", Time.time);
            QuadtreeLod.Select(camera, Descriptor.radius, maxDepth, lod.splitFactor, isReady, draw, wanted, shown, showing);
            (shown, showing) = (showing, shown);
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
                if (patch.stale && !patch.building) Schedule(patch, pair.Key);
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
            var patch = new Patch { mesh = mesh, renderer = Shapes.Part(transform, "Patch", mesh, terrain, Vector3.zero, Vector3.one).GetComponent<MeshRenderer>() };
            Schedule(patch, id);
            return patch;
        }

        // Starts a patch's mesh building, for the first time or again after the ground has changed.
        // Until the new one is ready the old one stays on show.
        void Schedule(Patch patch, NodeId id)
        {
            patch.data = Mesh.AllocateWritableMeshData(1);
            patch.job = new PatchMeshJob
            {
                face = id.face, quads = lod.quads, uvMin = id.UvMin, uvSize = id.UvSize, radius = Descriptor.radius,
                skirt = 2f * QuadtreeLod.NodeSize(Descriptor.radius, id.depth) / lod.quads, terrain = Descriptor.terrain, mesh = patch.data[0],
                volcanoes = new NativeArray<Volcano>(Array.FindAll(Descriptor.volcanoes, volcano => Reaches(id, volcano.direction, 1.15f * volcano.radius)), Allocator.Persistent),
                blasts = new NativeArray<Blast>(Array.FindAll(Descriptor.blasts, blast => Reaches(id, blast.crater.xyz, TerrainNoise.Reach(blast))), Allocator.Persistent),
            }.Schedule();
            patch.building = true;
            patch.stale = false;
        }

        // Whether something that shows this many metres from where it is comes within a patch.
        bool Reaches(NodeId id, float3 direction, float reach) =>
            math.distance(id.Centre, direction) * Descriptor.radius < QuadtreeLod.NodeSize(Descriptor.radius, id.depth) + reach;

        // Blows a crater of this size in the ground at a point given in the body's own frame, and
        // takes whatever grew there with it.
        public void Dig(float3 point, float size) => Remake(body => body.Dig(point, size, Time.time));

        // Levels a pad of this radius to land on, at the height the ground has at a point given in
        // the body's own frame.
        public void Level(float3 point, float size) => Remake(body => body.Level(point, size));

        // Lets a blast change the ground, then rebuilds the patches it shows on and clears what grew there.
        void Remake(Action<BodyDescriptor> blast)
        {
            var before = Descriptor.blasts;
            blast(Descriptor);
            if (Descriptor.blasts == before) return;
            var left = Descriptor.blasts[^1];
            foreach (var pair in patches)
                if (Reaches(pair.Key, left.crater.xyz, TerrainNoise.Reach(left)))
                    pair.Value.stale = true;
            if (TryGetComponent(out Scatter scatter)) scatter.Clear(left.crater);
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
            shown.Clear();
        }

        void OnDestroy() => Clear();
    }
}
