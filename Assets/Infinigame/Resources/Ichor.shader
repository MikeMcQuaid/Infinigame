Shader "Infinigame/Ichor"
{
    Properties { _Born ("Time spilled", Float) = 0 }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Born;
            CBUFFER_END
            struct Input { float4 pos : POSITION; float2 uv : TEXCOORD0; float3 normal : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Pixel { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; half grazing : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            Pixel Vert(Input v)
            {
                Pixel o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 world = TransformObjectToWorld(v.pos.xyz);
                o.pos = TransformWorldToHClip(world);
                o.uv = v.uv;
                o.grazing = 1 - abs(dot(TransformObjectToWorldNormal(v.normal), GetWorldSpaceNormalizeViewDir(world)));
                return o;
            }
            half4 Frag(Pixel i) : SV_Target
            {
                float age = max(0, _Time.y - _Born);
                float spread = 1 - 0.87 * exp(-1.15 * age);
                float r = length(i.uv) / spread;
                float ripple = sin(r * 25 - age * 5 + 2 * i.uv.x) * exp(-age * 0.6);
                float sheen = saturate(i.grazing * i.grazing + 0.13 * ripple + 0.12 * r);
                return half4(lerp(half3(0.16, 0.012, 0.25), half3(0.49, 0.24, 0.66), sheen),
                    (0.53 + 0.13 * sheen) * (1 - smoothstep(0.81, 1, r)));
            }
            ENDHLSL
        }
    }
}
