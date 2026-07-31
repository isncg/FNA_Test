// ShadowMap_ps.hlsl — Writes NDC depth to R32F render target.

float PSMain(float4 svPos : SV_POSITION) : SV_TARGET0
{
    return svPos.z;
}
