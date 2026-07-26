// ShadowMap_ps.hlsl — Writes NDC depth to R32F render target.
// SV_POSITION.z is [0,1] in Vulkan (DXC SPIR-V target).

float PSMain(float4 svPos : SV_POSITION) : SV_TARGET0
{
    return svPos.z;
}
