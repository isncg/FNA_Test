using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SceneRenderer;

/// <summary>GPU-ready mesh with vertex buffer, index buffer, and bounds.</summary>
public class Mesh
{
    public string Name = "Unnamed";
    public VertexBuffer? VertexBuffer;
    public IndexBuffer? IndexBuffer;
    public int PrimitiveCount;
    public int VertexCount;
    public BoundingSphere Bounds;

    /// <summary>PNT layout: Position(12), Normal(12), TexCoord(8) = 32 bytes per vertex.</summary>
    public const int PNT_STRIDE = 32;
}
