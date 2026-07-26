using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MaterialLib;

/// <summary>
/// Loader for the Utah teapot .tris model format.
/// Generates UV coordinates via cylindrical mapping since the raw model has no texcoords.
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

    /// <summary>Load a .tris file and generate smooth normals + spherical UVs.</summary>
    public static (Vertex[] vertices, int triangleCount) Load(string path)
    {
        var lines = File.ReadAllLines(path);
        int triCount = int.Parse(lines[0]);
        int vertCount = triCount * 3;

        var positions = new Vector3[vertCount];
        var faceNormals = new Vector3[triCount];

        // Parse all vertex positions and compute face normals
        // Format: count line, then per triangle: 3 vertex lines + 1 blank line
        int vi = 0;
        int lineIdx = 1; // skip the triangle count line
        for (int t = 0; t < triCount; t++)
        {
            // Skip any blank lines between triangles
            while (lineIdx < lines.Length && string.IsNullOrWhiteSpace(lines[lineIdx]))
                lineIdx++;

            var p0 = ParseVec3(lines[lineIdx++]);
            var p1 = ParseVec3(lines[lineIdx++]);
            var p2 = ParseVec3(lines[lineIdx++]);

            // Swap p1↔p2 to reverse winding → CW order for FNA's default
            // CullCounterClockwise (culls CCW, renders CW).
            positions[vi]     = p0;
            positions[vi + 1] = p2;  // v1
            positions[vi + 2] = p1;  // v2

            // CW outward normal: cross(v2-v0, v1-v0) = cross(edge02, edge01)
            var edge01 = p2 - p0;  // v1-v0 (stored CW order)
            var edge02 = p1 - p0;  // v2-v0
            faceNormals[t] = Vector3.Normalize(Vector3.Cross(edge02, edge01));
            vi += 3;
        }

        // Compute smooth vertex normals by averaging face normals
        var vertexNormals = new Vector3[vertCount];
        for (int i = 0; i < vertCount; i++)
        {
            var sum = Vector3.Zero;
            int count = 0;
            for (int j = 0; j < vertCount; j++)
            {
                if (Vector3.DistanceSquared(positions[i], positions[j]) < 0.0001f)
                {
                    sum += faceNormals[j / 3];
                    count++;
                }
            }
            vertexNormals[i] = count > 0 ? Vector3.Normalize(sum / count) : Vector3.Up;
        }

        // Generate spherical UVs
        // Find Y range for better UV mapping
        float yMin = float.MaxValue, yMax = float.MinValue;
        for (int i = 0; i < vertCount; i++)
        {
            yMin = MathF.Min(yMin, positions[i].Y);
            yMax = MathF.Max(yMax, positions[i].Y);
        }

        var vertices = new Vertex[vertCount];
        for (int i = 0; i < vertCount; i++)
        {
            var p = positions[i];
            // Cylindrical UV mapping (good for teapot shape)
            float u = 0.5f + MathF.Atan2(p.X, p.Z) / (2f * MathF.PI);
            float v = (p.Y - yMin) / (yMax - yMin);
            vertices[i] = new Vertex(p, vertexNormals[i], new Vector2(u, v));
        }

        return (vertices, triCount);
    }

    private static Vector3 ParseVec3(string line)
    {
        var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return new Vector3(
            float.Parse(parts[0]),
            float.Parse(parts[1]),
            float.Parse(parts[2])
        );
    }
}
