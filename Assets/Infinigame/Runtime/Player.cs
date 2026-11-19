using System;
using System.Collections.Generic;
using System.Linq;
using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.XR;

namespace Infinigame
{
    // The player on foot and their ship. The simulation lives in Core (Walker, Ship and Autopilot);
    // this turns controls into simulation input, takes the knocks, and puts the camera and the
    // hull where the state says.
    public class Player : MonoBehaviour
    {
        const float BoardingRange = 8f, MaxTurnRate = 2.5f, RollRate = 1.8f;
        // What a suit or a hull can take before anything has been won to strengthen it, and how
        // high a ship can climb before it has the drive to leave the air.
        public const float Full = 100f, Ceiling = 250f;

        [NonSerialized] public WalkerState walker;
        [NonSerialized] public ShipState ship;
        [NonSerialized] public bool flying, assist = true, landing, riding;
        // The ship is in the raiders' hands: there to be seen where they keep it, and not to be boarded.
        [NonSerialized] public bool captive;
        // Where the mission leads on this world, if it does, for the map of it to take in.
        [NonSerialized] public float3 beacon;
        // What is left of the player's suit and of the ship's hull. Either at nothing is the end of them.
        [NonSerialized] public float health = Full, hull = Full;
        // Everything won so far, as Campaign's bits: it decides what the player can carry, ride and fly.
        [NonSerialized] public int kit;
        public int fitted;
        public float energy = Suit.Capacity;
        public bool cloaked;
        public Game game;
        public Func<Controls> controls = DesktopControls.Read;

        SolarSystem system;
        Camera view;
        Transform model, traveller, pack, landingGear;
        float gearExtension;
        Renderer[] bodyRenderers;
        public float3 BackpackPort => (float3)pack.TransformPoint(new Vector3(0.18f, 0.22f, -0.27f)) + system.Anchor;
        public readonly Transform[] fists = new Transform[2];
        public void Calibrate() => tracked = false;
        public void Stain(Material material, int index)
        {
            Shapes.Part(traveller.Find(index % 2 == 0 ? "Left leg" : "Right leg"), "Violet blood", Shapes.Rock(2), material,
                new Vector3((index % 3 - 1) * 0.06f, -0.08f * (index / 3), 0.16f), new Vector3(0.09f, 0.12f, 0.018f), false);
        }
        MachineMotion motion;
        GameObject[] armour;
        GameObject[] outside;
        Cockpit cockpit;
        public Cockpit Cockpit => cockpit;
        // how far the last shots have thrown the aim up, in degrees
        float pitch, punch;
        bool chase, submerged, recall, detected;
        float standing = 1;
        Transform exhaust;
        public bool InSpace => math.length(Position) > Body.radius * Shapes.AirTop(Body);
        // which of its weapons the ship has live, and which the player has in hand
        int gun, arm;
        readonly List<float4> worlds = new List<float4>();
        readonly List<Rock> rocks = new List<Rock>();

