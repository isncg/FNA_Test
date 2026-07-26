// Skybox_vs.hlsl — Fullscreen triangle VS for equirectangular skybox.
// Computes the world-space view direction for each vertex using the
// camera's basis vectors and the field-of-view.  No matrix inversion
// needed — avoids Vulkan vs D3D viewport Y-convention ambiguity.
//
// For a vertex at NDC (x, y), the world-space view direction is:
//   forward + x * fovX * right + y * fovY * up
// then normalised in the pixel shader.

float3 CameraForward : register(c0); // world-space camera forward
float3 CameraRight   : register(c1); // world-space camera right
float3 CameraUp      : register(c2); // world-space camera up
float2 FovParams     : register(c3); // x = tanHalfFov*aspect, y = tanHalfFov

struct VS_INPUT
{
    float3 Position : POSITION0;
    float2 TexCoord : TEXCOORD0;
};

struct VS_OUTPUT
{
    float4 Position : SV_POSITION;
    float3 ViewDir  : TEXCOORD0; // unnormalised — PS will normalise
};

VS_OUTPUT VSMain(VS_INPUT input)
{
    VS_OUTPUT output;

    // Fullscreen triangle: clip-space position (already in NDC).
    // z=1.0 (far plane) so the skybox is behind all geometry.
    output.Position = float4(input.Position.xy, 1.0, 1.0);

    // Compute world-space view direction from NDC using camera basis.
    //
    // FNA3D_HLSL on Vulkan: the fullscreen triangle's Position.x is
    // interpolated in screen space with a sign flip relative to the
    // world-space Right vector.  X is negated to correct the horizontal
    // rotation direction (dragging right must shift the skybox left).
    //
    // Y is correct after HdriLoader fix (no negation needed).
    float3 dir = CameraForward
               - input.Position.x * FovParams.x * CameraRight
               + input.Position.y * FovParams.y * CameraUp;
    output.ViewDir = dir;

    return output;
}
