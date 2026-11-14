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
        public BodyDescriptor ReferenceBody => Descriptor.bodies[Reference];

        // Turns inertial directions into scene directions.
        public Quaternion SceneRotation { get; private set; }

        // Where the player is, relative to the reference body. Set by the player every frame.
        [NonSerialized] public float3 Focus;

        // Raised with (from, to) when the reference body changes so movers can rebase themselves.
        public event Action<int, int> ReferenceChanged;

        // Draws every orbit as a ring, for the view of the whole system.
        [NonSerialized] public bool ShowOrbits;

        Light sunlight;
        LineRenderer[] orbits;

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
            Material line = Shapes.Material("Glow", new Color(0.5f, 0.8f, 1f));
            for (int i = 0; i < Bodies.Length; i++)
            {
                if (descriptor.bodies[i].orbit.parent < 0) continue;
                orbits[i] = Bodies[i].gameObject.AddComponent<LineRenderer>();
                orbits[i].sharedMaterial = line;
                orbits[i].loop = true;
                orbits[i].positionCount = 128;
                orbits[i].shadowCastingMode = ShadowCastingMode.Off;
            }

            sunlight = FindAnyObjectByType<Light>();
            sunlight.color = Color.Lerp(Shapes.SunColour(descriptor.bodies[0]), Color.white, 0.5f);
            sunlight.intensity = 1.6f;
            // space is dark: stars for a sky and a little flat fill light so the night side is not pure black
            var stars = Resources.Load<Shader>("Stars");
            RenderSettings.skybox = stars != null && stars.isSupported ? new Material(stars) : null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.07f, 0.08f, 0.12f);
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
            Vector3 Scene(double3 inertial) => math.rotate(toScene, (float3)(inertial - origin));

            for (int i = 0; i < Bodies.Length; i++)
            {
                Bodies[i].transform.SetPositionAndRotation(Scene(Descriptor.PositionAt(i, Clock)), math.mul(toScene, Descriptor.bodies[i].Rotation(Clock)));
                if (orbits[i] == null) continue;
                orbits[i].enabled = ShowOrbits;
                if (!ShowOrbits) continue;
                var orbit = Descriptor.bodies[i].orbit;
                double3 centre = Descriptor.PositionAt(orbit.parent, Clock);
                orbits[i].widthMultiplier = Vector3.Distance(Camera.main.transform.position, Scene(centre)) * 0.003f;
                for (int k = 0; k < orbits[i].positionCount; k++)
                    orbits[i].SetPosition(k, Scene(centre + orbit.Position(orbit.period * k / orbits[i].positionCount)));
            }
            // the stars keep their inertial directions while the scene turns with the ground
            if (RenderSettings.skybox != null) RenderSettings.skybox.SetMatrix("_StarRotation", Matrix4x4.Rotate(ReferenceBody.Rotation(Clock)));
            // the sun is body 0 and its light falls from it towards the player
            sunlight.transform.rotation = Quaternion.LookRotation((Vector3)Focus - Bodies[0].transform.position);
        }
    }
}
