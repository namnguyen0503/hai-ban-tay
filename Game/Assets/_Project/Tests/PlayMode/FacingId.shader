// Dò nhìn xuyên (SeeThroughProbe): vẽ cả hai mặt, giữ mặt gần nhất; R = 1 nếu mặt gần nhất là mặt sau của vật liệu chỉ vẽ mặt trước
// (trong game chỗ đó bị cull nên thấy xuyên ra sau), G/B = số thứ tự lệnh vẽ (16 bit).
Shader "Hidden/HaiBanTay/FacingId"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            Cull Off
            ZWrite On
            ZTest LEqual
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _FacingId;
            float _FacingTwoSided;
            float _FacingFlip;
            float _FacingPass;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            float4 frag(Varyings input, bool front : SV_IsFrontFace) : SV_Target
            {
                bool isFront = (_FacingFlip > 0.5 ? !front : front) || _FacingTwoSided > 0.5;
                // Mặt trước vẽ sau cùng: ở đường viền hai mặt có thể cùng độ sâu lượng tử hóa. Giữ mặt trước khi hòa, giống vật
                // liệu Cull Back thật, thay vì để thứ tự tam giác sinh ra một vệt báo xuyên giả trên khối cube kín.
                if (isFront != (_FacingPass > 0.5)) discard;
                float back = isFront ? 0.0 : 1.0;
                return float4(back, fmod(_FacingId, 256.0) / 255.0, floor(_FacingId / 256.0) / 255.0, 1.0);
            }
            ENDHLSL
        }
    }
}
