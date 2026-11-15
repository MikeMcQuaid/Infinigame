// Northern and southern lights: curtains of green rising to violet in a shell high above each pole,
// seen only on the night side. The view ray is marched through the shell, so the curtains have
// height and stand up from the horizon instead of being painted on a dome.
Shader "Infinigame/Aurora"
{
    Properties
    {
        _Inner ("Lower edge radius", Float) = 1300
        _Outer ("Upper edge radius", Float) = 1420
        _Strength ("Strength", Float) = 3
    }
    SubShader
    {
        // after the air, before the clouds, which hide it from below
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-7" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Aurora"
            Tags { "LightMode" = "UniversalForwardOnly" }
            // back faces without a depth test, so the shell draws from inside and out; the march stops
            // at whatever the depth buffer says is in the way
            Cull Front
            ZWrite Off
            ZTest Always
            Blend One One

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Noise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Inner;
                float _Outer;
                float _Strength;
            CBUFFER_END

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings Vertex(float4 positionOS : POSITION)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                // in the planet's own frame, so the lights ring its poles and turn with it
                float3 rayWS = normalize(input.positionWS - _WorldSpaceCameraPos);
                float3 origin = TransformWorldToObject(_WorldSpaceCameraPos);
                float3 ray = TransformWorldToObjectDir(rayWS);
                float3 sun = TransformWorldToObjectDir(SunDirection());

                // the stretch of the ray between the shell's two edges, on the near side of the planet
                float b = dot(origin, ray);
                float c = dot(origin, origin);
                float outer = b * b - c + _Outer * _Outer;
                if (outer < 0.0) return 0;
                float enter = max(-b - sqrt(outer), 0.0);
                float leave = -b + sqrt(outer);
                float inner = b * b - c + _Inner * _Inner;
                if (inner > 0.0)
                {
                    if (c < _Inner * _Inner) enter = -b + sqrt(inner);
                    else if (-b - sqrt(inner) > 0.0) leave = -b - sqrt(inner);
                }
                float scene = LinearEyeDepth(SampleSceneDepth(GetNormalizedScreenSpaceUV(input.positionCS)), _ZBufferParams) / dot(rayWS, -UNITY_MATRIX_V[2].xyz);
                leave = min(leave, scene);
                if (leave <= enter) return 0;

                const int steps = 20;
                float stepLength = (leave - enter) / steps;
                float jitter = InterleavedGradientNoise(input.positionCS.xy, 0);
                float time = _Time.y * 0.04;
                half3 glow = 0;
                for (int i = 0; i < steps; i++)
                {
                    float3 p = origin + ray * (enter + (i + jitter) * stepLength);
                    float radius = length(p);
                    float3 direction = p / radius;
                    // an oval round each pole, dark by day
                    float oval = smoothstep(0.78, 0.86, abs(direction.y)) * (1.0 - smoothstep(0.93, 0.985, abs(direction.y)))
                        * smoothstep(0.05, -0.25, dot(direction, sun));
                    if (oval < 0.001) continue;
                    float height = (radius - _Inner) / (_Outer - _Inner);
                    // curtains hang where a slow noise crosses its middle value: thin wandering lines
                    // on the map that the march draws upwards into sheets
                    float2 map = direction.xz * 20.0;
                    map += 1.5 * Noise(float3(map * 0.35, time * 0.5));
                    float sheet = pow(smoothstep(0.88, 1.0, 1.0 - abs(Noise(float3(map, time)) * 2.0 - 1.0)), 1.5);
                    float rays = 0.35 + 0.65 * Noise(float3(map * 12.0, time * 6.0));
                    float profile = smoothstep(0.0, 0.06, height) * exp(-3.2 * height);
                    glow += lerp(half3(0.1, 1.0, 0.4), half3(0.65, 0.15, 0.9), smoothstep(0.2, 0.85, height)) * (sheet * rays * profile * oval);
                }
                return half4(glow * (stepLength * _Strength / (_Outer - _Inner)), 1.0);
            }
            ENDHLSL
        }
    }
}
