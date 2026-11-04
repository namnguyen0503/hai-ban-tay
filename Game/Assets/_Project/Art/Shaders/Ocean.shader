Shader "HaiBanTay/Ocean"
{
    Properties
    {
        _DeepColor("Màu sâu", Color) = (0.015, 0.13, 0.23, 1)
        _ShallowColor("Màu nông", Color) = (0.07, 0.35, 0.37, 1)
        _SkyColor("Màu trời phản chiếu", Color) = (0.64, 0.78, 0.86, 1)
        _ZenithColor("Đỉnh trời phản chiếu", Color) = (0.12, 0.38, 0.72, 1)
        _SunDirection("Hướng mặt trời", Vector) = (0, 1, 0, 0)
        _SunRadiance("Ánh mặt trời phản chiếu", Color) = (1, 0.84, 0.54, 1)
        _WaveA("Sóng A: hướng XZ, biên độ, bước sóng", Vector) = (0.766, -0.643, 0.065, 12)
        _WaveB("Sóng B: hướng XZ, biên độ, bước sóng", Vector) = (0.914, -0.407, 0.055, 8.1)
        _WaveC("Sóng C: hướng XZ, biên độ, bước sóng", Vector) = (0.990, -0.139, 0.045, 5.3)
        _WaveD("Sóng D: hướng XZ, biên độ, bước sóng", Vector) = (0.978, 0.208, 0.035, 3.4)
        _WaveE("Sóng E: hướng XZ, biên độ, bước sóng", Vector) = (0.883, 0.469, 0.025, 2.2)
        _WaveF("Sóng F: hướng XZ, biên độ, bước sóng", Vector) = (0.766, 0.643, 0.02, 1.5)
        _WaveScale("Cường độ sóng theo thời tiết", Float) = 1
        _RippleStrength("Gợn chi tiết", Float) = 0.22
        _WaveSpeed("Tốc độ sóng", Float) = 1
        _SkyReflection("Phản chiếu trời", Range(0, 1)) = 15
        _Gloss("Độ sắc highlight", Range(8, 256)) = 96
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "OceanForward"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Back
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _LIGHT_LAYERS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor, _ShallowColor, _SkyColor, _ZenithColor, _SunRadiance;
                float4 _SunDirection;
                float4 _WaveA, _WaveB, _WaveC, _WaveD, _WaveE, _WaveF;
                float _WaveSpeed, _SkyReflection, _Gloss, _WaveScale, _RippleStrength;
            CBUFFER_END

            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 waterPosition : TEXCOORD1;
                half fog : TEXCOORD2;
            };

            // Hash số học: nhiễu không cần texture hoặc phép sin trên từng góc ô.
            float Hash(float2 p)
            {
                float3 h = frac(float3(p.xyx) * 0.1031);
                h += dot(h, h.yzx + 33.33);
                return frac((h.x + h.y) * h.z);
            }

            // Giá trị và đạo hàm nhiễu trơn để đổi pháp tuyến mà không phải lấy mẫu lân cận.
            float3 Noise(float2 p)
            {
                float2 cell = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f), du = 6.0 * f * (1.0 - f);
                float a = Hash(cell), b = Hash(cell + float2(1, 0));
                float c = Hash(cell + float2(0, 1)), d = Hash(cell + 1.0);
                return float3(lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y),
                    lerp(b - a, d - c, u.y) * du.x, lerp(c - a, d - b, u.x) * du.y);
            }

            float2 WaveSlope(float4 wave, float2 p, float pixelWidth, float bend)
            {
                float2 direction = normalize(wave.xy);
                float k = TWO_PI / max(wave.w, 0.1);
                float phase = k * dot(direction, p) - sqrt(9.81 * k) * _Time.y * _WaveSpeed;
                // Lệch pha theo nhiễu phá sống sóng dài; loại sóng dưới hai pixel để tránh moire.
                float filter = 1.0 - smoothstep(0.8, 3.0, pixelWidth * k);
                return direction * cos(phase + bend) * wave.z * k * filter;
            }

            Varyings Vert(Attributes input)
            {
                Varyings o;
                // Lưới xa thưa: chỉ dùng pháp tuyến, tránh dời đỉnh sóng ngắn gây mặt nước gãy.
                o.positionWS = TransformObjectToWorld(input.positionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.waterPosition = input.positionOS.xz;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float2 p = input.waterPosition;
                float distanceToCamera = distance(_WorldSpaceCameraPos, input.positionWS);
                float pixelWidth = max(length(ddx(p)), length(ddy(p)));
                float3 broad = Noise(p * 0.32 + float2(-0.11, 0.07) * _Time.y);
                float3 fine = Noise(p * 2.1 + float2(0.19, -0.27) * _Time.y);
                float detailFade = 1.0 - smoothstep(25.0, 120.0, distanceToCamera);
                float bend = (broad.x - 0.5) * 6.0;
                p += broad.yz * 1.5 + fine.yz * 0.2 * detailFade;
                float2 slope = WaveSlope(_WaveA, p, pixelWidth, bend);
                slope += WaveSlope(_WaveB, p, pixelWidth, -bend + 1.7);
                slope += WaveSlope(_WaveC, p, pixelWidth, bend + 3.1);
                slope += WaveSlope(_WaveD, p, pixelWidth, -bend + 0.8);
                slope += WaveSlope(_WaveE, p, pixelWidth, bend + 4.3);
                slope += WaveSlope(_WaveF, p, pixelWidth, -bend + 2.4);
                slope *= _WaveScale * lerp(0.65, 1.0, broad.x);
                slope += _RippleStrength * (broad.yz + fine.yz * (1.0 - smoothstep(0.15, 0.7, pixelWidth))) * detailFade;
                slope *= lerp(1.0, 0.08, smoothstep(60.0, 320.0, distanceToCamera));
                half3 localNormal = normalize(float3(-slope.x, 1, -slope.y));
                half3 n = TransformObjectToWorldNormal(localNormal);
                half fresnel = 0.025h + 0.975h * pow(1.0h - saturate(dot(n, v)), 5.0h);
                // Màu nông/deep là preset nước, không cần depth texture hay pass khúc xạ.
                half3 water = lerp(_DeepColor.rgb, _ShallowColor.rgb, saturate(0.22h + 1.8h * (1.0h - localNormal.y)));
                half3 reflected = TransformWorldToObjectDir(reflect(-v, n));
                // Chân trời ấm chỉ nhuộm nhẹ phản chiếu; giữ sắc trời xanh ở thân sóng.
                // Trời ban ngày có B > R nên nhánh tiết chế màu ấm bằng 0.
                half warmth = saturate((_SkyColor.r - _SkyColor.b) * 4.0h);
                half3 horizon = lerp(_SkyColor.rgb, _ZenithColor.rgb, warmth * 0.85h);
                half3 sky = lerp(horizon, _ZenithColor.rgb, pow(saturate(reflected.y), 0.55h));
                // Phản chiếu có hấp thụ màu nước, không trải màu chân trời cam lên toàn bộ mặt biển.
                half noonBlue = saturate((_ZenithColor.b - 0.72h) * 4.35h);
                half3 color = lerp(water, sky * lerp(0.45h, 0.65h, noonBlue), fresnel * _SkyReflection);
                // Hướng nhìn ngược hẳn mặt trời cho tổng bằng 0: không chuẩn hóa vector rỗng (NaN tùy GPU).
                float3 sunSum = normalize(_SunDirection.xyz) + (float3)v;
                float sunSumSq = dot(sunSum, sunSum);
                half3 sunHalfway = sunSum * rsqrt(max(sunSumSq, 1e-6));
                half sunGlint = sunSumSq > 1e-6 ? pow(saturate(dot(n, sunHalfway)), _Gloss * 4.0h) : 0.0h;
                color += _SunRadiance.rgb * sunGlint * (0.3h + fresnel * 2.5h);
                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                bool matches = true;
                #ifdef _LIGHT_LAYERS
                    matches = IsMatchingLightLayer(light.layerMask, GetMeshRenderingLayer());
                #endif
                if (matches)
                {
                    color += light.color * light.shadowAttenuation
                        * saturate(dot(n, light.direction)) * water * 0.12h;
                }
                // Sương sát mặt nước vẫn xanh sẫm; sương cam như trời khiến biển thành đồi cát.
                half3 waterFog = lerp(_DeepColor.rgb, horizon, lerp(0.32h, 0.45h, noonBlue));
                return half4(MixFogColor(color, waterFog, input.fog), 1);
            }
            ENDHLSL
        }
    }
}
