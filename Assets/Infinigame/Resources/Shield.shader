Shader "Infinigame/Shield"
{
    Properties { _Hit ("Impact", Float) = 0  _FootOpen ("Ground passage", Float) = 0 }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float _Hit, _FootOpen;
            struct Attributes { float4 p : POSITION; float3 n : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos:SV_POSITION; float3 world:TEXCOORD0; float3 normal:TEXCOORD1; float3 field:TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            V Vertex(Attributes input)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos=TransformObjectToHClip(input.p.xyz); o.world=TransformObjectToWorld(input.p.xyz); o.normal=TransformObjectToWorldNormal(input.n);
                o.field = input.p.xyz * float3(length(TransformObjectToWorldDir(float3(1,0,0), false)),
                    length(TransformObjectToWorldDir(float3(0,1,0), false)), length(TransformObjectToWorldDir(float3(0,0,1), false)));
                return o;
            }
            float Stripe(float coordinate)
            {
                float footprint = max(fwidth(coordinate), 0.002);
                float edge = abs(frac(coordinate + 0.5) - 0.5);
                return (1 - smoothstep(0.025, 0.025 + footprint, edge)) * (1 - smoothstep(0.12, 0.4, footprint));
            }
            half4 Fragment(V i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float rim=pow(saturate(1-abs(dot(normalize(i.normal),GetWorldSpaceNormalizeViewDir(i.world)))),3);
                float diagonal = (i.field.x + i.field.z * 0.7) * 0.18;
                float lattice = max(Stripe(diagonal + i.field.y * 0.24), Stripe(diagonal - i.field.y * 0.24));
                float lanes = Stripe(i.field.y * 0.16);
                float pattern = lerp(lattice, lanes, _FootOpen);
                half3 tint = lerp(half3(0.62, 0.17, 0.78), half3(0.09, 0.69, 0.66), _FootOpen);
                return half4(tint, lerp(0.14, 0.035, _FootOpen) + pattern * 0.25 + rim * 0.22 + saturate(_Hit) * 0.18);
            }
            ENDHLSL
        }
    }
}
