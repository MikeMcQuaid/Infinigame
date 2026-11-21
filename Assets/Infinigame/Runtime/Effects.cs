using System;
using System.Collections.Generic;
using Infinigame.Core;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Random = Unity.Mathematics.Random;

namespace Infinigame
{
    // Everything that burns, bursts, smokes or steams. Each effect is put together from three
    // kinds of thing, none of them an object in the scene and each kind drawn in one instanced
    // call: puffs (balls of fire, smoke, steam or dust: the Puff shader), sparks (light with no
    // body: streaks, embers and the glare of a blast: the Spark shader) and wreckage (tumbling
    // pieces of hull). Like foes and bolts they are placed relative to the body the player is at.
    public class Effects : MonoBehaviour
    {
        // As many of each kind as one draw call takes. Puffs hurry through their lives as their
        // number nears it, so a long fight thins its smoke sooner and never runs out of them.
        const int Most = 1023;
        // The parts of a mushroom cloud.
        const int Cap = 0, Stem = 1, Ring = 2;

        struct Puff
        {
            // Where it is and how it moves: its own motion, which soon spends itself, and the
            // motion of what it came from, which only air stops. A tongue of flame has an axis.
            public float3 position, velocity, drift, axis;
            // how wide it is at first and at last, in metres, and how far it is drawn out along its axis
            public float age, life, from, to, stretch;
            public float roll, spin, seed;
            // how hot it is and how brightly lit from within, and what share of both it loses each second
            public float heat, glow, cooling;
            // how much sun reaches it through the rest of its cloud, how much it hides, how far it is already
            // eaten away to wisps when it begins, and the colour of its smoke
            public float shade, opacity, thin;
            public float3 colour;
            // what share of its speed it loses each second, and how fast it gathers speed upwards in air
            public float drag, lift;
            // which mushroom cloud it is part of, counting from 1, which part, and its place in that
            public int cloud, part;
            public float3 slot;
        }

        struct Spark
        {
            public float3 position, velocity, colour;
            // how many seconds of its flight it is drawn out along, and how far it hides what is behind it: glare hides nothing
            public float age, life, width, streak, hides;
            // what share of its speed it loses each second, and how much of gravity it feels: an ember rises
            public float drag, fall;
        }

        struct Chunk
        {
            public float3 position, velocity, axis, scale;
            public quaternion turn;
            // how far from the body's centre the ground is under it, and how nearly due its next puff of smoke is
            public float age, life, spin, floor, trail, damage;
            public uint seed;
            public bool organic;
        }

        // A smaller blast still to come: fuel and magazines go up after the craft that carried them.
        struct Pop
        {
            public float3 position, velocity;
            public float size, delay;
        }

        // A mushroom cloud, whose puffs are put where it says and not left to drift.
        struct Cloud
        {
            public float3 position, up, east, north;
            // curve is the radius of the ground its ring runs out over
            public float size, age, curve;
        }

        // The light a blast throws on everything round it.
        struct Glint
        {
            public Light lamp;
            public float3 position, velocity;
            public float power, age, life;
        }

        [NonSerialized] public Game game;

        static readonly int Look = Shader.PropertyToID("_Look"), Heat = Shader.PropertyToID("_Heat"), Smoke = Shader.PropertyToID("_Smoke"),
            Tail = Shader.PropertyToID("_Tail"), Glare = Shader.PropertyToID("_Glare");
        // every fire that has been kindled, with its light and how bright that was made
        static readonly List<(Transform fire, Light lamp, float bright)> fires = new List<(Transform, Light, float)>();
        // painted once: every puff there will ever be is cut from it

        readonly Puff[] puffs = new Puff[Most];
        readonly Spark[] sparks = new Spark[Most];
        readonly Chunk[] chunks = new Chunk[Most];
        readonly List<Pop> pops = new List<Pop>();
        readonly Cloud[] clouds = new Cloud[8];
        readonly Glint[] glints = new Glint[4];
        readonly Instances wreckage = new Instances(), tissue = new Instances();
        // what the shaders are told of each puff and spark, in the order they are drawn
        readonly Matrix4x4[] puffPlaces = new Matrix4x4[Most], sparkPlaces = new Matrix4x4[Most];
        readonly Vector4[] looks = new Vector4[Most], heats = new Vector4[Most], smokes = new Vector4[Most], tails = new Vector4[Most], glares = new Vector4[Most];
        readonly float[] nearness = new float[Most];
        readonly int[] order = new int[Most];
        int puffCount, sparkCount, chunkCount, kindled;
        Mesh card, shard;
        Material smoke, glare, iron, flesh, ichor;
        MaterialPropertyBlock puffLooks, sparkLooks;
        Random rnd = new Random(7);
        float3 sunward = new float3(0, 1, 0);
        float kindling;

