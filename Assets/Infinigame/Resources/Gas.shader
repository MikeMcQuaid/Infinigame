// The cloud tops of a gas giant: winds shear the cloud into bands by latitude, turbulence curls
// their edges and the boldest giants carry one great oval storm.
Shader "Infinigame/Gas"
{
    Properties
    {
        _Colour ("Cloud tops", Color) = (0.9, 0.7, 0.5, 1)
        _Banding ("Banding", Range(0, 1)) = 1
        _Seed ("Seed", Vector) = (0, 0, 0, 0)
        _Sky ("Colour of the air, and how thick it is", Color) = (0.9, 0.8, 0.7, 0)
        _AirRadius ("Top of the air", Float) = 1250
        _Haze ("Haze", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForwardOnly" }

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Noise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Colour;
                half4 _Sky;
                float4 _Seed;
                float _Banding;
                float _AirRadius;
                float _Haze;
            CBUFFER_END

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
            };

            Varyings Vertex(float4 positionOS : POSITION)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.positionOS = positionOS.xyz;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.positionOS);
                float drift = _Time.y * 0.01;
                // stretched along the equator, so eddies are drawn out into streaks
                float3 p = direction * float3(1.6, 7.0, 1.6) + _Seed.xyz * 0.013;
                float curl = Layers(p + float3(drift, 0.0, -drift)) - 0.47;
                float wide = sin((direction.y * 7.0 + curl * 1.3) * 3.14159);
                float fine = sin((direction.y * 23.0 + curl * 4.0) * 3.14159);
                float tone = 0.5 + 0.5 * (0.65 * wide + 0.35 * fine);
                // pale zones and darker, redder belts
                half3 albedo = lerp(_Colour.rgb, lerp(_Colour.rgb * half3(0.6, 0.47, 0.42), lerp(_Colour.rgb, 1.0, 0.45), tone), 0.2 + 0.8 * _Banding);

                // the great storm rides one belt in the southern hemisphere, three times as wide as it is tall
                float angle = _Seed.x * 0.1;
                float3 eye = normalize(float3(cos(angle), -0.36, sin(angle)));
                float3 east = normalize(cross(float3(0, 1, 0), eye));
                float2 offset = float2(dot(direction - eye, east), (direction.y - eye.y) * 2.6) + curl * 0.04;
                float storm = (1.0 - smoothstep(0.1, 0.22, length(offset))) * step(0.9, _Banding);
                albedo = lerp(albedo, half3(0.72, 0.3, 0.2) * (0.8 + 0.4 * Noise(float3(offset * 30.0, drift * 4.0))), storm * 0.85);

                float3 normalWS = TransformObjectToWorldNormal(direction);
                float facing = dot(normalWS, SunDirection());
                // the deep air scatters light round the terminator and dims the limb
                float limb = pow(saturate(dot(normalWS, GetWorldSpaceNormalizeViewDir(input.positionWS))), 0.4);
                half3 colour = albedo * (_SunLight.rgb * (0.55 * smoothstep(-0.15, 0.9, facing) * limb + 0.012) + SecondStarlight(normalWS) * 0.55 * limb);
                colour = lerp(colour, MixFog(colour, InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor)), _Haze);
                return half4(Veil(colour, input.positionWS, _Sky, _AirRadius), 1.0);
            }
            ENDHLSL
        }
    }
}
