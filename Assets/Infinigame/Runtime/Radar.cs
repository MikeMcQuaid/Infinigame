using System;
using System.Collections.Generic;
using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;

namespace Infinigame
{
    // A transparent, rendered sphere. Every contact has a direction in three dimensions.
    public class Radar : MonoBehaviour
    {
        [NonSerialized] public Game game;
        Transform globe;
        readonly List<Transform> contacts = new List<Transform>();
        Mesh marker;
        Material near, far, goal, glass;
        void Start()
        {
            game = FindAnyObjectByType<Game>();
            glass = new Material(Resources.Load<Shader>("Globe"));
            globe = Shapes.Part(transform, "Globe", Shapes.Orb(1, 32), glass, Vector3.zero, Vector3.one, false).transform;
            marker = Shapes.Orb(0.045f, 10);
            near = new Material(Resources.Load<Shader>("RadarContact")); near.SetColor("_Colour", new Color(1, 0.25f, 0.12f, 1));
            far = new Material(near); far.SetColor("_Colour", new Color(0.98f, 0.12f, 0.09f, 0.85f)); far.SetFloat("_Hollow", 1);
            goal = new Material(near); goal.SetColor("_Colour", new Color(0.2f, 1, 1, 1));
        }
        void LateUpdate()
        {
            if (globe == null || !game.Ready) return;
            bool visible = !game.Paused && !game.ShowControls && !game.Dead && !game.Hermit.Talking && !game.Player.SystemView && !game.Buried && !game.Fall.active && (!game.Player.flying || game.Player.Seated);
            globe.gameObject.SetActive(visible);
            int used = 0;
            var camera = Camera.main;
            if (game.Player.flying)
            {
                var mount = game.Player.Cockpit.RadarSocket;
                transform.SetPositionAndRotation(mount.position, mount.rotation);
                transform.localScale = Vector3.one * 0.085f;
            }
            else
            {
                transform.position = camera.ViewportToWorldPoint(new Vector3(0.87f, DeviceControls.Touch ? 0.75f : 0.83f, 3));
                transform.rotation = Quaternion.LookRotation(transform.position - camera.transform.position, camera.transform.up);
                transform.localScale = Vector3.one * 0.34f;
            }
            void Contact(float3 position, bool objective, bool airborne = false, bool brain = false)
            {
                float3 origin = game.Player.flying ? game.Player.Position : game.Player.ThirdPerson ? game.Player.Position + math.normalizesafe(game.Player.Position) * Walker.EyeHeight : (float3)camera.transform.position + game.System.Anchor;
                float3 local = (game.Player.Seated ? game.Player.Cockpit.transform : camera.transform).InverseTransformDirection(position - origin);
                local = math.normalizesafe(local, new float3(0, 0, 1));
                bool ahead = local.z >= 0;
                // The cockpit tilts its display. Helmet contacts keep their head-relative bearings.
                if (game.Player.flying) local = math.rotate(quaternion.RotateX(math.radians(-55)), local);
                if (used == contacts.Count) contacts.Add(Shapes.Part(transform, "Contact", marker, near, Vector3.zero, Vector3.one, false).transform);
                var contact = contacts[used++];
                contact.gameObject.SetActive(true);
                contact.localPosition = local * 1.05f;
                contact.GetComponent<MeshRenderer>().sharedMaterial = objective ? goal : ahead ? near : far;
                contact.localScale = objective ? Vector3.one * 3.3f : brain ? Vector3.one * 2 : airborne ? new Vector3(1.6f, 0.7f, 1) : Vector3.one;
            }
            if (visible)
            {
                foreach (var foe in game.Battle.Foes) if (foe.health > 0 && !foe.friendly && !game.Outpost.Conceals(game.Player.Position, foe.position) && (!game.Player.InSpace || game.System.ReferenceBody.kind == BodyKind.Station || foe.kind == FoeKind.Raider) && math.distance(foe.position, game.Player.Position) < (game.Player.InSpace ? 2500 : 500)) Contact(foe.position, false, foe.kind == FoeKind.Raider || foe.kind == FoeKind.Drone, foe.kind == FoeKind.Mind);
                if (game.Goal is float3 position) Contact(position, true);
            }
            for (int i = used; i < contacts.Count; i++) contacts[i].gameObject.SetActive(false);
        }
        void OnDestroy()
        {
            if (near != null) Destroy(near);
            if (far != null) Destroy(far);
            if (goal != null) Destroy(goal);
            if (glass != null) Destroy(glass);
        }
    }
}
