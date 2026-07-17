using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNA.Test;

namespace BasicEffectMatrix
{
    /// <summary>
    /// B1-B9 test suite for BasicEffect multi-technique vertex convention.
    /// Tests all 4 techniques (PNT, PT, PC, PCT) plus fog, alternating, degrade.
    /// </summary>
    public class MatrixTest : Game
    {
        private GraphicsDeviceManager graphics;
        private BasicEffect effect;
        private Texture2D checkerTex, whiteTex;
        private int testPhase;
        private int totalFailures;
        private VertexBuffer quadPT, quadPC, quadPCT;
        private VertexBuffer cube;
        private Matrix view, proj;

        public MatrixTest()
        {
            graphics = new GraphicsDeviceManager(this) {
                PreferredBackBufferWidth = 800, PreferredBackBufferHeight = 600,
                SynchronizeWithVerticalRetrace = false
            };
        }

        protected override void LoadContent()
        {
            effect = new BasicEffect(GraphicsDevice);
            effect.EnableDefaultLighting();
            checkerTex = TextureGen.Checkerboard(GraphicsDevice, 256, 32, Color.Orange, Color.White);
            whiteTex = TextureGen.White(GraphicsDevice);

            // Shared orthographic projection for pixel-predictable tests
            view = Matrix.Identity;
            proj = Matrix.CreateOrthographicOffCenter(0, 800, 600, 0, -1, 1);

            effect.View = view;
            effect.Projection = proj;
            effect.World = Matrix.Identity;
            effect.LightingEnabled = false;
            effect.TextureEnabled = false;
            effect.VertexColorEnabled = false;
            effect.FogEnabled = false;

            // PT quad (Position + TexCoord) — top-left 200x200
            quadPT = MakePTQuad();
            // PC quad (Position + Color) — top-right 200x200 with known color
            quadPC = MakePCQuad();
            // PCT quad (Position + Color + TexCoord)
            quadPCT = MakePCTQuad();

            // VPNT cube
            cube = new VertexBuffer(GraphicsDevice, typeof(VertexPositionNormalTexture),
                GeometryGen.Cube().Length, BufferUsage.WriteOnly);
            cube.SetData(GeometryGen.Cube());
        }

        private VertexBuffer MakePTQuad()
        {
            float l = 100, t = 100, r = 300, b = 300;
            var v = new VertexPositionTexture[] {
                new(new Vector3(l, t, 0), new Vector2(0, 0)),
                new(new Vector3(r, t, 0), new Vector2(1, 0)),
                new(new Vector3(l, b, 0), new Vector2(0, 1)),
                new(new Vector3(r, t, 0), new Vector2(1, 0)),
                new(new Vector3(r, b, 0), new Vector2(1, 1)),
                new(new Vector3(l, b, 0), new Vector2(0, 1)),
            };
            var vb = new VertexBuffer(GraphicsDevice, typeof(VertexPositionTexture), 6, BufferUsage.WriteOnly);
            vb.SetData(v);
            return vb;
        }

        private VertexBuffer MakePCQuad()
        {
            float l = 500, t = 100, r = 700, b = 300;
            // Use distinct R,G,B channels to verify BGRA byte order
            var c = new Color(0xCC, 0x66, 0x33, 0xFF); // R=204, G=102, B=51
            var v = new VertexPositionColor[] {
                new(new Vector3(l, t, 0), c),
                new(new Vector3(r, t, 0), c),
                new(new Vector3(l, b, 0), c),
                new(new Vector3(r, t, 0), c),
                new(new Vector3(r, b, 0), c),
                new(new Vector3(l, b, 0), c),
            };
            var vb = new VertexBuffer(GraphicsDevice, typeof(VertexPositionColor), 6, BufferUsage.WriteOnly);
            vb.SetData(v);
            return vb;
        }

        private VertexBuffer MakePCTQuad()
        {
            float l = 100, t = 350, r = 300, b = 550;
            var c = new Color(0x80, 0x80, 0xFF, 0xFF); // blue-ish vertex color
            var v = new VertexPositionColorTexture[] {
                new(new Vector3(l, t, 0), c, new Vector2(0, 0)),
                new(new Vector3(r, t, 0), c, new Vector2(1, 0)),
                new(new Vector3(l, b, 0), c, new Vector2(0, 1)),
                new(new Vector3(r, t, 0), c, new Vector2(1, 0)),
                new(new Vector3(r, b, 0), c, new Vector2(1, 1)),
                new(new Vector3(l, b, 0), c, new Vector2(0, 1)),
            };
            var vb = new VertexBuffer(GraphicsDevice, typeof(VertexPositionColorTexture), 6, BufferUsage.WriteOnly);
            vb.SetData(v);
            return vb;
        }

        protected override void Update(GameTime gt)
        {
            TestHarness.Tick(this, 3, RunAllTests);
        }

