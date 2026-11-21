// The sea: swells of three sizes that slide across each other, clear at the very edge and
// deepening through turquoise to blue-black with the water under the view ray, the sky mirrored
// at grazing angles, a path of glitter under the sun and a lace of foam along the shore.
Shader "Infinigame/Water"
{
    Properties
    {
        _Shallow ("Shallow", Color) = (0.15, 0.6, 0.6, 1)
        _Deep ("Deep", Color) = (0.02, 0.1, 0.25, 1)
        _Sky ("Colour of the air, and how thick it is", Color) = (0.4, 0.6, 1, 1)
        _AirRadius ("Top of the air", Float) = 1250
        _Waves ("Waves", 2D) = "bump" {}
        _Foam ("Foam", 2D) = "black" {}
    }
    SubShader
    {
        // before the atmosphere and clouds, and writing depth, so they sort correctly against it
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-20" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Water"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Noise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Shallow;
                half4 _Deep;
                half4 _Sky;
                float _AirRadius;
            CBUFFER_END
            float4x4 _Burrows[8];
            int _BurrowCount;
            TEXTURE2D(_Waves); SAMPLER(sampler_Waves);
            TEXTURE2D(_Foam);

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

            // A texture in the plane of two axes, with its pattern turned a quarter round when asked.
            half4 Flat(TEXTURE2D(map), float2 uv, float2 dx, float2 dy, bool turned, bool slopes)
            {
                half4 texel = SAMPLE_TEXTURE2D_GRAD(map, sampler_Waves, turned ? uv.yx : uv, turned ? dx.yx : dx, turned ? dy.yx : dy);
                if (slopes) texel.rg = UnpackNormal(texel).xy * 0.5 + 0.5;
                return turned ? texel.grba : texel;
            }

            // A texture projected along each axis the sea faces, at a scale. Axes that contribute nothing
            // are skipped, which is why the gradients are passed in. With slopes, what comes back is the
            // waves' slope in the sea's own frame instead of a colour.
            float3 Projected(TEXTURE2D(map), float3 position, float scale, float3 dpdx, float3 dpdy, float3 weights, bool turned, bool slopes)
            {
                float3 sum = 0.0, dx = dpdx * scale, dy = dpdy * scale;
                if (weights.x > 0.01)
                {
                    half3 texel = Flat(map, position.zy, dx.zy, dy.zy, turned, slopes).rgb;
                    sum += weights.x * (slopes ? float3(0.0, texel.g, texel.r) * 2.0 - float3(0.0, 1.0, 1.0) : texel);
                }
                if (weights.y > 0.01)
                {
                    half3 texel = Flat(map, position.xz, dx.xz, dy.xz, turned, slopes).rgb;
                    sum += weights.y * (slopes ? float3(texel.r, 0.0, texel.g) * 2.0 - float3(1.0, 0.0, 1.0) : texel);
                }
                if (weights.z > 0.01)
                {
                    half3 texel = Flat(map, position.xy, dx.xy, dy.xy, turned, slopes).rgb;
                    sum += weights.z * (slopes ? float3(texel.r, texel.g, 0.0) * 2.0 - float3(1.0, 1.0, 0.0) : texel);
                }
                return sum;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                for (int i = 0; i < _BurrowCount; i++)
                {
                    float3 cave = mul(_Burrows[i], float4(input.positionOS, 1)).xyz;
                    if (abs(cave.x) < 8 && cave.z > -25 && cave.z < 42 && cave.y > -16 && cave.y < 12) discard;
                }
                float3 upOS = normalize(input.positionOS);
                float3 weights = pow(abs(upOS), 4.0);
                weights /= dot(weights, 1.0);
                float3 dpdx = ddx(input.positionOS), dpdy = ddy(input.positionOS);
                // each size runs its own way, the middle one across the others, so no pattern holds for long
                float3 drift = _Time.y * float3(0.05, 0.02, 0.035);
                float3 slope = Projected(_Waves, input.positionOS * 0.023 + drift * 0.3, 0.023, dpdx, dpdy, weights, false, true) * 1.4
                    + Projected(_Waves, input.positionOS * 0.091 - drift * 0.7, 0.091, dpdx, dpdy, weights, true, true)
                    + Projected(_Waves, input.positionOS * 0.37 + drift * 1.6, 0.37, dpdx, dpdy, weights, false, true) * 0.6;
                // from high above the waves are too small to see, and the sea a mirror
                slope *= saturate(1.6 - distance(input.positionWS, _WorldSpaceCameraPos) / 800.0);

                float3 upWS = TransformObjectToWorldNormal(upOS);
                float3 viewWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float3 normalWS = TransformObjectToWorldNormal(normalize(upOS + slope * 0.22));
                // Seen from underneath, the surface faces down. From above the water the only underside in
                // view is a sliver of the sea's far side showing over the horizon, which is left out.
                bool beneath = dot(upWS, viewWS) < 0.0;
                clip(beneath && length(TransformWorldToObject(_WorldSpaceCameraPos)) > length(input.positionOS) ? -1.0 : 1.0);
                normalWS *= beneath ? -1.0 : 1.0;

                // The length of water the view ray crosses before it reaches the seabed, counted as no
                // more than three times how deep the water is: a glancing eye would otherwise see the
                // sea turn from clear to black along a line where the bed first drops away.
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float depth = max(LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams) - input.positionCS.w, 0.0), steep = abs(dot(upWS, viewWS));
                // near enough how deep the water is, which is what colours it
                float deep = depth * steep;
                depth *= min(1.0, 3.0 * steep);

                Light sun = GetMainLight();
                float daylight = saturate(dot(upWS, sun.direction) * 4.0 + 0.2);
                half3 light = sun.color * saturate(dot(upWS, sun.direction)) + SampleSH(upWS);
                // clear where it laps the sand, then green, then blue as the red and the green are soaked up in turn
                half3 body = lerp(_Shallow.rgb, _Deep.rgb, 1.0 - exp(-deep * 0.1)) * light;
                float fresnel = 0.02 + 0.98 * pow(1.0 - saturate(dot(normalWS, viewWS)), 5.0);
                // the sky it mirrors is deep overhead and pale at the horizon, which is the haze's own colour
                half3 mirrored = _Sky.rgb * daylight;
                if (!beneath) mirrored = lerp(unity_FogColor.rgb, mirrored, saturate(dot(reflect(-viewWS, normalWS), upWS) * 1.5));
                half3 colour = lerp(body, mirrored, fresnel);
                // glitter: a hard glint from each facet that faces just right, in a broad soft path below the sun
                float glint = saturate(dot(normalWS, normalize(sun.direction + viewWS)));
                colour += sun.color * daylight * (5.0 * pow(glint, 400.0) + 0.25 * pow(glint, 40.0));

                // foam lies along the shore in lace, and breathes in and out with the swell
                half lace = Projected(_Foam, input.positionOS * 0.21 + slope * 0.05, 0.21, dpdx, dpdy, weights, false, false).r;
                float foam = (1.0 - smoothstep(0.0, 0.45 + 0.25 * sin(_Time.y * 0.7 + lace * 4.0), depth)) * saturate(0.35 + lace);
                colour = lerp(colour, light, foam * 0.85);
                colour = MixFog(colour, InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor));
                return half4(Veil(colour, input.positionWS, _Sky, _AirRadius), saturate(lerp(1.0 - exp(-depth * 0.5), 1.0, fresnel) + foam));
            }
            ENDHLSL
        }
    }
}
