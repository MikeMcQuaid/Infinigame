// A shell round a planet that glows with sunlight scattered along each view ray: blue sky by day,
// warm at the terminator, clear to the stars at night, and a thin halo from space.
Shader "Infinigame/Atmosphere"
{
    Properties
    {
        _SkyColour ("Sky", Color) = (0.25, 0.5, 1, 1)
        _DuskColour ("Dusk", Color) = (1, 0.45, 0.2, 1)
        _CloudTop ("Opaque cloud deck", Float) = 0
        _PlanetRadius ("Planet Radius", Float) = 1000
        _AtmosphereRadius ("Atmosphere Radius", Float) = 1250
        _Density ("Density", Float) = 2.2
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Atmosphere"
            Tags { "LightMode" = "UniversalForward" }
            // back faces only, so the shell draws whether the camera is inside or outside it
            Cull Front
            ZWrite Off
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma multi_compile_instancing
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Noise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _SkyColour;
                half4 _DuskColour;
                float _PlanetRadius, _CloudTop;
                float _AtmosphereRadius;
                float _Density;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vertex(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 centre = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
                float3 origin = _WorldSpaceCameraPos - centre;
                float3 ray = normalize(input.positionWS - _WorldSpaceCameraPos);

                // The stretch of the ray inside the shell. It is not cut short where it would meet the
                // planet: the ground and sea are drawn over this wherever they stand, and a ray that
                // slips just under the skyline between their facets should see the thick air of the
                // horizon, not a sliver of the thin air above.
                float b = dot(origin, ray);
                // Measure the perpendicular miss distance without subtracting two orbital distances squared.
                float3 miss = cross(origin, ray);
                // At orbital distances the back of the shell can share the cloud tops' depth sample.
                if (_CloudTop > 0 && b < 0 && dot(origin, origin) > _AtmosphereRadius * _AtmosphereRadius
                    && dot(miss, miss) < 0.994 * _PlanetRadius * _PlanetRadius) discard;
                float shell = _AtmosphereRadius * _AtmosphereRadius - dot(miss, miss);
                if (shell < 0) return 0;
                float enter = max(-b - sqrt(shell), 0);
                float leave = -b + sqrt(shell);

                const int steps = 8;
                // Thin enough at the top of the shell that its edge fades into space. These worlds are
                // small, so their air stands tall next to them: from far off it is drawn hugging the
                // ground more closely, which leaves the thin bright line a real world shows from space.
                float scaleHeight = (_AtmosphereRadius - _PlanetRadius) * 0.125 * lerp(1.0, 0.4, smoothstep(1.0, 2.2, length(origin) / _AtmosphereRadius));
                float stepLength = (leave - enter) / steps;
                float3 sun = SunDirection();
                float sunward = saturate(dot(ray, sun));
                float depth = 0, warm = 0;
                for (int i = 0; i < steps; i++)
                {
                    float3 p = origin + ray * (enter + (i + 0.5) * stepLength);
                    float radius = length(p);
                    float facing = dot(p / radius, sun);
                    // air thins with height; the planet's shadow fades it out past the terminator
                    float lit = exp(-max(radius - _PlanetRadius, 0) / scaleHeight) * stepLength * smoothstep(-0.25, 0.15, facing);
                    // the warm band hugs the terminator and is strongest looking towards the sun
                    warm += lit * (1 - smoothstep(-0.05, 0.25, facing)) * (0.35 + 0.65 * sunward);
                    depth += lit;
                }
                float thickness = depth * _Density / scaleHeight, low = warm / max(depth, 1e-5);
                float alpha = 1 - exp(-thickness);
                // Deep colour overhead paling to the horizon. The low sun's own colour lies along the
                // horizon, where its light has come furthest through the air; the sky above it deepens.
                float3 colour = lerp(Scattered(0.7 * thickness * (1 - 0.7 * low), _SkyColour.rgb), _DuskColour.rgb * alpha * 1.2, low * (1 - exp(-0.12 * thickness)));
                // a bright haze round the sun itself
                colour += (pow(sunward, 12) * 0.35 + pow(sunward, 400) * 2) * alpha;
                return half4(colour * (1 + Grain(input.positionCS.xy) * float3(0.012, 0.012, 0.024)), alpha);
            }
            ENDHLSL
        }
    }
}