        private void RunAllTests()
        {
            // We need to render each test on its own frame, then read back.
            // Since we're in the assertion callback, we run sub-tests by
            // directly invoking Draw for each phase and reading back.
            totalFailures = 0;

            B1_PNT_Lit();
            B3_PT_Tex();
            B4_PT_Solid();
            B5_PC_VertexColor();
            B6_PCT_VertexColorTex();
            B7_Alternating();
            B8_Fog();
            B9_VCLitDegrade();

            TestHarness.Report("BasicEffectMatrix", totalFailures);
        }

        // ─── Test helpers ───────────────────────────────────────────

        private Color[] RenderOneFrame(Action setup)
        {
            setup();
            GraphicsDevice.Clear(Color.CornflowerBlue);
            effect.CurrentTechnique.Passes[0].Apply();
            GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, 2);
            // Force GPU flush by reading backbuffer
            return TestHarness.ReadBackbuffer(GraphicsDevice);
        }

        private void ResetEffect()
        {
            effect.World = Matrix.Identity;
            effect.View = view;
            effect.Projection = proj;
            effect.LightingEnabled = false;
            effect.TextureEnabled = false;
            effect.VertexColorEnabled = false;
            effect.FogEnabled = false;
            effect.DiffuseColor = Vector3.One;
            effect.Texture = null;
        }

        /// <summary>Prime the effect: Apply once to trigger OnApply (may switch technique),
        /// then get the pass from the now-correct CurrentTechnique and Apply again.</summary>
        private void PrimeEffect()
        {
            effect.CurrentTechnique.Passes[0].Apply();
        }

        // ─── B1: PNT lit ────────────────────────────────────────────

        private void B1_PNT_Lit()
        {
            // Use a 3D perspective to test the lit cube
            var perspProj = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 800f / 600f, 0.1f, 100f);
            var camPos = new Vector3(0, 0, 3);
            var lookAt = Matrix.CreateLookAt(camPos, Vector3.Zero, Vector3.Up);

            ResetEffect();
            effect.View = lookAt;
            effect.Projection = perspProj;
            effect.World = Matrix.Identity;
            effect.LightingEnabled = true;
            effect.PreferPerPixelLighting = false;

