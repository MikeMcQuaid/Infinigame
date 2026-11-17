using Infinigame.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Infinigame
{
    [RequireComponent(typeof(CanvasRenderer))]
    public class WeaponGlyph : Graphic
    {
        int shape = -1;
        public void Show(Weapon weapon)
        {
            int next = weapon.melee ? 0 : weapon.homing > 0 || weapon.blast >= Combat.Nuclear ? 1 : weapon.beam ? 2 : weapon.rail ? 3 : weapon.sweep ? 4 : 5;
            if (shape == next) return;
            shape = next; SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Vector2[] outline = shape switch
            {
                0 => new[] { new Vector2(12, 5), new Vector2(12, 24), new Vector2(5, 32), new Vector2(10, 40), new Vector2(20, 30), new Vector2(20, 46), new Vector2(28, 46), new Vector2(28, 35), new Vector2(36, 44), new Vector2(44, 40), new Vector2(51, 36), new Vector2(55, 22), new Vector2(46, 5) },
                1 => new[] { new Vector2(10, 15), new Vector2(30, 21), new Vector2(115, 21), new Vector2(140, 30), new Vector2(115, 39), new Vector2(30, 39), new Vector2(10, 45), new Vector2(20, 30) },
                _ => new[] { new Vector2(5, 15), new Vector2(5, 40), new Vector2(45, 40), new Vector2(60, 35), new Vector2(105, 35), new Vector2(105, 25), new Vector2(60, 25), new Vector2(40, 15), new Vector2(30, 15), new Vector2(25, 4), new Vector2(12, 4), new Vector2(15, 15) },
            };
            void Line(Vector2 a, Vector2 b)
            {
                Vector2 normal = new Vector2(b.y - a.y, a.x - b.x).normalized * 1.4f;
                int i = mesh.currentVertCount;
                mesh.AddVert(a - normal, color, Vector2.zero); mesh.AddVert(a + normal, color, Vector2.zero);
                mesh.AddVert(b + normal, color, Vector2.zero); mesh.AddVert(b - normal, color, Vector2.zero);
                mesh.AddTriangle(i, i + 1, i + 2); mesh.AddTriangle(i, i + 2, i + 3);
            }
            for (int i = 0; i < outline.Length; i++) Line(outline[i], outline[(i + 1) % outline.Length]);
            if (shape == 2 || shape == 3 || shape == 5)
                for (int i = 0; i < (shape == 2 ? 2 : 1); i++) Line(new Vector2(112, 28 + i * 6), new Vector2(shape == 3 ? 175 : 156, 28 + i * 6));
            if (shape == 4) for (int i = -2; i <= 2; i++) Line(new Vector2(112, 30), new Vector2(160, 30 + i * 10));
        }
    }
}
