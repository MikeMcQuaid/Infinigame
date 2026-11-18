// A ball of fire: an explosion, a muzzle flash or a bolt in flight. Added to whatever is behind
// it and brightest where it is seen face on, so that its edge fades away instead of ending.
Shader "Infinigame/Flash"
{
    Properties
    {
        [HDR] _Colour ("Colour", Color) = (8, 4, 1.2, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Flash"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Blend One One
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Colour;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                float facing = saturate(dot(normalize(input.normalWS), GetWorldSpaceNormalizeViewDir(input.positionWS)));
                float ripple = 0.5 + 0.5 * sin(facing * 24 - _Time.y * 16 + 2 * input.normalWS.y);
                float core = facing * facing * facing;
                return half4(_Colour.rgb * (0.72 * core + 0.2 * ripple * facing * (1 - facing))
                    + saturate(_Colour.rgb) * core * core * 0.26, 1);
            }
            ENDHLSL
        }
    }
}
