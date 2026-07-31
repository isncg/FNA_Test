// SSR_vs.hlsl — Fullscreen triangle VS for Screen-Space Reflections.
struct VS_OUTPUT
{
    float4 Position : SV_POSITION;
    float2 UV       : TEXCOORD0;
};

VS_OUTPUT VSMain(uint vid : SV_VertexID)
{
    VS_OUTPUT o;
    o.UV = float2((vid << 1) & 2, vid & 2);
    o.Position = float4(o.UV * float2(2, -2) + float2(-1, 1), 0, 1);
    return o;
}
