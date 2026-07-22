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

        /// <summary>
        /// Cylinder along Y axis, centered at origin, radius 1, height 2 (Y=-1 to Y=+1).
        /// Rings = vertical subdivisions, Slices = horizontal subdivisions.
        /// </summary>
        public static VertexPositionNormalTexture[] Cylinder(int rings, int slices)
        {
            var verts = new List<VertexPositionNormalTexture>();

            // Side wall: rings × slices quads, each quad = 2 triangles = 6 verts
            for (int i = 0; i < rings; i++)
            {
                float y0 = -1f + 2f * (float)i / rings;
                float y1 = -1f + 2f * (float)(i + 1) / rings;
                float v0 = (float)i / rings;
                float v1 = (float)(i + 1) / rings;

                for (int j = 0; j < slices; j++)
                {
                    float a0 = MathHelper.TwoPi * (float)j / slices;
                    float a1 = MathHelper.TwoPi * (float)(j + 1) / slices;
                    float u0 = (float)j / slices;
                    float u1 = (float)(j + 1) / slices;

                    float x0 = (float)Math.Cos(a0), z0 = (float)Math.Sin(a0);
                    float x1 = (float)Math.Cos(a1), z1 = (float)Math.Sin(a1);

                    var n00 = new Vector3(x0, 0, z0);
                    var n01 = new Vector3(x1, 0, z1);

                    // Flipped winding for correct outward-facing render
                    verts.Add(new VertexPositionNormalTexture(new Vector3(x0, y0, z0), n00, new Vector2(u0, v0)));
                    verts.Add(new VertexPositionNormalTexture(new Vector3(x1, y0, z1), n01, new Vector2(u1, v0)));
                    verts.Add(new VertexPositionNormalTexture(new Vector3(x0, y1, z0), n00, new Vector2(u0, v1)));
                    verts.Add(new VertexPositionNormalTexture(new Vector3(x0, y1, z0), n00, new Vector2(u0, v1)));
                    verts.Add(new VertexPositionNormalTexture(new Vector3(x1, y0, z1), n01, new Vector2(u1, v0)));
                    verts.Add(new VertexPositionNormalTexture(new Vector3(x1, y1, z1), n01, new Vector2(u1, v1)));
                }
            }

            // Top cap (Y = +1): triangle fan, flipped winding
            var topN = Vector3.UnitY;
            var topC = new Vector3(0, 1, 0);
            for (int j = 0; j < slices; j++)
            {
                float a0 = MathHelper.TwoPi * (float)j / slices;
                float a1 = MathHelper.TwoPi * (float)(j + 1) / slices;
                float u0 = (float)j / slices;
                float u1 = (float)(j + 1) / slices;
                float um = (u0 + u1) * 0.5f;

                float x0 = (float)Math.Cos(a0), z0 = (float)Math.Sin(a0);
                float x1 = (float)Math.Cos(a1), z1 = (float)Math.Sin(a1);

                verts.Add(new VertexPositionNormalTexture(topC, topN, new Vector2(um, 0)));
                verts.Add(new VertexPositionNormalTexture(new Vector3(x1, 1, z1), topN, new Vector2(u1, 0)));
                verts.Add(new VertexPositionNormalTexture(new Vector3(x0, 1, z0), topN, new Vector2(u0, 0)));
            }

            // Bottom cap (Y = -1): triangle fan, flipped winding
            var botN = -Vector3.UnitY;
            var botC = new Vector3(0, -1, 0);
            for (int j = 0; j < slices; j++)
            {
                float a0 = MathHelper.TwoPi * (float)j / slices;
                float a1 = MathHelper.TwoPi * (float)(j + 1) / slices;
                float u0 = (float)j / slices;
                float u1 = (float)(j + 1) / slices;
                float um = (u0 + u1) * 0.5f;

                float x0 = (float)Math.Cos(a0), z0 = (float)Math.Sin(a0);
                float x1 = (float)Math.Cos(a1), z1 = (float)Math.Sin(a1);

                verts.Add(new VertexPositionNormalTexture(botC, botN, new Vector2(um, 0)));
                verts.Add(new VertexPositionNormalTexture(new Vector3(x0, -1, z0), botN, new Vector2(u0, 0)));
                verts.Add(new VertexPositionNormalTexture(new Vector3(x1, -1, z1), botN, new Vector2(u1, 0)));
            }

            return verts.ToArray();
        }

        /// <summary>
        /// Cone along Y axis, centered at origin. Base at Y=-1 (radius=1), apex at Y=+1 (radius=0).
        /// Rings = vertical subdivisions, Slices = horizontal subdivisions.
        /// </summary>
        public static VertexPositionNormalTexture[] Cone(int rings, int slices)
        {
            var verts = new List<VertexPositionNormalTexture>();

            // Side wall: rings × slices quads with tapering radius
            for (int i = 0; i < rings; i++)
            {
                float t0 = (float)i / rings;
                float t1 = (float)(i + 1) / rings;
                float y0 = -1f + 2f * t0;
                float y1 = -1f + 2f * t1;
                float r0 = 1f - t0;
                float r1 = 1f - t1;
                float v0 = t0;
                float v1 = t1;

                // Cone normal: angled outward. For a cone with half-angle α:
                // tan(α) = 1/2 → α ≈ 26.565°
                // Normal has cos(α) horizontally and sin(α) vertically
                // cos(α) = 2/√5 ≈ 0.8944, sin(α) = 1/√5 ≈ 0.4472
                float nH = 0.894427f;
                float nV = 0.447214f;

                for (int j = 0; j < slices; j++)
                {
                    float a0 = MathHelper.TwoPi * (float)j / slices;
                    float a1 = MathHelper.TwoPi * (float)(j + 1) / slices;
                    float u0 = (float)j / slices;
                    float u1 = (float)(j + 1) / slices;

                    float cx0 = (float)Math.Cos(a0), sz0 = (float)Math.Sin(a0);
                    float cx1 = (float)Math.Cos(a1), sz1 = (float)Math.Sin(a1);

                    // Normals at each corner (radial direction × cos(α) + up × sin(α))
                    var n00 = new Vector3(cx0 * nH, nV, sz0 * nH);
                    var n01 = new Vector3(cx1 * nH, nV, sz1 * nH);

                    Vector3 v00 = new Vector3(cx0 * r0, y0, sz0 * r0);
                    Vector3 v10 = new Vector3(cx0 * r1, y1, sz0 * r1);
                    Vector3 v01 = new Vector3(cx1 * r0, y0, sz1 * r0);
                    Vector3 v11 = new Vector3(cx1 * r1, y1, sz1 * r1);

                    verts.Add(new VertexPositionNormalTexture(v00, n00, new Vector2(u0, v0)));
                    verts.Add(new VertexPositionNormalTexture(v01, n01, new Vector2(u1, v0)));
                    verts.Add(new VertexPositionNormalTexture(v10, n00, new Vector2(u0, v1)));
                    verts.Add(new VertexPositionNormalTexture(v10, n00, new Vector2(u0, v1)));
                    verts.Add(new VertexPositionNormalTexture(v01, n01, new Vector2(u1, v0)));
                    verts.Add(new VertexPositionNormalTexture(v11, n01, new Vector2(u1, v1)));
                }
            }

            // Base cap (Y = -1): triangle fan, flipped winding
            var baseN = -Vector3.UnitY;
            var baseC = new Vector3(0, -1, 0);
            for (int j = 0; j < slices; j++)
            {
                float a0 = MathHelper.TwoPi * (float)j / slices;
                float a1 = MathHelper.TwoPi * (float)(j + 1) / slices;
                float u0 = (float)j / slices;
                float u1 = (float)(j + 1) / slices;
                float um = (u0 + u1) * 0.5f;

                float x0 = (float)Math.Cos(a0), z0 = (float)Math.Sin(a0);
                float x1 = (float)Math.Cos(a1), z1 = (float)Math.Sin(a1);

                verts.Add(new VertexPositionNormalTexture(baseC, baseN, new Vector2(um, 0)));
                verts.Add(new VertexPositionNormalTexture(new Vector3(x0, -1, z0), baseN, new Vector2(u0, 0)));
                verts.Add(new VertexPositionNormalTexture(new Vector3(x1, -1, z1), baseN, new Vector2(u1, 0)));
            }

            return verts.ToArray();
        }

        /// <summary>
        /// Square pyramid centered at origin. Base is at Y = -height/2 (halfSize × halfSize),
        /// apex at Y = +height/2. Each triangular side face = 2 triangles, base = 2 triangles.
        /// </summary>
        public static VertexPositionNormalTexture[] Pyramid(float halfSize, float height)
        {
            var verts = new List<VertexPositionNormalTexture>();
            float hh = height * 0.5f;
            float s = halfSize;

            Vector3 apex   = new Vector3(0,  hh, 0);
            Vector3 bl     = new Vector3(-s, -hh, -s);
            Vector3 br     = new Vector3( s, -hh, -s);
            Vector3 tl     = new Vector3(-s, -hh,  s);
            Vector3 tr     = new Vector3( s, -hh,  s);

            // Helper: one triangular face
            void AddTri(Vector3 a, Vector3 b, Vector3 c, Vector3 n, Vector2 uva, Vector2 uvb, Vector2 uvc)
            {
                verts.Add(new VertexPositionNormalTexture(a, n, uva));
                verts.Add(new VertexPositionNormalTexture(b, n, uvb));
                verts.Add(new VertexPositionNormalTexture(c, n, uvc));
            }

            // Face normals: compute from cross product of edges
            Vector3 frontN  = ComputeNormal(bl, apex, br);  // front (-Z face)
            Vector3 backN   = ComputeNormal(tr, apex, tl);  // back (+Z face)
            Vector3 leftN   = ComputeNormal(tl, apex, bl);  // left (-X face)
            Vector3 rightN  = ComputeNormal(br, apex, tr);  // right (+X face)
            Vector3 baseN   = -Vector3.UnitY;

            // Front face (-Z): BL → apex → BR
            AddTri(bl, apex, br, frontN, new Vector2(0,0), new Vector2(0.5f,1), new Vector2(1,0));
            // Back face (+Z): TR → apex → TL
            AddTri(tr, apex, tl, backN, new Vector2(0,0), new Vector2(0.5f,1), new Vector2(1,0));
            // Left face (-X): TL → apex → BL
            AddTri(tl, apex, bl, leftN, new Vector2(0,0), new Vector2(0.5f,1), new Vector2(1,0));
            // Right face (+X): BR → apex → TR
            AddTri(br, apex, tr, rightN, new Vector2(0,0), new Vector2(0.5f,1), new Vector2(1,0));

            // Base (two triangles): flipped winding for downward-facing normal
            AddTri(bl, br, tl, baseN, new Vector2(0,0), new Vector2(1,0), new Vector2(0,1));
            AddTri(tl, br, tr, baseN, new Vector2(0,1), new Vector2(1,0), new Vector2(1,1));

            return verts.ToArray();
        }

        private static Vector3 ComputeNormal(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 d = Vector3.Normalize(b - a);
            Vector3 e = Vector3.Normalize(c - a);
            Vector3 n = Vector3.Cross(d, e);
            n.Normalize();
            return n;
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
