using UnityEngine;
using UnityEngine.UI;

namespace Infinigame
{
    [RequireComponent(typeof(CanvasRenderer))]
    public class WeaponGlyph : Graphic
    {
        int shape = -1;
        Vector3[] vertices;
        int[] indices;

        public void Show(int slot)
        {
            if (shape == slot) return;
            shape = slot;
            var mark = Models.Parts("Visor weapon marks")[shape];
            vertices = mark.vertices;
            indices = mark.triangles;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (vertices == null) return;
            foreach (var vertex in vertices) mesh.AddVert(vertex, color, Vector2.zero);
            for (int i = 0; i < indices.Length; i += 3) mesh.AddTriangle(indices[i], indices[i + 1], indices[i + 2]);
        }
    }
}