        void Awake()
        {
            card = Models.Parts("Effect card")[0];
            shard = Shapes.Rock(2);
            smoke = new Material(Resources.Load<Shader>("Puff")) { enableInstancing = true };
            smoke.SetTexture("_Billows", Resources.Load<Texture2D>("Surfaces/Billows"));
            glare = new Material(Resources.Load<Shader>("Spark")) { enableInstancing = true };
            iron = Shapes.Hull(new Color(0.3f, 0.26f, 0.24f), 0.7f);
            flesh = Shapes.Solid(Surface.Chitin, new Color(0.38f, 0.035f, 0.6f), true);
            ichor = new Material(Resources.Load<Shader>("Ichor"));
            puffLooks = new MaterialPropertyBlock();
            sparkLooks = new MaterialPropertyBlock();
            for (int i = 0; i < glints.Length; i++)
            {
                var lamp = glints[i].lamp = new GameObject("Blast light").AddComponent<Light>();
                lamp.transform.SetParent(transform, false);
                lamp.type = LightType.Point;
                lamp.color = new Color(1f, 0.72f, 0.45f);
                lamp.enabled = false;
            }
            Clear();
        }

        // A ship or a drone blowing up: a flash, a turbulent fireball that rolls and darkens, smoke
        // that lingers, sparks and tumbling wreckage. size is the craft's size in metres (a raider
        // is about 10, a drone about 1.2, the citadel hundreds).
        public void Explode(float3 position, float3 velocity, float size)
        {
            Blast(position, velocity, size);
            float slow = Slow(size);
            for (int i = 0, pieces = (int)math.clamp(3f + size / 2f, 4f, 12f); i < pieces; i++)
            {
                float3 way = rnd.NextFloat3Direction();
                Shed(position + way * (0.4f * size), velocity + way * (size * rnd.NextFloat(1.2f, 5f) / slow), size * rnd.NextFloat(0.07f, 0.2f), slow * rnd.NextFloat(2.5f, 5f));
            }
            // then what it carried goes up, here and there about the wreck: all through the citadel as it comes apart
            for (int i = 0, more = size > 50f ? 10 : size > 3f ? 3 : 1; i < more; i++)
                pops.Add(new Pop
                {
                    position = position + rnd.NextFloat3Direction() * (0.7f * size), velocity = velocity * 0.7f + rnd.NextFloat3Direction() * (size / slow),
                    size = size * rnd.NextFloat(0.3f, 0.5f), delay = slow * rnd.NextFloat(0.15f, more > 3 ? 1.6f : 1f),
                });
        }

        // A shot striking a hull without destroying it: sparks, and a chunk or two knocked off along
        // the surface normal. Given the craft's velocity, what comes off it keeps up with it at first.
        public void Hit(float3 position, float3 normal, float size, float3 velocity = default)
        {
            Add(new Spark { position = position, velocity = velocity, colour = new float3(5f, 3.5f, 2f), life = 0.14f, width = 0.8f + 0.4f * size });
            for (int i = 0; i < 10; i++)
                Add(new Spark
                {
                    position = position, velocity = velocity + math.normalize(normal + 0.8f * rnd.NextFloat3Direction()) * (math.sqrt(size) * rnd.NextFloat(5f, 22f)), colour = Fire(rnd.NextFloat(0.6f, 0.9f)),
                    life = rnd.NextFloat(0.25f, 0.8f), width = 0.02f + 0.01f * size, streak = 0.035f, hides = 1f, drag = 1.5f, fall = 1f,
                });
            for (int i = 0, pieces = rnd.NextInt(1, 3); i < pieces; i++)
                Shed(position, velocity + math.normalize(normal + 0.5f * rnd.NextFloat3Direction()) * (math.sqrt(size) * rnd.NextFloat(2f, 7f)), size * rnd.NextFloat(0.03f, 0.07f), rnd.NextFloat(1.5f, 2.6f));
            // a breath of fire off the wound, gone to smoke at once
            Add(new Puff
            {
                position = position, velocity = normal * (0.5f * size), drift = velocity, life = rnd.NextFloat(0.8f, 1.4f), from = 0.12f * size, to = 0.55f * size,
                roll = rnd.NextFloat(6.28f), spin = rnd.NextFloat(-2f, 2f), seed = rnd.NextFloat(), heat = 1.2f, glow = 1f, cooling = 5f, shade = 0.8f, opacity = 0.8f, colour = 0.08f, drag = 2.5f, lift = 2f,
            });
        }

