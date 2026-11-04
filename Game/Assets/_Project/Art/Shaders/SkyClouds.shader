Shader "HaiBanTay/SkyClouds"
{
    Properties
    {
        _HorizonColor("Haze xa", Color) = (0.6, 0.75, 0.88, 1)
        _SunDirection("Hướng sáng", Vector) = (0, 1, 0, 0)
        _Night("Mức đêm", Range(0, 1)) = 0
        _Storm("Mức bão", Range(0, 1)) = 0
        _LitTint("Nhân màu phần sáng", Color) = (1, 1, 1, 1)
        _ShadeTint("Nhân màu phần tối", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Background" "Queue"="Background+1" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Back
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _HorizonColor, _LitTint, _ShadeTint;
                float4 _SunDirection;
                float _Night, _Storm;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float3 normalOS : NORMAL; float3 center : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half3 normal : TEXCOORD0; half haze : TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 offset = input.positionOS - input.center;
                // Khi bão, cụm mây hạ thấp và lan rộng; cùng mesh, không thêm lớp overdraw.
                float3 p = lerp(input.positionOS, input.center * float3(1, 0.52, 1) + offset * float3(1.6, 1.15, 1.6), _Storm);
                p.xz += float2(24, 8) * sin(_Time.y * 0.003);
                o.positionCS = TransformObjectToHClip(p);
                o.normal = normalize(input.normalOS / lerp(float3(1, 1, 1), float3(1.6, 1.15, 1.6), _Storm));
                o.haze = smoothstep(150, 500, length(input.center.xz));
                return o;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half3 n = normalize(input.normal);
                half light = saturate(dot(n, normalize(_SunDirection.xyz))) * 0.55 + saturate(n.y * 0.5 + 0.5) * 0.45;
                half3 lit = lerp(half3(0.98, 0.98, 0.96), half3(0.52, 0.55, 0.57), _Storm) * _LitTint.rgb;
                half3 shade = lerp(half3(0.46, 0.56, 0.64), half3(0.17, 0.21, 0.25), _Storm) * _ShadeTint.rgb;
                half3 color = lerp(shade, lit, smoothstep(0.05, 0.85, light)) * lerp(1, 0.18, _Night);
                return half4(lerp(color, _HorizonColor.rgb, input.haze * 0.55), 1);
            }
            ENDHLSL
        }
    }
}
