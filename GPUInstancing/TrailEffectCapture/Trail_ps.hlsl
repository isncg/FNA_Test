// Trail pixel shader — pass-through color with alpha from vertex color.
// Alpha blending is configured at the render-state level (BlendState.AlphaBlend).

float4 PSMain(float4 color : COLOR0) : SV_TARGET0
{
    return color;
}
