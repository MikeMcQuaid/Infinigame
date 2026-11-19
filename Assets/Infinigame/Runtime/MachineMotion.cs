using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;

namespace Infinigame
{
    // A few articulated joints keep the generated bodies readable in motion.
    [DefaultExecutionOrder(-10)]
    public class MachineMotion : MonoBehaviour
    {
        public bool quadruped, seated, working;
        public float speed, strike, recoil, aimPitch;
        public int weapon;
        public Transform look, planet;
        public bool LandedFoot { get; private set; }
        public BodyDescriptor ground;
        Transform torso, head;
        Transform[] legs, knees, arms, elbows;
        Vector3 home, torsoHome;
        float phase;
        void Start()
        {
            home = transform.localPosition;
            torso = transform.Find("Torso");
            torsoHome = torso.localPosition;
            head = torso.Find(quadruped ? "Neck/Head" : "Head");
            legs = new Transform[quadruped ? 4 : 2]; knees = new Transform[legs.Length];
            for (int i = 0; i < legs.Length; i++)
            {
                legs[i] = transform.Find(quadruped ? "Leg " + i : i == 0 ? "Left leg" : "Right leg");
                knees[i] = legs[i].Find("Knee");
            }
            if (!quadruped)
            {
                arms = new[] { torso.Find("Left arm"), torso.Find("Right arm") };
                elbows = new[] { arms[0].Find("Elbow"), arms[1].Find("Elbow") };
            }
            phase = quadruped ? home.x * 0.7f + home.z * 0.3f : 0;
        }
        void LateUpdate()
        {
            if (working && !seated && Time.deltaTime > 0)
            {
                float t = Time.time * 0.18f + home.x;
                Vector3 desired = home + new Vector3(Mathf.Sin(t) * 3, 0, Mathf.Cos(t) * 2);
                Vector3 moving = transform.parent.TransformDirection(new Vector3(Mathf.Cos(t) * 0.54f, 0, -Mathf.Sin(t) * 0.36f));
                transform.localPosition = desired;
                if (planet != null)
                {
                    float3 at = planet.InverseTransformPoint(transform.position), up = math.normalizesafe(at);
                    transform.position = planet.TransformPoint(up * ground.GroundRadius(up));
                    transform.rotation = Quaternion.LookRotation(moving, planet.TransformDirection(up));
                }
                speed = moving.magnitude;
            }
            float lastPhase = phase;
            phase = Mathf.Repeat(phase + Time.deltaTime * (quadruped ? Mathf.Min(speed / 0.917f, 4.2f) * 2 * Mathf.PI : speed * 2.2f), Mathf.PI * 2);
            LandedFoot = quadruped && !seated && (phase < lastPhase || lastPhase < Mathf.PI && phase >= Mathf.PI);
            float stride = Mathf.Clamp01(speed / (quadruped ? 1 : 5));
            torso.localPosition = torsoHome + Vector3.up * (seated ? -0.2f : Mathf.Cos(phase * 2) * 0.025f * stride);
            for (int i = 0; i < legs.Length; i++)
            {
                if (quadruped && !seated)
                {
                    float2 foot = GroundStride.Foot(phase / (2 * Mathf.PI) + (i == 1 || i == 2 ? 0.5f : 0)) * stride;
                    float depth = 0.83f - foot.y * 0.19f, forward = foot.x * 0.55f;
                    float reach = Mathf.Sqrt(depth * depth + forward * forward);
                    const float thigh = 0.4045f, shin = 0.51f;
                    float sign = i < 2 ? 1 : -1;
                    float upper = -Mathf.Atan2(forward, depth) - sign * Mathf.Acos(Mathf.Clamp((thigh * thigh + reach * reach - shin * shin) / (2 * thigh * reach), -1, 1));
                    float bend = sign * Mathf.Acos(Mathf.Clamp((reach * reach - thigh * thigh - shin * shin) / (2 * thigh * shin), -1, 1));
                    legs[i].localRotation = Quaternion.Euler(upper * Mathf.Rad2Deg + 8.53f, 0, 0);
                    knees[i].localRotation = Quaternion.Euler(bend * Mathf.Rad2Deg - 8.53f, 0, 0);
                    continue;
                }
                float beat = Mathf.Sin(phase + i * Mathf.PI);
                legs[i].localRotation = Quaternion.Euler(seated ? i < 2 ? -70 : 70 : beat * 28 * stride, 0, 0);
                knees[i].localRotation = Quaternion.Euler(seated ? 100 : -Mathf.Max(0, -beat) * 42 * stride, 0, 0);
            }
            if (quadruped && !seated)
            {
                recoil = Mathf.MoveTowards(recoil, 0, Time.deltaTime * 6);
                torso.localRotation = Quaternion.Euler(4 * recoil + 2 * Mathf.Cos(phase * 2) * stride, 0, 3 * Mathf.Sin(phase) * stride);
                if (look == null) head.localRotation = Quaternion.Euler(-3 * recoil - Mathf.Cos(phase * 2) * stride, 0, -2 * Mathf.Sin(phase) * stride);
            }
            if (arms != null)
                for (int i = 0; i < arms.Length; i++)
                {
                    arms[i].localRotation = Quaternion.Euler(-Mathf.Sin(phase + i * Mathf.PI) * 18 * stride, 0, i == 0 ? -7 : 7);
                    elbows[i].localRotation = Quaternion.Euler(-12 - 15 * stride, 0, 0);
                }
            if (!quadruped && arms != null)
            {
                if (!seated && look == null)
                {
                    for (int i = 0; i < arms.Length; i++)
                    {
                        // A beam or rail gauntlet fires from the right wrist.
                        if (i == 0 && (weapon == 1 || weapon == 3)) continue;
                        float side = i == 0 ? -1 : 1;
                        arms[i].localRotation = Quaternion.Euler((weapon == 1 ? -75 : weapon == 3 ? -82 : weapon == 4 ? -35 : weapon == 0 ? -56 : 25) - aimPitch, weapon == 4 ? side * 55 : weapon == 3 ? -12 : 0, side * (weapon == 4 ? 65 : weapon == 3 ? -5 : 10));
                        elbows[i].localRotation = Quaternion.Euler(weapon == 1 ? -15 : weapon == 3 ? -5 : weapon == 0 ? -53 : -45, 0, 0);
                    }
                }
                torso.localRotation = Quaternion.Euler(recoil * 16, strike * 18, recoil * 9);
                arms[1].localRotation *= Quaternion.Euler(-strike * 40, 0, -strike * 12);
                elbows[1].localRotation *= Quaternion.Euler(strike * 48, 0, 0);
            }
            if (look != null)
            {
                Vector3 direction = head.parent.InverseTransformDirection(look.position - head.position);
                float yaw = Mathf.Clamp(Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg, -55, 55);
                float pitch = Mathf.Clamp(-Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg, -25, 25);
                head.localRotation = Quaternion.Slerp(head.localRotation, Quaternion.Euler(pitch, yaw, 0), 1 - Mathf.Exp(-4 * Time.deltaTime));
            }
        }
    }
}
