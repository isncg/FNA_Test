// EnvDebug_ps.hlsl — Environment map debug visualization.
// Samples the HDR equirectangular env map at screen UV and applies
// basic tone mapping for SDR display.

Texture2D    EnvMap     : register(t0);
SamplerState EnvSampler : register(s0);

float2 PanOffset  : register(c0);
float  Zoom       : register(c2);

float4 PSMain(float2 texCoord : TEXCOORD0) : SV_TARGET0
{
    // texCoord is in [0,1] across the fullscreen triangle.
    // Pan + zoom the view into the equirectangular map.
    float2 uv = (texCoord - 0.5) / Zoom + 0.5 + PanOffset;

    float3 color = EnvMap.SampleLevel(EnvSampler, uv, 0).rgb;

    // Reinhard tone map + gamma for SDR display
    color = color / (1.0 + color);
    color = pow(max(color, 0.0), 1.0 / 2.2);

    return float4(color, 1.0);
}
