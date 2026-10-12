#ifndef MANHATTAN_SKYLINE_FALLBACK_MOTION_INCLUDED
#define MANHATTAN_SKYLINE_FALLBACK_MOTION_INCLUDED

// The stock motion pass projects current/previous object matrices directly for
// velocity. Apply the same world-space recess to both, including camera motion.
// Its raster path already uses SkylineFallbackVertexPositionInputs.
float4x4 SkylineFallbackMotionModel()
{
    float4x4 value = UNITY_MATRIX_M;
    value._m13 -= 0.001;
    return value;
}
float4x4 SkylineFallbackPreviousMotionModel()
{
    float4x4 value = UNITY_PREV_MATRIX_M;
    value._m13 -= 0.001;
    return value;
}
#undef UNITY_MATRIX_M
#define UNITY_MATRIX_M SkylineFallbackMotionModel()
#undef UNITY_PREV_MATRIX_M
#define UNITY_PREV_MATRIX_M SkylineFallbackPreviousMotionModel()
#endif
