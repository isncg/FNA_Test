// DebugView_ps.hlsl — Direct GBuffer channel visualization.
// DebugChannel: 0=RGB, 1=RRR (red channel grayscale), 2=GGG (green channel grayscale), 3=AAA (alpha grayscale)

Texture2D    InputTex  : register(t0);
SamplerState InputSamp : register(s0);

float DebugChannel : register(c0);

struct PS_INPUT
{
    float4 Position : SV_POSITION;
    float2 UV       : TEXCOORD0;
};

float4 PSMain(PS_INPUT input) : SV_TARGET0
{
    uint w, h;
    InputTex.GetDimensions(w, h);
    float2 uv = input.Position.xy / float2(w, h);

    float4 c = InputTex.Sample(InputSamp, uv);

    if (DebugChannel < 0.5)
        return float4(c.rgb, 1.0);     // RGB
    else if (DebugChannel < 1.5)
        return float4(c.rrr, 1.0);     // R grayscale
    else if (DebugChannel < 2.5)
        return float4(c.ggg, 1.0);     // G grayscale
    else
        return float4(c.aaa, 1.0);     // A grayscale
}
