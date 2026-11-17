using System;
using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.XR;

namespace Infinigame
{
    [DefaultExecutionOrder(-50)]
    public class Arms : MonoBehaviour
    {
        [NonSerialized] public Game game;
        Transform hands;
        float crackle, nextPunch;
        int stains;
        public void Stain(Material material)
        {
            if (gauntlets[1] == null || stains >= 12) return;
            int index = stains++;
            Shapes.Part(gauntlets[index % 2], "Violet blood", Shapes.Rock(2), material,
                new Vector3((index % 3 - 1) * 0.06f, 0.085f, 0.02f + index / 3 * 0.03f), new Vector3(0.065f, 0.012f, 0.075f), false);
            game.Player.Stain(material, index);
        }
        readonly Vector3[] previous = new Vector3[2];
        readonly bool[] tracked = new bool[2];
        int punchFrame = -1;
        public float3 PunchOrigin { get; private set; }
        public float3 PunchDirection { get; private set; }
        public bool MotionPunch => punchFrame == Time.frameCount;
        void Update()
        {
            if (!DeviceControls.VR || game == null) return;
            for (int i = 0; i < 2; i++)
            {
                var node = i == 0 ? XRNode.LeftHand : XRNode.RightHand;
                var device = InputDevices.GetDeviceAtXRNode(node);
                bool valid = device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 local);
                Vector3 delta = local - previous[i];
                if (valid && tracked[i] && !game.Paused && !game.ShowControls && !game.Player.SystemView && !game.Hermit.Talking
                    && Time.time >= nextPunch && delta.magnitude > 0.025f && delta.magnitude / math.max(0.001f, Time.unscaledDeltaTime) > 2.2f
                    && game.Player.Hand(node, out var position, out _))
                {
                    punchFrame = Time.frameCount; nextPunch = Time.time + math.max(0.35f, game.Player.Weapon.period);
                    PunchOrigin = (float3)position + game.System.Anchor;
                    PunchDirection = game.Player.TrackingRotation * delta.normalized;
                }
                previous[i] = local; tracked[i] = valid;
            }
        }
        public float3 Muzzle(Weapon weapon)
        {
            if (gauntlets[1] == null) return game.Player.Position;
            Vector3 port = weapon.beam ? new Vector3(0, 0.04f, 0.16f) : weapon.rail ? new Vector3(0.07f, 0.1f, -0.08f)
                : weapon.homing > 0 ? new Vector3(0, 0.14f, -0.22f) : new Vector3(0, 0, 0.22f);
            return (float3)gauntlets[1].TransformPoint(port) + game.System.Anchor;
        }
        readonly Transform[] gauntlets = new Transform[2];
        void Start()
        {
            hands = new GameObject("Gauntlets").transform;
            hands.SetParent(Camera.main.transform, false);
            foreach (float side in new[] { -1f, 1f })
            {
                int index = side < 0 ? 0 : 1;
                gauntlets[index] = Models.Gauntlet();
                gauntlets[index].name = side < 0 ? "Left gauntlet" : "Right gauntlet";
                gauntlets[index].SetParent(hands, false);
                gauntlets[index].localPosition = new Vector3(side * 0.29f, -0.25f, 0.46f);
                gauntlets[index].localScale = new Vector3(side < 0 ? -1 : 1, 1, 1);

            }
        }
        void LateUpdate()
        {
            if (hands == null) return;
            hands.gameObject.SetActive((!game.Player.flying || DeviceControls.VR && game.Player.Seated) && !game.Player.ThirdPerson && !game.Player.SystemView && !game.Title && !game.Dead && !game.Frontier.Collapsing && game.Player.Has(Gear.Suit));
            if (DeviceControls.VR)
            {
                for (int i = 0; i < 2; i++)
                    if (game.Player.Hand(i == 0 ? XRNode.LeftHand : XRNode.RightHand, out var position, out var rotation))
                        gauntlets[i].SetPositionAndRotation(position, rotation * Quaternion.Euler(90, 0, 0));
                return;
            }
            float punch = game.Player.Weapon.melee ? math.saturate(1 - (Time.time - game.Battle.Fired) / 0.25f) : 0;
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1 : 1;
                float strike = ((int)(game.Battle.Fired * 10) % 2 == i) ? punch : punch * 0.2f;
                float raised = game.Player.Weapon.rail ? 0.09f : game.Player.Weapon.beam ? 0.04f : game.Player.Weapon.homing > 0 ? -0.06f : 0;
                gauntlets[i].localPosition = Vector3.Lerp(gauntlets[i].localPosition, new Vector3(side * (0.29f - strike * 0.12f), -0.25f + raised + strike * 0.08f, 0.46f + strike * 0.38f), 1 - math.exp(-24 * Time.deltaTime));
                gauntlets[i].localRotation = Quaternion.Slerp(gauntlets[i].localRotation, Quaternion.Euler(game.Player.Weapon.beam ? -65 : -strike * 22, -side * strike * 18, game.Player.Weapon.rail ? side * 75 : side * strike * 15), 1 - math.exp(-20 * Time.deltaTime));
                if (!game.Player.Powered && hands.gameObject.activeSelf && Time.time >= crackle)
                    game.Effects.Crackle((float3)gauntlets[i].position + game.System.Anchor);
            }
            if (Time.time >= crackle) crackle = Time.time + 0.08f;
            hands.localPosition = new Vector3(0, math.sin(Time.time * 8) * math.min(math.length(game.Player.Velocity), 8) * 0.001f, game.Player.Weapon.melee ? punch * 0.2f : -punch * 0.025f);
        }
    }
}
