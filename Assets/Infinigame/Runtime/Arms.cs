using System;
using System.Collections.Generic;
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
        Transform hands, wristWeapons, activeModule, railCharge;
        readonly Dictionary<Gear, Transform> wristPorts = new Dictionary<Gear, Transform>();
        float crackle;
        bool aligning;
        Transform Fist(int side) => DeviceControls.VR ? gauntlets[side] : game.Player.fists[side];

        readonly PunchStroke[] strokes = new PunchStroke[2];
        public readonly float3[] punchOrigins = new float3[2], punchDirections = new float3[2];
        public readonly float[] punchLengths = new float[2];
        public void Contact(int hand) => strokes[hand].Contact();
        public int PunchHands { get; private set; }
        public bool MotionPunch => PunchHands != 0;
        void Update()
        {
            PunchHands = 0;
            if (!DeviceControls.VR || game == null) return;
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            bool play = !game.Paused && !game.ShowControls && !game.Player.SystemView && !game.Hermit.Talking;
            bool trackedHead = head.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 eye);
            for (int i = 0; i < 2; i++)
            {
                var node = i == 0 ? XRNode.LeftHand : XRNode.RightHand;
                var device = InputDevices.GetDeviceAtXRNode(node);
                if (!play || !trackedHead || !device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 hand))
                { strokes[i] = default; continue; }
                Vector3 local = hand - eye;
                if (!strokes[i].Step(local, Vector3.down * 0.18f, Time.unscaledDeltaTime, game.Buried ? 0.8f : 2.2f)) continue;
                float3 sweep = game.Player.TrackingRotation * (Vector3)strokes[i].Travel;
                float3 direction = math.normalize(sweep);
                if (game.Buried && math.dot(direction, math.normalizesafe(game.Player.Position)) <= 0.2f) continue;
                if (!game.Player.Hand(node, out var position, out var rotation)) continue;
                PunchHands |= 1 << i;
                var glove = game.GlovePose(i, position, rotation);
                float3 knuckles = glove.position + glove.rotation * GauntletFingers.Knuckles;
                punchOrigins[i] = knuckles + game.System.Anchor - sweep;
                punchLengths[i] = math.length(sweep) + 0.07f;
                if (game.Buried) Contact(i);
                punchDirections[i] = direction;
            }
        }
        public Pose Muzzle(Weapon weapon)
        {
            if (wristPorts.TryGetValue(weapon.needs, out var port)) return new Pose(port.position, port.rotation);
            var fist = Fist(1);
            return fist != null ? new Pose(fist.TransformPoint(0, 0, 0.22f), fist.rotation)
                : new Pose(game.Player.Position - game.System.Anchor, Quaternion.identity);
        }
        readonly GauntletFingers[] digits = new GauntletFingers[2];
        readonly Transform[] gauntlets = new Transform[2];
        void Start()
        {
            hands = new GameObject("Gauntlets").transform;
            hands.SetParent(Camera.main.transform, false);
            game.Player.fists[0].GetComponentInParent<MachineMotion>(true).trackedGloves = gauntlets;
            foreach (float side in new[] { -1f, 1f })
            {
                int index = side < 0 ? 0 : 1;
                gauntlets[index] = Models.Gauntlet();
                digits[index] = gauntlets[index].gameObject.AddComponent<GauntletFingers>();
                gauntlets[index].name = side < 0 ? "Left gauntlet" : "Right gauntlet";
                gauntlets[index].SetParent(hands, false);
                gauntlets[index].localPosition = new Vector3(side * 0.29f, -0.25f, 0.46f);
                gauntlets[index].localScale = new Vector3(side < 0 ? -1 : 1, 1, 1);
            }
            wristWeapons = Models.WristWeapons();
            railCharge = wristWeapons.Find("Rail gauntlet/Rail charge");
            wristWeapons.SetParent(Fist(1), false);
            foreach (var weapon in Combat.Arms)
            {
                var module = wristWeapons.Find(weapon.name);
                if (module != null) wristPorts.Add(weapon.needs, module.Find("Port"));
            }
        }
        public void TrackGloves()
        {
            if (hands == null) return;
            if (wristWeapons.parent != Fist(1)) wristWeapons.SetParent(Fist(1), false);
            if (!DeviceControls.VR) return;
            for (int i = 0; i < 2; i++)
            {
                var node = i == 0 ? XRNode.LeftHand : XRNode.RightHand;
                if (game.Player.Hand(node, out var position, out var rotation))
                {
                    var pose = game.GlovePose(i, position, rotation);
                    gauntlets[i].SetPositionAndRotation(pose.position, pose.rotation);
                    if (hands.gameObject.activeSelf) digits[i].Follow(node);
                }
            }
        }

        void LateUpdate()
        {
            if (hands == null) return;
            var module = !game.Player.flying && game.Player.Armed && wristPorts.TryGetValue(game.Player.Weapon.needs, out var port) ? port.parent : null;
            if (module != activeModule)
            {
                if (activeModule != null) activeModule.gameObject.SetActive(false);
                if (module != null) module.gameObject.SetActive(true);
                activeModule = module;
            }
            bool charging = module == railCharge.parent && game.Battle.Charging && !game.Paused && !game.Player.SystemView && !game.Hermit.Talking && !game.ShowControls && !game.Dead;
            railCharge.gameObject.SetActive(charging);
            if (charging)
                railCharge.localScale = Vector3.Lerp(new Vector3(0.45f, 0.45f, 0.12f), Vector3.one, game.Battle.RailCharge);
            bool adjusting = DeviceControls.VR && game.FittingHands;
            if (adjusting != aligning)
            {
                aligning = adjusting;
                foreach (var part in hands.GetComponentsInChildren<Renderer>(true))
                    part.gameObject.layer = aligning ? LayerMask.NameToLayer("UI") : hands.gameObject.layer;
            }
            hands.gameObject.SetActive(adjusting || DeviceControls.VR && !game.Player.flying && !game.Player.ThirdPerson && !game.Player.SystemView && !game.Title && !game.Dead && !game.Frontier.Collapsing && game.Player.Has(Gear.Suit));
            if (DeviceControls.VR) return;
            float punch = game.Player.Weapon.melee ? math.saturate(1 - (Time.time - game.Battle.Fired) / 0.25f) : 0;
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1 : 1;
                float strike = ((int)(game.Battle.Fired * 10) % 2 == i) ? punch : punch * 0.2f;
                float raised = game.Player.Weapon.rail ? 0.09f : game.Player.Weapon.beam ? 0.04f : game.Player.Weapon.homing > 0 ? -0.06f : 0;
                gauntlets[i].localPosition = Vector3.Lerp(gauntlets[i].localPosition, new Vector3(side * (0.29f - strike * 0.12f), -0.25f + raised + strike * 0.08f, 0.46f + strike * 0.38f), 1 - math.exp(-24 * Time.deltaTime));
                gauntlets[i].localRotation = Quaternion.Slerp(gauntlets[i].localRotation, Quaternion.Euler(game.Player.Weapon.beam ? -65 : -strike * 22, -side * strike * 18, game.Player.Weapon.rail ? side * 75 : side * strike * 15), 1 - math.exp(-20 * Time.deltaTime));
                if (!game.Player.Powered && !game.Player.flying && !game.Title && !game.Dead && Time.time >= crackle)
                    game.Effects.Crackle((float3)Fist(i).position + game.System.Anchor);
            }
            if (Time.time >= crackle) crackle = Time.time + 0.08f;
            hands.localPosition = new Vector3(0, math.sin(Time.time * 8) * math.min(math.length(game.Player.Velocity), 8) * 0.001f, game.Player.Weapon.melee ? punch * 0.2f : -punch * 0.025f);
        }
    }
}
