using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MaterialLib;

/// <summary>
/// PNT vertex type used by this demo's meshes.
///
/// This used to also load the Utah teapot from a .tris dump and invent normals
/// and UVs for it: normals by welding coincident positions (O(n^2)) and UVs by
/// cylindrical projection. The teapot now comes from <c>GlutTeapot</c>, which
/// evaluates GLUT's Bezier patches and gets both attributes exactly, so only the
/// vertex layout is left here.
/// </summary>
public static class TeapotModel
{
    public struct Vertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 TexCoord;

        public Vertex(Vector3 pos, Vector3 norm, Vector2 uv)
        {
            Position = pos;
            Normal = norm;
            TexCoord = uv;
        }
    }

    public static readonly VertexDeclaration VertexDeclaration = new(
        new VertexElement(0,  VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
        new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
        new VertexElement(24, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0)
    );

    public const int Stride = 32; // Vector3(12) + Vector3(12) + Vector2(8)
}
