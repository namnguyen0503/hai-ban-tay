Shader "HaiBanTay/SkyDome"
{
    Properties
    {
        _ZenithColor("Đỉnh trời", Color) = (0.12, 0.38, 0.72, 1)
        _HorizonColor("Chân trời", Color) = (0.64, 0.78, 0.86, 1)
        _BelowColor("Dưới chân trời", Color) = (0.22, 0.25, 0.27, 1)
        _SunDirection("Hướng mặt trời", Vector) = (0, 1, 0, 0)
        _MoonDirection("Hướng trăng", Vector) = (0, -1, 0, 0)
        _SunColor("Mặt trời", Color) = (1, 0.84, 0.54, 1)
        _MoonColor("Trăng", Color) = (0.65, 0.76, 1, 1)
        _DiscRadius("Bán kính góc đĩa", Range(0.002, 0.04)) = 0.012
        _Night("Mức đêm", Range(0, 1)) = 0
        _Stars("Sao nhìn thấy", Range(0, 1)) = 0
        _Storm("Mây bão", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Background" "Queue"="Background" }
        Pass
        {
            Name "SkyDome"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _ZenithColor, _HorizonColor, _BelowColor, _SunColor, _MoonColor;
                float4 _SunDirection, _MoonDirection;
                float _DiscRadius, _Night, _Stars, _Storm;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS);
                // Tia từ camera giữ mặt trời/sao ở vô cực, không lệch khi đi dọc boong.
                o.direction = TransformWorldToObjectDir(TransformObjectToWorld(input.positionOS) - _WorldSpaceCameraPos, false);
                return o;
            }
            float Hash(float3 p) { return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453); }
            float CloudHash(float2 p)
            {
                float3 h = frac(float3(p.xyx) * 0.1031);
                h += dot(h, h.yzx + 33.33);
                return frac((h.x + h.y) * h.z);
            }
            float CloudNoise(float2 p)
            {
                float2 c = floor(p), u = frac(p);
                u = u * u * (3 - 2 * u);
                return lerp(lerp(CloudHash(c), CloudHash(c + float2(1, 0)), u.x),
                    lerp(CloudHash(c + float2(0, 1)), CloudHash(c + 1), u.x), u.y);
            }
            half Disc(float3 direction, float3 body)
            {
                return smoothstep(cos(_DiscRadius * 1.2), cos(_DiscRadius), dot(direction, normalize(body)));
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float3 d = normalize(input.direction);
                half3 color = d.y >= 0 ? lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(saturate(d.y), 0.55))
                    : lerp(_HorizonColor.rgb, _BelowColor.rgb, saturate(-d.y * 3));
                half cloud = 0;
                if (_Storm > 0.001 && d.y > 0.005)
                {
                    // Một lớp chiếu phối cảnh, ba tần số; sáng đỉnh và tối bụng tạo khối, không raymarch.
                    float2 p = d.xz / max(d.y + 0.08, 0.025) * lerp(1.8, 0.85, _Storm);
                    p += float2(0.012, 0.006) * _Time.y;
                    float broad = CloudNoise(p);
                    float detail = CloudNoise(p * 2.7 + 13.8);
                    float field = broad * 0.65 + detail * 0.25 + CloudNoise(p * 7.1 + 31.4) * 0.10;
                    cloud = smoothstep(lerp(0.53, 0.22, _Storm), lerp(0.65, 0.47, _Storm), field);
                    cloud *= smoothstep(0.005, 0.065, d.y) * _Storm;
                    float underside = saturate((CloudNoise(p + float2(0, 0.28)) - broad) * 3 + 0.45);
                    half3 lit = lerp(half3(0.96, 0.97, 0.96), half3(0.42, 0.46, 0.50), _Storm);
                    half3 shade = lerp(half3(0.52, 0.63, 0.73), half3(0.17, 0.21, 0.26), _Storm);
                    half3 cloudColor = lerp(shade, lit, underside * 0.7 + detail * 0.3);
                    cloudColor *= lerp(1, 0.18, _Night);
                    cloudColor = lerp(_HorizonColor.rgb, cloudColor, smoothstep(0.005, 0.18, d.y));
                    color = lerp(color, cloudColor, cloud);
                }
                // Sao cố định trên vòm; không dùng thời gian để tránh nhấp nháy noise.
                float3 cell = floor(d * 420);
                float3 starCenter = (cell + 0.5) / 420;
                float star = step(0.997, Hash(cell)) * (1 - smoothstep(0.0004, 0.0018, length(d - starCenter)));
                color += star * _Stars * (1 - cloud) * smoothstep(0.02, 0.15, d.y) * half3(0.65, 0.75, 1);
                color += Disc(d, _SunDirection.xyz) * _SunColor.rgb * (1 - _Night * 0.8) * (1 - cloud) * (1 - _Storm);
                color += Disc(d, _MoonDirection.xyz) * _MoonColor.rgb * _Night * (1 - cloud);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
