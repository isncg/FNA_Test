using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;

namespace FNA.Test
{
    /// <summary>Procedural geometry generation — no external model files needed.</summary>
    public static class GeometryGen
    {
        /// <summary>A full-screen quad in NDC [-1,1] with texcoords.</summary>
        public static VertexPositionTexture[] Quad()
        {
            return new VertexPositionTexture[]
            {
                new VertexPositionTexture(new Vector3(-1,  1, 0), new Vector2(0, 0)),
                new VertexPositionTexture(new Vector3( 1,  1, 0), new Vector2(1, 0)),
                new VertexPositionTexture(new Vector3(-1, -1, 0), new Vector2(0, 1)),
                new VertexPositionTexture(new Vector3( 1,  1, 0), new Vector2(1, 0)),
                new VertexPositionTexture(new Vector3( 1, -1, 0), new Vector2(1, 1)),
                new VertexPositionTexture(new Vector3(-1, -1, 0), new Vector2(0, 1)),
            };
        }

        /// <summary>
        /// A unit cube centered at origin, with position + normal + texcoord per vertex.
        /// 6 faces × 2 triangles × 3 verts = 36 vertices.
        /// </summary>
        public static VertexPositionNormalTexture[] Cube()
        {
            var verts = new List<VertexPositionNormalTexture>(36);
            // Face data: normal, tangent vectors, u/v scaling
            AddCubeFace(verts, Vector3.UnitZ,  Vector3.UnitX, Vector3.UnitY);  // front
            AddCubeFace(verts, -Vector3.UnitZ, -Vector3.UnitX, Vector3.UnitY); // back
            AddCubeFace(verts, Vector3.UnitX,  -Vector3.UnitZ, Vector3.UnitY); // right
            AddCubeFace(verts, -Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY);  // left
            AddCubeFace(verts, Vector3.UnitY,  Vector3.UnitZ, Vector3.UnitX);  // top (swap r/u for correct winding)
            AddCubeFace(verts, -Vector3.UnitY, -Vector3.UnitZ, Vector3.UnitX); // bottom (swap r/u for correct winding)
            return verts.ToArray();
        }

        private static void AddCubeFace(List<VertexPositionNormalTexture> verts,
            Vector3 normal, Vector3 right, Vector3 up)
        {
            Vector3 half = normal * 0.5f;
            right *= 0.5f;
            up *= 0.5f;
            // Two triangles per face (CW winding for XNA default CullCounterClockwiseFace)
            // Triangle 1
            verts.Add(MkVPNT(half - right - up, normal, 0, 0));
            verts.Add(MkVPNT(half - right + up, normal, 0, 1));
            verts.Add(MkVPNT(half + right - up, normal, 1, 0));
            // Triangle 2
            verts.Add(MkVPNT(half + right - up, normal, 1, 0));
            verts.Add(MkVPNT(half - right + up, normal, 0, 1));
            verts.Add(MkVPNT(half + right + up, normal, 1, 1));
        }

        private static VertexPositionNormalTexture MkVPNT(Vector3 pos, Vector3 n, float u, float v)
        {
            return new VertexPositionNormalTexture(pos, n, new Vector2(u, v));
        }

        /// <summary>
        /// A unit cube centered at origin, position-only (36 vertices, 12 triangles).
        /// Same topology as Cube() but without normals or texcoords.
        /// </summary>
        public static Vector3[] CubePositions()
        {
            var verts = new Vector3[36];
            int i = 0;
            AddCubePosFace(verts, ref i,  Vector3.UnitZ,  Vector3.UnitX, Vector3.UnitY);  // front
            AddCubePosFace(verts, ref i, -Vector3.UnitZ, -Vector3.UnitX, Vector3.UnitY);  // back
            AddCubePosFace(verts, ref i,  Vector3.UnitX, -Vector3.UnitZ, Vector3.UnitY);  // right
            AddCubePosFace(verts, ref i, -Vector3.UnitX,  Vector3.UnitZ, Vector3.UnitY);  // left
            AddCubePosFace(verts, ref i,  Vector3.UnitY,  Vector3.UnitZ, Vector3.UnitX);  // top
            AddCubePosFace(verts, ref i, -Vector3.UnitY, -Vector3.UnitZ, Vector3.UnitX);  // bottom
            return verts;
        }

