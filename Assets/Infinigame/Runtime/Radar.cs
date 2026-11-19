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
        Material near, far, goal, air, mind;
        void Start()
        {
            game = FindAnyObjectByType<Game>();
            globe = Shapes.Part(transform, "Globe", Shapes.Sphere(1, 32), new Material(Resources.Load<Shader>("Globe")), Vector3.zero, Vector3.one, false).transform;
            marker = Shapes.Sphere(0.045f, 10);
            near = new Material(Resources.Load<Shader>("RadarContact")); near.SetColor("_Colour", new Color(1, 0.25f, 0.12f, 1));
            far = new Material(near); far.SetColor("_Colour", new Color(1, 0.55f, 0.35f, 0.5f)); far.SetFloat("_Hollow", 1);
            air = new Material(near); air.SetColor("_Colour", new Color(0.85f, 0.45f, 1, 1));
            mind = new Material(near); mind.SetColor("_Colour", new Color(1, 0.3f, 0.8f, 1));
            goal = new Material(near); goal.SetColor("_Colour", new Color(0.2f, 1, 1, 1));
        }
        void LateUpdate()
        {
            if (globe == null || !game.Ready) return;
            bool visible = !game.Paused && !game.ShowControls && !game.Dead && !game.Hermit.Talking && !game.Player.SystemView && !game.Buried && !game.Fall.active;
            globe.gameObject.SetActive(visible);
            int used = 0;
            var camera = Camera.main;
            transform.SetPositionAndRotation(camera.ViewportToWorldPoint(new Vector3(0.87f, DeviceControls.Touch ? 0.75f : 0.83f, 3)), camera.transform.rotation);
            if (!game.Player.flying)
                transform.rotation = Quaternion.LookRotation(transform.position - camera.transform.position, camera.transform.up);
            transform.localScale = Vector3.one * 0.34f;
            if (DeviceControls.VR && game.Player.Seated)
            {
                var mount = game.Player.Cockpit.transform;
                transform.SetPositionAndRotation(mount.TransformPoint(new Vector3(0.45f, 0.6f, 1.4f)), mount.rotation);
                transform.localScale = Vector3.one * 0.12f;
            }
            void Contact(float3 position, bool objective, bool airborne = false, bool brain = false)
            {
                float3 origin = game.Player.flying ? game.Player.Position : game.Player.ThirdPerson ? game.Player.Position + math.normalizesafe(game.Player.Position) * Walker.EyeHeight : (float3)camera.transform.position + game.System.Anchor;
                float3 local = camera.transform.InverseTransformDirection(position - origin);
                local = math.normalizesafe(local, new float3(0, 0, 1));
                // The cockpit tilts its display. Helmet contacts keep their head-relative bearings.
                if (game.Player.flying) local = math.rotate(quaternion.RotateX(math.radians(-55)), local);
                if (used == contacts.Count) contacts.Add(Shapes.Part(transform, "Contact", marker, near, Vector3.zero, Vector3.one, false).transform);
                var contact = contacts[used++];
                contact.gameObject.SetActive(true);
                contact.localPosition = local * 1.05f;
                contact.GetComponent<MeshRenderer>().sharedMaterial = objective ? goal : brain ? mind : airborne ? air : local.z > 0 ? far : near;
                contact.localScale = brain ? Vector3.one * 2 : airborne ? new Vector3(1.6f, 0.7f, 1) : Vector3.one;
            }
            if (visible)
            {
                foreach (var foe in game.Battle.Foes) if (foe.health > 0 && !foe.friendly && !game.Outpost.Conceals(game.Player.Position, foe.position) && (!game.Player.InSpace || game.System.ReferenceBody.kind == BodyKind.Station || foe.kind == FoeKind.Raider) && math.distance(foe.position, game.Player.Position) < (game.Player.InSpace ? 2500 : 500)) Contact(foe.position, false, foe.kind == FoeKind.Raider || foe.kind == FoeKind.Drone, foe.kind == FoeKind.Mind);
                if (game.Goal is float3 position && !game.Outpost.Conceals(game.Player.Position, position)) Contact(position, true);
            }
            for (int i = used; i < contacts.Count; i++) contacts[i].gameObject.SetActive(false);
        }
        void OnDestroy()
        {
            if (mind != null) Destroy(mind);
            if (air != null) Destroy(air);
            if (near != null) Destroy(near);
            if (far != null) Destroy(far);
            if (goal != null) Destroy(goal);
            if (marker != null) Destroy(marker);
        }
    }
}