            GraphicsDevice.Clear(Color.CornflowerBlue);
            effect.CurrentTechnique.Passes[0].Apply();
            GraphicsDevice.SetVertexBuffer(cube);
            GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, 12);

            var px = TestHarness.ReadBackbuffer(GraphicsDevice);
            // Front face center should be lit (non-background)
            totalFailures += TestHarness.AssertCoverage(px, Color.CornflowerBlue, 0.02f, "B1-PNT-lit-coverage");
        }

        // ─── B3: PT tex ─────────────────────────────────────────────

        private void B3_PT_Tex()
        {
            ResetEffect();
            effect.TextureEnabled = true;
            effect.Texture = checkerTex;
            PrimeEffect();

            GraphicsDevice.Clear(Color.CornflowerBlue);
            effect.CurrentTechnique.Passes[0].Apply();
            GraphicsDevice.SetVertexBuffer(quadPT);
            GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, 2);

            var px = TestHarness.ReadBackbuffer(GraphicsDevice);
            // Checkerboard quad visible
            totalFailures += TestHarness.AssertCoverage(px, Color.CornflowerBlue, 0.03f, "B3-PT-tex-coverage");

            // Sample center of quad — should be orange or white from checker
            totalFailures += TestHarness.AssertPixel(px, 800, 200, 200, Color.Orange, 80, "B3-PT-orange");
        }

        // ─── B4: PT solid (no texture, pure DiffuseColor) ────────────

        private void B4_PT_Solid()
        {
            ResetEffect();
            effect.DiffuseColor = new Vector3(1, 0, 0); // pure red
            effect.TextureEnabled = false;
            PrimeEffect();

            GraphicsDevice.Clear(Color.CornflowerBlue);
            effect.CurrentTechnique.Passes[0].Apply();
            GraphicsDevice.SetVertexBuffer(quadPT);
            GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, 2);

            var px = TestHarness.ReadBackbuffer(GraphicsDevice);
            // Center of quad should be red
            totalFailures += TestHarness.AssertPixel(px, 800, 200, 200, Color.Red, 20, "B4-PT-red");
        }

        // ─── B5: PC vertex color (BGRA byte order check) ─────────────

        private void B5_PC_VertexColor()
        {
            ResetEffect();
            effect.VertexColorEnabled = true;
            PrimeEffect();

            GraphicsDevice.Clear(Color.CornflowerBlue);
            effect.CurrentTechnique.Passes[0].Apply();
            GraphicsDevice.SetVertexBuffer(quadPC);
            GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, 2);

            var px = TestHarness.ReadBackbuffer(GraphicsDevice);
            // Quad center should have the vertex color: R=204, G=102, B=51
            var expected = new Color(204, 102, 51, 255);
            totalFailures += TestHarness.AssertPixel(px, 800, 600, 200, expected, 10, "B5-PC-vcolor");
        }

        // ─── B6: PCT vertex color + texture ──────────────────────────

        private void B6_PCT_VertexColorTex()
        {
            ResetEffect();
            effect.VertexColorEnabled = true;
            effect.TextureEnabled = true;
            effect.Texture = checkerTex;
            PrimeEffect();

            GraphicsDevice.Clear(Color.CornflowerBlue);
            effect.CurrentTechnique.Passes[0].Apply();
            GraphicsDevice.SetVertexBuffer(quadPCT);
            GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, 2);

            var px = TestHarness.ReadBackbuffer(GraphicsDevice);
            totalFailures += TestHarness.AssertCoverage(px, Color.CornflowerBlue, 0.03f, "B6-PCT-coverage");

            // Checker * vertex color modulation: orange * blue-ish → darker
            // Just verify it's not background (non-black, non-blue)
            var center = px[450 * 800 + 200];
            bool hasContent = center != Color.CornflowerBlue && center != Color.Black;
            if (!hasContent)
            {
                Console.WriteLine($"FAIL [B6-PCT-modulation]: unexpected color {center}");
                totalFailures++;
            }
        }

        // ─── B7: Technique alternating (PC ↔ PNT across frames) ──────

        private void B7_Alternating()
        {
            int fails = 0;
            var perspProj = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 800f / 600f, 0.1f, 100f);
            var lookAt = Matrix.CreateLookAt(new Vector3(0, 0, 3), Vector3.Zero, Vector3.Up);

            // Frame 1: PC technique (vertex color quad)
            ResetEffect();
            effect.VertexColorEnabled = true;
            PrimeEffect();
            GraphicsDevice.Clear(Color.CornflowerBlue);
            effect.CurrentTechnique.Passes[0].Apply();
            GraphicsDevice.SetVertexBuffer(quadPC);
            GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, 2);
            var px1 = TestHarness.ReadBackbuffer(GraphicsDevice);
            var c1 = px1[200 * 800 + 600]; // center of PC quad
            fails += TestHarness.AssertPixel(px1, 800, 600, 200, new Color(204, 102, 51, 255), 10, "B7-PC");

            // Frame 2: PNT technique (lit cube)
            ResetEffect();
            effect.View = lookAt;
            effect.Projection = perspProj;
            effect.LightingEnabled = true;
            effect.PreferPerPixelLighting = false;
            PrimeEffect();
            GraphicsDevice.Clear(Color.CornflowerBlue);
            effect.CurrentTechnique.Passes[0].Apply();
            GraphicsDevice.SetVertexBuffer(cube);
            GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, 12);
            var px2 = TestHarness.ReadBackbuffer(GraphicsDevice);
            fails += TestHarness.AssertCoverage(px2, Color.CornflowerBlue, 0.02f, "B7-PNT");

            // Frame 3: Back to PC
            ResetEffect();
            effect.VertexColorEnabled = true;
            PrimeEffect();
            GraphicsDevice.Clear(Color.CornflowerBlue);
            effect.CurrentTechnique.Passes[0].Apply();
            GraphicsDevice.SetVertexBuffer(quadPC);
            GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, 2);
            var px3 = TestHarness.ReadBackbuffer(GraphicsDevice);
            fails += TestHarness.AssertPixel(px3, 800, 600, 200, new Color(204, 102, 51, 255), 10, "B7-PC-round2");

            totalFailures += fails;
            // Restore orthographic
            ResetEffect();
        }

        // ─── B8: Fog ─────────────────────────────────────────────────

        private void B8_Fog()
        {
            ResetEffect();
            effect.TextureEnabled = true;
            effect.Texture = whiteTex;
            effect.FogEnabled = true;
            effect.FogStart = 0f;
            effect.FogEnd = 500f;
            effect.FogColor = Vector3.One; // white fog
            effect.DiffuseColor = new Vector3(1, 0, 0); // red diffuse
            PrimeEffect();

            GraphicsDevice.Clear(Color.CornflowerBlue);
            effect.CurrentTechnique.Passes[0].Apply();
            GraphicsDevice.SetVertexBuffer(quadPT);
            GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, 2);

            var px = TestHarness.ReadBackbuffer(GraphicsDevice);
            // With fog on, the quad should be visible but fogged (not exactly red)
            totalFailures += TestHarness.AssertCoverage(px, Color.CornflowerBlue, 0.03f, "B8-fog-coverage");
        }

        // ─── B9: vc && lit degrade ───────────────────────────────────

        private void B9_VCLitDegrade()
        {
            ResetEffect();
            effect.VertexColorEnabled = true;
            effect.LightingEnabled = true; // vc && lit → should degrade
            effect.TextureEnabled = false;
            PrimeEffect();

            GraphicsDevice.Clear(Color.CornflowerBlue);
            effect.CurrentTechnique.Passes[0].Apply();
            GraphicsDevice.SetVertexBuffer(quadPC);
            GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, 2);

            var px = TestHarness.ReadBackbuffer(GraphicsDevice);
            // Should render without lighting (degraded) — quad visible with vertex color
            totalFailures += TestHarness.AssertPixel(px, 800, 600, 200, new Color(204, 102, 51, 255), 10, "B9-degrade");
        }

        static void Main(string[] args)
        {
            TestHarness.ParseArgs(args);
            using var g = new MatrixTest();
            g.Run();
        }
    }
}
