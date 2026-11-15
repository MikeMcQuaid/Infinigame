Shader "Infinigame/Globe"
{
    SubShader
    {
        Tags { "Queue"="Overlay-10" "RenderType"="Transparent" }
        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct V { float4 pos:SV_POSITION; float3 p:TEXCOORD0; float3 view:TEXCOORD1; };
            V Vertex(float4 p:POSITION) { V o; o.pos=TransformObjectToHClip(p.xyz); o.p=p.xyz; o.view=TransformWorldToObject(GetCameraPositionWS())-p.xyz; return o; }
            half4 Fragment(V i):SV_Target
            {
                float3 n=normalize(i.p); float front=dot(n,normalize(i.view));
                float2 grid=float2(atan2(n.x,n.z)*3.81972, asin(n.y)*3.81972);
                float2 lines=abs(frac(grid+0.5)-0.5)/max(fwidth(grid),0.001);
                float ink=1-saturate(min(lines.x,lines.y)-0.4);
                float rim=pow(1-abs(front),4);
                return half4(0.15,0.65,0.85,(0.035+ink*0.33+rim*0.3)*(front>0?1:0.35));
            }
            ENDHLSL
        }
    }
}
