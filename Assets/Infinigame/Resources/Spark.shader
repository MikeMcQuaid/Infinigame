// Light with no body to it: a spark drawn out along its flight, an ember, the glare of a blast.
// Added to whatever is behind it. A spark also hides that as far as it is bright, so that against
// a bright sky a dim one is a dim spark and not a pale smear; glare hides nothing. One too thin to
// see is kept a pixel and a half wide and dimmed instead, so that it neither crawls nor winks
// out. Instanced: Effects draws them all in one call.
Shader "Infinigame/Spark"
{
    SubShader
    {
        // after the smoke, which it shines through
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+1" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Spark"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            UNITY_INSTANCING_BUFFER_START(Sparks)
                // how far back along its flight it is drawn, and how wide it is, in metres
                UNITY_DEFINE_INSTANCED_PROP(float4, _Tail)
                // its colour, and how far it hides what is behind it
                UNITY_DEFINE_INSTANCED_PROP(float4, _Glare)
            UNITY_INSTANCING_BUFFER_END(Sparks)

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                // across and along it from -1 to 1, how many times longer than wide it is, and how many metres of it sink softly into the ground
                float4 uv : TEXCOORD0;
                half4 glare : TEXCOORD1;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                float4 tail = UNITY_ACCESS_INSTANCED_PROP(Sparks, _Tail), glare = UNITY_ACCESS_INSTANCED_PROP(Sparks, _Glare);
                float3 head = TransformWorldToView(float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23));
                float3 back = TransformWorldToViewDir(tail.xyz);
                float width = max(tail.w, -head.z * 3.0 / (abs(UNITY_MATRIX_P._m11) * _ScreenParams.y)), drawn = length(back.xy);
                float2 along = drawn > 1e-4 ? back.xy / drawn : float2(0.0, 1.0), corner = input.positionOS.xy * 2.0;
                float reach = 0.5 * (drawn + width);
                output.positionCS = TransformWViewToHClip(head - 0.5 * back + float3(along * (corner.y * reach) + float2(along.y, -along.x) * (corner.x * 0.5 * width), 0.0));
                output.uv = float4(corner, 2.0 * reach / width, 0.5 * width);
                output.glare = half4(MixFogColor(glare.rgb * (tail.w / width), 0.0, ComputeFogFactor(output.positionCS.z)), glare.a);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                // round at both ends, however long
                float2 from = float2(input.uv.x, max(abs(input.uv.y) * input.uv.z - (input.uv.z - 1.0), 0.0));
                float behind = LinearEyeDepth(SampleSceneDepth(GetNormalizedScreenSpaceUV(input.positionCS)), _ZBufferParams) - input.positionCS.w;
                float profile = saturate(1.0 - dot(from, from));
                float head = saturate(0.55 + 0.45 * input.uv.y);
                half3 light = (input.glare.rgb * (0.42 + 0.58 * head) + min(input.glare.rgb, 1) * profile * profile * head)
                    * (profile * profile * saturate(behind / input.uv.w));
                return half4(light, input.glare.a * saturate(max(light.r, max(light.g, light.b))));
            }
            ENDHLSL
        }
    }
}
