// A shell round a planet that glows with sunlight scattered along each view ray: blue sky by day,
// warm at the terminator, clear to the stars at night, and a thin halo from space.
Shader "Infinigame/Atmosphere"
{
    Properties
    {
        _SkyColour ("Sky", Color) = (0.25, 0.5, 1, 1)
        _DuskColour ("Dusk", Color) = (1, 0.45, 0.2, 1)
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
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _SkyColour;
                half4 _DuskColour;
                float _PlanetRadius;
                float _AtmosphereRadius;
                float _Density;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                float3 centre = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
                float3 origin = _WorldSpaceCameraPos - centre;
                float3 ray = normalize(input.positionWS - _WorldSpaceCameraPos);

                // the stretch of the ray inside the shell, cut short where it meets the planet
                float b = dot(origin, ray);
                float shell = b * b - dot(origin, origin) + _AtmosphereRadius * _AtmosphereRadius;
                if (shell < 0) return 0;
                float enter = max(-b - sqrt(shell), 0);
                float leave = -b + sqrt(shell);
                float ground = b * b - dot(origin, origin) + _PlanetRadius * _PlanetRadius;
                if (ground > 0 && -b - sqrt(ground) > 0) leave = min(leave, -b - sqrt(ground));

                const int steps = 8;
                float scaleHeight = (_AtmosphereRadius - _PlanetRadius) * 0.25;
                float stepLength = (leave - enter) / steps;
                float3 sun = normalize(_MainLightPosition.xyz);
                float3 light = 0;
                float depth = 0;
                for (int i = 0; i < steps; i++)
                {
                    float3 p = origin + ray * (enter + (i + 0.5) * stepLength);
                    float radius = length(p);
                    float facing = dot(p / radius, sun);
                    // air thins with height; the planet's shadow fades it out past the terminator
                    float lit = exp(-max(radius - _PlanetRadius, 0) / scaleHeight) * stepLength * smoothstep(-0.25, 0.15, facing);
                    light += lit * lerp(_DuskColour.rgb, _SkyColour.rgb, smoothstep(-0.1, 0.5, facing));
                    depth += lit;
                }
                float alpha = 1 - exp(-depth * _Density / scaleHeight);
                return half4(light / max(depth, 1e-5) * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
