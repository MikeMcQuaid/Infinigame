using UnityEngine;

namespace Infinigame
{
    public class IchorPool : MonoBehaviour
    {
        Vector3 size;
        float age;
        void Start() => size = transform.localScale;
        void Update()
        {
            age += Time.deltaTime;
            transform.localScale = Vector3.Scale(size, new Vector3(Mathf.Lerp(0.15f, 1, age / 5), 1, Mathf.Lerp(0.15f, 1, age / 5)));
            if (age >= 5) enabled = false;
        }
    }
}