        readonly List<Transform> stains = new List<Transform>();
        public void Bleed(float3 position, float size)
        {
            var body = game.System.ReferenceBody;
            if (body.kind != BodyKind.Rock && body.kind != BodyKind.Station) return;
            float3 up = body.kind == BodyKind.Station ? new float3(0, 1, 0) : math.normalizesafe(position);
            float3 floor = body.kind == BodyKind.Station ? new float3(position.x, -28.95f, position.z) : up * (body.GroundRadius(up) + 0.045f);
            var pool = Shapes.Part(game.System.Bodies[game.System.Reference].transform, "Violet ichor pool", null,
                ichor, floor, Vector3.one, false).transform;
            pool.localRotation = Quaternion.FromToRotation(Vector3.up, up);
            var liquid = pool.gameObject.AddComponent<IchorPool>(); liquid.body = body; liquid.radius = size;
            stains.Add(pool);
            while (stains.Count > 48) { if (stains[0] != null) Destroy(stains[0].gameObject); stains.RemoveAt(0); }
        }
        public void Flesh(float3 position, float3 normal, float size, float3 velocity)
        {
            if (math.distance(position, game.Player.Position) < 5) game.GetComponent<Arms>().Stain(flesh);
            for (int i = 0; i < 12; i++)
            {
                float3 spray = math.normalizesafe(normal + rnd.NextFloat3Direction()) * rnd.NextFloat(2, 9);
                Add(new Puff { position = position, velocity = spray, drift = velocity, life = 0.7f, from = 0.06f * size, to = 0.18f * size,
                    colour = new float3(0.4f, 0.025f, 0.7f), shade = 0.7f, opacity = 0.8f, drag = 2, seed = rnd.NextFloat() });
                if (i < 5 && chunkCount < Most)
                {
                    Shed(position, velocity + spray, rnd.NextFloat(0.08f, 0.22f) * size, 3);
                    chunks[chunkCount - 1].organic = true;
                    chunks[chunkCount - 1].damage = 0;
                }
            }
        }

        public void Impact(float3 position, float3 up, float size, bool beam)
        {
            float3 centre = position + up * (0.10f * size);
            for (int i = 0; i < (beam ? 2 : 4); i++)
            {
                float3 outward = Walker.Project(rnd.NextFloat3Direction(), up);
                bool ember = i == 0;
                float3 plume = math.normalizesafe(up + outward * (ember ? 0.4f : 2.6f));
                Add(new Puff { position = centre + outward * (size * (ember ? 0.04f : 0.34f)), axis = plume,
                    velocity = plume * (size * (ember ? 1 : 4)), lift = 1, drag = 3,
                    from = (ember ? 0.19f : 0.16f) * size, to = (ember ? 0.58f : 1.2f) * size, stretch = ember ? 1.2f : 1.9f,
                    roll = rnd.NextFloat(6.28f), spin = rnd.NextFloat(-1.8f, 1.8f), seed = rnd.NextFloat(),
                    heat = ember ? 0.61f : 0.04f, cooling = ember ? 4 : 2, glow = ember ? 0.32f : 0.04f, shade = 0.85f, thin = 0.22f,
                    colour = ember ? new float3(0.31f, 0.11f, 0.041f) : i % 2 == 0 ? new float3(0.16f, 0.1f, 0.06f) : new float3(0.22f, 0.24f, 0.25f),
                    life = ember ? 0.34f : rnd.NextFloat(0.44f, 0.73f), opacity = ember ? 0.64f : 0.48f });
            }
            for (int i = 0; i < (beam ? 4 : 8); i++)
                Add(new Spark { position = centre, velocity = math.normalizesafe(up + rnd.NextFloat3Direction() * 1.3f) * rnd.NextFloat(2, 8) * math.sqrt(size),
                    colour = beam && i % 2 == 0 ? new float3(0.2f, 0.76f, 0.91f) : i % 3 == 0 ? new float3(1, 0.82f, 0.53f) : new float3(1, 0.39f, 0.1f),
                    life = rnd.NextFloat(0.18f, 0.46f), width = 0.028f, streak = 0.026f, fall = 1 });
        }

        public void Crackle(float3 position)
        {
            for (int i = 0; i < 2; i++) Add(new Spark { position = position, velocity = rnd.NextFloat3Direction() * 2,
                colour = new float3(0.3f, 3, 5), life = 0.1f, width = 0.015f, streak = 0.08f });
        }

