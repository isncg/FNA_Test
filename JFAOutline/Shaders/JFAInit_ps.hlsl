// JFA Init: Converts mask texture to initial seed UV coordinates.
// White mask pixels -> own UV as seed. Black pixels -> (-1, -1) sentinel.

Texture2D<float4> MaskTexture : register(t0);
SamplerState MaskSampler : register(s0);

float4 RTInvSize : register(c0); // (1/w, 1/h, w, h)

float2 PSMain(float4 svPos : SV_Position, float2 texCoord : TEXCOORD0) : SV_Target0
{
    // Integer-pixel load for exact mask read
    int3 loadCoord = int3(svPos.xy, 0);
    float mask = MaskTexture.Load(loadCoord).r;

    if (mask > 0.5)
        return texCoord;              // seed: store own UV
    else
        return float2(-1.0, -1.0);   // sentinel: no seed
}
