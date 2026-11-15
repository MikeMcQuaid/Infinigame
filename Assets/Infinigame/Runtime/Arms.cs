using System;
using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.XR;

namespace Infinigame
{
    public class Arms : MonoBehaviour
    {
        [NonSerialized] public Game game;
        Transform hands;
        readonly Transform[] gauntlets = new Transform[2];
        void Start()
        {
            hands = new GameObject("Gauntlets").transform;
            hands.SetParent(Camera.main.transform, false);
            foreach (float side in new[] { -1f, 1f })
            {
                int index = side < 0 ? 0 : 1;
                gauntlets[index] = new GameObject(side < 0 ? "Left gauntlet" : "Right gauntlet").transform;
                gauntlets[index].SetParent(hands, false);
                MeshKit plate = new MeshKit(), cable = new MeshKit(), light = new MeshKit();
                plate.Box(new Vector3(side * 0.29f, -0.24f, 0.45f), new Vector3(0.18f, 0.16f, 0.24f), 0.035f);
                plate.Box(new Vector3(side * 0.32f, -0.28f, 0.24f), new Vector3(0.16f, 0.19f, 0.3f), 0.04f);
                for (int i = 0; i < 4; i++)
                {
                    float x = side * (0.23f + i * 0.04f);
                    plate.Box(new Vector3(x, -0.24f, 0.58f), new Vector3(0.035f, 0.1f, 0.09f), 0.012f);
                    plate.Rod(new Vector3(x, -0.18f, 0.3f), new Vector3(x, -0.16f, 0.52f), 0.008f, 6);
                    cable.Rod(new Vector3(x, -0.29f, 0.1f), new Vector3(x, -0.29f, 0.39f), 0.009f, 6);
                    light.Ball(new Vector3(x, -0.17f, 0.51f), 0.009f, 8);
                }
                light.Box(new Vector3(side * 0.29f, -0.155f, 0.45f), new Vector3(0.1f, 0.015f, 0.035f));
                Shapes.Part(gauntlets[index], "Fluted plate", plate.Mesh("Plate"), Shapes.Material("Props", new Color(0.3f, 0.38f, 0.46f)), Vector3.zero, Vector3.one);
                Shapes.Part(gauntlets[index], "Cable mail", cable.Mesh("Cable"), Shapes.Material("Props", new Color(0.07f, 0.1f, 0.12f)), Vector3.zero, Vector3.one);
                Shapes.Part(gauntlets[index], "Heat seams", light.Mesh("Seams"), Shapes.Material("Glow", new Color(0.25f, 2.5f, 4)), Vector3.zero, Vector3.one, false);
            }
        }
        void LateUpdate()
        {
            if (hands == null) return;
            hands.gameObject.SetActive(!game.Player.flying && !game.Player.SystemView && !game.Title && !game.Dead && game.Player.Has(Gear.Suit));
            if (DeviceControls.VR)
            {
                for (int i = 0; i < 2; i++)
                    if (game.Player.Hand(i == 0 ? XRNode.LeftHand : XRNode.RightHand, out var position, out var rotation))
                        gauntlets[i].SetPositionAndRotation(position - rotation * new Vector3(i == 0 ? -0.29f : 0.29f, -0.24f, 0.45f), rotation);
                return;
            }
            float punch = math.saturate(1 - (Time.time - game.Battle.Fired) / 0.25f);
            hands.localPosition = new Vector3(0, math.sin(Time.time * 8) * math.min(math.length(game.Player.Velocity), 8) * 0.001f, game.Player.Weapon.melee ? punch * 0.2f : -punch * 0.025f);
        }
    }
}
