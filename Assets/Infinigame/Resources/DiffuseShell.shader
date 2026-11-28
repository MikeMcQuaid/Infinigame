// Bounded diffuse light for alien shell and wreckage in the Windows HDR player.
Shader "Infinigame/Diffuse shell"
{
    Properties
    {
        [MainColor] _BaseColor ("Tint", Color) = (1,1,1,1)
        [MainTexture] _BaseMap ("Chitin", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END
            struct Input { float3 pos : POSITION; half3 normal : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Pixel { float4 pos : SV_POSITION; half3 normal : TEXCOORD0; float2 uv : TEXCOORD1; float3 world : TEXCOORD2; };
            Pixel Vert(Input v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Pixel o;
                o.world = TransformObjectToWorld(v.pos);
                o.pos = TransformWorldToHClip(o.world);
                o.normal = TransformObjectToWorldNormal(v.normal);
                o.uv = v.uv;
                return o;
            }
            half4 Frag(Pixel i) : SV_Target
            {
                half3 normal = SafeNormalize(i.normal);
                Light sun = GetMainLight(TransformWorldToShadowCoord(i.world));
                half3 light = max(0.12h, SampleSH(normal)) + sun.color * saturate(dot(normal, sun.direction)) * sun.shadowAttenuation;
                half3 shell = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                return half4(saturate(shell * min(light, 1.2h)), 1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
