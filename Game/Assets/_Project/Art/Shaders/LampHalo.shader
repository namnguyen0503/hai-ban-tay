Shader "HaiBanTay/LampHalo"
{
    // Quầng sáng quanh đèn phố: quả cầu cộng sáng (additive), sáng nhất ở tâm khi nhìn thẳng và tắt dần ra mép,
    // nên không cần billboard hướng camera. Không ghi độ sâu, vẫn bị nhà/cột che đúng.
    Properties
    {
        [HDR] _Color("Màu quầng (HDR)", Color) = (1.4, 0.62, 0.2, 1)
        _Power("Độ gắt (cao = quầng hẹp)", Range(0.5, 8)) = 2.5
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+1" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Power;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 viewWS : TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(input.positionOS);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.viewWS = GetWorldSpaceViewDir(positionWS);
                return o;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half f = pow(saturate(dot(normalize(input.normalWS), normalize(input.viewWS))), _Power);
                return half4(_Color.rgb * f, 1);
            }
            ENDHLSL
        }
    }
}
