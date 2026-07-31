// Fullscreen_vs.hlsl — Shared fullscreen triangle vertex shader.
// Generates a single triangle covering NDC [-1,1]×[-1,1] from SV_VertexID.
// No vertex buffer required — all vertex data is procedurally generated.
//
// Usage: Set no vertex buffer before DrawPrimitives(3). The rasterizer
// state must be CullNone (the triangle covers the full viewport).
//
// Reference: FNA3D_HLSL convention — positions use D3D viewport
// (y=+1 at top, y=-1 at bottom).

struct VS_OUTPUT
{
    float4 Position : SV_POSITION;
    float2 UV       : TEXCOORD0;
};

VS_OUTPUT VSMain(uint vid : SV_VertexID)
{
    VS_OUTPUT o;
    // Generate NDC position from vertex ID (3-vertex triangle strip logic)
    // vid=0 → (-1, 1), vid=1 → (3, 1), vid=2 → (-1, -3)
    o.UV = float2((vid << 1) & 2, vid & 2);
    o.Position = float4(o.UV * float2(2, -2) + float2(-1, 1), 0, 1);
    return o;
}
