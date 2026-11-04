Shader "HaiBanTay/TreeBillboard"
{
    Properties { _BaseMap("Tán cây RGBA", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half fog : TEXCOORD1; };
            Varyings Vert(Attributes i)
            {
                Varyings o;
                float3 origin = TransformObjectToWorld(float3(0,0,0));
                float3 view = GetCameraPositionWS() - origin;
                float3 right = normalize(float3(view.z,0,-view.x));
                // Chỉ xoay quanh trục đứng; tán xa không nằm ngửa khi camera nhìn xuống.
                float sx = length(unity_ObjectToWorld._m00_m10_m20);
                float sy = length(unity_ObjectToWorld._m01_m11_m21);
                float3 p = origin + right * i.positionOS.x * sx + float3(0,i.positionOS.y * sy,0);
                o.positionCS = TransformWorldToHClip(p); o.uv = i.uv;
                o.fog = ComputeFogFactor(o.positionCS.z); return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv);
                clip(c.a-.35h);
                Light sun = GetMainLight();
                half3 lighting = SampleSH(half3(0,1,0)) + sun.color * saturate(sun.direction.y) * .35h;
                return half4(MixFog(c.rgb * lighting,i.fog),1);
            }
            ENDHLSL
        }
    }
}
