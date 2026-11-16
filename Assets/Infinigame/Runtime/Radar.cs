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
        Material near, far, goal;
        void Start()
        {
            game = FindAnyObjectByType<Game>();
            globe = Shapes.Part(transform, "Globe", Shapes.Sphere(1, 32), new Material(Resources.Load<Shader>("Globe")), Vector3.zero, Vector3.one, false).transform;
            marker = Shapes.Sphere(0.045f, 10);
            near = new Material(Resources.Load<Shader>("RadarContact")); near.SetColor("_Colour", new Color(1, 0.25f, 0.12f, 1));
            far = new Material(near); far.SetColor("_Colour", new Color(1, 0.55f, 0.35f, 0.5f)); far.SetFloat("_Hollow", 1);
            goal = new Material(near); goal.SetColor("_Colour", new Color(0.2f, 1, 1, 1));
        }
        void LateUpdate()
        {
            if (globe == null) return;
            bool visible = !game.Paused && !game.ShowControls && !game.Dead && !game.Hermit.Talking && !game.Player.SystemView && !game.Buried && !game.Frontier.Collapsing;
            globe.gameObject.SetActive(visible);
            int used = 0;
            var camera = Camera.main;
            transform.SetPositionAndRotation(camera.ViewportToWorldPoint(new Vector3(0.87f, 0.19f, 3)), camera.transform.rotation);
            transform.localScale = Vector3.one * 0.34f;
            void Contact(float3 position, bool objective)
            {
                float3 local = camera.transform.InverseTransformDirection(position - game.Player.Position);
                local = math.normalizesafe(local, new float3(0, 0, 1));
                // Ahead is the upper hemisphere, behind the lower. Depth remains visible through the globe.
                local = math.rotate(quaternion.RotateX(math.radians(-55)), local);
                if (used == contacts.Count) contacts.Add(Shapes.Part(transform, "Contact", marker, near, Vector3.zero, Vector3.one, false).transform);
                var contact = contacts[used++];
                contact.gameObject.SetActive(true);
                contact.localPosition = local * 1.05f;
                contact.GetComponent<MeshRenderer>().sharedMaterial = objective ? goal : local.z > 0 ? far : near;
            }
            if (visible)
            {
                foreach (var foe in game.Battle.Foes) if (game.Player.Powered && foe.health > 0 && !foe.friendly && (foe.kind == FoeKind.Raider) == game.Player.InSpace && math.distance(foe.position, game.Player.Position) < (game.Player.InSpace ? 2500 : 80)) Contact(foe.position, false);
                if (game.Goal is float3 position) Contact(position, true);
            }
            for (int i = used; i < contacts.Count; i++) contacts[i].gameObject.SetActive(false);
        }
        void OnDestroy()
        {
            if (near != null) Destroy(near);
            if (far != null) Destroy(far);
            if (goal != null) Destroy(goal);
            if (marker != null) Destroy(marker);
        }
    }
}
