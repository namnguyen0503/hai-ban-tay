Shader "HaiBanTay/PlayerHeadShadow"
{
    Properties
    {
        _BaseMap("Màu nền", 2D) = "white" {}
        _BaseColor("Màu", Color) = (1,1,1,1)
        _Cutoff("Ngưỡng alpha", Range(0,1)) = 0.5
        _Cull("Culling", Float) = 2
        _HidePlayerHead("Ẩn với camera chính", Float) = 1
        _Smoothness("Độ bóng", Range(0,1)) = 0.08
        _Metallic("Kim loại", Range(0,1)) = 0
        _SpecColor("Phản xạ", Color) = (0.2,0.2,0.2,1)
        _EmissionColor("Phát sáng", Color) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        // Bóng không phụ thuộc camera; đầu và mũ luôn đổ bóng.
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        Pass
        {
            Name "OutsideView"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex LitPassVertex
            #pragma fragment OutsideFragment
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"
            float _HidePlayerHead;
            // Dùng ánh sáng Lit chuẩn: cùng light probe, light layer và Forward+ với áo; không tự tính ánh sáng đầu.
            void OutsideFragment(Varyings input, out half4 color : SV_Target0
                #ifdef _WRITE_RENDERING_LAYERS
                , out uint layers : SV_Target1
                #endif
            )
            {
                clip(0.5 - _HidePlayerHead);
                LitPassFragment(input, color
                    #ifdef _WRITE_RENDERING_LAYERS
                    , layers
                    #endif
                );
            }
            ENDHLSL
        }
    }
}
