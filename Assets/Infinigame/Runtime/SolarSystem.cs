using System;
using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Infinigame
{
    // Runs the clock and places the bodies. The scene is the frame of the body the player is nearest:
    // that body sits still at the origin while the rest of the system wheels round it, so the ground
    // underfoot never moves and positions near the player keep full float precision.
    [DefaultExecutionOrder(-100)]
    public class SolarSystem : MonoBehaviour
    {
        public SolarSystemDescriptor Descriptor { get; private set; }
        public CelestialBody[] Bodies { get; private set; }
        public int Reference { get; private set; }
        public double Clock { get; private set; }
        public void RestoreClock(double time) => Clock = time;
        public int Gate => Array.FindIndex(Descriptor.bodies, body => body.kind == BodyKind.Gate);
        public BodyDescriptor ReferenceBody => Descriptor.bodies[Reference];

        // Turns inertial directions into scene directions.
        public Quaternion SceneRotation { get; private set; }

        // Where the scene's origin is in the reference body's frame. Out between the worlds the only
        // reference is the sun, tens of kilometres off, where floats are too coarse to draw a cockpit
        // steadily. There the origin rides with the player instead of sitting on the body.
        public float3 Anchor { get; private set; }

        // How hard it is raining on the player, 0 to 1.
        public float Rain { get; private set; }

        // Where the player is, relative to the reference body. Set by the player every frame.
        [NonSerialized] public float3 Focus;

        // Where a body's centre is in the reference body's frame, now or some seconds ahead.
        public float3 ToReference(int body, double ahead = 0) => Descriptor.ToLocal(Reference, Clock + ahead, Descriptor.PositionAt(body, Clock + ahead));

        // The raiders' base, as a body.
        public int Station => Array.FindIndex(Descriptor.bodies, body => body.kind == BodyKind.Station);

        // Whether a body is worth a marker and a place on the autopilot's list from where the player
        // is: the star, the planets and the base always, a moon from within its own planet's space
        // or from wherever it is near enough to show as more than a speck.
        public bool Surveyed;
        public bool Charted(int body) => !Descriptor.bodies[body].destroyed && (Descriptor.bodies[body].orbit.parent <= 0 || body == Reference
            || Descriptor.bodies[body].radius > 0.004f * math.distance(Focus, ToReference(body)));

        // Raised with (from, to) when the reference body changes so movers can rebase themselves.
        public event Action<int, int> ReferenceChanged;

        // Draws every orbit as a ring, for the view of the whole system; and for the map of one
        // world, leaves out the haze of its air.
        [NonSerialized] public bool ShowOrbits, Clear;

        Light sunlight, secondLight;
        LineRenderer[] orbits;
        readonly Vector4[] storms = new Vector4[Weather.Storms];

        public void Build(SolarSystemDescriptor descriptor, int reference)
        {
            if (Bodies != null)
                foreach (var body in Bodies)
                    Destroy(body.gameObject);
            Descriptor = descriptor;
            Reference = reference;
            Bodies = new CelestialBody[descriptor.bodies.Length];
            for (int i = 0; i < Bodies.Length; i++)
            {
                Bodies[i] = new GameObject().AddComponent<CelestialBody>();
                Bodies[i].transform.SetParent(transform, false);
                Bodies[i].Init(descriptor.bodies[i]);
            }
            orbits = new LineRenderer[Bodies.Length];
            // the interface's shader: seen from both sides, since a ring's winding depends on where you stand
            var line = new Material(Resources.Load<Shader>("Overlay"));
            for (int i = 0; i < Bodies.Length; i++)
            {
                if (descriptor.bodies[i].orbit.parent < 0) continue;
                orbits[i] = Bodies[i].gameObject.AddComponent<LineRenderer>();
                orbits[i].sharedMaterial = line;
                orbits[i].startColor = orbits[i].endColor = new Color(0.45f, 0.75f, 1f, 0.55f);
                orbits[i].loop = true;
                orbits[i].positionCount = 128;
                orbits[i].shadowCastingMode = ShadowCastingMode.Off;
            }

            // found once, while the scene's sun is the only light there is
            if (sunlight == null) sunlight = FindAnyObjectByType<Light>();
            RenderSettings.skybox = new Material(Resources.Load<Shader>("Stars"));
            // shade is lit from where the sky is, which on a small round world is not always +y: Place works it out
            RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            // a run begins by daylight: the clock is wound on until the sun is well up over the landing site
            float3 landing = ReferenceBody.FindLanding(descriptor.seed + 1u);
            if (Clock == 0)
                for (int i = 0; i < 96 && math.dot(landing, math.normalize(ToReference(0))) < 0.5f; i++) Clock += math.abs(ReferenceBody.spinPeriod) / 96;
            Place();
        }

        void Update()
        {
            Clock += Time.deltaTime;
            int next = Descriptor.ReferenceBody(Descriptor.ToInertial(Reference, Clock, Focus), Clock, Reference);
            if (next != Reference)
            {
                int previous = Reference;
                Reference = next;
                float3 still = float3.zero;
                Descriptor.Rebase(previous, next, Clock, ref Focus, ref still);
                ReferenceChanged?.Invoke(previous, next);
            }
            Place();
        }

        void Place()
        {
            double3 origin = Descriptor.PositionAt(Reference, Clock);
            quaternion toScene = math.inverse(ReferenceBody.Rotation(Clock));
            SceneRotation = toScene;
            Anchor = Reference == 0 ? Focus : float3.zero;
            Vector3 Scene(double3 inertial) => math.rotate(toScene, (float3)(inertial - origin)) - Anchor;

            for (int i = 0; i < Bodies.Length; i++)
            {
                Bodies[i].IsReference = i == Reference;
                Bodies[i].transform.SetPositionAndRotation(Scene(Descriptor.PositionAt(i, Clock)), math.mul(toScene, Descriptor.bodies[i].Rotation(Clock)));
                if (orbits[i] == null) continue;
                orbits[i].enabled = ShowOrbits && !Descriptor.bodies[i].destroyed;
                if (!ShowOrbits) continue;
                var orbit = Descriptor.bodies[i].orbit;
                double3 centre = Descriptor.PositionAt(orbit.parent, Clock);
                orbits[i].widthMultiplier = Vector3.Distance(Camera.main.transform.position, Scene(centre)) * 0.003f;
                for (int k = 0; k < orbits[i].positionCount; k++)
                    orbits[i].SetPosition(k, Scene(centre + orbit.Position(orbit.period * k / orbits[i].positionCount)));
            }

            // Every world's shaders light it from where the sun (body 0) stands for that world, and the
            // home world's clouds gather where its storms are.
            // sunlight is close to white whatever colour the star looks, so grey moons stay grey
            Color sun = Color.Lerp(Shapes.SunColour(Descriptor.bodies[0]), Color.white, 0.8f);
            Shader.SetGlobalVector("_SunPosition", Bodies[0].transform.position);
            Shader.SetGlobalColor("_SunLight", sun * 1.8f);
            int second = Array.FindLastIndex(Descriptor.bodies, star => star.kind == BodyKind.Sun);
            if (second > 0 && secondLight == null)
            {
                secondLight = new GameObject("Companion starlight").AddComponent<Light>();
                secondLight.transform.SetParent(transform, false);
                secondLight.type = LightType.Directional;
            }
            if (secondLight != null)
            {
                secondLight.enabled = second > 0;
                if (second > 0)
                {
                    secondLight.transform.rotation = Quaternion.LookRotation((Vector3)(Focus - ToReference(second)));
                    secondLight.color = Color.Lerp(Shapes.SunColour(Descriptor.bodies[second]), Color.white, 0.8f);
                    secondLight.intensity = 0.8f * math.smoothstep(-0.1f, 0.2f, math.dot(math.normalizesafe(Focus), math.normalizesafe(ToReference(second) - Focus)));
                }
            }
            Shader.SetGlobalVector("_SecondSunPosition", second > 0 ? Bodies[second].transform.position : Vector3.zero);
            Shader.SetGlobalColor("_SecondSunLight", second > 0 ? secondLight.color * 0.8f : Color.black);
            for (int i = 0; i < storms.Length; i++) storms[i] = Weather.Storm(Descriptor.bodies[SolarSystemDescriptor.Home], i, Clock);
            Shader.SetGlobalVectorArray("_Storms", storms);
            // the lowest detail level goes without the costlier touches, such as shading within the clouds
            Shader.SetGlobalFloat("_Fine", LodSettings.Tier > 0 ? 1f : 0f);

            // The scene's one light and its air follow the player. Sunlight reddens and dims through
            // the air near the horizon and is blocked by the ground once the sun has set. Shade is lit
            // by the whole sky, most of all from overhead, and a little by sunlight thrown back up off
            // the ground. Haze is the colour of the sky at the horizon and thins with height, cloud
            // and rain grey everything, and the sea is murky.
            var body = ReferenceBody;
            float3 up = math.normalizesafe(Focus), toSun = math.normalizesafe((float3)Bodies[0].transform.position + Anchor - Focus);
            float height = math.length(Focus) - body.radius, elevation = math.dot(up, toSun);
            float air = body.air > 0f ? math.exp(-math.max(height, 0f) / (body.radius * (Shapes.AirTop(body) - 1f) * 0.125f)) : 0f;
            float day = math.smoothstep(-0.1f, 0.25f, elevation), low = air * math.saturate(body.air) * (1f - math.smoothstep(0f, 0.4f, elevation));
            var stars = RenderSettings.skybox;
            if (stars != null)
            {
                stars.SetMatrix("_StarRotation", Matrix4x4.Rotate(body.Rotation(Clock)));
                float daylight = math.smoothstep(-0.17f, 0.04f, elevation);
                if (second > 0)
                    daylight = math.max(daylight, math.smoothstep(-0.17f, 0.04f, math.dot(up, math.normalizesafe(ToReference(second) - Focus))));
                stars.SetFloat("_Brightness", 1.8f * (ShowOrbits || Clear ? 1 : 1 - math.smoothstep(0.005f, 0.3f, air * body.air) * daylight));
            }
            float blocked = math.saturate(1f - height / (0.5f * body.radius)) * (1f - math.smoothstep(-0.15f, 0.02f, elevation));
            bool underCloud = height < body.radius * (Shapes.CloudDeck(body) - 1f), underwater = body.SeaRadius > 0f && height + Walker.EyeHeight < 0f && !body.Dry(Focus);
            Rain = underCloud ? Weather.Rain(body, up, Clock) : 0f;
            float overcast = math.max(Rain, underCloud && body.cloud >= 1f && body.kind == BodyKind.Rock ? 0.75f : 0f);
            Color sky = Shapes.Colour(body.sky), haze = Shapes.Haze(body), dusk = Shapes.Colour(body.dusk), grey = new Color(0.45f, 0.48f, 0.52f);

            sunlight.transform.rotation = Quaternion.LookRotation(-(Vector3)toSun);
            sunlight.color = Color.Lerp(sun, Color.Lerp(dusk, Color.white, 0.25f), low);
            // cloud takes the edge off the sun and scatters the rest, so shade under it is soft and never deep
            sunlight.intensity = (1 - body.blight * air * 0.4f) * 1.8f * (1f - 0.5f * low) * (1f - blocked) * (1f - 0.45f * overcast);
            Color skylight = Color.Lerp(new Color(0.05f, 0.056f, 0.075f), Color.Lerp(Color.Lerp(sky, haze, 0.5f), grey, overcast) * (0.62f + 0.2f * overcast), day * air * math.saturate(body.air)).linear;
            var shade = new SphericalHarmonicsL2();
            // by night what little light there is comes from all round
            shade.AddAmbientLight(skylight * (1f - 0.3f * day));
            shade.AddDirectionalLight((Vector3)up, skylight, 0.3f * day);
            shade.AddDirectionalLight(-(Vector3)up, sunlight.color.linear * new Color(0.5f, 0.42f, 0.28f), 0.1f * sunlight.intensity * math.saturate(elevation * 3f));
            RenderSettings.ambientProbe = shade;
            RenderSettings.fog = air > 0.02f && !Clear;
            RenderSettings.fogDensity = underwater ? 0.05f : 0.0011f * air * body.air * (1f + 7f * Rain + body.blight * 2);
            RenderSettings.fogColor = (underwater ? Shapes.SeaColour(0.6f) : Color.Lerp(Color.Lerp(dusk, haze, math.smoothstep(0f, 0.3f, elevation)), grey, 0.8f * overcast)) * (0.08f + 0.92f * day);
        }
    }
}
