using UnityEngine;
using UnityEngine.XR;
#if INFINIGAME_HANDS
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.Gestures;
using UnityEngine.XR.Hands.OpenXR;
#endif

namespace Infinigame
{
    public class GauntletFingers : MonoBehaviour
    {
        readonly Transform[,] bones = new Transform[5, 3];
        readonly float[] curls = { 0.78f, 0.78f, 0.78f, 0.78f, 0.78f };

        void Awake()
        {
            for (int digit = 0; digit < 5; digit++)
            {
                bones[digit, 0] = transform.Find("Digit " + digit);
                bones[digit, 1] = bones[digit, 0].Find("Middle");
                bones[digit, 2] = bones[digit, 1].Find("Tip");
            }
        }

        public static Quaternion Bend(int digit, int joint, float curl)
        {
            bool thumb = digit == 0;
            float angle = Mathf.Lerp(joint == 0 ? -5 : 4, thumb ? 49 : joint == 1 ? 86 : 68, curl);
            float spread = joint != 0 ? 0 : thumb ? Mathf.Lerp(-58, -19, curl) : (digit - 2.5f) * 3 * (1 - curl);
            return Quaternion.Euler(angle, spread, thumb && joint == 0 ? 32 : 0);
        }

        public void SetCurl(int digit, float value)
        {
            curls[digit] = Mathf.Clamp01(value);
            for (int joint = 0; joint < 3; joint++) bones[digit, joint].localRotation = Bend(digit, joint, curls[digit]);
        }

        public void Follow(XRNode node)
        {
            var controller = InputDevices.GetDeviceAtXRNode(node);
            controller.TryGetFeatureValue(CommonUsages.trigger, out float trigger);
            controller.TryGetFeatureValue(CommonUsages.grip, out float grip);
            controller.TryGetFeatureValue(CommonUsages.primaryTouch, out bool primaryTouch);
            controller.TryGetFeatureValue(CommonUsages.secondaryTouch, out bool secondaryTouch);
            controller.TryGetFeatureValue(CommonUsages.primary2DAxisTouch, out bool stickTouch);
#if INFINIGAME_HANDS
            var tracking = HandTracking.subsystem;
            var hand = tracking != null && tracking.running ? (node == XRNode.LeftHand ? tracking.leftHand : tracking.rightHand) : default;
#endif
            for (int i = 0; i < 5; i++)
            {
                float target = !controller.isValid ? 0.78f : i == 0 ? (primaryTouch || secondaryTouch || stickTouch ? 0.8f : 0.15f) : i == 1 ? trigger : grip;
#if INFINIGAME_HANDS
                if (hand.isTracked && hand.CalculateFingerShape((XRHandFingerID)i, XRFingerShapeTypes.FullCurl).TryGetFullCurl(out float measured))
                    target = measured;
#endif
                SetCurl(i, Mathf.MoveTowards(curls[i], target, Time.unscaledDeltaTime * 14));
            }
        }
    }
}
