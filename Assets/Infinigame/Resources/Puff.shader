// A puff of fire, smoke, steam or dust: a card that faces the eye, cut by a painted field of
// crowding heads into a ball with a skin like a cauliflower's, and lit as a solid thing is: by the
// sun on the side that faces it, by the sky, and from inside by whatever is still burning. Hot, it
// glows through the creases between its heads while the heads crust over with soot. Cool, it is
// smoke that thins from its edges inwards. It is laid over what is behind it, never added, so
// smoke is dark against a bright sky. Instanced: Effects draws every puff in one call, far ones first.
Shader "Infinigame/Puff"
{
    Properties
    {
        _Billows ("Heads in red, their slopes in green and blue, wisps in alpha", 2D) = "gray" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Puff"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Fire.hlsl"

            TEXTURE2D(_Billows); SAMPLER(sampler_Billows);

            UNITY_INSTANCING_BUFFER_START(Puffs)
                // how far it has turned, how far through its life it is, a number of its own and how far it is drawn out along its axis
                UNITY_DEFINE_INSTANCED_PROP(float4, _Look)
                // how hot it is, how brightly lit from within, how much sun reaches it through the rest of its cloud and how eaten away it is
                UNITY_DEFINE_INSTANCED_PROP(float4, _Heat)
                // the colour of its smoke, and how much that hides
                UNITY_DEFINE_INSTANCED_PROP(float4, _Smoke)
            UNITY_INSTANCING_BUFFER_END(Puffs)

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                // across the puff from -1 to 1, how many metres of it sink softly into the ground, and the fog
                float4 uv : TEXCOORD0;
                // how far through its life, its own number, how eaten away, and how far the eye is from being inside it
                float4 look : TEXCOORD1;
                float3 heat : TEXCOORD2;
                half4 smoke : TEXCOORD3;
                // where the sun is in the puff's own frame: across, up and towards the eye
                float3 sun : TEXCOORD4;
                half3 sky : TEXCOORD5;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                float4 look = UNITY_ACCESS_INSTANCED_PROP(Puffs, _Look), heat = UNITY_ACCESS_INSTANCED_PROP(Puffs, _Heat);
                // the matrix says only where the puff is and which way its axis runs, as long as the puff is wide
                float3 centre = TransformWorldToView(float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23));
                float3 axis = TransformWorldToViewDir(float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21));
                float size = length(axis), away = length(centre), stretch = 1.0;
                float3 toEye = -centre / away, across = normalize(cross(float3(0.0, 1.0, 0.0), toEye)), up = cross(toEye, across);
                if (look.w > 1.0)
                {
                    // a tongue of flame stands along its axis, and seen end on is only a round puff
                    float3 flat = axis / size - toEye * dot(axis / size, toEye);
                    stretch = lerp(1.0, look.w, saturate(length(flat)));
                    up = normalize(flat + up * 1e-3);
                    across = cross(up, toEye);
                }
                else
                {
                    float s, c;
                    sincos(look.x, s, c);
                    float3 turned = across * c + up * s;
                    up = up * c - across * s;
                    across = turned;
                }
                float2 corner = input.positionOS.xy * 2.0;
                // it thins to nothing as the eye passes through it, and is then not drawn at all
                float inside = saturate((away - 0.2) / (0.6 * size));
                output.positionCS = inside > 0.0 ? TransformWViewToHClip(centre + (across * corner.x + up * (corner.y * stretch)) * (0.5 * size)) : float4(2.0, 2.0, 2.0, 1.0);
                output.uv = float4(corner, 0.35 * size, ComputeFogFactor(output.positionCS.z));
                output.look = float4(look.yz, heat.w, inside);
                output.heat = heat.xyz;
                output.smoke = UNITY_ACCESS_INSTANCED_PROP(Puffs, _Smoke);
                float3 sun = TransformWorldToViewDir(_MainLightPosition.xyz);
                output.sun = float3(dot(sun, across), dot(sun, up), dot(sun, toEye));
                // the sky lights the top of it and the ground its underside
                output.sky = SampleSH(mul((float3x3)UNITY_MATRIX_I_V, normalize(across * corner.x + up * corner.y + toEye * 0.6)));
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                float2 uv = input.uv.xy;
                float out2 = dot(uv, uv);
                clip(1.0 - out2);
                float age = input.look.x, eaten = input.look.z;
                // every puff takes its own piece of the painting, which swells as the puff does while the wisps drift across it
                float2 at = uv * (0.27 - 0.07 * age) + frac(input.look.y * float2(75.49, 56.98));
                half4 heads = SAMPLE_TEXTURE2D(_Billows, sampler_Billows, at);
                half4 wisps = SAMPLE_TEXTURE2D(_Billows, sampler_Billows, at * 1.9 + (heads.gb - 0.5) * 0.06 + age * float2(0.11, -0.23));
                float body = 0.75 * heads.r + 0.25 * wisps.r;
                // a ball whose skin stands out in heads and is cut deep between them, less what has been eaten out of it
                float thickness = 1.0 - out2 - 1.5 * (1.0 - body) * out2 - eaten * (0.1 + 1.3 * wisps.a);
                // soft where it meets the ground instead of cut by it
                float behind = LinearEyeDepth(SampleSceneDepth(GetNormalizedScreenSpaceUV(input.positionCS)), _ZBufferParams) - input.positionCS.w;
                float density = saturate(thickness * lerp(5.0, 1.6, eaten)) * saturate(behind / input.uv.z) * input.look.w;

                // the sun on a ball whose skin slopes as the heads do. It wraps round as light does in smoke, and some of it
                // comes through from behind: the paler the smoke the more, so that steam is lit all the way through
                float3 normal = normalize(float3(uv * 0.55 - (heads.gb - 0.5) * 2.4 - (wisps.gb - 0.5) * 0.9, sqrt(1.0 - out2) + 0.3));
                float sunlit = saturate(dot(normal, input.sun) * 0.6 + 0.4);
                // the creases lie in the shade of the heads beside them
                half3 colour = input.smoke.rgb * (_MainLightColor.rgb * (lerp(0.3 + 0.5 * input.smoke.g, 1.0, sunlit * sunlit) * input.heat.z) + input.sky) * (0.45 + 0.55 * body);
                // its own fire lights it from within, most where the eye sees deepest
                colour += (input.smoke.rgb + 0.05) * half3(2.6, 1.0, 0.25) * (input.heat.y * (1.2 - body));
                // and shows through: hottest down in the creases, while the heads crust over with soot
                half3 fire = Fire(input.heat.x * (1.4 - 1.15 * body) * (1.0 - 0.4 * out2));
                float alpha = density * input.smoke.a;
                return half4(MixFogColor(colour * alpha + fire * density, unity_FogColor.rgb * alpha, input.uv.w), alpha);
            }
            ENDHLSL
        }
    }
}
