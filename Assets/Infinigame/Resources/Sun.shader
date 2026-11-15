// The sun's disc: added to whatever is behind it, so it shines through the sky and clouds, and
// untouched by haze. Brighter than white, which is what makes it bloom.
Shader "Infinigame/Sun"
{
    Properties
    {
        [HDR] _Colour ("Colour", Color) = (8, 6, 4, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Sun"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Blend One One
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Colour;
            CBUFFER_END

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings Vertex(float4 positionOS : POSITION, float3 normalOS : NORMAL)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(normalOS);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                // a little darker towards the limb, as real stars are
                float facing = saturate(dot(normalize(input.normalWS), GetWorldSpaceNormalizeViewDir(input.positionWS)));
                return half4(_Colour.rgb * (0.55 + 0.45 * sqrt(facing)), 1.0);
            }
            ENDHLSL
        }
    }
}
