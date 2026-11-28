// Place stars on the six faces of the inertial sky. Filter each point to the angular size of a pixel.
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
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4x4 _StarRotation;
                float _Brightness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionCS.z = UNITY_RAW_FAR_CLIP_VALUE * output.positionCS.w;
                output.direction = mul((float3x3)_StarRotation, input.positionOS.xyz);
                return output;
            }

            float3 Hash(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx);
            }

            half3 Layer(float2 uv, float face, float cells, float rarity)
            {
                float2 p = uv * cells;
                float3 random = Hash(float3(floor(p), face));
                float2 offset = frac(p) - 0.5 - (random.xy - 0.5) * 0.50;
                float radius = lerp(0.035, 0.108, random.x);
                float width = max(radius, min(0.23, max(fwidth(p.x), fwidth(p.y))));
                float star = (1 - smoothstep(0.0, width, length(offset))) * radius / width;
                return star * step(rarity, random.z) * lerp(half3(1, 0.81, 0.66), half3(0.72, 0.87, 1), random.y);
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                float3 d = input.direction, axis = abs(d);
                float2 uv;
                float face;
                if (axis.x >= max(axis.y, axis.z)) { uv = d.yz / axis.x; face = d.x > 0 ? 0 : 1; }
                else if (axis.y >= axis.z) { uv = d.xz / axis.y; face = d.y > 0 ? 2 : 3; }
                else { uv = d.xy / axis.z; face = d.z > 0 ? 4 : 5; }
                return half4((Layer(uv, face, 29, 0.91) + 0.65 * Layer(uv, face + 6, 57, 0.96)) * _Brightness, 1);
            }
            ENDHLSL
        }
    }
}
