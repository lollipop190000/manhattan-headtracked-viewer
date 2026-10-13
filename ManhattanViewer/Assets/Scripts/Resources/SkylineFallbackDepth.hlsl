#ifndef MANHATTAN_SKYLINE_FALLBACK_DEPTH_INCLUDED
#define MANHATTAN_SKYLINE_FALLBACK_DEPTH_INCLUDED

// The legacy hull rounds building heights to millimetres. Recess that private
// untextured fallback by 1mm in world Y to keep rounded roofs behind detail.
// Moving the closed hull together preserves its seams. Imported geometry is
// never changed; the same adjustment is used by every camera raster pass.
float3 SkylineFallbackPositionOS(float3 positionOS)
{
    return positionOS + mul((float3x3)UNITY_MATRIX_I_M, float3(0, -0.001, 0));
}

VertexPositionInputs SkylineFallbackVertexPositionInputs(float3 positionOS)
{
    return GetVertexPositionInputs(SkylineFallbackPositionOS(positionOS));
}

float4 SkylineFallbackObjectToHClip(float3 positionOS)
{
    return TransformObjectToHClip(SkylineFallbackPositionOS(positionOS));
}

// Included after LitInput/Core, before each stock camera pass.
#define GetVertexPositionInputs SkylineFallbackVertexPositionInputs
#define TransformObjectToHClip SkylineFallbackObjectToHClip
#endif
