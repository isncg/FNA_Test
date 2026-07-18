// ParticleEffect pixel shader — glow disk texture modulated by vertex color.
// Additive blending is set at the render-state level (BlendState.Additive).

Texture2D<float4> ParticleTexture : register(t0);
SamplerState ParticleSampler : register(s0);

float4 PSMain(float2 texCoord : TEXCOORD0, float4 color : COLOR0) : SV_TARGET0
{
    float4 texColor = ParticleTexture.Sample(ParticleSampler, texCoord);
    return texColor * color;  // soft glow × fire gradient
}
