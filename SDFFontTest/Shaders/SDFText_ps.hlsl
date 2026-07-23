// SDF Text Pixel Shader
// Single-channel Signed Distance Field: samples the grayscale texture
// where brightness encodes distance from the glyph edge.
// 0 = black (far outside), 1 = white (far inside), 0.5 = edge.
//
// Uses a 5-tap max filter (center + 4 neighbors) to compensate for the
// single-channel SDF's concave-corner underestimation. This replaces the
// MSDF median trick with a simpler spatial dilation.
//
// Descriptor Set layout:
//   Set 2 Binding 0: SDFTexture (t0) + SDFSampler (s0)
//   Set 3 Binding 0: $Globals { Smoothing, OutlineColor, OutlineWidth }

Texture2D<float4> SDFTexture   : register(t0);
SamplerState      SDFSampler   : register(s0);

float  Smoothing    : register(c4);
float4 OutlineColor : register(c5);
float  OutlineWidth : register(c6);
float  Weight       : register(c7);

float4 PSMain(
    float4 color    : COLOR0,
    float2 texCoord : TEXCOORD0
) : SV_TARGET0
{
    uint2 texSize;
    SDFTexture.GetDimensions(texSize.x, texSize.y);
    float2 ts = float2(1.0 / texSize.x, 1.0 / texSize.y);

    float distance = SDFTexture.Sample(SDFSampler, texCoord).r;

    // 0.5 = SDF edge (original font outline).
    // Two-sided smoothstep centered at fillEdge so that at Weight=0 the
    // visible edge matches the original font — not expanded outward.
    float fillEdge = 0.5 - Weight;
    float fillAlpha = smoothstep(
        fillEdge - Smoothing,
        fillEdge + Smoothing,
        distance);

    // Outline: band OUTSIDE the glyph.  The outline threshold sits
    // OutlineWidth units outward from fillEdge and is clamped above
    // Smoothing so the band never reaches the far background (distance=0).
    float outlineOuter = max(Smoothing, fillEdge - OutlineWidth);
    float outlineAlpha = smoothstep(
        outlineOuter - Smoothing,
        outlineOuter,
        distance);
    // Suppress outline where the glyph fill is already opaque.
    outlineAlpha *= 1.0 - fillAlpha;

    // Blend fill and outline colors weighted by their relative alpha,
    // then premultiply once. Avoids double-multiplication of alpha.
    float alpha = max(outlineAlpha, fillAlpha);
    float alphaSafe = max(alpha, 0.0001);
    float fillWeight = fillAlpha / alphaSafe;
    float3 rgb = lerp(OutlineColor.rgb, color.rgb, fillWeight);

    return float4(rgb * alpha, alpha);
}
