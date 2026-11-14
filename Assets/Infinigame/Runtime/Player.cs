using System;
using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;

namespace Infinigame
{
    // The player on foot and their ship. The simulation lives in Core (Walker and Ship); this
    // turns controls into simulation input and puts the camera and the hull where the state says.
    public class Player : MonoBehaviour
    {
        const float BoardingRange = 8f, MaxTurnRate = 2.5f, RollRate = 1.8f;

        [NonSerialized] public WalkerState walker;
        [NonSerialized] public ShipState ship;
        [NonSerialized] public bool flying, assist = true, landing;
        public Func<Controls> controls = DesktopControls.Read;

        SolarSystem system;
        Camera view;
        Transform hull;
        GameObject canopy;
        float pitch;
        bool chase;

        BodyDescriptor Body => system.ReferenceBody;
        public float3 Position => flying ? ship.position : walker.position;
        public float3 Velocity => flying ? ship.velocity : walker.velocity;
        public bool SystemView { get; private set; }
        bool NearShip => ship.landed && math.distance(walker.position, ship.position) < BoardingRange;

        // What the interact key would do right now, for the HUD.
        public string Prompt => flying ? ship.landed ? "F  leave ship" : landing ? "landing" : "F  land"
            : NearShip ? "F  board ship" : null;

        public void Init(SolarSystem solarSystem)
        {
            system = solarSystem;
            system.ReferenceChanged += (from, to) =>
            {
                system.Descriptor.Rebase(from, to, system.Clock, ref walker.position, ref walker.velocity);
                system.Descriptor.Rebase(from, to, system.Clock, ref ship.position, ref ship.velocity);
                quaternion turn = system.Descriptor.Turn(from, to, system.Clock);
                walker.forward = math.rotate(turn, walker.forward);
                ship.rotation = math.mul(turn, ship.rotation);
            };
            view = Camera.main;
            view.clearFlags = CameraClearFlags.Skybox;
            view.backgroundColor = Color.black;
            view.nearClipPlane = 0.1f;
            view.farClipPlane = 200000f;
            hull = Shapes.ShipModel();
            canopy = hull.Find("Canopy").gameObject;
        }

        // Stand on a gentle patch of the reference body with the ship parked a few steps ahead.
        public void Spawn()
        {
            float3 direction = Body.FindLanding(system.Descriptor.seed + 1u);
            float3 facing = math.normalize(math.cross(direction, new float3(0.2f, 0.9f, 0.4f)));
            walker = Walker.Spawn(Body, direction, facing);
            ship = Ship.Spawn(Body, walker.position + facing * 14f, math.cross(direction, facing));
            flying = landing = false;
            pitch = 0f;
        }

        // Arrive in the ship high above the reference body, as after a warp.
        public void SpawnInOrbit()
        {
            Spawn();
            float3 direction = math.normalize(ship.position);
            ship.position = direction * (Body.radius * 1.8f);
            ship.rotation = quaternion.LookRotationSafe(-direction, math.rotate(ship.rotation, new float3(0, 0, 1)));
            ship.landed = false;
            flying = true;
        }

        void Update()
        {
            var c = controls();
            SystemView = c.systemView;
            // while looking at the whole system the player holds still
            if (SystemView) c = default;
            system.ShowOrbits = SystemView;
            if (c.toggleAssist) assist = !assist;
            if (c.toggleView) chase = !chase;
            if (c.interact) Interact();

            // simulate in steps of at most 1/60 s so a slow frame cannot tunnel or overshoot
            float frame = Mathf.Clamp(Time.deltaTime, 1e-4f, 0.1f);
            int steps = Mathf.Max(1, Mathf.CeilToInt(frame * 60f));
            float dt = frame / steps;
            if (flying)
            {
                var thrust = new float3(c.move.x, c.lift, c.move.y);
                if (math.lengthsq(thrust) > 0f) landing = false;
                var input = new ShipInput
                {
                    thrust = thrust, boost = c.sprint, land = landing,
                    turn = new float3(math.clamp(math.radians(c.look) / frame, -MaxTurnRate, MaxTurnRate).yx, c.roll * RollRate),
                };
                for (int i = 0; i < steps; i++) Ship.Step(ref ship, input, Body, assist, dt);
                if (ship.landed) landing = false;
            }
            else
            {
                pitch = Mathf.Clamp(pitch + c.look.y, -89f, 89f);
                for (int i = 0; i < steps; i++)
                {
                    Walker.Step(ref walker, new WalkerInput { move = c.move, yaw = i == 0 ? math.radians(c.look.x) : 0f, jump = c.jump, sprint = c.sprint }, Body, dt);
                    // an empty ship brings itself down
                    Ship.Step(ref ship, new ShipInput { land = true }, Body, true, dt);
                }
            }
            system.Focus = Position;

            hull.SetPositionAndRotation(ship.position, ship.rotation);
            canopy.SetActive(!flying || chase || SystemView);
            if (SystemView)
            {
                // high above the sun, looking down on every orbit at once, steady against the stars
                float reach = (float)system.Descriptor.bodies[1].orbit.radius * 1.9f;
                view.transform.SetPositionAndRotation(system.Bodies[0].transform.position + system.SceneRotation * new Vector3(0f, reach, -reach * 0.35f),
                    system.SceneRotation * Quaternion.LookRotation(new Vector3(0f, -1f, 0.35f)));
            }
            else if (flying)
            {
                Quaternion rotation = ship.rotation;
                view.transform.SetPositionAndRotation((Vector3)ship.position + rotation * (chase ? new Vector3(0f, 4f, -15f) : new Vector3(0f, 0.95f, 1.2f)), rotation);
            }
            else
            {
                float3 up = math.normalize(walker.position);
                view.transform.SetPositionAndRotation(walker.position + up * Walker.EyeHeight,
                    Quaternion.LookRotation(walker.forward, up) * Quaternion.Euler(-pitch, 0f, 0f));
            }
            // speed stretches the view a little
            view.fieldOfView = Mathf.Lerp(view.fieldOfView, 70f + 25f * math.saturate(math.length(Velocity) / Tuning.ShipSpaceSpeed) + (flying || !c.sprint ? 0f : 4f), 5f * frame);
        }

        void Interact()
        {
            if (!flying && NearShip) flying = true;
            else if (flying && ship.landed)
            {
                float3 forward = math.rotate(ship.rotation, new float3(0, 0, 1));
                walker = Walker.Spawn(Body, ship.position + math.rotate(ship.rotation, new float3(3.5f, 0, 0)), forward);
                flying = false;
                pitch = 0f;
            }
            else if (flying) landing = !landing;
        }
    }
}
