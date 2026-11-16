Shader "Infinigame/Accretion"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct V { float4 position:SV_POSITION; float3 local:TEXCOORD0; };
            V Vertex(float4 position:POSITION)
            {
                V result; result.position = TransformObjectToHClip(position.xyz); result.local = position.xyz; return result;
            }
            half4 Fragment(V input):SV_Target
            {
                float radius = length(input.local.xz), angle = atan2(input.local.z, input.local.x);
                float band = saturate((radius - 1.04) * 12) * exp(-(radius - 1.04) * 1.5);
                float flow = 0.65 + 0.2 * sin(radius * 95 + sin(angle * 7 - _Time.y * 2) * 3)
                    + 0.15 * sin(radius * 170 - angle * 3 + _Time.y * 6);
                float hot = exp(-(radius - 1.1) * 3);
                half3 colour = lerp(half3(1.6, 0.15, 0.025), half3(4, 3.5, 2.4), saturate(hot));
                return half4(colour * (0.8 + 0.2 * sin(angle + _Time.y)), band * flow);
            }
            ENDHLSL
        }
    }
}
