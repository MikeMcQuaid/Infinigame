Shader "Infinigame/Shield"
{
    Properties { _Hit ("Impact", Float) = 0 }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float _Hit;
            struct V { float4 pos:SV_POSITION; float3 world:TEXCOORD0; float3 normal:TEXCOORD1; };
            V Vertex(float4 p:POSITION, float3 n:NORMAL)
            {
                V o; o.pos=TransformObjectToHClip(p.xyz); o.world=TransformObjectToWorld(p.xyz); o.normal=TransformObjectToWorldNormal(n); return o;
            }
            half4 Fragment(V i):SV_Target
            {
                float rim=pow(1-abs(dot(normalize(i.normal),normalize(GetCameraPositionWS()-i.world))),3);
                return half4(0.15,0.6,1,0.06+rim*0.28+_Hit*0.5);
            }
            ENDHLSL
        }
    }
}
