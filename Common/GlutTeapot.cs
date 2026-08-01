using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNA.Test
{
    /// <summary>
    /// Generates the Utah teapot the same way GLUT's <c>glutSolidTeapot</c> does
    /// (lib/glut/glut_teapot.c): 10 bicubic Bezier patches, mirrored into the
    /// classic 32, tessellated on a uniform parameter grid.
    ///
    /// This replaces loading a pre-triangulated .tris dump, and fixes texturing
    /// at the source: GLUT feeds a 2x2 texcoord control mesh over the same
    /// parameter domain as the surface, which evaluates to exactly (u, v). So
    /// each patch gets UV [0,1]^2 straight from the Bezier parameters instead of
    /// a projection guessed from the vertex positions. Normals likewise come
    /// from the analytic partial derivatives, the equivalent of GL_AUTO_NORMAL,
    /// so no position welding is needed.
    /// </summary>
    public static class GlutTeapot
    {
        /* Rim, body, lid and bottom are reflected in both x and y; handle and
         * spout across y only. Index into ControlPoints.
         */
        private static readonly int[][] PatchData =
        {
            // rim
            new[] { 102, 103, 104, 105, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 },
            // body
            new[] { 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27 },
            new[] { 24, 25, 26, 27, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40 },
            // lid
            new[] { 96, 96, 96, 96, 97, 98, 99, 100, 101, 101, 101, 101, 0, 1, 2, 3 },
            new[] { 0, 1, 2, 3, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 117 },
            // bottom
            new[] { 118, 118, 118, 118, 124, 122, 119, 121, 123, 126, 125, 120, 40, 39, 38, 37 },
            // handle
            new[] { 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56 },
            new[] { 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63, 64, 28, 65, 66, 67 },
            // spout
            new[] { 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83 },
            new[] { 80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95 },
        };

        /// <summary>The 127 control points, in GLUT's original Z-up space.</summary>
        private static readonly Vector3[] ControlPoints =
        {
            new(0.2f, 0f, 2.7f), new(0.2f, -0.112f, 2.7f), new(0.112f, -0.2f, 2.7f), new(0f, -0.2f, 2.7f),
            new(1.3375f, 0f, 2.53125f), new(1.3375f, -0.749f, 2.53125f), new(0.749f, -1.3375f, 2.53125f), new(0f, -1.3375f, 2.53125f),
            new(1.4375f, 0f, 2.53125f), new(1.4375f, -0.805f, 2.53125f), new(0.805f, -1.4375f, 2.53125f), new(0f, -1.4375f, 2.53125f),
            new(1.5f, 0f, 2.4f), new(1.5f, -0.84f, 2.4f), new(0.84f, -1.5f, 2.4f), new(0f, -1.5f, 2.4f),
            new(1.75f, 0f, 1.875f), new(1.75f, -0.98f, 1.875f), new(0.98f, -1.75f, 1.875f), new(0f, -1.75f, 1.875f),
            new(2f, 0f, 1.35f), new(2f, -1.12f, 1.35f), new(1.12f, -2f, 1.35f), new(0f, -2f, 1.35f),
            new(2f, 0f, 0.9f), new(2f, -1.12f, 0.9f), new(1.12f, -2f, 0.9f), new(0f, -2f, 0.9f),
            new(-2f, 0f, 0.9f),
            new(2f, 0f, 0.45f), new(2f, -1.12f, 0.45f), new(1.12f, -2f, 0.45f), new(0f, -2f, 0.45f),
            new(1.5f, 0f, 0.225f), new(1.5f, -0.84f, 0.225f), new(0.84f, -1.5f, 0.225f), new(0f, -1.5f, 0.225f),
            new(1.5f, 0f, 0.15f), new(1.5f, -0.84f, 0.15f), new(0.84f, -1.5f, 0.15f), new(0f, -1.5f, 0.15f),
            new(-1.6f, 0f, 2.025f), new(-1.6f, -0.3f, 2.025f), new(-1.5f, -0.3f, 2.25f), new(-1.5f, 0f, 2.25f),
            new(-2.3f, 0f, 2.025f), new(-2.3f, -0.3f, 2.025f), new(-2.5f, -0.3f, 2.25f), new(-2.5f, 0f, 2.25f),
            new(-2.7f, 0f, 2.025f), new(-2.7f, -0.3f, 2.025f), new(-3f, -0.3f, 2.25f), new(-3f, 0f, 2.25f),
            new(-2.7f, 0f, 1.8f), new(-2.7f, -0.3f, 1.8f), new(-3f, -0.3f, 1.8f), new(-3f, 0f, 1.8f),
            new(-2.7f, 0f, 1.575f), new(-2.7f, -0.3f, 1.575f), new(-3f, -0.3f, 1.35f), new(-3f, 0f, 1.35f),
            new(-2.5f, 0f, 1.125f), new(-2.5f, -0.3f, 1.125f), new(-2.65f, -0.3f, 0.9375f), new(-2.65f, 0f, 0.9375f),
            new(-2f, -0.3f, 0.9f), new(-1.9f, -0.3f, 0.6f), new(-1.9f, 0f, 0.6f),
            new(1.7f, 0f, 1.425f), new(1.7f, -0.66f, 1.425f), new(1.7f, -0.66f, 0.6f), new(1.7f, 0f, 0.6f),
            new(2.6f, 0f, 1.425f), new(2.6f, -0.66f, 1.425f), new(3.1f, -0.66f, 0.825f), new(3.1f, 0f, 0.825f),
            new(2.3f, 0f, 2.1f), new(2.3f, -0.25f, 2.1f), new(2.4f, -0.25f, 2.025f), new(2.4f, 0f, 2.025f),
            new(2.7f, 0f, 2.4f), new(2.7f, -0.25f, 2.4f), new(3.3f, -0.25f, 2.4f), new(3.3f, 0f, 2.4f),
            new(2.8f, 0f, 2.475f), new(2.8f, -0.25f, 2.475f), new(3.525f, -0.25f, 2.49375f), new(3.525f, 0f, 2.49375f),
            new(2.9f, 0f, 2.475f), new(2.9f, -0.15f, 2.475f), new(3.45f, -0.15f, 2.5125f), new(3.45f, 0f, 2.5125f),
            new(2.8f, 0f, 2.4f), new(2.8f, -0.15f, 2.4f), new(3.2f, -0.15f, 2.4f), new(3.2f, 0f, 2.4f),
            new(0f, 0f, 3.15f), new(0.8f, 0f, 3.15f), new(0.8f, -0.45f, 3.15f), new(0.45f, -0.8f, 3.15f), new(0f, -0.8f, 3.15f),
            new(0f, 0f, 2.85f),
            new(1.4f, 0f, 2.4f), new(1.4f, -0.784f, 2.4f), new(0.784f, -1.4f, 2.4f), new(0f, -1.4f, 2.4f),
            new(0.4f, 0f, 2.55f), new(0.4f, -0.224f, 2.55f), new(0.224f, -0.4f, 2.55f), new(0f, -0.4f, 2.55f),
            new(1.3f, 0f, 2.55f), new(1.3f, -0.728f, 2.55f), new(0.728f, -1.3f, 2.55f), new(0f, -1.3f, 2.55f),
            new(1.3f, 0f, 2.4f), new(1.3f, -0.728f, 2.4f), new(0.728f, -1.3f, 2.4f), new(0f, -1.3f, 2.4f),
            new(0f, 0f, 0f),
            new(1.425f, -0.798f, 0f), new(1.5f, 0f, 0.075f), new(1.425f, 0f, 0f),
            new(0.798f, -1.425f, 0f), new(0f, -1.5f, 0.075f), new(0f, -1.425f, 0f),
            new(1.5f, -0.84f, 0.075f), new(0.84f, -1.5f, 0.075f),
        };

        /// <summary>
        /// Builds the teapot as a triangle list. <paramref name="grid"/> is the
        /// tessellation per patch per axis (GLUT uses 7 for glutSolidTeapot);
        /// <paramref name="scale"/> matches glutSolidTeapot's scale argument.
        ///
        /// Vertices come out Y-up, centred on the origin horizontally, with the
        /// same orientation GLUT produces. Winding is clockwise when seen from
        /// outside, which is what RasterizerState.CullCounterClockwise wants.
        /// </summary>
        public static VertexPositionNormalTexture[] Build(int grid = 7, float scale = 1f)
        {
            if (grid < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(grid));
            }

            var verts = new List<VertexPositionNormalTexture>(
                PatchData.Length * 4 * grid * grid * 6);

            var p = new Vector3[4, 4];
            var q = new Vector3[4, 4];
            var r = new Vector3[4, 4];
            var s = new Vector3[4, 4];

            for (int i = 0; i < PatchData.Length; i += 1)
            {
                int[] patch = PatchData[i];
                for (int j = 0; j < 4; j += 1)
                {
                    for (int k = 0; k < 4; k += 1)
                    {
                        Vector3 cp = ControlPoints[patch[j * 4 + k]];
                        Vector3 cpRev = ControlPoints[patch[j * 4 + (3 - k)]];

                        p[j, k] = cp;
                        // Mirror in Y, with u reversed so the surface normal stays outward
                        q[j, k] = new Vector3(cpRev.X, -cpRev.Y, cpRev.Z);
                        // Mirror in X (also u reversed), and in both axes
                        r[j, k] = new Vector3(-cpRev.X, cpRev.Y, cpRev.Z);
                        s[j, k] = new Vector3(-cp.X, -cp.Y, cp.Z);
                    }
                }

                Tessellate(verts, p, grid, scale);
                Tessellate(verts, q, grid, scale);
                if (i < 6)
                {
                    Tessellate(verts, r, grid, scale);
                    Tessellate(verts, s, grid, scale);
                }
            }

            return verts.ToArray();
        }

        private static void Tessellate(List<VertexPositionNormalTexture> verts,
            Vector3[,] ctrl, int grid, float scale)
        {
            for (int iu = 0; iu < grid; iu += 1)
            {
                for (int iv = 0; iv < grid; iv += 1)
                {
                    float u0 = (float) iu / grid, u1 = (float) (iu + 1) / grid;
                    float v0 = (float) iv / grid, v1 = (float) (iv + 1) / grid;

                    var a = Evaluate(ctrl, u0, v0, scale);
                    var b = Evaluate(ctrl, u1, v0, scale);
                    var c = Evaluate(ctrl, u1, v1, scale);
                    var d = Evaluate(ctrl, u0, v1, scale);

                    /* (a, b, c) is counter-clockwise seen from outside because
                     * the analytic normal is dS/du x dS/dv, so emit the reverse
                     * for CullCounterClockwise, which renders clockwise faces.
                     */
                    verts.Add(a); verts.Add(c); verts.Add(b);
                    verts.Add(a); verts.Add(d); verts.Add(c);
                }
            }
        }

        private static VertexPositionNormalTexture Evaluate(Vector3[,] ctrl,
            float u, float v, float scale)
        {
            Bernstein(u, out float bu0, out float bu1, out float bu2, out float bu3);
            Bernstein(v, out float bv0, out float bv1, out float bv2, out float bv3);
            BernsteinDeriv(u, out float du0, out float du1, out float du2, out float du3);
            BernsteinDeriv(v, out float dv0, out float dv1, out float dv2, out float dv3);

            Span<float> bU = stackalloc float[4] { bu0, bu1, bu2, bu3 };
            Span<float> bV = stackalloc float[4] { bv0, bv1, bv2, bv3 };
            Span<float> dU = stackalloc float[4] { du0, du1, du2, du3 };
            Span<float> dV = stackalloc float[4] { dv0, dv1, dv2, dv3 };

            Vector3 pos = Vector3.Zero, tanU = Vector3.Zero, tanV = Vector3.Zero;
            for (int j = 0; j < 4; j += 1)
            {
                for (int k = 0; k < 4; k += 1)
                {
                    Vector3 cp = ctrl[j, k];
                    pos += bV[j] * bU[k] * cp;
                    tanU += bV[j] * dU[k] * cp;
                    tanV += dV[j] * bU[k] * cp;
                }
            }

            /* The equivalent of GL_AUTO_NORMAL. Outward for all four mirrored
             * variants: mirroring flips the cross product's handedness and the
             * reversed u parameter flips it back.
             */
            Vector3 normal = Vector3.Cross(tanU, tanV);
            if (normal.LengthSquared() > 1e-12f)
            {
                normal = Vector3.Normalize(normal);
            }
            else
            {
                /* Degenerate at the lid and bottom poles, where a whole control
                 * row is the same point. Fall back to the axis direction.
                 */
                normal = new Vector3(0f, 0f, pos.Z >= 1.5f ? 1f : -1f);
            }

            /* GLUT's transform: glRotatef(270, 1,0,0), glScalef(0.5s),
             * glTranslatef(0, 0, -1.5). Applied to the vertex in reverse order,
             * and the rotation maps (x, y, z) -> (x, z, -y), giving Y-up.
             */
            Vector3 t = 0.5f * scale * new Vector3(pos.X, pos.Y, pos.Z - 1.5f);
            var outPos = new Vector3(t.X, t.Z, -t.Y);
            var outNormal = new Vector3(normal.X, normal.Z, -normal.Y);

            // GLUT maps a 2x2 texcoord control mesh over the patch domain, which
            // evaluates to exactly (u, v).
            return new VertexPositionNormalTexture(outPos, outNormal, new Vector2(u, v));
        }

        private static void Bernstein(float t, out float b0, out float b1,
            out float b2, out float b3)
        {
            float mt = 1f - t;
            b0 = mt * mt * mt;
            b1 = 3f * mt * mt * t;
            b2 = 3f * mt * t * t;
            b3 = t * t * t;
        }

        private static void BernsteinDeriv(float t, out float d0, out float d1,
            out float d2, out float d3)
        {
            float mt = 1f - t;
            d0 = -3f * mt * mt;
            d1 = 3f * mt * mt - 6f * mt * t;
            d2 = 6f * mt * t - 3f * t * t;
            d3 = 3f * t * t;
        }
    }
}
