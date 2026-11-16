using Infinigame.Core;
using Unity.Mathematics;
using UnityEngine;

namespace Infinigame
{
    // A few articulated joints keep the generated bodies readable in motion.
    public class MachineMotion : MonoBehaviour
    {
        public bool quadruped, seated, working;
        public float speed, strike, recoil;
        public int weapon;
        public Transform look, planet;
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
            phase += speed * Time.deltaTime * (quadruped ? 4 : 2.2f);
            float stride = Mathf.Clamp01(speed / (quadruped ? 1 : 5));
            torso.localPosition = torsoHome + Vector3.up * (seated ? -0.2f : Mathf.Cos(phase * 2) * 0.025f * stride);
            for (int i = 0; i < legs.Length; i++)
            {
                float beat = Mathf.Sin(phase + (quadruped ? i == 0 || i == 3 ? 0 : Mathf.PI : i * Mathf.PI));
                legs[i].localRotation = Quaternion.Euler(seated ? i < 2 ? -70 : 70 : beat * 28 * stride, 0, 0);
                knees[i].localRotation = Quaternion.Euler(seated ? 100 : -Mathf.Max(0, -beat) * 42 * stride, 0, 0);
            }
            if (arms != null)
                for (int i = 0; i < arms.Length; i++)
                {
                    arms[i].localRotation = Quaternion.Euler(-Mathf.Sin(phase + i * Mathf.PI) * 18 * stride, 0, i == 0 ? -7 : 7);
                    elbows[i].localRotation = Quaternion.Euler(-12 - 15 * stride, 0, 0);
                }
            if (!quadruped && arms != null)
            {
                if (weapon > 0)
                {
                    for (int i = 0; i < arms.Length; i++)
                    {
                        float side = i == 0 ? -1 : 1;
                        arms[i].localRotation = Quaternion.Euler(weapon == 1 ? -75 : weapon == 3 ? -82 : weapon == 4 ? -35 : 25, weapon == 4 ? side * 55 : 0, side * (weapon == 3 ? 30 : weapon == 4 ? 65 : 10));
                        elbows[i].localRotation = Quaternion.Euler(weapon == 1 ? -15 : weapon == 3 ? -5 : -45, 0, weapon == 3 ? side * 70 : 0);
                    }
                }
                torso.localRotation = Quaternion.Euler(recoil * 16, strike * 18, recoil * 9);
                arms[1].localRotation *= Quaternion.Euler(-strike * 85, 0, -strike * 12);
                elbows[1].localRotation *= Quaternion.Euler(strike * 35, 0, 0);
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
