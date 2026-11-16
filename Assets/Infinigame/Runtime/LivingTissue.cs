using UnityEngine;

namespace Infinigame
{
    // Cosmetic breathing only: the tunnel's collision remains fixed.
    public class LivingTissue : MonoBehaviour
    {
        Vector3 scale;
        void Start() => scale = transform.localScale;
        void Update() => transform.localScale = Vector3.Scale(scale, new Vector3(1 + Mathf.Sin(Time.time * 1.2f + transform.localPosition.z) * 0.045f, 1, 1));
    }
}
