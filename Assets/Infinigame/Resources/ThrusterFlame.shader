Shader "Infinigame/ThrusterFlame"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float3 p:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 clip:SV_POSITION; float3 p:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V Vert(A input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                V o;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.p = input.p;
                float t = input.p.y * 0.5 + 0.5;
                float ripple = sin(8 * input.p.x + 14 * t - _Time.y * 18) * sin(9 * input.p.z - _Time.y * 7);
                input.p.xz *= (1.45 - t) * (1 + 0.12 * t * ripple);
                o.clip = TransformObjectToHClip(input.p);
                return o;
            }
            half4 Frag(V i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float t = saturate(i.p.y * 0.5 + 0.5);
                float filaments = 0.5 + 0.5 * sin(12 * i.p.x + 17 * i.p.z - t * 24 + _Time.y * 18);
                half3 colour = lerp(half3(0.67,0.93,1), half3(0.08,0.54,0.91), smoothstep(0.08,0.45,t));
                colour = lerp(colour, half3(0.77,0.29,0.13), smoothstep(0.48,0.95,t) * filaments);
                return half4(colour, (1 - t*t) * lerp(0.35,0.94,filaments));
            }
            ENDHLSL
        }
    }
}
