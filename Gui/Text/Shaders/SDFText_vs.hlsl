// SDF Text Vertex Shader
// Transforms screen-space glyph quads for SDF font rendering.
//
// Convention C1/C2: VS_INPUT field order matches VertexPositionColorTexture
// element order exactly. Only Position/Color/TexCoord are declared.
//
// Descriptor Set layout:
//   Set 1 Binding 0: $Globals { float4x4 MatrixTransform; }

float4x4 MatrixTransform : register(c0);

struct VS_INPUT
{
    float3 Position : POSITION0;     // location 0
    float4 Color    : COLOR0;        // location 1 — BGRA → normalized RGBA
    float2 TexCoord : TEXCOORD0;     // location 2
};

struct VS_OUTPUT
{
    float4 Position : SV_POSITION;
    float4 Color    : COLOR0;
    float2 TexCoord : TEXCOORD0;
};

VS_OUTPUT VSMain(VS_INPUT input)
{
    VS_OUTPUT output;
    output.Position = mul(float4(input.Position, 1.0), MatrixTransform);
    output.Color    = input.Color;
    output.TexCoord = input.TexCoord;
    return output;
}
