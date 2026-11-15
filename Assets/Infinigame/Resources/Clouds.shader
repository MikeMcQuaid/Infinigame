// A drifting cloud deck: a shell round the planet whose cover comes from layered noise, lit on
// the side of each cloud that faces the sun, shaded on the other and lost in haze far off.
Shader "Infinigame/Clouds"
{
    Properties
    {
        _Cover ("Clear sky fraction", Range(0, 1)) = 0.52
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Stormy ("Gathers into storms", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-5" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Clouds"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Noise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                float _Cover;
                float _Stormy;
            CBUFFER_END
            // Storm centres in the planet's frame, with their radius in radians in w, and whether the
            // machine has detail to spare. Set by SolarSystem.
            float4 _Storms[4];
            float _Fine;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.positionOS = input.positionOS.xyz;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            // How much cloud there is in a direction from the planet's centre: all of it in x, and only
            // its broad shapes in y. The whole deck drifts slowly round the planet's axis, and warping
            // the lookup by itself pulls the blobs into wisps.
            float2 Cloud(float3 direction, int octaves)
            {
                float s, c;
                sincos(_Time.y * 0.004, s, c);
                float3 p = float3(direction.x * c - direction.z * s, direction.y, direction.x * s + direction.z * c) * 5.0;
                p += Layers(p * 2.1, 3) * 0.7;
                float2 sum = 0.0;
                float amplitude = 0.5;
                for (int i = 0; i < octaves; i++)
                {
                    sum.x += amplitude * Noise(p);
                    if (i == 2) sum.y = sum.x;
                    p = p * 2.02 + 7.3;
                    amplitude *= 0.5;
                }
                return sum;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.positionOS);
                float2 here = Cloud(direction, 5);
                float density = here.x;
                // under a storm the cloud closes over and darkens, out to well beyond where its rain falls
                float storm = 0.0;
                for (int i = 0; i < 4; i++)
                    storm = max(storm, 1.0 - smoothstep(0.8, 1.8, acos(clamp(dot(direction, _Storms[i].xyz), -1.0, 1.0)) / max(_Storms[i].w, 0.001)));
                storm *= _Stormy;
                float clear = _Cover - 0.4 * storm;
                float cover = smoothstep(clear, clear + 0.16, density);

                float3 normalWS = TransformObjectToWorldNormal(direction);
                float3 sun = SunDirection();
                float facing = dot(normalWS, sun);
                // A cloud is bright where its sunward side thins and grey where more cloud stands between
                // it and the sun: one more reading of its broad shapes, a little way towards the sun, tells
                // which. The lower the sun, the further that way and the stronger the shading. The
                // smallest machines go without.
                float sunward = 0.0;
                if (_Fine > 0.5) sunward = Cloud(normalize(direction + TransformWorldToObjectDir(sun - normalWS * facing, false) * 0.025), 3).y - here.y;
                // low sun reddens the cloud it reaches; the night side only catches the ambient light
                half3 sunlight = _SunLight.rgb * lerp(half3(1.0, 0.45, 0.2), 1.0, smoothstep(0.0, 0.35, facing)) * saturate(facing * 2.5 + 0.3);
                half3 colour = (sunlight * (1.0 - clamp(sunward * 7.0, -0.25, 0.55)) + SampleSH(normalWS)) * _Tint.rgb
                    * lerp(1.0, 0.6, smoothstep(clear + 0.1, clear + 0.4, density)) * lerp(1.0, 0.5, storm);
                colour = MixFog(colour, InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor));
                // fade out close to the camera so flying through is a soft whiteout, not a wall
                float fade = saturate((distance(_WorldSpaceCameraPos, input.positionWS) - 4.0) / 80.0);
                return half4(colour, cover * 0.9 * fade);
            }
            ENDHLSL
        }
    }
}
