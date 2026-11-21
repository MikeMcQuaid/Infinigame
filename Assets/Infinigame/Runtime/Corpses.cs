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
            public float radius, age, still, supported, stain;
            public float3[] before;
            public float3 anchor;
            public bool asleep;
            public Blast[] terrain;
            public int rooms;
        }
        readonly List<Body> bodies = new List<Body>();
        public int Count => bodies.Count;
        public int ActiveCount => bodies.Count(body => body.world == game.System.Reference && !body.asleep);
        public void AddModel(Transform model, float3 at, float3 velocity, bool ship)
        {
            Add(Models.Wreck(ship ? "Ship" : "Robot"), model.GetComponentInChildren<MeshRenderer>().sharedMaterial, at, model.rotation, velocity, ship ? 0.8f : 0.25f);
        }
        public void Add(ModelParts wreck, Material material, float3 at, Quaternion rotation, float3 velocity, float radius, float scale = 1, bool crumble = false, float stain = 0)
        {
            while (bodies.Count >= (LodSettings.Tier == 0 ? 16 : 40)) Remove(0);
            var points = wreck.centres.Select(centre => game.Outpost.Resolve(at, at + (float3)(rotation * centre * scale), radius, out _, false)).ToArray();
            var parts = wreck.meshes.Select(mesh => Shapes.Part(transform, "Wreck / remains", mesh, material, Vector3.zero, Vector3.one * scale).transform).ToArray();
            foreach (var part in parts)
            {
                part.rotation = rotation;
                if (material.HasProperty("_Organic") && material.GetFloat("_Organic") > 0)
                { var wound = new MaterialPropertyBlock(); wound.SetFloat("_Damage", 0.28f); part.GetComponent<Renderer>().SetPropertyBlock(wound); }
            }
            var joints = new Ragdoll(points, crumble ? float3.zero : velocity);
            if (crumble) for (int i = 0; i < joints.joined.Length; i++) joints.joined[i] = false;
            bodies.Add(new Body { world = game.System.Reference, joints = joints, parts = parts, rotation = rotation, radius = radius, before = new float3[points.Length], stain = stain });
        }
        public void Strike(float3 from, float3 step, float damage, float radius = 0.7f)
        {
            foreach (var body in bodies)
                if (body.world == game.System.Reference && body.joints.Strike(from, step, damage, radius)) { body.age = body.still = body.supported = 0; body.asleep = false; }
        }
        public void Clear() { while (bodies.Count > 0) Remove(bodies.Count - 1); }
        void Remove(int index)
        {
            foreach (var part in bodies[index].parts) if (part != null) Destroy(part.gameObject);
            bodies.RemoveAt(index);
        }
        bool Supported(Body body, BodyDescriptor world)
        {
            bool connectedOnGround = false;
            for (int i = 0; i < body.parts.Length; i++)
            {
                float3 at = body.joints.positions[i];
                float3 up = world.kind == BodyKind.Station ? new float3(0, 1, 0) : math.normalizesafe(at);
                bool contact = world.kind != BodyKind.Station && math.abs(math.length(at) - world.GroundRadius(up) - body.radius) < 0.12f;
                if (!contact) game.Outpost.Resolve(at, at - up * 0.16f, body.radius, out contact, false);
                if (i == 0 || body.joints.joined[i]) connectedOnGround |= contact;
                else if (!contact) return false;
            }
            return connectedOnGround;
        }
        void LateUpdate()
        {
            float dt = math.min(Time.deltaTime, 0.05f);
            foreach (var body in bodies)
            {
                bool local = body.world == game.System.Reference;
                bool changedWorld = body.parts[0].gameObject.activeSelf != local;
                if (changedWorld) foreach (var part in body.parts) part.gameObject.SetActive(local);
                if (local && body.asleep && (body.terrain != game.System.ReferenceBody.blasts || body.rooms != game.Outpost.GeometryVersion))
                {
                    var world = game.System.ReferenceBody;
                    body.terrain = world.blasts; body.rooms = game.Outpost.GeometryVersion;
                    if (!Supported(body, world)) { body.asleep = false; body.age = body.still = body.supported = 0; }
                }
                if (!local || !changedWorld && body.asleep && math.all(body.anchor == game.System.Anchor)) continue;
                body.anchor = game.System.Anchor;
                if (!body.asleep && dt > 0)
                {
                    for (int step = 0; step < 3; step++)
                    {
                        Array.Copy(body.joints.positions, body.before, body.before.Length);
                        body.joints.Step(game.System.ReferenceBody, dt / 3, body.radius);
                        for (int i = 0; i < body.parts.Length; i++)
                        {
                            float3 next = game.Outpost.Resolve(body.before[i], body.joints.positions[i], body.radius, out bool hit, false);
                            if (hit) body.joints.velocities[i] = (next - body.before[i]) * (3 / dt);
                            body.joints.positions[i] = next;
                        }
                    }
                    body.age += dt;
                    float travel = 0;
                    float3 centre = float3.zero;
                    for (int i = 0; i < body.parts.Length; i++)
                    {
                        travel = math.max(travel, math.distancesq(body.before[i], body.joints.positions[i]));
                        centre += body.joints.positions[i];
                    }
                    body.still = travel < math.square(0.002f + dt * 0.08f) ? body.still + dt : 0;
                    if (body.still > 0.45f || body.age >= 12)
                    {
                        centre /= body.parts.Length;
                        var world = game.System.ReferenceBody;
                        bool grounded = Supported(body, world);
                        body.supported = grounded && travel < math.square(0.003f + dt * 0.8f) ? body.supported + dt : 0;
                        body.asleep = grounded && (body.still > 0.45f || body.supported > 0.75f);
                        if (body.asleep) { body.terrain = world.blasts; body.rooms = game.Outpost.GeometryVersion; }
                        if (body.asleep && body.stain > 0)
                        {
                            game.Effects.Bleed(centre, body.stain);
                            body.stain = 0;
                        }
                    }
                }
                for (int i = 0; i < body.parts.Length; i++)
                {
                    var part = body.parts[i];
                    part.position = body.joints.positions[i] - game.System.Anchor;
                    float speed = math.length(body.joints.velocities[i]);
                    if (!body.asleep && speed > 0.4f) part.rotation = body.rotation * Quaternion.Euler(body.age * 25, i * body.age * 8, body.age * 35);
                }
            }
        }
        void OnDestroy() => Clear();
    }
}
