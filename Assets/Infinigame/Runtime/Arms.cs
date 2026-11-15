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
            hands.gameObject.SetActive(!game.Player.flying && !game.Player.ThirdPerson && !game.Player.SystemView && !game.Title && !game.Dead && game.Player.Has(Gear.Suit));
            if (DeviceControls.VR)
            {
                for (int i = 0; i < 2; i++)
                    if (game.Player.Hand(i == 0 ? XRNode.LeftHand : XRNode.RightHand, out var position, out var rotation))
                        gauntlets[i].SetPositionAndRotation(position, rotation);
                return;
            }
            float punch = math.saturate(1 - (Time.time - game.Battle.Fired) / 0.25f);
            hands.localPosition = new Vector3(0, math.sin(Time.time * 8) * math.min(math.length(game.Player.Velocity), 8) * 0.001f, game.Player.Weapon.melee ? punch * 0.2f : -punch * 0.025f);
        }
    }
}
