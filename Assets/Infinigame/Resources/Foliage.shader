// Needles and blades of grass: cut out of a texture's alpha, seen from both sides and lit as thin
// living things are. Sunlight wraps round them and glows through from behind, the inside of a
// crown and the foot of a tuft sit in the plant's own shade, and every plant takes the lushness or
// dryness of the ground it stands on, then differs a little from the next.
// The texture's v runs from the inside of the plant (a bough at the trunk, a blade at its root) outwards.
Shader "Infinigame/Foliage"
{
    Properties
    {
        _BaseMap ("Colour, cut out by alpha", 2D) = "white" {}
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.3
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half _Cutoff;
        CBUFFER_END
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        // The texture, with its alpha turned into how much of the pixel the plant covers: an edge
        // one pixel wide however far off it is, so that distance thins nothing away.
        half4 Cut(float2 uv)
        {
            half4 texel = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
            texel.a = saturate((texel.a - _Cutoff) / max(fwidth(texel.a), 0.0001) + 0.5);
            clip(texel.a - 0.2);
            return texel;
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForwardOnly" }
            // with several samples to a pixel, a part-covered pixel is part drawn
            AlphaToMask On

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            // instanced plants have no reflection probe of their own: these find them the scene's
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Noise.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                // the plant's own tint, and the fog factor
                half4 tintAndFog : TEXCOORD3;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                // The same broad patches of lush and dry, pale and dark, that the Terrain shader paints on
                // the ground, read where the plant stands, and then a number of the plant's own.
                float3 foot = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
                half broad = Noise(foot * 0.021 + 3.1), patchy = Noise(foot * 0.13 + 11.7);
                output.tintAndFog.rgb = lerp(half3(0.82, 1.0, 0.8), half3(1.3, 1.1, 0.72), broad) * (0.84 + 0.32 * (0.6 * broad + 0.4 * patchy))
                    * (0.85 + 0.3 * frac(sin(dot(foot, float3(12.9898, 78.233, 37.719))) * 43758.5453));
                output.tintAndFog.a = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                half4 texel = Cut(input.uv);
                half3 albedo = texel.rgb * input.tintAndFog.rgb;

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalize(input.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                half4 shadowMask = half4(1, 1, 1, 1);
                // the plant's own shade deepens inwards
                half open = lerp(0.45, 1.0, input.uv.y);
                AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(inputData.normalizedScreenSpaceUV, open);

                Light sun = GetMainLight(inputData, shadowMask, aoFactor);
                // light wraps round something this thin, and comes through it, yellowed, when the sun is behind
                half3 lit = saturate(dot(inputData.normalWS, sun.direction) * 0.6 + 0.4)
                    + half3(1.0, 0.95, 0.35) * pow(saturate(dot(-inputData.viewDirectionWS, sun.direction)), 4.0);
                half3 light = sun.color * (sun.shadowAttenuation * lerp(0.7, 1.0, input.uv.y)) * lit;

                #if defined(_ADDITIONAL_LIGHTS)
                uint pixelLightCount = GetAdditionalLightsCount();
                #if USE_CLUSTER_LIGHT_LOOP
                [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                {
                    Light lamp = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
                    light += lamp.color * (lamp.distanceAttenuation * lamp.shadowAttenuation * saturate(dot(inputData.normalWS, lamp.direction) * 0.6 + 0.4));
                }
                #endif
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light lamp = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
                    light += lamp.color * (lamp.distanceAttenuation * lamp.shadowAttenuation * saturate(dot(inputData.normalWS, lamp.direction) * 0.6 + 0.4));
                LIGHT_LOOP_END
                #endif

                // Skylight and the sky's sheen are taken as every other surface takes them, which is what
                // there is to see a meadow by at night, dimmed by the plant's own shade alone: the
                // screen's reckoning of how hemmed in each blade is by the rest would leave it black.
                BRDFData brdf;
                half opaque = 1.0;
                InitializeBRDFData(albedo, 0.0, 0.0, 0.05, opaque, brdf);
                half3 colour = albedo * light + GlobalIllumination(brdf, brdf, 0.0, SampleSH(inputData.normalWS), open, inputData.positionWS, inputData.normalWS, inputData.viewDirectionWS, inputData.normalizedScreenSpaceUV);
                return half4(MixFog(colour, InitializeInputDataFog(float4(input.positionWS, 1.0), input.tintAndFog.a)), texel.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, TransformObjectToWorldNormal(input.normalOS), _LightDirection)));
                output.uv = input.uv;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                Cut(input.uv);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                Cut(input.uv);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                Cut(input.uv);
                return half4(normalize(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }
}
