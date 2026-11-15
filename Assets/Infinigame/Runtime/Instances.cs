using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Infinigame
{
    // The copies of one mesh to draw this frame with GPU instancing. Each keeps its own array,
    // which is left alone from the moment it is drawn until the next frame.
    public class Instances
    {
        static readonly int Damage = Shader.PropertyToID("_Damage");

        Matrix4x4[] matrices = new Matrix4x4[64];
        // how battered each one is, for the Hull shader: kept only by a list whose every copy is added with it
        float[] battered, batch;
        MaterialPropertyBlock block;

        public int Count { get; private set; }

        public void Clear() => Count = 0;

        public void Add(Matrix4x4 one)
        {
            if (Count == matrices.Length) Array.Resize(ref matrices, Count * 2);
            matrices[Count++] = one;
        }

        // Adds one that has taken damage, from 0 for whole to 1 for nearly destroyed. The Hull shader
        // eats holes out of it to match, in a pattern picked by the seed so that no two break up alike.
        public void Add(Matrix4x4 one, float damage, uint seed = 0)
        {
            Add(one);
            if (battered == null || battered.Length < matrices.Length) Array.Resize(ref battered, matrices.Length);
            // one number for the shader: the pattern is its whole part and the damage its fraction
            battered[Count - 1] = seed % 16 + math.clamp(damage, 0f, 0.999f);
        }

        // Adds many at once, each drawn at this share of its size. Scaling a thing's axes shrinks
        // it towards its own origin, which for a plant is its foot.
        public void Add(Matrix4x4[] many, float size)
        {
            if (Count + many.Length > matrices.Length) Array.Resize(ref matrices, math.max(matrices.Length * 2, Count + many.Length));
            Array.Copy(many, 0, matrices, Count, many.Length);
            for (int i = Count; size < 1f && i < Count + many.Length; i++) matrices[i] *= Matrix4x4.Scale(Vector3.one * size);
            Count += many.Length;
        }

        public void Draw(Mesh mesh, Material material, bool shadows, Bounds bounds)
        {
            var parameters = new RenderParams(material) { worldBounds = bounds, shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off, receiveShadows = true };
            if (battered != null) parameters.matProps = block ??= new MaterialPropertyBlock();
            for (int start = 0; start < Count; start += 1023)
            {
                int count = math.min(1023, Count - start);
                if (battered != null)
                {
                    // a block's array keeps the length it is first given, so it is always given a full one
                    Array.Copy(battered, start, batch ??= new float[1023], 0, count);
                    block.SetFloatArray(Damage, batch);
                }
                Graphics.RenderMeshInstanced(parameters, mesh, 0, matrices, count, start);
            }
        }
    }
}
