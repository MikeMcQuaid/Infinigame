// A falling raindrop drawn as a faint streak. Instanced: a storm is many hundreds of these.
Shader "Infinigame/Rain"
{
    Properties
    {
        _Colour ("Colour", Color) = (0.75, 0.82, 0.9, 0.38)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Rain"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Colour;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 Vertex(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return TransformObjectToHClip(input.positionOS.xyz);
            }

            half4 Fragment() : SV_Target
            {
                // rain is only as bright as the day around it
                return half4(_Colour.rgb * (_MainLightColor.rgb * 0.35 + unity_AmbientSky.rgb + 0.02), _Colour.a);
            }
            ENDHLSL
        }
    }
}
