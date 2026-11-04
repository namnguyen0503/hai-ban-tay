// Fragment hai mặt cho HaiBanTay/LitTwoSided: mặt sau lật pháp tuyến và tiếp tuyến (giữ nguyên bitangent = sign·cross(n, t)), rồi gọi
// đúng fragment của URP Lit. Mặt trong vỏ tàu, lòng tóc, lòng vải nhận sáng theo hướng của chính nó.
#ifndef HBT_LIT_TWO_SIDED_INCLUDED
#define HBT_LIT_TWO_SIDED_INCLUDED

void HbtFlipBackFace(inout Varyings input, FRONT_FACE_TYPE face)
{
    float s = IS_FRONT_VFACE(face, 1.0, -1.0);
    input.normalWS *= s;
#if defined(REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR)
    input.tangentWS.xyz *= s;
#endif
}

#endif

#if defined(LIT_TWO_SIDED_FORWARD) && !defined(HBT_LIT_TWO_SIDED_FORWARD_DONE)
#define HBT_LIT_TWO_SIDED_FORWARD_DONE
void LitTwoSidedFragment(
    Varyings input
    , FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC
    , out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
)
{
    HbtFlipBackFace(input, face);
    LitPassFragment(input, outColor
#ifdef _WRITE_RENDERING_LAYERS
        , outRenderingLayers
#endif
    );
}
#endif

#if defined(LIT_TWO_SIDED_DEPTH_NORMALS) && !defined(HBT_LIT_TWO_SIDED_DEPTH_NORMALS_DONE)
#define HBT_LIT_TWO_SIDED_DEPTH_NORMALS_DONE
void LitTwoSidedDepthNormalsFragment(
    Varyings input
    , FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC
    , out half4 outNormalWS : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
)
{
    HbtFlipBackFace(input, face);
    DepthNormalsFragment(input, outNormalWS
#ifdef _WRITE_RENDERING_LAYERS
        , outRenderingLayers
#endif
    );
}
#endif
