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
        public int strikingHand = 1;
        public float speed, strike, recoil, aimPitch, turn, waistYaw, crouch;
        public Transform[] trackedGloves;
        public int weapon;
        public Transform look, planet;
        public bool LandedFoot { get; private set; }
        public BodyDescriptor ground;
        Transform torso, head;
        Transform[] legs, knees, arms, elbows;
        public Vector3 lean;
        Vector3 home, torsoHome;
        float phase;
        // The walk or stand-up pose has already placed the boots. Take any extra eye drop at the knees.
        public void FitTrackedHead(Vector3 offset, float hipDrop)
        {
            float squat = Mathf.Clamp(-offset.y * 0.85f, 0, 0.53f);
            float lower = Mathf.Max(0, squat - hipDrop);
            transform.position -= transform.up * lower;
            crouch = Mathf.Max(crouch, Mathf.Acos(1 - squat / 0.90f) * Mathf.Rad2Deg / 66f);
            lean = Vector3.ClampMagnitude(new Vector3(offset.x * 0.55f, Mathf.Clamp((offset.y + lower) * 0.65f, -0.18f, 0.08f), offset.z * 0.55f), 0.32f);
        }

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
            torso.localPosition = torsoHome + lean + Vector3.up * (seated ? -0.2f : Mathf.Cos(phase * 2) * 0.025f * stride);
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
                float swing = Mathf.Lerp(beat, Mathf.Abs(beat) * (i == 0 ? turn : -turn), Mathf.Abs(turn));
                legs[i].localRotation = Quaternion.Euler(seated ? i < 2 ? -70 : 70 : swing * 28 * stride - 66 * crouch, turn * beat * 13, 0);
                knees[i].localRotation = Quaternion.Euler(seated ? 100 : -Mathf.Max(0, -beat) * 42 * stride + 132 * crouch, 0, 0);
            }
            if (quadruped && !seated)
            {
                recoil = Mathf.MoveTowards(recoil, 0, Time.deltaTime * 6);
                torso.localRotation = Quaternion.Euler(4 * recoil + 2 * Mathf.Cos(phase * 2) * stride, 0, 3 * Mathf.Sin(phase) * stride);
                if (look == null) head.localRotation = Quaternion.Euler(-3 * recoil - Mathf.Cos(phase * 2) * stride, 0, -2 * Mathf.Sin(phase) * stride);
            }
            if (arms != null)
            {
                for (int i = 0; i < arms.Length; i++)
                {
                    float side = i == 0 ? -1 : 1;
                    Quaternion shoulder = Quaternion.Euler(-Mathf.Sin(phase + i * Mathf.PI) * 18 * stride, 0, side * 7);
                    Quaternion elbow = Quaternion.Euler(-12 - 15 * stride, 0, 0);
                    if (!seated && look == null)
                    {
                        bool wristWeapon = weapon >= 1 && weapon <= 3;
                        float raise = wristWeapon ? (i == 0 ? -52 : weapon == 3 ? -82 : -75) : weapon == 4 ? -35 : -56;
                        shoulder = Quaternion.Euler(raise - aimPitch, weapon == 4 ? side * 55 : wristWeapon ? -side * 9 : 0,
                            side * (weapon == 4 ? 65 : wristWeapon ? 5 : 10));
                        elbow = Quaternion.Euler(wristWeapon ? -90 - raise : weapon == 0 ? -53 : -45, 0, 0);
                    }
                    if (crouch > 0)
                    {
                        shoulder = Quaternion.Slerp(shoulder, Quaternion.Euler(-104, -side * 17, -side * 9), crouch);
                        elbow = Quaternion.Slerp(elbow, Quaternion.Euler(-56, 0, 0), crouch);
                    }
                    if (i == strikingHand)
                    {
                        shoulder *= Quaternion.Euler(-strike * 40, 0, -strike * 12);
                        elbow *= Quaternion.Euler(strike * 48, 0, 0);
                    }
                    float blend = 1 - Mathf.Exp(-16 * Time.deltaTime);
                    arms[i].localRotation = Quaternion.Slerp(arms[i].localRotation, shoulder, blend);
                    elbows[i].localRotation = Quaternion.Slerp(elbows[i].localRotation, elbow, blend);
                }
                torso.localRotation = Quaternion.AngleAxis(waistYaw, Vector3.up) * Quaternion.Euler(recoil * 16 + crouch * 22, strike * 18, recoil * 9);
                if (trackedGloves != null && DeviceControls.VR)
                    for (int i = 0; i < arms.Length; i++)
                    {
                        var glove = trackedGloves[i];
                        if (glove == null || !glove.gameObject.activeInHierarchy) continue;
                        Vector3 wrist = glove.TransformPoint(0, 0, -0.12f);
                        Vector3 reach = wrist - arms[i].position;
                        if (reach.sqrMagnitude < 0.001f) continue;
                        Vector3 bend = Vector3.ProjectOnPlane(transform.right * (i == 0 ? -1 : 1) - transform.forward * 0.75f - transform.up * 0.85f, reach).normalized;
                        float half = Mathf.Min(0.335f, reach.magnitude * 0.5f);
                        Vector3 joint = arms[i].position + reach.normalized * half + bend * Mathf.Sqrt(0.335f * 0.335f - half * half);
                        arms[i].rotation = Quaternion.FromToRotation(-arms[i].up, joint - arms[i].position) * arms[i].rotation;
                        elbows[i].rotation = Quaternion.FromToRotation(-elbows[i].up, wrist - elbows[i].position) * elbows[i].rotation;
                        elbows[i].localScale = new Vector3(1, Mathf.Clamp(Vector3.Distance(elbows[i].position, wrist) / 0.33f, 0.8f, 1.18f), 1);
                    }
            }
            if (look != null)
            {
                if (quadruped)
                {
                    var neck = head.parent;
                    Vector3 bearing = torso.InverseTransformDirection(look.position - head.position);
                    float lift = -Mathf.Atan2(bearing.y, new Vector2(bearing.x, bearing.z).magnitude) * Mathf.Rad2Deg;
                    float swivel = Mathf.Atan2(bearing.x, bearing.z) * Mathf.Rad2Deg;
                    var target = Quaternion.Euler(Mathf.Clamp(lift * 0.45f, -31, 18), Mathf.Clamp(swivel * 0.42f, -24, 24), 0);
                    neck.localRotation = Quaternion.Slerp(neck.localRotation, target, 1 - Mathf.Exp(-3 * Time.deltaTime));
                }
                Vector3 direction = head.parent.InverseTransformDirection(look.position - head.position);
                float yaw = Mathf.Clamp(Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg, -55, 55);
                float pitch = Mathf.Clamp(-Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg, -25, 25);
                head.localRotation = Quaternion.Slerp(head.localRotation, Quaternion.Euler(pitch, yaw, 0), 1 - Mathf.Exp(-4 * Time.deltaTime));
            }
        }
    }
}
