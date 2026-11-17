using UnityEngine;

namespace Infinigame
{
    public static partial class Models
    {
        public static Transform RoomPanel(Vector3 half, bool door)
        {
            var root = new GameObject(door ? "Membrane" : "Living chamber").transform;
            var shell = new MeshKit();
            shell.Box(Vector3.zero, half * 2, Mathf.Min(1f, Mathf.Min(half.x, Mathf.Min(half.y, half.z)) * 0.3f));
            var ribs = new MeshKit();
            var panels = new MeshKit();
            bool along = half.x < half.z;
            float width = along ? half.z : half.x;
            if (half.y > 1)
                for (float at = -width + 1; at < width; at += 2.5f)
                {
                    Vector3 Point(float y, float bulge) => along ? new Vector3(half.x + bulge, y, at) : new Vector3(at, y, half.z + bulge);
                    foreach (float side in new[] { -1f, 1f })
                    {
                        Vector3 Flip(Vector3 p) => along ? new Vector3(p.x * side, p.y, p.z) : new Vector3(p.x, p.y, p.z * side);
                        Vector3 atPlate = Flip(Point(0, 0.035f));
                        Vector3 size = along ? new Vector3(0.12f, half.y * 1.65f, 2.05f) : new Vector3(2.05f, half.y * 1.65f, 0.12f);
                        panels.Box(atPlate, size, 0.055f);
                        for (int vent = 0; vent < 5; vent++)
                        {
                            Vector3 atVent = Flip(Point(-half.y * 0.55f + vent * 0.09f, 0.11f));
                            ribs.Box(atVent, along ? new Vector3(0.05f, 0.035f, 0.85f) : new Vector3(0.85f, 0.035f, 0.05f), 0.015f);
                        }
                        ribs.Tube(MeshKit.Path(20, Flip(Point(-half.y, 0)), Flip(Point(-half.y * 0.35f, 0.22f)), Flip(Point(half.y * 0.5f, 0.12f)), Flip(Point(half.y, 0))), 0.65f, 16, 0.2f);
                    }
                }
            Add(root, "Living membrane", shell, Shapes.Solid(Surface.Chitin, new Color(0.28f, 0.08f, 0.23f), true));
            Add(root, "Chitin scales", panels, Shapes.Solid(Surface.Chitin, new Color(0.38f, 0.12f, 0.3f), true));
            Add(root, "Grown ribs", ribs, Shapes.Solid(Surface.Chitin, new Color(0.45f, 0.055f, 0.28f), true));
            return root;
        }
    }
}
