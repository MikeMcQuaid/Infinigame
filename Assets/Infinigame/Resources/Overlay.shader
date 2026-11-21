// The interface: text, panels and the system map's orbit lines, drawn last and over everything.
// Unity's own interface shader tests against the depth buffer when a canvas hangs in front of a
// camera, which this canvas does so that it can sit far enough out to stay sharp wherever the camera is.
Shader "Infinigame/Overlay"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
        _Cull ("Cull", Float) = 0
        _Solid ("Solid tint", Float) = 0
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Pass
        {
            Cull [_Cull]
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            // set by the canvas: white for font atlases, which only carry alpha
            half4 _TextureSampleAdd, _Tint;
            half _Solid;

            struct Attributes
            {
                UNITY_VERTEX_INPUT_INSTANCE_ID
                float4 positionOS : POSITION;
                half4 colour : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                UNITY_VERTEX_OUTPUT_STEREO
                float4 positionCS : SV_POSITION;
                half4 colour : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings Vertex(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.colour = input.colour;
                output.uv = input.uv;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                if (_Solid > 0.5) return _Tint;
                return (SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) + _TextureSampleAdd) * input.colour * _Tint;
            }
            ENDHLSL
        }
    }
}
