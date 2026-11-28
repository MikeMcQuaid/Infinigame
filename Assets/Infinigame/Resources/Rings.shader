// A planet's rings from a distance, where their rocks blur into a sheet: thousands of ringlets with a
// dark division, lit on the side the sun is on, glowing dimly from behind, and cut across by the
// planet's shadow.
Shader "Infinigame/Rings"
{
    Properties
    {
        _Colour ("Colour", Color) = (0.85, 0.78, 0.65, 1)
        _Density ("Density", Range(0, 1)) = 0.9
        _PlanetRadius ("Planet radius", Float) = 1000
        _Seed ("Seed", Float) = 0
        _Profile ("How full, inner edge to outer", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-15" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Rings"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma multi_compile_instancing
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Noise.hlsl"

            TEXTURE2D(_Profile);
            SAMPLER(sampler_Profile);

            CBUFFER_START(UnityPerMaterial)
                half4 _Colour;
                float _Density;
                float _PlanetRadius;
                float _Seed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.uv;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float across = input.uv.x;
                float fine = Noise(float3(across * 160.0, _Seed, 0.5));
                // the same bands and division that decide where the rocks themselves are
                float broad = SAMPLE_TEXTURE2D(_Profile, sampler_Profile, float2(across, 0.5)).r;
                float density = broad * (0.55 + 0.45 * fine);
                // close to, the sheet thins to a haze of dust and the rocks in it take over
                density *= lerp(0.3, 1.0, smoothstep(150.0, 2500.0, distance(_WorldSpaceCameraPos, input.positionWS)));

                float3 centre = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
                float3 fromCentre = input.positionWS - centre;
                float3 sight = normalize(input.positionWS - _WorldSpaceCameraPos);
                float3 miss = cross(fromCentre, sight);
                if (dot(fromCentre, sight) > 0 && dot(centre - _WorldSpaceCameraPos, sight) > 0 && dot(miss, miss) < _PlanetRadius * _PlanetRadius) discard;
                float3 toSun = SunDirection();
                float behind = -dot(fromCentre, toSun);
                float lit = behind > 0.0 ? smoothstep(0.97, 1.03, length(fromCentre + toSun * behind) / _PlanetRadius) : 1.0;

                float3 normalWS = TransformObjectToWorldNormal(float3(0, 1, 0));
                float sunSide = dot(normalWS, toSun);
                float sameSide = sunSide * dot(normalWS, _WorldSpaceCameraPos - input.positionWS);
                float light = (0.3 + 0.7 * abs(sunSide)) * (sameSide > 0.0 ? 1.0 : 0.4) * (0.06 + 0.94 * lit);
                return half4(_Colour.rgb * (0.75 + 0.5 * broad) * _SunLight.rgb * 0.6 * light, density * _Density);
            }
            ENDHLSL
        }
    }
}
