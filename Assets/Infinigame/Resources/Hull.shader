// The armour of a craft that can be shot to pieces. Each one drawn carries how battered it is
// (see Instances.Add): as that rises, ragged holes open in it and spread, their edges glowing
// with heat and the metal round them scorched black, and through them shows the inside, on fire.
// Given a glow of its own it is the craft's lights instead, which go dark where they are scorched
// and are eaten by the same holes.
Shader "Infinigame/Hull"
{
    Properties
    {
        [MainColor] _BaseColor ("Colour", Color) = (0.36, 0.3, 0.28, 1)
        _Metallic ("Metallic", Range(0, 1)) = 0.3
        _Smoothness ("Smoothness", Range(0, 1)) = 0.5
        [HDR] _Glow ("Glow of its own", Color) = (0, 0, 0, 1)
        _Size ("Size of the craft in metres, which its holes are sized by", Float) = 10
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }
        // the inside shows through the holes
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Noise.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _Glow;
            half _Metallic;
            half _Smoothness;
            float _Size;
        CBUFFER_END

        UNITY_INSTANCING_BUFFER_START(Hulls)
            // how battered it is from 0 to 1, with the number of its pattern of holes added as a whole number
            UNITY_DEFINE_INSTANCED_PROP(float, _Damage)
        UNITY_INSTANCING_BUFFER_END(Hulls)

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        // A point of the hull in its own frame, and with it how battered the hull is.
        float4 Place(Attributes input)
        {
            return float4(input.positionOS.xyz, UNITY_ACCESS_INSTANCED_PROP(Hulls, _Damage));
        }

        // How far past the ragged edge of a hole a point of the hull is, in the lumpy field the
        // holes are cut from: above nothing it is gone. A whole hull has no holes, and one about to
        // blow up has lost about half of itself.
        float Breach(float4 place)
        {
            float3 p = place.xyz * (3.2 / _Size) + floor(place.w) * 7.31;
            float worn = frac(place.w);
            return 0.56 * Noise(p) + 0.28 * Noise(p * 2.7 + 4.1) + 0.16 * Noise(p * 8.3 + 9.7) - 0.5 - 0.5 * pow(1.0 - worn, 2.5);
        }

        // How much of the pixel the hull still covers: an edge a pixel wide however far off it is.
        float Cover(float breach)
        {
            float cover = saturate(0.5 - breach / max(fwidth(breach), 1e-5));
            clip(cover - 0.2);
            return cover;
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
            // instanced craft have no reflection probe of their own: these find them the scene's
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Fire.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 place : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                output.place = Place(input);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Fragment(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float breach = Breach(input.place), cover = Cover(breach), worn = frac(input.place.w);
                bool outside = IS_FRONT_VFACE(face, true, false);
                // scorched black for a way round each hole, and glowing at its very edge. From too far
                // off to make the edge out it is drawn wider and dimmer, so that it never glitters.
                float scorch = worn > 0.0 ? smoothstep(-0.16, -0.02, breach) : 0.0;
                float rim = max(0.05, 2.0 * fwidth(breach)), edge = worn > 0.0 ? saturate(1.0 + breach / rim) : 0.0;
                // what smoulders comes and goes
                float ember = Noise(input.place.xyz * (9.0 / _Size) + _Time.y * 0.5);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalize(input.normalWS) * (outside ? 1.0 : -1.0);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                // the inside is all soot
                float soot = outside ? 0.94 * scorch : 0.94;
                surface.albedo = lerp(_BaseColor.rgb, half3(0.02, 0.018, 0.016), soot);
                surface.metallic = _Metallic * (1.0 - soot);
                surface.smoothness = _Smoothness * (1.0 - 0.85 * soot);
                surface.occlusion = 1.0;
                surface.alpha = 1.0;
                surface.emission = _Glow.rgb * (1.0 - scorch) + Fire(edge * (0.45 + 0.3 * ember)) * (0.05 / rim)
                    + (outside ? 0.0 : 1.0) * Fire(worn * (0.2 + 0.4 * ember));
                half4 colour = UniversalFragmentPBR(inputData, surface);
                return half4(MixFog(colour.rgb, inputData.fogCoord), cover);
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
                float4 place : TEXCOORD0;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                output.place = Place(input);
                output.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(TransformObjectToWorld(input.positionOS.xyz), TransformObjectToWorldNormal(input.normalOS), _LightDirection)));
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                Cover(Breach(input.place));
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
                float4 place : TEXCOORD0;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                output.place = Place(input);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                Cover(Breach(input.place));
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
                float4 place : TEXCOORD1;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                output.place = Place(input);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Fragment(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                Cover(Breach(input.place));
                return half4(normalize(input.normalWS) * IS_FRONT_VFACE(face, 1.0, -1.0), 0.0);
            }
            ENDHLSL
        }
    }
}
