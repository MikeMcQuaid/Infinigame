// A skybox of procedural stars: one possible star per cell of a 3D grid cut by the view direction.
// _StarRotation turns view directions back into the fixed frame of the stars.
Shader "Infinigame/Stars"
{
    Properties
    {
        _Brightness ("Brightness", Float) = 1.5
    }
    SubShader
    {
        Tags { "RenderType" = "Background" "Queue" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4x4 _StarRotation;
                float _Brightness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = mul((float3x3)_StarRotation, input.positionOS.xyz);
                return output;
            }

            float3 Hash(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx);
            }

            half3 Layer(float3 direction, float cells, float rarity)
            {
                float3 p = direction * cells;
                float3 random = Hash(floor(p));
                float3 offset = frac(p) - 0.5 - (random - 0.5) * 0.6;
                float star = smoothstep(0.12, 0.0, length(offset)) * step(rarity, random.x);
                // mostly white, some warm, some blue
                return star * lerp(half3(1, 0.8, 0.6), half3(0.7, 0.85, 1), random.y) * (0.4 + random.z);
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.direction);
                return half4((Layer(direction, 90, 0.86) + Layer(direction, 220, 0.9) * 0.6) * _Brightness, 1);
            }
            ENDHLSL
        }
    }
}
