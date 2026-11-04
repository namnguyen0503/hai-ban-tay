Shader "HaiBanTay/DirtOverlay"
{
    // Lớp bẩn bám trong lòng nồi, chảo, thành nồi cao: màu tối không đổ bóng, alpha = nhiễu × mặt nạ cọ (R, 0 = đã cọ sạch).
    // Chỗ bàn chải đã đi qua có mặt nạ về 0 nên trong suốt; chỗ chưa chải còn nguyên vết nhiễu. Cùng đường nhiễu và ngưỡng cắt
    // như vật liệu Dirt cũ (URP Unlit + alpha clip) để màu và độ loang đúng như các ảnh đã duyệt.
    Properties
    {
        _BaseColor("Màu bẩn", Color) = (0.12, 0.1, 0.08, 1)
        _BaseMap("Nhiễu (alpha)", 2D) = "white" {}
        _MaskTex("Mặt nạ cọ (R)", 2D) = "white" {}
        _Cutoff("Ngưỡng cắt", Range(0, 1.1)) = 0.2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap);  SAMPLER(sampler_BaseMap);
            TEXTURE2D(_MaskTex);  SAMPLER(sampler_MaskTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
                float4 _MaskTex_ST;
                half _Cutoff;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS);
                o.uv = input.uv;
                return o;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half noise = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw).a;
                half mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, input.uv).r;
                clip(noise * mask - _Cutoff);
                return half4(_BaseColor.rgb, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            Cull Off
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap);  SAMPLER(sampler_BaseMap);
            TEXTURE2D(_MaskTex);  SAMPLER(sampler_MaskTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
                float4 _MaskTex_ST;
                half _Cutoff;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS);
                o.uv = input.uv;
                return o;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half noise = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw).a;
                half mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, input.uv).r;
                clip(noise * mask - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }
}