        // A nuclear blast: a blinding flash, a fireball, a stem and a rolling cap that rise along up,
        // a ring racing out along the ground, lasting many seconds. size is the fireball's radius in
        // metres (about 100).
        public void Mushroom(float3 position, float3 up, float size)
        {
            // it takes the place of the cloud that has stood longest, which by now has blown away
            int oldest = 0;
            for (int i = 1; i < clouds.Length; i++)
                if (clouds[i].age > clouds[oldest].age)
                    oldest = i;
            for (int i = 0; i < puffCount; i++)
                if (puffs[i].cloud == oldest + 1)
                    puffs[i].life = 0f;
            float3 east = math.normalize(math.cross(up, math.abs(up.y) < 0.9f ? new float3(0, 1, 0) : new float3(1, 0, 0)));
            // on a world the ring follows the ground round; anywhere else it runs out flat
            clouds[oldest] = new Cloud { position = position, up = up, east = east, north = math.cross(up, east), size = size, curve = game.System.ReferenceBody.kind == BodyKind.Rock ? math.length(position) : 1e6f };

            // the flash, which for a moment is all there is to see, and the glare that outlasts it
            Add(new Spark { position = position + up * (0.5f * size), colour = new float3(40f, 36f, 30f), life = 0.4f, width = 5f * size });
            Add(new Spark { position = position + up * (0.5f * size), colour = new float3(10f, 8.5f, 6.5f), life = 1f, width = 9f * size });
            Flash(position + up * size, float3.zero, 40f * size * size, 30f * size, 4f);

            for (int i = 0; i < 64; i++)
            {
                // The cap: most of its puffs are its skin and the rest its burning heart. A place in
                // it is how far out from the core of the rolling ring, where round the stem and
                // where round the core.
                float core = i % 4 == 0 ? rnd.NextFloat(0.2f, 0.55f) : rnd.NextFloat(0.6f, 1f);
                Add(new Puff
                {
                    cloud = oldest + 1, part = Cap, slot = new float3(core, rnd.NextFloat(6.28f), rnd.NextFloat(6.28f)), life = rnd.NextFloat(36f, 46f), from = 0.9f * size, to = 1.45f * size,
                    roll = rnd.NextFloat(6.28f), spin = rnd.NextFloat(-0.25f, 0.25f), seed = rnd.NextFloat(), heat = 2f - 0.6f * core, glow = 1f, cooling = 0.12f + 0.06f * core, opacity = 1f, colour = new float3(0.42f, 0.38f, 0.34f),
                });
            }
            for (int i = 0; i < 30; i++)
            {
                // the stem: how far up it, where round it and how far out
                float high = (i + rnd.NextFloat()) / 30f;
                Add(new Puff
                {
                    cloud = oldest + 1, part = Stem, slot = new float3(high, rnd.NextFloat(6.28f), rnd.NextFloat(0.6f, 1.2f)), life = rnd.NextFloat(30f, 40f), from = 0.5f * size, to = 0.85f * size,
                    roll = rnd.NextFloat(6.28f), spin = rnd.NextFloat(-0.4f, 0.4f), seed = rnd.NextFloat(), heat = 0.5f + 0.9f * high, glow = 1f, cooling = 0.3f, opacity = 0.95f, colour = new float3(0.3f, 0.26f, 0.22f),
                });
            }
            for (int i = 0; i < 44; i++)
                // the ring: how far ahead of the rest it runs, where round it and how high it rides
                Add(new Puff
                {
                    cloud = oldest + 1, part = Ring, slot = new float3(rnd.NextFloat(), 6.28f * (i + rnd.NextFloat()) / 44f, rnd.NextFloat()), life = rnd.NextFloat(12f, 17f), from = 0.35f * size, to = 1.15f * size,
                    roll = rnd.NextFloat(6.28f), spin = rnd.NextFloat(-0.5f, 0.5f), seed = rnd.NextFloat(), heat = 0.6f, glow = 1f, cooling = 1.6f, opacity = 0.65f, colour = new float3(0.5f, 0.46f, 0.4f),
                });
        }

        // A shot or a blast striking water: a burst of steam that rises and thins.
        public void Steam(float3 position, float3 up, float size)
        {
            float slow = math.clamp(math.sqrt(size / 3f), 0.7f, 3f);
            for (int i = 0; i < 18; i++)
            {
                // of every three puffs one is thrown straight up and falls back, one rolls out over the water and one rises from those and thins
                int kind = i % 3;
                float Of(float thrown, float rolling, float rising) => kind == 0 ? thrown : kind == 1 ? rolling : rising;
                float3 way = rnd.NextFloat3Direction(), aside = math.normalizesafe(way - up * math.dot(way, up));
                Add(new Puff
                {
                    position = position + up * (size * Of(0.3f, 0.25f, 0.6f)) + aside * (size * rnd.NextFloat(0.3f)),
                    velocity = (up * Of(rnd.NextFloat(3f, 6f), 0.1f, rnd.NextFloat(0.6f, 1.4f)) + aside * Of(rnd.NextFloat(0.5f), rnd.NextFloat(1f, 2.4f), rnd.NextFloat(0.6f))) * (size / slow),
                    life = slow * Of(rnd.NextFloat(1f, 1.8f), rnd.NextFloat(2.5f, 4.5f), rnd.NextFloat(4f, 7f)), from = size * Of(0.4f, 0.6f, 0.8f), to = size * Of(1.3f, 2.2f, 3.4f),
                    roll = rnd.NextFloat(6.28f), spin = rnd.NextFloat(-0.8f, 0.8f), seed = rnd.NextFloat(), shade = 1f, opacity = Of(0.85f, 0.6f, 0.45f), thin = Of(0f, 0.1f, 0.3f), colour = new float3(0.9f, 0.92f, 0.95f),
                    drag = Of(1.8f, 1.6f, 1f) / slow, lift = Of(-7f, 0.25f, 1.6f),
                });
            }
        }

        // Everything in flight is dropped: when the scene's frame of reference changes, or a new system is built.
        public void Clear()
        {
            puffCount = sparkCount = chunkCount = 0;
            pops.Clear();
            for (int i = 0; i < clouds.Length; i++) clouds[i].age = float.MaxValue;
            for (int i = 0; i < glints.Length; i++) glints[i].life = 0f;
        }