        private static void AddCubePosFace(Vector3[] verts, ref int i,
            Vector3 normal, Vector3 right, Vector3 up)
        {
            Vector3 half = normal * 0.5f;
            right *= 0.5f;
            up *= 0.5f;
            // CW winding for XNA default CullCounterClockwiseFace
            // Triangle 1
            verts[i++] = half - right - up;
            verts[i++] = half - right + up;
            verts[i++] = half + right - up;
            // Triangle 2
            verts[i++] = half + right - up;
            verts[i++] = half - right + up;
            verts[i++] = half + right + up;
        }

        /// <summary>
        /// UV sphere centered at origin, radius 1.
        /// Stacks = vertical subdivisions, Slices = horizontal subdivisions.
        /// </summary>
        public static VertexPositionNormalTexture[] Sphere(int stacks, int slices)
        {
            var verts = new List<VertexPositionNormalTexture>(stacks * slices * 6);
            for (int i = 0; i < stacks; i++)
            {
                float phi0 = MathHelper.Pi * (float)i / stacks;
                float phi1 = MathHelper.Pi * (float)(i + 1) / stacks;
                for (int j = 0; j < slices; j++)
                {
                    float theta0 = MathHelper.TwoPi * (float)j / slices;
                    float theta1 = MathHelper.TwoPi * (float)(j + 1) / slices;

                    Vector3 v00 = SpherePoint(phi0, theta0);
                    Vector3 v10 = SpherePoint(phi1, theta0);
                    Vector3 v01 = SpherePoint(phi0, theta1);
                    Vector3 v11 = SpherePoint(phi1, theta1);

                    float u0 = (float)j / slices;
                    float u1 = (float)(j + 1) / slices;
                    float v0 = (float)i / stacks;
                    float v1 = (float)(i + 1) / stacks;

                    // Triangle 1
                    verts.Add(new VertexPositionNormalTexture(v00, v00, new Vector2(u0, v0)));
                    verts.Add(new VertexPositionNormalTexture(v10, v10, new Vector2(u0, v1)));
                    verts.Add(new VertexPositionNormalTexture(v01, v01, new Vector2(u1, v0)));
                    // Triangle 2
                    verts.Add(new VertexPositionNormalTexture(v10, v10, new Vector2(u0, v1)));
                    verts.Add(new VertexPositionNormalTexture(v11, v11, new Vector2(u1, v1)));
                    verts.Add(new VertexPositionNormalTexture(v01, v01, new Vector2(u1, v0)));
                }
            }
            return verts.ToArray();
        }

        private static Vector3 SpherePoint(float phi, float theta)
        {
            float sp = (float)Math.Sin(phi);
            return new Vector3(sp * (float)Math.Cos(theta), (float)Math.Cos(phi), sp * (float)Math.Sin(theta));
        }

