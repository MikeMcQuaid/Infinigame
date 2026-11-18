Shader "Infinigame/EnergyRibbon"
{
    Properties { [MainColor][HDR] _Tint ("Emitter", Color) = (0.25,1,1.5,1) }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+1" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
            CBUFFER_END
            struct Input { float4 pos : POSITION; float2 uv : TEXCOORD0; half4 colour : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Pixel { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; half4 colour : COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            Pixel Vert(Input v)
            {
                Pixel o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = TransformObjectToHClip(v.pos.xyz);
                o.uv = v.uv; o.colour = v.colour * _Tint;
                return o;
            }
            half4 Frag(Pixel i) : SV_Target
            {
                float across = i.uv.y * 2 - 1;
                float wave = sin(i.uv.x * 34 - _Time.y * 16);
                float halo = saturate(1 - abs(across));
                float filament = pow(saturate(1 - abs(across - 0.27 * wave)), 7);
                float core = pow(halo, 9);
                return half4((i.colour.rgb * (0.36 * halo * halo + 0.5 * filament + core) + 0.28 * core) * i.colour.a, 0);
            }
            ENDHLSL
        }
    }
}
