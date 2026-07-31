// Skybox_vs.hlsl — Fullscreen triangle VS for equirectangular skybox.
// Computes world-space view direction from camera basis vectors.
// (Identical to MaterialLib version.)

float3 CameraForward : register(c0);
float3 CameraRight   : register(c1);
float3 CameraUp      : register(c2);
float2 FovParams     : register(c3);

struct VS_OUTPUT
{
    float4 Position : SV_POSITION;
    float3 ViewDir  : TEXCOORD0;
};

VS_OUTPUT VSMain(uint vid : SV_VertexID)
{
    VS_OUTPUT output;

    float2 uv = float2((vid << 1) & 2, vid & 2);
    float2 ndc = float2(uv * float2(2, -2) + float2(-1, 1));

    output.Position = float4(ndc, 1.0, 1.0);

    float3 dir = CameraForward
               - ndc.x * FovParams.x * CameraRight
               + ndc.y * FovParams.y * CameraUp;
    output.ViewDir = dir;

    return output;
}