        public Vector3 TrackingPosition { get; private set; }
        public Quaternion TrackingRotation { get; private set; }
        Vector3 trackingOrigin;
        bool tracked;
        public bool Hand(XRNode node, out Vector3 position, out Quaternion rotation)
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            bool valid = device.TryGetFeatureValue(CommonUsages.devicePosition, out position);
            valid &= device.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation);
            position = TrackingPosition + TrackingRotation * (position - trackingOrigin);
            rotation = TrackingRotation * rotation;
            return DeviceControls.VR && valid;
        }

        public bool Aim(XRNode node, out Vector3 position, out Quaternion rotation)
        {
            bool tracked = Hand(node, out position, out rotation);
            var controller = UnityEngine.InputSystem.InputSystem.GetDevice<UnityEngine.InputSystem.XR.XRController>(node == XRNode.LeftHand ? UnityEngine.InputSystem.CommonUsages.LeftHand : UnityEngine.InputSystem.CommonUsages.RightHand);
            var aim = controller?.TryGetChildControl<UnityEngine.InputSystem.Controls.QuaternionControl>("pointerRotation");
            var point = controller?.TryGetChildControl<UnityEngine.InputSystem.Controls.Vector3Control>("pointerPosition");
            if (aim != null) rotation = TrackingRotation * aim.ReadValue();
            if (point != null) position = TrackingPosition + TrackingRotation * (point.ReadValue() - trackingOrigin);
            return tracked;
        }

        BodyDescriptor Body => system.ReferenceBody;
        public float3 Position => flying ? ship.position : walker.position;
        public float3 Velocity => flying ? ship.velocity : walker.velocity;
        public float Altitude => math.length(Position) - math.max(Body.GroundRadius(math.normalize(Position)), Body.SeaRadius);
        // Looking at the map: of the whole system once there is a ship that can cross it, and until
        // then of the world underfoot.
        public bool SystemView { get; private set; }
        public bool PlanetView => SystemView && Body.kind == BodyKind.Rock && (!Has(Gear.Ship) || game.MapZoom < 0.5f && game.MapSelection == system.Reference);
        // Where the copilot sits, in a ship that has such a seat.
        public Transform Seat => model.Find("Cockpit/Copilot");
        public bool ShipOverhead => game.Stage <= Campaign.Recovery && system.Reference == SolarSystemDescriptor.Home && !Has(Gear.Ship);
        public bool NearShip => (Has(Gear.Ship) || captive) && math.distance(walker.position, ship.position) < BoardingRange;

        public bool Has(Gear gear) => Campaign.Has(Suit.Modules.Contains(gear) ? fitted : kit, gear);
        public float MaxHealth => (Full + (Has(Gear.Armour) ? 50f : 0f)) * game.Progression.Toughness;
        public float MaxHull => (Full + (Has(Gear.Plating) ? 50f : 0f) + (Has(Gear.Bulkheads) ? 100f : 0f)) * game.Progression.Toughness;

        // The world the autopilot is flying to, or -1.
        public int Destination { get; private set; } = -1;
        public bool Autopilot => Destination >= 0;

        // The world whose surface the ship's instruments measure to, and how far off it is: where
        // the autopilot is headed, or else whichever is nearest.
        public int Ahead { get; private set; }
        public float Range { get; private set; }

        // The weapon that would fire now, once it has been won: one of the ship's in flight, one of
        // those carried on foot. Arm is which of the latter.
        public bool Powered => Campaign.Has(kit, Gear.Beams);
        public bool CanRecall => Has(Gear.Ship) || captive && InSpace;
        public Weapon Weapon
        {
            get
            {
                var weapon = flying ? Combat.Guns[gun] : Combat.Arms[Powered ? arm : 0];
                if (!flying && !Powered) { weapon.name = "Emergency fists"; weapon.damage = 18; weapon.period = 0.55f; weapon.blast = 0; }
                if (Powered)
                {
                    weapon.damage *= game.Progression.Damage;
                    weapon.period /= weapon.homing > 0 ? game.Progression.Recovery : game.Progression.Rate;
                    weapon.charge /= game.Progression.Rate;
                }
                return weapon;
            }
        }
        public int Arm => arm;
        public bool Armed => Has(Weapon.needs);
        // Out past the moon without a shield, the sun's radiation is eating at the hull.
        public bool Irradiated { get; private set; }
        // When the ship last tried to climb higher than its engines can take it.
        public float Stalled { get; private set; } = float.MinValue;

        // How hot it is outside, in kelvin.
        public float Heat { get; private set; }

        // What last hurt the player, as the epitaph would put it, and when.
        public string Cause { get; private set; }
        public float HurtAt { get; private set; } = float.MinValue;
        // The heat the suit, or in flight the hull, can stand.
        public float HeatLimit => flying ? Climate.HullLimit + (Has(Gear.Plating) ? 300f : 0f) : Has(Gear.Suit) ? Suit.HeatLimit : Climate.SuitLimit;
        public bool Dead => health <= 0f || hull <= 0f;
        // In the pilot's seat, looking out through the canopy.
        public bool Seated => flying && !chase && !SystemView && !game.Fall.active;
        public bool ThirdPerson => !flying && chase && !DeviceControls.VR;

        // What the interact key would do right now, for the HUD. Landing is offered once there is ground
        // near enough to land on, which a star or a giant's cloud tops never are.
        public string Prompt => flying ? ship.landed ? "Leave ship" : landing || Autopilot || Body.kind != BodyKind.Rock || Altitude > Body.radius ? null : "Land"
            : NearShip ? "Board ship" : null;

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
            view.farClipPlane = 2500000f;
            model = Models.Ship();
            landingGear = model.Find("Exterior/Landing gear");
            traveller = Models.Traveller();
            motion = traveller.gameObject.AddComponent<MachineMotion>();
            fists[0] = traveller.Find("Torso/Left arm/Elbow/Gauntlet");
            fists[1] = traveller.Find("Torso/Right arm/Elbow/Gauntlet");
            bodyRenderers = traveller.GetComponentsInChildren<Renderer>();
            armour = traveller.GetComponentsInChildren<Transform>().Where(part => part.name == "Armour").Select(part => part.gameObject).ToArray();
            pack = traveller.Find("Torso/Thruster pack");
            // from the pilot's seat the cockpit stands in for the hull round it
            outside = new[] { model.Find("Exterior").gameObject };
            cockpit = model.Find("Cockpit").GetComponent<Cockpit>();
        }

        // Stand on a gentle patch of the reference body with the ship parked a few steps ahead.
        public void Spawn()
        {
            recall = detected = false;
            float3 direction = system.Reference == SolarSystemDescriptor.Home ? Campaign.Site(system.Descriptor, game.Stage >= 2 && game.Stage <= 4 ? game.Stage - 1 : 0) : Body.FindLanding(system.Descriptor.seed + 1u);
            float3 facing = math.normalize(math.cross(direction, new float3(0.2f, 0.9f, 0.4f)));
            walker = Walker.Spawn(Body, direction, facing);
            ship = Ship.Spawn(Body, walker.position + facing * 14f, math.cross(direction, facing));
            flying = landing = riding = false;
            Destination = -1;
            pitch = punch = 0f;
        }

        // Stands the ship on the ground at a spot on the reference body, as when it is won back.
        public void Park(float3 at) => ship = Ship.Spawn(Body, at, math.normalize(math.cross(at, new float3(0.2f, 0.9f, 0.4f))));

        // Takes up a weapon that has just been won.
        public void Wield(Gear gear)
        {
            if (Array.FindIndex(Combat.Arms, weapon => weapon.needs == gear) is int carried && carried >= 0) arm = carried;
            if (Array.FindIndex(Combat.Guns, weapon => weapon.needs == gear) is int mounted && mounted >= 0) gun = mounted;
        }

        // A shot throws the aim up, and it settles back by itself.
        public void Kick(float degrees) => punch += degrees;

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

        // Takes this much off the hull in flight, or off the player on foot, and remembers why.
        public void Hurt(float amount, string cause)
        {
            if (game.GodMode || game.Fall.active || amount <= 0f || Dead) return;
            if (flying) hull -= amount;
            else
            {
                float absorbed = Has(Gear.Armour) ? math.min(energy, amount * 0.65f) : 0;
                energy -= absorbed;
                health -= amount - absorbed;
            }
            Cause = cause;
            HurtAt = Time.time;
        }

        // Sets the autopilot for the world nearest the middle of the view, or switches it off.
        // Returns what happened, for the HUD.
        public string Travel()
        {
            if (Autopilot)
            {
                Destination = -1;
                return "AUTOPILOT OFF";
            }
            if (!flying)
            {
                if (!CanRecall) return captive ? "CLIMB ABOVE THE ATMOSPHERE TO ESTABLISH THE SHIP LINK" : "NO SHIP LINK";
                recall = true;
                game.Hermit.Remark(InSpace ? "Your suit transponder is detected. The ship is matching your velocity. Hold position, then board when it reaches you." : "Ship recall accepted. Clearing the terrain and landing beside you.");
                return $"SHIP INBOUND · HOLD POSITION · {DeviceControls.Use} TO BOARD";
            }
            if (!Has(Gear.Orbiter)) return "THE AUTOPILOT NEEDS AN ORBITAL DRIVE";
            float nearest = math.cos(math.radians(14f));
            for (int i = 0; i < system.Bodies.Length; i++)
            {
                float3 to = system.Bodies[i].transform.position - view.transform.position;
                float facing = math.dot(math.normalize(to), view.transform.forward);
                // only what has a marker to face, not the one you are already beside, and nowhere the radiation would kill
                if (!system.Charted(i) || system.Descriptor.bodies[i].kind == BodyKind.Station && game.Stage < Campaign.Citadel || facing < nearest || math.length(to) < Core.Autopilot.Standoff(system.Descriptor.bodies[i]) * 1.1f
                    || (!Has(Gear.Shield) && system.Descriptor.Planet(i) != SolarSystemDescriptor.Home)) continue;
                nearest = facing;
                Destination = i;
            }
            if (!Autopilot) return "FACE A WORLD'S MARKER TO FLY THERE";
            landing = false;
            return $"AUTOPILOT  ·  {system.Descriptor.bodies[Destination].name.ToUpperInvariant()}";
        }

        public string TravelTo(int world)
        {
            if (!flying) return Travel();
            if (!Has(Gear.Orbiter)) return "ORBITAL DRIVE REQUIRED";
            if (world < 0 || world >= system.Bodies.Length || !system.Charted(world)
                || system.Descriptor.bodies[world].kind == BodyKind.Station && game.Stage < Campaign.Citadel) return "DESTINATION NOT CHARTED";
            if (!Has(Gear.Shield) && system.Descriptor.Planet(world) != SolarSystemDescriptor.Home) return "RADIATION SHIELD REQUIRED";
            Destination = world; landing = false;
            return $"AUTOPILOT · {system.Descriptor.bodies[world].name.ToUpperInvariant()} · CLOSE MAP TO DEPART";
        }

        // One frame, driven by the game so that menus and conversations can hold the player still.
        public void Tick(Controls c)
        {
            float3 shipBefore = ship.position;
            SystemView = c.systemView;
            // while looking at the whole system the player holds still
            if (SystemView || Time.timeScale == 0f) c = default;
            system.ShowOrbits = SystemView && !PlanetView && !game.MapSummary;
            system.Clear = PlanetView;
            // a map of the ground is no use through cloud
            if (system.Bodies[system.Reference].transform.Find("Clouds") is Transform clouds) clouds.gameObject.SetActive(!PlanetView);
            if (c.toggleAssist) assist = !assist;
            // what is in hand is something that has been won, if anything has: a cheat or a new game can take it away
            if (!Has(Combat.Arms[arm].needs)) arm = math.max(0, Array.FindLastIndex(Combat.Arms, weapon => Has(weapon.needs)));
            if (!Has(Combat.Guns[gun].needs)) gun = 0;
            if (c.select > 0 || c.cycle != 0)
            {
                var rack = flying ? Combat.Guns : Combat.Arms;
                ref int held = ref (flying ? ref gun : ref arm);
                int next = c.select > 0 ? c.select - 1 : held;
                // the wheel passes over whatever has not been won yet
                for (int i = 0; i < rack.Length && c.select == 0; i++)
                {
                    next = (next + c.cycle + rack.Length) % rack.Length;
                    if (Has(rack[next].needs)) break;
                }
                if (next < rack.Length && Has(rack[next].needs)) held = next;
            }
            if (c.toggleView) chase = !chase;
            riding = false;
            if (c.reload && Has(Gear.Stealth)) cloaked = !cloaked;
            cloaked &= Has(Gear.Stealth) && !flying && !c.fire;
            if (c.interact) Interact();

            // simulate in steps of at most 1/60 s so a slow frame cannot tunnel or overshoot
            float frame = Time.timeScale == 0 ? 0 : Mathf.Clamp(Time.deltaTime, 1e-4f, 0.1f);
            int steps = frame > 0 ? Mathf.Max(1, Mathf.CeilToInt(frame * 60f)) : 0;
            float dt = frame / math.max(1, steps);
            if (DeviceControls.Touch && c.look.sqrMagnitude > 0.001f && c.look.magnitude < 4 && !SystemView && frame > 0)
            {
                float3 eye = (float3)view.transform.position + system.Anchor;
                int target = Combat.AssistedTarget(game.Battle.Foes, eye, view.transform.forward, flying ? 1800 : 220, flying ? 18 : 14);
                if (target >= 0)
                {
                    float3 at = game.Battle.Foes[target].position;
                    bool visible = game.Outpost.Visible(eye, at) && !game.Outpost.Shield.Blocks(eye, at - eye, out _);
                    for (int sample = 1; visible && sample < 12; sample++)
                    {
                        float3 point = math.lerp(eye, at, sample / 12f);
                        visible = math.length(point) >= Combat.Surface(Body, point);
                    }
                    if (visible)
                    {
                        Vector3 local = view.transform.InverseTransformDirection(at - eye).normalized;
                        c.look += new Vector2(Mathf.Atan2(local.x, local.z), Mathf.Asin(local.y)) * (Mathf.Rad2Deg * (1 - math.exp(-12 * frame)));
                    }
                }
            }
            energy = math.min(Suit.Capacity + (Has(Gear.Capacitor) ? 50 : 0), energy + (Powered ? Suit.Recharge * game.Progression.Energy : 0) * frame);
            if (cloaked && !Suit.Spend(ref energy, 18 * frame)) cloaked = false;
            float3 before = Position;
            var bodies = system.Descriptor.bodies;
            if (Autopilot && bodies[Destination].destroyed) Destination = -1;
            if (flying)
            {
                var thrust = new float3(c.move.x, c.lift, c.move.y);
                // touching the controls takes the ship back
                if (math.lengthsq(thrust) > 0f) (landing, Destination) = (false, -1);
                if (Autopilot)
                {
                    // where the target is, how it is moving and what else is about, all as seen from here
                    float3 centre = system.ToReference(Destination), drift = (system.ToReference(Destination, 0.5) - centre) / 0.5f;
                    worlds.Clear();
                    for (int i = 0; i < bodies.Length; i++)
                        if (i != Destination && !bodies[i].destroyed)
                            worlds.Add(new float4(system.ToReference(i), Core.Autopilot.Berth(bodies[i])));
                    for (int i = 0; i < steps && Autopilot; i++)
                    {
                        if (Core.Autopilot.Step(ref ship, centre, drift, Core.Autopilot.Standoff(bodies[Destination]), worlds, dt)) Destination = -1;
                        // whatever its course, it never takes the ship into the ground
                        float3 over = math.normalize(ship.position);
                        ship.position = over * math.max(math.length(ship.position), Ship.Floor(Body, over) + Ship.Clearance + 2f);
                    }
                }
                else
                {
                    var input = new ShipInput
                    {
                        thrust = thrust, boost = c.sprint, land = landing,
                        turn = new float3(math.clamp(math.radians(c.look) / math.max(frame, 1e-4f), -MaxTurnRate, MaxTurnRate).yx, c.roll * RollRate),
                    };
                    for (int i = 0; i < steps; i++) Ship.Step(ref ship, input, Body, assist, dt);
                    // until it has the drive to leave the air, the engines give out where the air thins
                    float3 above = math.normalize(ship.position);
                    float over = math.length(ship.position) - math.max(Body.GroundRadius(above), Body.SeaRadius) - Ceiling;
                    if (over > 0f && !Has(Gear.Orbiter))
                    {
                        ship.position -= above * over;
                        ship.velocity -= above * math.max(0f, math.dot(ship.velocity, above));
                        Stalled = Time.time;
                    }
                }
                if (ship.landed) landing = false;
                float crash = Combat.Crash(ship.impact);
                Hurt(crash, Body.kind == BodyKind.Sun ? $"Flew into {Body.name}" : Body.kind == BodyKind.Station ? "Flew into the Presence citadel" : $"Crashed on {Body.name}");
                // a hard enough landing leaves its mark on the ground
                if (crash > 0f) system.Bodies[system.Reference].Dig(ship.position, math.clamp(0.08f * ship.impact, 2f, 12f));
                // the autopilot threads the rocks of a belt or a ring itself
                if (frame > 0 && !Autopilot && Rings.Hit(Body, ship.position, Combat.ShipRadius, system.Clock, rocks, out float3 normal, out float3 moving))
                {
                    // a rock in the rings: bounce off it, and pay for how hard
                    float into = math.dot(ship.velocity - moving, normal);
                    if (into < 0f)
                    {
                        ship.velocity -= normal * (into * 1.5f);
                        Hurt(Combat.Graze(-into, hull), Body.kind == BodyKind.Sun ? "Struck an asteroid in the belt" : $"Struck a rock in the rings of {Body.name}");
                    }
                    ship.position += normal * 0.5f;
                }
            }
            else
            {
                pitch = Mathf.Clamp(pitch + c.look.y, -89f, 89f);
                punch = Mathf.MoveTowards(punch, 0f, (6f + 8f * punch) * frame);
                for (int i = 0; i < steps; i++)
                {
                    walker.fuel = energy > 0 ? Walker.JetpackFuel : 0;
                    Walker.Step(ref walker, new WalkerInput
                    {
                        move = c.move, yaw = i == 0 ? math.radians(c.look.x) : 0f, jump = c.jump, sprint = c.sprint, space = InSpace, lift = c.lift, bare = !Has(Gear.Thrusters) || energy < 15 * dt, riding = false, pitch = math.radians(pitch),
                    }, Body, dt);
                    if (Has(Gear.Thrusters) && !walker.grounded && (c.jump || math.lengthsq(c.move) > 0)) Suit.Spend(ref energy, 15 * dt);
                    float3 up = math.normalizesafe(walker.position);
                    float ceiling = Body.GroundRadius(up) + (Has(Gear.Orbiter) ? Suit.Ceiling : Suit.SurfaceCeiling);
                    if (math.length(walker.position) > ceiling)
                    {
                        walker.position = up * ceiling;
                        walker.velocity -= up * math.max(0, math.dot(walker.velocity, up));
                    }
                    if (InSpace && captive && !detected) { detected = true; Travel(); }
                    if (recall)
                    {
                        float3 destination = walker.position + walker.forward * (InSpace ? 4 : 13) + up * (InSpace ? 0 : 4);
                        float3 delta = destination - ship.position;
                        float distance = math.length(delta);
                        float3 desired = math.normalizesafe(delta) * math.min(100, distance * 0.7f) + (InSpace ? walker.velocity : float3.zero);
                        float3 shipUp = math.normalizesafe(ship.position);
                        float floor = Body.GroundRadius(shipUp) + 8;
                        if (!InSpace && distance > 24 && math.length(ship.position) < floor + 25) desired += shipUp * 30;
                        ship.velocity = Walker.MoveTowards(ship.velocity, desired, 35 * dt);
                        ship.position += ship.velocity * dt;
                        shipUp = math.normalizesafe(ship.position);
                        ship.position = shipUp * math.max(math.length(ship.position), Body.GroundRadius(shipUp) + 4);
                        ship.rotation = math.slerp(ship.rotation, quaternion.LookRotationSafe(walker.forward, up), math.saturate(dt * 2));
                        ship.landed = !InSpace && distance < 5;
                    }
                    else if (!ShipOverhead) Ship.Step(ref ship, new ShipInput { land = true }, Body, true, dt);
                }
                // the bike's wheels take half of a landing
                Hurt(Combat.Fall(walker.impact * (riding ? 0.5f : 1f)), $"Fell to the ground on {Body.name}");
                if (walker.swimming && !Has(Gear.Suit))
                {
                    if (!submerged) game.Notify("I DON'T KNOW HOW TO SWIM  ·  GET BACK TO SHORE");
                    Hurt(18 * frame, "You don't know how to swim");
                }
                submerged = walker.swimming;
                // a suit mends itself once left alone a while
                if (Time.time - HurtAt > 6f) health = math.min(MaxHealth, health + 4f * frame * game.Progression.Recovery);
            }
            if (frame > 0 && game != null && game.Outpost != null)
            {
                float3 up = math.normalizesafe(Position);
                float3 offset = flying ? float3.zero : up * Combat.PersonRadius;
                float3 resolved = game.Outpost.Resolve(before + offset, Position + offset, flying ? Combat.ShipRadius : Combat.PersonRadius, out bool hit) - offset;
                if (hit)
                {
                    if (flying) { ship.position = resolved; Hurt(Combat.Graze(math.length(ship.velocity), hull), "Struck a tunnel wall"); ship.velocity *= 0.3f; }
                    else { walker.position = resolved; walker.velocity = (resolved - before) / math.max(frame, 1e-4f); walker.grounded = true; }
                }
            }
            if (frame > 0 && !flying && game.Outpost.LandingLocked && game.Outpost.Shield.Bounce(before, ref walker.position, ref walker.velocity, 0))
                game.Notify("CLEAR THE LANDING ZONE BEFORE ENTERING ON FOOT");
            if (frame > 0 && game.Outpost.Shield.Bounce(shipBefore, ref ship.position, ref ship.velocity, Combat.ShipRadius))
            {
                ship.landed = landing = false; Destination = -1;
                if (flying) Hurt(20, "Struck a shield"); else if (!game.GodMode) hull = math.max(1, hull - 20);
                game.Outpost.Flash(); game.Notify("SHIP REPELLED · LAND OUTSIDE THE FIELD");
            }
            walker.impact = ship.impact = 0f;
            // heat: a star cooks a ship that comes close, a giant one that sinks into it, and a furnace world anyone who steps outside
            Heat = Climate.At(system.Descriptor, system.Reference, Position + math.normalizesafe(Position) * (flying ? 0f : Walker.EyeHeight), system.Clock);
            Hurt(Combat.Scorch(Heat, HeatLimit) * frame, Body.kind == BodyKind.Sun ? $"Burned up too close to {Body.name}"
                : Body.kind == BodyKind.Gas ? $"Cooked in the depths of {Body.name}" : $"Roasted on {Body.name}");
            // out past the moon the sun's radiation gets into a ship with no shield against it
            Irradiated = (flying || !Has(Gear.Suit)) && !Has(Gear.Shield) && system.Descriptor.Planet(system.Reference) != SolarSystemDescriptor.Home;
            if (game.Stage < Campaign.Citadel || !Has(Gear.Nukes))
            {
                float3 citadel = system.ToReference(system.Station);
                if (math.distance(Position, citadel) < system.Descriptor.bodies[system.Station].radius + 900)
                    Hurt(180 * frame, "Destroyed by the citadel exclusion field: final mission equipment required");
            }
            if (Irradiated) Hurt(Combat.Radiation * frame, "Cooked by radiation out beyond Luna");
            // a ship on the ground is patched up, and one with a mender aboard mends as it flies once it is left alone
            if (!Dead && (ship.landed || (Has(Gear.Mender) && Time.time - HurtAt > 6f))) hull = math.min(MaxHull, hull + (ship.landed ? 3f : 2f) * frame * game.Progression.Recovery);
            system.Focus = Position;

            // the instruments measure to a surface: the destination's, or the nearest there is
            Range = float.MaxValue;
            for (int i = 0; i < bodies.Length; i++)
            {
                if (bodies[i].destroyed) continue;
                float gap = i == system.Reference ? Altitude : math.distance(Position, system.ToReference(i)) - bodies[i].radius;
                if (Autopilot ? i != Destination : gap >= Range) continue;
                Range = math.max(gap, 0f);
                Ahead = i;
            }

            // everything is drawn relative to the scene's origin, which out in space rides with the player
            model.SetPositionAndRotation(ship.position - system.Anchor, ship.rotation);
            bool wheelsDown = ship.landed || flying && Body.kind == BodyKind.Rock && Altitude < 15
                && (landing || math.dot(ship.velocity, math.normalizesafe(ship.position)) < -0.5f);
            gearExtension = Mathf.MoveTowards(gearExtension, wheelsDown ? 1 : 0, frame * 1.6f);
            landingGear.gameObject.SetActive(gearExtension > 0);
            landingGear.localPosition = Vector3.up * (0.65f * (1 - gearExtension));
            landingGear.localScale = new Vector3(1, Mathf.SmoothStep(0.08f, 1, gearExtension), 1);
            traveller.gameObject.SetActive(!flying && !game.Buried && !SystemView && !game.Title && !Dead);
            if (traveller.gameObject.activeSelf)
            {
                traveller.SetPositionAndRotation(walker.position - system.Anchor, quaternion.LookRotationSafe(walker.forward, math.normalizesafe(walker.position)));
                foreach (string part in new[] { "Torso/Head", "Torso/Left arm", "Torso/Right arm" })
                    if (traveller.Find(part) is Transform limb) limb.gameObject.SetActive(ThirdPerson || part != "Torso/Head" && !DeviceControls.VR);
                foreach (var plate in armour) plate.SetActive(Has(Gear.Suit));
                pack.gameObject.SetActive(Has(Gear.Thrusters));
                if (exhaust == null)
                    exhaust = Shapes.Part(pack, "Jet exhaust", Shapes.Sphere(1, 16), Shapes.Material("Glow", new Color(0.15f, 2, 5)), new Vector3(0, -0.6f, 0), new Vector3(0.2f, 0.8f, 0.2f), false).transform;
                exhaust.gameObject.SetActive(Powered && !walker.grounded && (c.jump || c.move.sqrMagnitude > 0));
                if (exhaust.gameObject.activeSelf) exhaust.localScale = new Vector3(0.17f, 0.65f + 0.1f * math.sin(Time.time * 36), 0.17f);
                motion.strike = Weapon.melee ? math.saturate(1 - (Time.time - game.Battle.Fired) / 0.35f) : 0;
                motion.recoil = math.saturate(1 - (Time.time - HurtAt) / 0.4f);
                motion.weapon = Powered ? Arm : 0;
                motion.aimPitch = pitch + punch;
                motion.speed = walker.grounded ? math.length(Velocity) : 0;
            }
            // until it is won back the ship is not there to be seen
            foreach (var part in outside) part.SetActive(!Seated && (ShipOverhead || Has(Gear.Ship)));
            cockpit.gameObject.SetActive(Seated);
            if (Seated)
            {
                // every instrument in front of the pilot shows what the ship is really doing
                float3 up = math.normalize(ship.position), nose = math.rotate(ship.rotation, new float3(0, 0, 1));
                float3 wing = math.rotate(ship.rotation, new float3(1, 0, 0)), roof = math.rotate(ship.rotation, new float3(0, 1, 0));
                float2 look = math.clamp(math.radians(c.look) / math.max(frame, 1e-4f), -MaxTurnRate, MaxTurnRate) / MaxTurnRate;
                int fitted = 0;
                for (int i = 0; i < Combat.Guns.Length; i++)
                    if (Has(Combat.Guns[i].needs))
                        fitted |= 1 << i;
                cockpit.Show(new Readings
                {
                    speed = math.length(ship.velocity), climb = math.dot(ship.velocity, up), range = Range, ahead = bodies[Ahead].name, altitude = Altitude,
                    heat = Heat, heatLimit = HeatLimit, hull = hull / MaxHull,
                    thrust = new Vector3(c.move.x, c.lift, c.move.y), turn = new Vector3(look.y, look.x, c.roll),
                    pitch = math.asin(math.clamp(math.dot(nose, up), -1f, 1f)), bank = math.atan2(-math.dot(wing, up), math.dot(roof, up)),
                    boost = c.sprint, assist = assist, landing = landing, landed = ship.landed, autopilot = Autopilot, irradiated = Irradiated,
                    firing = c.fire && Armed && !Autopilot, warp = Has(Gear.GateParts), weapon = gun, weapons = fitted,
                });
            }
            if (PlanetView)
            {
                // over the middle of the way to where the mission leads, as high as it takes to have both ends of it in
                // sight with room to spare: a few hundred metres for a walk, right back for the far side of the world
                float3 over = math.normalize(Position), axis = math.normalizesafe(over + math.normalizesafe(beacon), over);
                float apart = Body.radius * math.acos(math.clamp(math.dot(over, math.normalizesafe(beacon, over)), -1f, 1f));
                view.transform.SetPositionAndRotation(system.Bodies[system.Reference].transform.position + (Vector3)(axis * (Body.radius + math.min(2.2f * apart + 260f, 4f * Body.radius) * game.MapZoom)),
                    Quaternion.LookRotation(-axis, flying ? math.rotate(ship.rotation, new float3(0, 0, 1)) : walker.forward));
            }
            else if (SystemView)
            {
                // high above the sun, looking down on every orbit at once, steady against the stars
                float reach = (float)bodies.Max(body => body.orbit.parent == 0 ? body.orbit.radius : 0) * 1.9f * game.MapZoom;
                // set to one side, so the system sits clear of the table of its worlds
                view.transform.SetPositionAndRotation((game.MapZoom < 0.6f ? system.Bodies[game.MapSelection].transform.position : system.Bodies[0].transform.position) + system.SceneRotation * new Vector3(0, reach, -reach * 0.35f),
                    system.SceneRotation * Quaternion.LookRotation(new Vector3(0f, -1f, 0.35f)));
            }
            else if (flying)
            {
                Quaternion rotation = ship.rotation;
                view.transform.SetPositionAndRotation(model.position + rotation * (chase ? new Vector3(0f, 4f, -15f) : new Vector3(0f, 0.95f, 1.2f)), rotation);
            }
            else
            {
                float3 up = math.normalize(walker.position);
                Quaternion looking = Quaternion.LookRotation(walker.forward, up) * Quaternion.Euler(-pitch - punch, 0f, 0f);
                standing = game.Buried ? 0 : math.min(1, standing + frame * 0.7f);
                if (!DeviceControls.VR) looking *= Quaternion.Euler(-65 * (1 - standing), 0, 12 * (1 - standing));
                float3 eye = walker.position + up * (DeviceControls.VR ? Walker.EyeHeight : math.lerp(0.45f, Walker.EyeHeight, standing)), camera = eye;
                if (ThirdPerson)
                {
                    float3 desired = eye + (float3)(looking * new Vector3(0.55f, 0.55f, -4.8f));
                    for (int i = 1; i <= 32; i++)
                    {
                        float3 sample = math.lerp(eye, desired, i / 32f);
                        if (math.length(sample) < Body.GroundRadius(math.normalizesafe(sample)) + 0.2f) break;
                        camera = sample;
                    }
                    camera = game.Outpost.Resolve(eye, camera, 0.15f, out _);
                }
                view.transform.SetPositionAndRotation(camera - system.Anchor, looking);
            }
            TrackingPosition = view.transform.position;
            TrackingRotation = view.transform.rotation;
            if (DeviceControls.VR && !SystemView)
            {
                var head = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
                if (head.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position))
                {
                    if (!tracked) { trackingOrigin = position; tracked = true; }
                    view.transform.position += view.transform.rotation * (position - trackingOrigin);
                }
                if (head.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion orientation)) view.transform.rotation *= orientation;
            }
            if (!flying && !ThirdPerson && !SystemView)
            {
                if (DeviceControls.VR)
                {
                    Vector3 up = math.normalizesafe(walker.position);
                    Vector3 forward = Vector3.ProjectOnPlane(view.transform.forward, up).normalized;
                    if (forward.sqrMagnitude < 0.1f) forward = walker.forward;
                    traveller.SetPositionAndRotation(view.transform.position - up * Walker.EyeHeight - forward * 0.08f, Quaternion.LookRotation(forward, up));
                }
                var torso = traveller.Find("Torso");
                var leftArm = fists[0].parent.parent;
                var rightArm = fists[1].parent.parent;
                foreach (var part in bodyRenderers)
                {
                    bool upper = part.transform.IsChildOf(torso) && !part.transform.IsChildOf(leftArm) && !part.transform.IsChildOf(rightArm);
                    part.forceRenderingOff = !DeviceControls.VR && upper || Vector3.Distance(part.bounds.ClosestPoint(view.transform.position), view.transform.position) < 0.18f;
                }
            }
            else foreach (var part in bodyRenderers) part.forceRenderingOff = false;
            if (!DeviceControls.VR && view.targetTexture == null) view.ResetAspect();
            // speed stretches the view a little, and the map of a world closes in until that world fills it
            if (!DeviceControls.VR) view.fieldOfView = Mathf.Lerp(view.fieldOfView, PlanetView ? 26f : !flying && !SystemView && Weapon.rail && game.Battle.Charging ? math.lerp(60, 34, game.Battle.RailCharge) : 70f + 25f * math.saturate(math.length(Velocity) / Tuning.ShipSpaceSpeed)
                + (flying ? 0f : 12f * math.saturate((math.length(Velocity) - Tuning.WalkSpeed) / (Tuning.BikeSpeed - Tuning.WalkSpeed))), 5f * frame);
        }

        // A blast has gone off here: a ship parked on the edge of the hole has to settle again.
        public void Shaken(float3 point, float size)
        {
            if (math.distance(ship.position, point) < size + 6f) ship.landed = false;
        }

        void Interact()
        {
            if (!flying && NearShip) { if (captive) game.Advance(); recall = false; (flying, riding) = (true, false); }
            else if (flying && ship.landed)
            {
                float3 forward = math.rotate(ship.rotation, new float3(0, 0, 1));
                walker = Walker.Spawn(Body, ship.position + math.rotate(ship.rotation, new float3(3.5f, 0, 0)), forward);
                flying = false;
                pitch = 0f;
            }
            else if (flying && !Autopilot && Body.kind != BodyKind.Gas) landing = !landing;
        }
    }
}