        // Makes a camp's fire burn: licking flames, embers rising, smoke, and a light that flickers.
        // fire is the "Fire" child of the camp built by Models.Camp(). The flames modelled there
        // make way for these.
        public static void Kindle(Transform fire)
        {
            var modelled = fire.Find("Flame");
            if (modelled != null) modelled.gameObject.SetActive(false);
            var lamp = fire.GetComponent<Light>();
            fires.Add((fire, lamp, lamp != null ? lamp.intensity : 0f));
        }

        // The colour of something this hot: dull red, then orange, yellow and white, and far brighter the hotter. The same sum as Fire.hlsl.
        static float3 Fire(float heat) => new float3(1f, 0.9f, 0.7f) * math.pow(new float3(math.min(heat, 1f)), new float3(1f, 2.2f, 5f)) * (14f * heat * heat);

        // Big things burn, fall apart and clear for longer.
        static float Slow(float size) => math.clamp(math.sqrt(size / 10f), 0.5f, 3f);

        // How wide a puff is by now: it swells fast at first and then hardly at all.
        static float Wide(in Puff puff)
        {
            float left = 1f - puff.age / puff.life;
            return math.lerp(puff.to, puff.from, left * left * left);
        }

        void Add(in Puff puff)
        {
            if (puffCount < Most) puffs[puffCount++] = puff;
        }

        void Add(in Spark spark)
        {
            // Impact glare can cover both eyes at arm’s length. Keep the moving sparks.
            if (Shapes.MatteCombat && spark.streak == 0 && spark.width >= 0.25f) return;
            if (sparkCount < Most) sparks[sparkCount++] = spark;
        }

        // A piece of hull comes away, tumbling: a plate more than a lump, mostly holes, their edges still glowing.
        void Shed(float3 position, float3 velocity, float size, float life)
        {
            if (chunkCount == Most) return;
            chunks[chunkCount++] = new Chunk
            {
                position = position, velocity = velocity, axis = rnd.NextFloat3Direction(), scale = size * new float3(1f, rnd.NextFloat(0.25f, 0.6f), rnd.NextFloat(0.5f, 1f)), turn = rnd.NextQuaternionRotation(),
                life = life, spin = rnd.NextFloat(3f, 12f), floor = Ship.Surface(game.System.ReferenceBody, math.normalizesafe(position, new float3(0, 1, 0))), damage = rnd.NextFloat(0.45f, 0.8f), seed = rnd.NextUInt(),
            };
        }

        // Lights everything round a blast for a moment, with the lamp that is doing least.
        void Flash(float3 position, float3 velocity, float power, float range, float life)
        {
            if (Shapes.MatteCombat) return;
            int dimmest = 0;
            float Left(in Glint glint) => glint.age < glint.life ? glint.power * (1f - glint.age / glint.life) : 0f;
            for (int i = 1; i < glints.Length; i++)
                if (Left(glints[i]) < Left(glints[dimmest]))
                    dimmest = i;
            if (Left(glints[dimmest]) > power) return;
            glints[dimmest].lamp.range = range;
            (glints[dimmest].position, glints[dimmest].velocity, glints[dimmest].power, glints[dimmest].age, glints[dimmest].life) = (position, velocity, power, 0f, life);
        }

        // A ball of fire that rolls out, darkens and leaves its smoke hanging, with its flash and its sparks.
        void Blast(float3 position, float3 velocity, float size)
        {
            float slow = Slow(size);
            Add(new Spark { position = position, velocity = velocity, colour = new float3(1.6f, 1.3f, 0.9f), life = 0.1f + 0.08f * slow, width = 4f * size });
            Flash(position, velocity, 30f * size * size, 14f * size, 0.15f + 0.35f * slow);
            for (int i = 0, most = (int)math.clamp(8f + size, 9f, 36f); i < most; i++)
            {
                // two in three are the fire itself, most of it rolling over itself in the middle with a
                // tongue or two thrown clear; the third is slower, cooler and stays as smoke
                bool lingers = i % 3 == 2;
                float3 way = rnd.NextFloat3Direction();
                float thrown = rnd.NextFloat();
                Add(new Puff
                {
                    position = position + way * (size * rnd.NextFloat(0.7f)), velocity = way * (size * (lingers ? rnd.NextFloat(0.3f, 1.4f) : math.lerp(0.5f, 4.5f, thrown * thrown)) / slow), drift = velocity,
                    life = slow * (lingers ? rnd.NextFloat(6f, 10f) : rnd.NextFloat(3f, 5.5f)), from = 0.8f * size, to = size * (lingers ? 2.8f : 2.4f),
                    roll = rnd.NextFloat(6.28f), spin = rnd.NextFloat(-1.5f, 1.5f) / slow, seed = rnd.NextFloat(), heat = lingers ? 0.6f : 1.3f, glow = 1f, cooling = (lingers ? 3f : 2.1f) / slow,
                    // the side of the ball away from the sun is in the shade of the rest
                    shade = 0.3f + 0.7f * math.saturate(0.5f + 0.5f * math.dot(way, sunward)), opacity = lingers ? 0.75f : 0.95f, thin = lingers ? 0.2f : 0f, colour = lingers ? 0.16f : 0.07f, drag = 3f / slow, lift = 3f * slow,
                });
            }
            for (int i = 0, most = (int)math.clamp(10f + 2f * size, 12f, 48f); i < most; i++)
                Add(new Spark
                {
                    position = position, velocity = velocity + rnd.NextFloat3Direction() * (size * rnd.NextFloat(3f, 14f) / slow), colour = Fire(rnd.NextFloat(0.6f, 0.85f)),
                    life = slow * rnd.NextFloat(0.5f, 1.6f), width = 0.03f + 0.012f * size, streak = 0.025f, hides = 1f, drag = 1.2f, fall = 1f,
                });
        }

