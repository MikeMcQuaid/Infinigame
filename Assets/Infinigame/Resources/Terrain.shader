// Ground for every body: four painted layers (beach, lowland, cliff, summit) blended by the
// elevation and steepness written into the mesh UVs. Each layer is projected along the three axes
// so nothing stretches on cliffs, drawn fine underfoot and broad far off so it never blurs to one
// flat colour, and gives way to its neighbour along the relief of both, not along a soft line.
// Over them lies whatever blasts and volcanoes have left, which the mesh carries too: soot, rock
// that ran molten and glows until it has cooled, and pools of meltwater.
Shader "Infinigame/Terrain"
{
    Properties
    {
        _Albedo4 ("Rock that has run molten, with its relief in alpha", 2D) = "black" {}
        _Normal4 ("Molten rock normal", 2D) = "bump" {}
        _Clock ("Seconds on the clock that blasts are timed by", Float) = 0
        _Albedo0 ("Beach, with its relief in alpha", 2D) = "white" {}
        _Normal0 ("Beach normal", 2D) = "bump" {}
        _Albedo1 ("Lowland", 2D) = "white" {}
        _Normal1 ("Lowland normal", 2D) = "bump" {}
        _Albedo2 ("Cliff", 2D) = "white" {}
        _Normal2 ("Cliff normal", 2D) = "bump" {}
        _Albedo3 ("Summit", 2D) = "white" {}
        _Normal3 ("Summit normal", 2D) = "bump" {}
        _Bands ("Beach top, cliff start, summit start, sea level", Vector) = (0.2, 0.6, 0.8, 0.2)
        _Blend ("Band blend widths, then metres of height to one of elevation", Vector) = (0.01, 0.05, 0.05, 100)
        _Low ("Tint below sea level", Color) = (0.5, 0.6, 0.7, 1)
        _Tint ("Tint of everything but the summit layer", Color) = (1, 1, 1, 1)
        _Saturation ("Saturation", Float) = 1
        _Shore ("Has a sea: wet sand at its edge, silt on its bed, meadows that dry in patches", Float) = 0
        _Sky ("Colour of the air, and how thick it is", Color) = (0.25, 0.5, 1, 0)
        _AirRadius ("Top of the air", Float) = 1250
        _Haze ("How near: 1 lit by the scene, 0 lit as a far world", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _Bands;
            float4 _Blend;
            half4 _Low;
            half4 _Tint;
            half4 _Sky;
            float _Saturation;
            float _Shore;
            float _AirRadius;
            float _Haze, _Blight;
            float _Clock;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            // what blasts and volcanoes have left, as PatchMeshJob describes it
            half4 paint : COLOR;
            float2 heat : TEXCOORD1;
        };
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForwardOnly" }

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Noise.hlsl"

            TEXTURE2D(_Albedo0); TEXTURE2D(_Albedo1); TEXTURE2D(_Albedo2); TEXTURE2D(_Albedo3); TEXTURE2D(_Albedo4);
            TEXTURE2D(_Normal0); TEXTURE2D(_Normal1); TEXTURE2D(_Normal2); TEXTURE2D(_Normal3); TEXTURE2D(_Normal4);
            SAMPLER(sampler_Albedo0);

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                float3 normalOS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                half4 paint : TEXCOORD5;
                float2 heat : TEXCOORD6;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.positionOS = input.positionOS.xyz;
                output.normalOS = input.normalOS;
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                output.paint = input.paint;
                output.heat = input.heat;
                return output;
            }

            // One layer at one size: this share of its colour, of the height of its relief (in alpha)
            // and of its surface slope, projected along each axis the ground faces. Axes that
            // contribute nothing are skipped, which is most of them on any pixel; that is why the
            // texture gradients are passed in rather than taken inside the branches.
            half4 Projected(TEXTURE2D(albedoMap), TEXTURE2D(normalMap), float scale, float share, float3 position, float3 dpdx, float3 dpdy, float3 axes, inout float3 slope)
            {
                half4 colour = 0;
                float3 p = position * scale, dx = dpdx * scale, dy = dpdy * scale;
                if (axes.x > 0.01)
                {
                    float2 n = UnpackNormal(SAMPLE_TEXTURE2D_GRAD(normalMap, sampler_Albedo0, p.zy, dx.zy, dy.zy)).xy;
                    colour += axes.x * SAMPLE_TEXTURE2D_GRAD(albedoMap, sampler_Albedo0, p.zy, dx.zy, dy.zy);
                    slope += share * axes.x * float3(0.0, n.y, n.x);
                }
                if (axes.y > 0.01)
                {
                    float2 n = UnpackNormal(SAMPLE_TEXTURE2D_GRAD(normalMap, sampler_Albedo0, p.xz, dx.xz, dy.xz)).xy;
                    colour += axes.y * SAMPLE_TEXTURE2D_GRAD(albedoMap, sampler_Albedo0, p.xz, dx.xz, dy.xz);
                    slope += share * axes.y * float3(n.x, 0.0, n.y);
                }
                if (axes.z > 0.01)
                {
                    float2 n = UnpackNormal(SAMPLE_TEXTURE2D_GRAD(normalMap, sampler_Albedo0, p.xy, dx.xy, dy.xy)).xy;
                    colour += axes.z * SAMPLE_TEXTURE2D_GRAD(albedoMap, sampler_Albedo0, p.xy, dx.xy, dy.xy);
                    slope += share * axes.z * float3(n.x, n.y, 0.0);
                }
                return colour * share;
            }

            // A layer as it looks from here. Underfoot it is drawn at its true size. Far off, where
            // that grain would blur away and its repeating would show, the same texture is drawn
            // several times the size instead. A layer that is not here at all costs nothing.
            half4 Layer(TEXTURE2D(albedoMap), TEXTURE2D(normalMap), float weight, float2 scales, float far, float3 position, float3 dpdx, float3 dpdy, float3 axes, out float3 slope)
            {
                half4 colour = 0;
                slope = 0;
                if (weight < 0.004) return colour;
                if (far < 0.99) colour += Projected(albedoMap, normalMap, scales.x, 1.0 - far, position, dpdx, dpdy, axes, slope);
                if (far > 0.01) colour += Projected(albedoMap, normalMap, scales.y, far, position + 37.0, dpdx, dpdy, axes, slope);
                return colour;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                float3 normalOS = normalize(input.normalOS);
                float3 axes = pow(abs(normalOS), 8.0);
                axes /= dot(axes, 1.0);
                float3 dpdx = ddx(input.positionOS), dpdy = ddy(input.positionOS);
                float away = distance(input.positionWS, _WorldSpaceCameraPos), far = smoothstep(10.0, 60.0, away);

                // broad patches, lusher or drier, paler or darker, roughen the borders between layers
                // and keep any one of them from looking the same for a mile
                half broad = Noise(input.positionOS * 0.021 + 3.1), patchy = Noise(input.positionOS * 0.13 + 11.7);
                half mottle = 0.6 * broad + 0.4 * patchy;
                float elevation = input.uv.x + (mottle - 0.5) * _Blend.y;
                half steep = input.uv.y;
                float aboveSea = (input.uv.x - _Bands.w) * _Blend.w;
                // silt settles over the sea bed however steep it is, from a little below the waterline
                half silted = _Shore * (1.0 - smoothstep(-1.5, 0.0, aboveSea));

                // lowland everywhere, then cliffs where it is steep or high, sand by the sea, snow on top
                float4 weights = float4(0, 1, 0, 0);
                weights = lerp(weights, float4(0, 0, 1, 0), max(smoothstep(0.38, 0.62, steep + (mottle - 0.5) * 0.25), smoothstep(_Bands.y - _Blend.y, _Bands.y + _Blend.y, elevation)));
                weights = lerp(weights, float4(1, 0, 0, 0), (1.0 - smoothstep(_Bands.x - _Blend.x, _Bands.x + _Blend.x, input.uv.x)) * (1.0 - smoothstep(0.5, 0.8, steep) * (1.0 - silted)));
                weights = lerp(weights, float4(0, 0, 0, 1), smoothstep(_Bands.z - _Blend.z, _Bands.z + _Blend.z, elevation) * (1.0 - smoothstep(0.6, 0.9, steep)));

                float3 slope0, slope1, slope2, slope3;
                half4 beach = Layer(_Albedo0, _Normal0, weights.x, float2(0.62, 0.13), far, input.positionOS, dpdx, dpdy, axes, slope0);
                half4 lowland = Layer(_Albedo1, _Normal1, weights.y, float2(0.5, 0.093), far, input.positionOS, dpdx, dpdy, axes, slope1);
                half4 cliffs = Layer(_Albedo2, _Normal2, weights.z, float2(0.3, 0.061), far, input.positionOS, dpdx, dpdy, axes, slope2);
                half4 summit = Layer(_Albedo3, _Normal3, weights.w, float2(0.5, 0.11), far, input.positionOS, dpdx, dpdy, axes, slope3);
                // meadows are lush in some places and dry towards straw in others
                lowland.rgb *= lerp(1.0, lerp(half3(0.82, 1.0, 0.8), half3(1.3, 1.1, 0.72), broad), _Shore);

                // Where two layers meet, whichever stands higher in its own relief shows: sand lies in
                // the cracks of the rock and grass takes root in its hollows.
                float4 heights = (float4(beach.a, lowland.a, cliffs.a, summit.a) + weights) * step(0.004, weights);
                weights = max(heights - max(max(heights.x, heights.y), max(heights.z, heights.w)) + 0.15, 0.0);
                weights /= dot(weights, 1.0);
                half3 albedo = beach.rgb * weights.x + lowland.rgb * weights.y + cliffs.rgb * weights.z + summit.rgb * weights.w;
                float3 slope = slope0 * weights.x + slope1 * weights.y + slope2 * weights.z + slope3 * weights.w;
                half relief = dot(weights, float4(beach.a, lowland.a, cliffs.a, summit.a));

                albedo = lerp(dot(albedo, half3(0.3, 0.59, 0.11)).xxx, albedo, _Saturation);
                // stone takes the colour of its world; ice stays white
                albedo *= lerp(_Tint.rgb, 1.0, weights.w);
                albedo = lerp(albedo, half3(0.08, 0.015, 0.13), _Blight * smoothstep(0.15, 0.65, mottle));
                albedo *= (0.84 + 0.32 * mottle) * lerp(_Low.rgb, 1.0, saturate((input.uv.x - _Bands.w) / 0.03 + 1.0));
                // the sea wets its shore: darker there, and smooth enough to shine
                half wet = _Shore * (1.0 - smoothstep(0.05, 0.6, aboveSea + 0.3 * (patchy - 0.5))) * smoothstep(-2.0, -0.3, aboveSea);
                albedo *= 1.0 - 0.4 * wet;

                // What blasts and volcanoes have left. Most ground has none of it, and pays nothing.
                half3 glow = 0.0;
                half gloss = dot(weights, float4(0.25, 0.1, 0.2, 0.5)), pooled = 0.0;
                float3 upOS = normalize(input.positionOS);
                if (any(input.paint.rgb > 0.004))
                {
                    half fine = Noise(input.positionOS * 1.9 + 5.3), ash = 0.5 * patchy + 0.5 * fine, remote = smoothstep(80.0, 300.0, away);
                    // Soot: burnt black, with ash lying on it in patches. Near to, it gives out along a
                    // ragged edge. Far off, where the mesh has spread a small mark thin, it is a smudge.
                    half soot = lerp(smoothstep(0.3, 0.5, input.paint.r + 0.5 * (ash - 0.5)), saturate(1.2 * input.paint.r), remote);
                    albedo = lerp(albedo, (0.006 + 0.03 * fine * fine) * (0.5 + relief), 0.96 * soot);
                    gloss = lerp(gloss, 0.08, soot);

                    // The height field holds a level pool inside the crater. Flow is confined to its surface.
                    float age = max(0, _Clock - input.heat.x);
                    half lava = saturate(input.paint.a * input.paint.g);
                    if (lava > 0.004)
                    {
                        float3 slope4;
                        float3 flow = float3(sin(_Clock * 0.13), cos(_Clock * 0.11), sin(_Clock * 0.09 + 2)) * 0.5;
                        flow -= normalize(input.positionOS) * dot(flow, normalize(input.positionOS));
                        half4 crust = Layer(_Albedo4, _Normal4, lava, float2(0.4, 0.083), far, input.positionOS + flow, dpdx, dpdy, axes, slope4);
                        // from far off the cracks are too fine to see, and what shows is the two together
                        half cool = saturate(age / 75.0), plate = lerp(smoothstep(0.1, 0.4, crust.a), 0.75, remote);
                        half hot = lerp(1.0 - cool * cool, saturate(0.85 - 2.0 * cool), plate);
                        // a volcano's lake never cools: it stays crusted and cracked, brighter where it wells up
                        hot = max(hot, saturate(input.heat.y * lerp(0.85, 0.1, plate) * (0.55 + 0.9 * Noise(input.positionOS * 0.3 + float3(0.0, 0.12 * _Clock, 0.0)))));
                        glow = lava * (half3(2.4, 0.5, 0.06) * hot + half3(0.6, 0.8, 0.55) * hot * hot * hot * hot);
                        albedo = lerp(albedo, crust.rgb, lava);
                        slope = lerp(slope, slope4, lava);
                        relief = lerp(relief, crust.a, lava);
                        gloss = lerp(gloss, 0.55 + 0.2 * hot, lava);
                    }

                    // Meltwater: the ice it ran over is left wet and dark, and where it stands it is
                    // clear at the edge, then blue.
                    float depth = (input.paint.b - 0.5) * 8.0;
                    half damp = smoothstep(0.2, 0.47, input.paint.b);
                    albedo *= lerp(1.0, half3(0.48, 0.57, 0.66), damp);
                    gloss = lerp(gloss, 0.6, damp);
                    if (depth > 0.0)
                    {
                        pooled = smoothstep(0.0, 0.06, depth);
                        albedo = lerp(albedo, lerp(half3(0.3, 0.62, 0.66), half3(0.03, 0.14, 0.24), 1.0 - exp(-1.5 * depth)), pooled * (0.5 + 0.5 * saturate(depth * 2.5)));
                        // it lies level and all but still
                        float3 ripple = float3(Noise(input.positionOS * 2.3 + 0.4 * _Time.y), Noise(input.positionOS * 2.3 + 7.1 - 0.3 * _Time.y), Noise(input.positionOS * 2.3 + 13.7)) - 0.5;
                        normalOS = normalize(lerp(normalOS, upOS + 0.06 * ripple, pooled));
                        slope *= 1.0 - pooled;
                        gloss = lerp(gloss, 0.7, pooled);
                    }
                }

                float3 normalWS = TransformObjectToWorldNormal(normalize(normalOS + slope));
                // a world seen from afar is lit from where the sun stands for it, so its phase is right
                half3 distant = Veil(albedo * (_SunLight.rgb * (0.55 * saturate(dot(normalWS, SunDirection())) + 0.01) + SecondStarlight(normalWS) * 0.55) + glow, input.positionWS, _Sky, _AirRadius);
                if (_Haze <= 0.0) return half4(distant, 1.0);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.smoothness = lerp(gloss, 0.75, wet);
                surface.emission = glow;
                // little light finds its way down between blades of grass or into a crack
                surface.occlusion = lerp(0.65, 1.0, smoothstep(0.0, 0.5, relief));
                surface.alpha = 1.0;

                half4 colour = UniversalFragmentPBR(inputData, surface);
                if (pooled > 0.0)
                {
                    // still water mirrors the sky, most of all at a glancing angle, and glitters under the sun
                    Light sun = GetMainLight();
                    float3 upWS = TransformObjectToWorldNormal(upOS);
                    float daylight = saturate(dot(upWS, sun.direction) * 4.0 + 0.2), glint = saturate(dot(normalWS, normalize(sun.direction + inputData.viewDirectionWS)));
                    half3 mirrored = lerp(unity_FogColor.rgb, _Sky.rgb * daylight, saturate(dot(reflect(-inputData.viewDirectionWS, normalWS), upWS) * 1.5));
                    colour.rgb = lerp(colour.rgb, mirrored, pooled * (0.02 + 0.98 * pow(1.0 - saturate(dot(normalWS, inputData.viewDirectionWS)), 5.0)))
                        + pooled * sun.color * daylight * (5.0 * pow(glint, 400.0) + 0.25 * pow(glint, 40.0));
                }
                // Only the body the camera is near is lit by the scene's own light, through its own haze.
                // _Haze eases between the two on the way out, so leaving a world changes nothing at a stroke.
                colour.rgb = lerp(distant, Veil(MixFog(colour.rgb, inputData.fogCoord), input.positionWS, _Sky, _AirRadius), _Haze);
                return colour;
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            float4 Vertex(Attributes input) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, TransformObjectToWorldNormal(input.normalOS), _LightDirection));
                return ApplyShadowClamping(positionCS);
            }

            half4 Fragment() : SV_Target
            {
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

            float4 Vertex(Attributes input) : SV_POSITION
            {
                return TransformObjectToHClip(input.positionOS.xyz);
            }

            half4 Fragment(float4 positionCS : SV_POSITION) : SV_Target
            {
                return positionCS.z;
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

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                return half4(normalize(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }
}
