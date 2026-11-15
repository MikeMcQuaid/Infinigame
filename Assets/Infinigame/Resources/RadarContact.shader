Shader "Infinigame/RadarContact"
{
    Properties { _Colour ("Colour", Color)=(1,0.2,0.1,1) _Hollow ("Far side", Float)=0 }
    SubShader
    {
        Tags { "Queue"="Overlay-9" "RenderType"="Transparent" }
        Pass
        {
            ZTest Always
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            half4 _Colour; float _Hollow;
            struct V { float4 p:SV_POSITION; float3 n:TEXCOORD0; float3 view:TEXCOORD1; };
            V Vertex(float4 p:POSITION,float3 n:NORMAL) { V o; o.p=TransformObjectToHClip(p.xyz); o.n=TransformObjectToWorldNormal(n); o.view=GetCameraPositionWS()-TransformObjectToWorld(p.xyz); return o; }
            half4 Fragment(V i):SV_Target { half4 c=_Colour; c.a*=1-_Hollow*smoothstep(0.35,0.7,abs(dot(normalize(i.n),normalize(i.view)))); return c; }
            ENDHLSL
        }
    }
}