        // Puts a puff of a mushroom cloud where it belongs at this moment, and says how much sun reaches it there.
        void Place(ref Puff puff, in Cloud cloud)
        {
            float t = cloud.age, size = cloud.size, swell = 1f - math.exp(-5f * t);
            // how far out the cap reaches, and how high its middle stands
            float reach = size * (0.75f + 0.75f * (1f - math.exp(-t / 8f))) * swell, height = size * (0.5f + 3f * (1f - math.exp(-t / 9f)));
            math.sincos(puff.slot.y + (puff.part == Stem ? 0.12f * t : 0f), out float sin, out float cos);
            float3 outward = cloud.east * cos + cloud.north * sin, heart = cloud.position + cloud.up * height;
            if (puff.part == Cap)
            {
                // A ball of fire at first, then a ring that widens and rolls: up through the middle,
                // out over the top and down the outside, the inside turning faster than the skin.
                float ring = reach * math.lerp(0.12f, 0.6f, math.smoothstep(0f, 10f, t)), tube = puff.slot.x * (reach - ring);
                math.sincos(puff.slot.z - 2.2f * math.log(1f + t / 2f) / (0.35f + puff.slot.x), out float rise, out float spread);
                puff.position = heart + cloud.up * (0.7f * tube * rise) + outward * (ring + tube * spread);
            }
            else if (puff.part == Stem)
            {
                // drawn up after the cap, flared at its foot and where it runs into the cap
                float high = puff.slot.x;
                puff.position = cloud.position + cloud.up * (high * math.max(height - 0.35f * reach, 0f))
                    + outward * (size * swell * puff.slot.z * (0.1f + 0.3f * math.pow(1f - high, 4f) + 0.14f * high * high));
                heart = puff.position - outward * size;
            }
            else
            {
                // racing out over the ground, fast at first, and round with it where the ground is round
                float bend = size * (0.5f + (3.2f + 0.6f * puff.slot.x) * (1f - math.exp(-t / 3f))) / cloud.curve;
                puff.position = cloud.position - cloud.up * cloud.curve
                    + (cloud.up * math.cos(bend) + outward * math.sin(bend)) * (cloud.curve + (0.25f + 0.2f * puff.slot.z) * Wide(puff));
                heart = cloud.position;
            }
            puff.shade = math.saturate(0.6f + 0.5f * math.dot(math.normalizesafe(puff.position - heart), sunward));
        }

