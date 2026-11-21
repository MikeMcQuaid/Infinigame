using UnityEngine;

namespace Infinigame
{
    [PreferBinarySerialization]
    public class ModelParts : ScriptableObject
    {
        public Mesh[] meshes;
        public Vector3[] centres;
    }
}