        /// <summary>
        /// Skinned cylinder along Y axis, centered at origin, height 2.
        /// Rings = vertical subdivisions, Slices = horizontal subdivisions.
        /// Each vertex has BlendIndices and BlendWeights for 2-bone skinning.
        /// Bone0 = root (Y=-1), Bone1 = pivot (Y=+1). Weights interpolate linearly.
        /// </summary>
        public static SkinnedVertex[] SkinnedCylinder(int rings, int slices)
        {
            var verts = new List<SkinnedVertex>(rings * slices * 6);
            for (int i = 0; i < rings; i++)
            {
                float y0 = -1f + 2f * (float)i / rings;
                float y1 = -1f + 2f * (float)(i + 1) / rings;
                // Weight: 1.0 at bottom (bone0) → 1.0 at top (bone1)
                float w0 = (y0 + 1f) / 2f;
                float w1 = (y1 + 1f) / 2f;

                for (int j = 0; j < slices; j++)
                {
                    float a0 = MathHelper.TwoPi * (float)j / slices;
                    float a1 = MathHelper.TwoPi * (float)(j + 1) / slices;

                    float x0 = (float)Math.Cos(a0), z0 = (float)Math.Sin(a0);
                    float x1 = (float)Math.Cos(a1), z1 = (float)Math.Sin(a1);

                    Vector3 n00 = new Vector3(x0, 0, z0); n00.Normalize();
                    Vector3 n10 = new Vector3(x0, 0, z0); n10.Normalize();
                    Vector3 n01 = new Vector3(x1, 0, z1); n01.Normalize();
                    Vector3 n11 = new Vector3(x1, 0, z1); n11.Normalize();

                    float u0 = (float)j / slices, u1 = (float)(j + 1) / slices;
                    float v0 = (float)i / rings, v1 = (float)(i + 1) / rings;

                    // Bone indices: float4 — cast to int in shader. x=bone0, w=bone1
                    var bi = new Vector4(0, 0, 0, 1);
                    // Triangle 1
                    verts.Add(new SkinnedVertex(
                        new Vector3(x0, y0, z0), n00, new Vector2(u0, v0),
                        bi, new Vector4(1-w0,0,0,w0)));
                    verts.Add(new SkinnedVertex(
                        new Vector3(x1, y0, z1), n01, new Vector2(u1, v0),
                        bi, new Vector4(1-w0,0,0,w0)));
                    verts.Add(new SkinnedVertex(
                        new Vector3(x0, y1, z0), n10, new Vector2(u0, v1),
                        bi, new Vector4(1-w1,0,0,w1)));
                    // Triangle 2
                    verts.Add(new SkinnedVertex(
                        new Vector3(x0, y1, z0), n10, new Vector2(u0, v1),
                        bi, new Vector4(1-w1,0,0,w1)));
                    verts.Add(new SkinnedVertex(
                        new Vector3(x1, y0, z1), n01, new Vector2(u1, v0),
                        bi, new Vector4(1-w0,0,0,w0)));
                    verts.Add(new SkinnedVertex(
                        new Vector3(x1, y1, z1), n11, new Vector2(u1, v1),
                        bi, new Vector4(1-w1,0,0,w1)));
                }
            }
            return verts.ToArray();
        }

        /// <summary>Quad with two texcoord sets (for DualTextureEffect).</summary>
        public static DualTextureVertex[] DualTextureQuad()
        {
            return new DualTextureVertex[]
            {
                new DualTextureVertex(new Vector3(-1,  1, 0), new Vector2(0,0), new Vector2(0,0)),
                new DualTextureVertex(new Vector3( 1,  1, 0), new Vector2(1,0), new Vector2(1,0)),
                new DualTextureVertex(new Vector3(-1, -1, 0), new Vector2(0,1), new Vector2(0,1)),
                new DualTextureVertex(new Vector3( 1,  1, 0), new Vector2(1,0), new Vector2(1,0)),
                new DualTextureVertex(new Vector3( 1, -1, 0), new Vector2(1,1), new Vector2(1,1)),
                new DualTextureVertex(new Vector3(-1, -1, 0), new Vector2(0,1), new Vector2(0,1)),
            };
        }
    }

    /// <summary>Custom vertex for dual-texture rendering.</summary>
    public struct DualTextureVertex : IVertexType
    {
        public Vector3 Position;
        public Vector2 TexCoord0;
        public Vector2 TexCoord1;

        public DualTextureVertex(Vector3 pos, Vector2 tc0, Vector2 tc1)
        {
            Position = pos; TexCoord0 = tc0; TexCoord1 = tc1;
        }

        public static readonly VertexDeclaration VertexDeclaration = new VertexDeclaration(
            new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
            new VertexElement(12, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
            new VertexElement(20, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 1)
        );
        VertexDeclaration IVertexType.VertexDeclaration { get { return VertexDeclaration; } }
    }

    /// <summary>Custom vertex for skinned rendering (2 bones).</summary>
    public struct SkinnedVertex : IVertexType
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 TexCoord;
        public Vector4 BlendIndices;
        public Vector4 BlendWeights;

        public SkinnedVertex(Vector3 pos, Vector3 nrm, Vector2 tc, Vector4 idx, Vector4 wgt)
        {
            Position = pos; Normal = nrm; TexCoord = tc;
            BlendIndices = idx; BlendWeights = wgt;
        }

        public static readonly VertexDeclaration VertexDeclaration = new VertexDeclaration(
            new VertexElement(0,  VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
            new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
            new VertexElement(24, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
            new VertexElement(32, VertexElementFormat.Vector4, VertexElementUsage.BlendIndices, 0),
            new VertexElement(48, VertexElementFormat.Vector4, VertexElementUsage.BlendWeight, 0)
        );
        VertexDeclaration IVertexType.VertexDeclaration { get { return VertexDeclaration; } }
    }
}
