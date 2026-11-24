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
            struct V { float4 pos:SV_POSITION; float3 world:TEXCOORD0; float3 normal:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            V Vertex(Attributes input)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos=TransformObjectToHClip(input.p.xyz); o.world=TransformObjectToWorld(input.p.xyz); o.normal=TransformObjectToWorldNormal(input.n); return o;
            }
            half4 Fragment(V i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float rim=pow(saturate(1-abs(dot(normalize(i.normal),GetWorldSpaceNormalizeViewDir(i.world)))),3);
                float bands = smoothstep(0.75, 0.96, cos(i.world.y * 0.7));
                half3 tint = lerp(half3(0.62, 0.17, 0.78), half3(0.09, 0.69, 0.66), _FootOpen);
                return half4(tint, lerp(0.15, 0.032, _FootOpen) + bands * 0.12 + rim * 0.26 + _Hit * 0.32);
            }
            ENDHLSL
        }
    }
}