        void Update()
        {
            var system = game.System;
            var body = system.ReferenceBody;
            float3 anchor = system.Anchor, eye = (float3)Camera.main.transform.position + anchor;
            sunward = math.normalizesafe((float3)system.Bodies[0].transform.position + anchor - eye, sunward);
            // smoke rises and wreckage slows where there is air, and everything falls towards the body
            float air = body.air > 0f ? body.radius * Shapes.AirTop(body) : 0f, pull = body.surfaceGravity * body.radius * body.radius;
            // nothing moves while the game is paused
            float dt = math.min(Time.deltaTime, 0.1f);

            // fires burn: a tongue of flame thirty-six times a second, and now and then an ember and a breath of smoke
            fires.RemoveAll(static camp => camp.fire == null);
            int tongues = (int)((kindling += dt) * 36f);
            kindling -= tongues / 36f;
            foreach (var (fire, lamp, bright) in fires)
            {
                float3 hearth = (float3)fire.position + anchor, up = fire.up;
                // from over the horizon there is nothing of it to see
                if (dt == 0f || math.distancesq(hearth, eye) > 1500f * 1500f) continue;
                if (lamp != null) lamp.intensity = bright * (0.75f + 0.35f * Mathf.PerlinNoise(2.7f * Time.time, 0f) + 0.15f * Mathf.PerlinNoise(9f * Time.time, 7f));
                for (int i = 0; i < tongues; i++)
                {
                    float3 way = rnd.NextFloat3Direction(), aside = math.normalizesafe(way - up * math.dot(way, up)) * (0.3f * math.sqrt(rnd.NextFloat()));
                    // flames lean in over the middle of the fire as they climb, and taper
                    float3 rising = up * rnd.NextFloat(1f, 1.9f) - aside * 0.8f;
                    Add(new Puff
                    {
                        position = hearth + up * 0.14f + aside, velocity = rising, axis = math.normalize(rising), stretch = rnd.NextFloat(1.8f, 2.8f), life = rnd.NextFloat(0.5f, 0.95f),
                        from = rnd.NextFloat(0.3f, 0.5f), to = 0.12f, seed = rnd.NextFloat(), heat = 0.85f, cooling = 1.2f, shade = 1f, opacity = 0.12f, drag = 0.6f, lift = 1.2f,
                    });
                    if ((kindled + i) % 6 == 0)
                        Add(new Spark
                        {
                            position = hearth + up * 0.3f + aside, velocity = up * rnd.NextFloat(0.9f, 2.4f) + way * 0.4f, colour = Fire(rnd.NextFloat(0.5f, 0.62f)),
                            life = rnd.NextFloat(0.8f, 2.4f), width = 0.009f, streak = 0.02f, hides = 1f, drag = 0.5f, fall = -0.12f,
                        });
                    if ((kindled + i) % 8 == 0)
                        Add(new Puff
                        {
                            position = hearth + up * 0.9f + aside, velocity = up * 0.8f + way * 0.15f, life = rnd.NextFloat(3.5f, 5.5f), from = 0.3f, to = 1.2f, roll = rnd.NextFloat(6.28f), spin = rnd.NextFloat(-0.4f, 0.4f),
                            seed = rnd.NextFloat(), glow = 0.3f, cooling = 1.2f, shade = 0.9f, opacity = 0.12f, thin = 0.45f, colour = new float3(0.5f, 0.52f, 0.56f), drag = 0.25f, lift = 0.25f,
                        });
                }
            }
            kindled += tongues;

            for (int i = pops.Count - 1; i >= 0 && dt > 0f; i--)
            {
                var pop = pops[i];
                pop.position += pop.velocity * dt;
                pop.delay -= dt;
                pops[i] = pop;
                if (pop.delay > 0f) continue;
                pops.RemoveAt(i);
                Blast(pop.position, pop.velocity, pop.size);
            }
            for (int i = 0; i < clouds.Length; i++) clouds[i].age += dt;

            for (int i = 0; i < glints.Length; i++)
            {
                ref var glint = ref glints[i];
                glint.age += dt;
                glint.position += glint.velocity * dt;
                float left = 1f - glint.age / math.max(glint.life, 1e-3f);
                glint.lamp.enabled = left > 0f;
                glint.lamp.intensity = glint.power * left * left;
                glint.lamp.transform.position = glint.position - anchor;
            }

            for (int i = chunkCount - 1; i >= 0 && dt > 0f; i--)
            {
                ref var chunk = ref chunks[i];
                if ((chunk.age += dt) >= chunk.life)
                {
                    chunk = chunks[--chunkCount];
                    continue;
                }
                // the citadel blows up at the very centre of its own frame, where there is no way down
                float far = math.max(math.length(chunk.position), 1f);
                float3 up = chunk.position / far;
                chunk.velocity = (chunk.velocity - up * (pull / (far * far) * dt)) * math.exp(far < air ? -0.2f * dt : 0f);
                chunk.position += chunk.velocity * dt;
                // it bounces where it comes down, and soon lies still
                float sunk = chunk.floor + 0.3f * chunk.scale.x - math.length(chunk.position), falling = math.dot(chunk.velocity, up);
                if (sunk > 0f && falling < 0f)
                {
                    chunk.position += up * sunk;
                    chunk.velocity = (chunk.velocity - up * falling) * 0.5f - up * (0.3f * falling);
                    chunk.spin *= 0.4f;
                }
                chunk.turn = math.mul(quaternion.AxisAngle(chunk.axis, chunk.spin * dt), chunk.turn);
                // It trails fire at first and then smoke: a puff for each length of one that it travels,
                // so that they run together, though never more than twenty a second, and only while
                // there are puffs to spare.
                float burning = 1f - chunk.age / (0.6f * chunk.life), puffed = 0.3f + 1.5f * chunk.scale.x;
                if (chunk.organic || (chunk.trail += dt * math.min(math.length(chunk.velocity) / (0.8f * puffed), 20f)) < 1f || burning < 0f || puffCount > Most * 2 / 3) continue;
                chunk.trail = 0f;
                Add(new Puff
                {
                    position = chunk.position - math.normalizesafe(chunk.velocity) * (0.5f * puffed), velocity = rnd.NextFloat3Direction() * (0.6f * chunk.scale.x), drift = 0.3f * chunk.velocity, life = rnd.NextFloat(0.8f, 1.3f), from = puffed, to = 2.6f * puffed,
                    roll = rnd.NextFloat(6.28f), spin = rnd.NextFloat(-2f, 2f), seed = rnd.NextFloat(), heat = 0.9f * burning, glow = burning, cooling = 4f, shade = 0.8f, opacity = 0.7f, colour = 0.1f, drag = 2f, lift = 1.5f,
                });
            }

            float hurry = 1f + 3f * math.saturate((puffCount / (float)Most - 0.6f) / 0.4f);
            for (int i = puffCount - 1; i >= 0 && dt > 0f; i--)
            {
                ref var puff = ref puffs[i];
                if ((puff.age += dt * hurry) >= puff.life)
                {
                    puff = puffs[--puffCount];
                    continue;
                }
                float cooled = math.exp(-puff.cooling * dt), slowed = math.exp(-puff.drag * dt);
                puff.heat *= cooled;
                puff.glow *= cooled;
                puff.roll += puff.spin * dt;
                if (puff.cloud > 0)
                {
                    Place(ref puff, clouds[puff.cloud - 1]);
                    continue;
                }
                float far = math.max(math.length(puff.position), 1f);
                bool inAir = far < air;
                puff.velocity = puff.velocity * slowed + puff.position * (inAir ? puff.lift * dt / far : 0f);
                puff.drift *= inAir ? math.exp(-1.5f * dt) : 1f;
                // it stops turning over as it stops spreading
                puff.spin *= math.lerp(1f, slowed, 0.3f);
                puff.position += (puff.velocity + puff.drift) * dt;
            }

            for (int i = sparkCount - 1; i >= 0 && dt > 0f; i--)
            {
                ref var spark = ref sparks[i];
                if ((spark.age += dt) >= spark.life)
                {
                    spark = sparks[--sparkCount];
                    continue;
                }
                float far = math.max(math.length(spark.position), 1f);
                spark.velocity = (spark.velocity - spark.position * (spark.fall * pull / (far * far * far) * dt)) * math.exp(-spark.drag * dt);
                spark.position += spark.velocity * dt;
            }

            // Drawn every frame, paused or not. Smoke hides what is behind it, so puffs go far ones first.
            var bounds = new Bounds(Vector3.zero, Vector3.one * 20000f);
            for (int i = 0; i < puffCount; i++)
            {
                nearness[i] = -math.distancesq(puffs[i].position, eye);
                order[i] = i;
            }
            Array.Sort(nearness, order, 0, puffCount);
            for (int i = 0; i < puffCount; i++)
            {
                ref var puff = ref puffs[order[i]];
                float age = puff.age / puff.life, wide = Wide(puff);
                float3 at = puff.position - anchor, axis = puff.stretch > 1f ? puff.axis * wide : new float3(0f, wide, 0f);
                puffPlaces[i] = new Matrix4x4(new Vector4(wide, 0f, 0f, 0f), new Vector4(axis.x, axis.y, axis.z, 0f), new Vector4(0f, 0f, wide, 0f), new Vector4(at.x, at.y, at.z, 1f));
                looks[i] = new Vector4(puff.roll, age, puff.seed, puff.stretch);
                // it is eaten away as it ages
                heats[i] = new Vector4(puff.heat, puff.glow, puff.shade, math.lerp(0.1f + puff.thin, 0.85f, age));
                // there in a moment, not at once, and gone by the end without ever vanishing
                smokes[i] = new Vector4(puff.colour.x, puff.colour.y, puff.colour.z, puff.opacity * math.saturate(puff.age / 0.06f) * (1f - age * age * age));
            }
            if (puffCount > 0)
            {
                puffLooks.SetVectorArray(Look, looks);
                puffLooks.SetVectorArray(Heat, heats);
                puffLooks.SetVectorArray(Smoke, smokes);
                Graphics.RenderMeshInstanced(new RenderParams(smoke) { worldBounds = bounds, matProps = puffLooks, shadowCastingMode = ShadowCastingMode.Off }, card, 0, puffPlaces, puffCount);
            }

            for (int i = 0; i < sparkCount; i++)
            {
                ref var spark = ref sparks[i];
                float left = 1f - spark.age / spark.life;
                float3 tail = spark.velocity * spark.streak;
                sparkPlaces[i] = Matrix4x4.Translate(spark.position - anchor);
                tails[i] = new Vector4(tail.x, tail.y, tail.z, spark.width);
                // as it cools it reddens: its blue goes first and its red last
                glares[i] = new Vector4(spark.colour.x * left, spark.colour.y * left * left, spark.colour.z * left * left * left * left, spark.hides);
            }
            if (sparkCount > 0)
            {
                sparkLooks.SetVectorArray(Tail, tails);
                sparkLooks.SetVectorArray(Glare, glares);
                Graphics.RenderMeshInstanced(new RenderParams(glare) { worldBounds = bounds, matProps = sparkLooks, shadowCastingMode = ShadowCastingMode.Off }, card, 0, sparkPlaces, sparkCount);
            }

            wreckage.Clear(); tissue.Clear();
            for (int i = 0; i < chunkCount; i++)
                // a piece shrinks away at the end and is never seen to vanish
                (chunks[i].organic ? tissue : wreckage).Add(Matrix4x4.TRS(chunks[i].position - anchor, chunks[i].turn, chunks[i].scale * math.saturate(2f * (chunks[i].life - chunks[i].age))), chunks[i].damage, chunks[i].seed);
            wreckage.Draw(shard, iron, false, bounds);
            tissue.Draw(shard, flesh, true, bounds);
        }

    }
}
