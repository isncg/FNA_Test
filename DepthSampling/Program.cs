using System;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNA.Test;

namespace DepthSamplingTest
{
    /// <summary>
    /// Phase 1 test: depth-stencil buffers created with SAMPLER usage.
    ///
    /// After the Phase 1 driver change, depth-stencil targets are created with
    /// SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET | SAMPLER (when supported).
    /// This test verifies the new usage flag does not break depth testing on
    /// either depth-buffer code path:
    ///   A. Faux backbuffer depth (SDLGPU_INTERNAL_CreateFauxBackbuffer)
    ///   B. RenderTarget2D depth  (SDLGPU_GenDepthStencilRenderbuffer)
    ///
    /// Scene: near red quad (z=0.25) + far green quad (z=0.75), both centered.
    /// With depth testing the red quad must occlude the green one at the
    /// center; the green ring stays visible outside the red quad.
    ///
    /// Actual depth *sampling* from a shader requires the Phase 2 texture
    /// wrapping API; RenderDoc can confirm the SAMPLER usage flag on
    /// vkCreateImage until then.
    /// </summary>
    public class DepthSamplingGame : Game
    {
        private GraphicsDeviceManager graphics;
        private Effect effect;
        private RenderTarget2D rt;
        private VertexPositionColor[] verts;

        private static readonly Color ClearCol = new Color(0, 0, 64, 255);
        private static readonly Color NearCol = new Color(255, 0, 0, 255);
        private static readonly Color FarCol = new Color(0, 255, 0, 255);

        private const int RTSize = 256;

        public DepthSamplingGame()
        {
            graphics = new GraphicsDeviceManager(this)
            {
                PreferredBackBufferWidth = 800,
                PreferredBackBufferHeight = 600,
                PreferredDepthStencilFormat = DepthFormat.Depth24Stencil8,
                SynchronizeWithVerticalRetrace = false
            };
            Window.Title = "DepthSampling — Phase 1 | ESC=quit";
        }

        protected override void LoadContent()
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("DepthSamplingTest.DepthQuad.feb");
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            effect = new Effect(GraphicsDevice, ms.ToArray());

            // Exercises SDLGPU_GenDepthStencilRenderbuffer with SAMPLER usage
            rt = new RenderTarget2D(GraphicsDevice, RTSize, RTSize, false,
                SurfaceFormat.Color, DepthFormat.Depth24Stencil8);

            // Near red quad: x,y in [-0.4, 0.4], z = 0.25
            // Far green quad: x,y in [-0.8, 0.8], z = 0.75
            // Y-symmetric so assertions are independent of Y-flip conventions.
            verts = new VertexPositionColor[12];
            FillQuad(verts, 0, 0.4f, 0.25f, NearCol);
            FillQuad(verts, 6, 0.8f, 0.75f, FarCol);

            Console.WriteLine("[DepthSampling] Effect loaded, RT (D24S8) created.");
        }

        private static void FillQuad(VertexPositionColor[] v, int offset,
            float half, float z, Color color)
        {
            var bl = new Vector3(-half, -half, z);
            var br = new Vector3(half, -half, z);
            var tr = new Vector3(half, half, z);
            var tl = new Vector3(-half, half, z);
            v[offset + 0] = new VertexPositionColor(bl, color);
            v[offset + 1] = new VertexPositionColor(br, color);
            v[offset + 2] = new VertexPositionColor(tr, color);
            v[offset + 3] = new VertexPositionColor(bl, color);
            v[offset + 4] = new VertexPositionColor(tr, color);
            v[offset + 5] = new VertexPositionColor(tl, color);
        }

        private void DrawScene()
        {
            GraphicsDevice.Clear(
                ClearOptions.Target | ClearOptions.DepthBuffer | ClearOptions.Stencil,
                ClearCol, 1.0f, 0);
            GraphicsDevice.DepthStencilState = DepthStencilState.Default;
            GraphicsDevice.RasterizerState = RasterizerState.CullNone;
            GraphicsDevice.BlendState = BlendState.Opaque;

            effect.CurrentTechnique.Passes[0].Apply();
            GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, verts, 0, 4);
        }

        protected override void Draw(GameTime gameTime)
        {
            // Path B: RenderTarget2D with its own D24S8 renderbuffer
            GraphicsDevice.SetRenderTarget(rt);
            DrawScene();
            GraphicsDevice.SetRenderTarget(null);

            // Path A: faux backbuffer depth
            DrawScene();

            base.Draw(gameTime);
        }

        protected override void Update(GameTime gameTime)
        {
            if (Microsoft.Xna.Framework.Input.Keyboard.GetState()
                .IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Escape))
            {
                Exit();
            }

            TestHarness.Tick(this, 3, RunAssertions);
            base.Update(gameTime);
        }

        private void RunAssertions()
        {
            int failures = 0;

            // ── Path A: backbuffer ──────────────────────────────────────
            var px = TestHarness.ReadBackbuffer(GraphicsDevice);
            int w = GraphicsDevice.PresentationParameters.BackBufferWidth;
            int h = GraphicsDevice.PresentationParameters.BackBufferHeight;

            // Center: near red quad wins the depth test over far green
            failures += TestHarness.AssertPixel(px, w, w / 2, h / 2,
                NearCol, 3, "backbuffer center = near red");
            // NDC x=0.6: inside green quad only (|0.6| > 0.4, < 0.8)
            failures += TestHarness.AssertPixel(px, w, (int)((0.6f + 1) / 2 * w), h / 2,
                FarCol, 3, "backbuffer ring = far green");
            // Corner: clear color
            failures += TestHarness.AssertPixel(px, w, 10, 10,
                ClearCol, 3, "backbuffer corner = clear");

            // ── Path B: RenderTarget2D ──────────────────────────────────
            var rtPx = new Color[RTSize * RTSize];
            rt.GetData(rtPx);

            failures += TestHarness.AssertPixel(rtPx, RTSize, RTSize / 2, RTSize / 2,
                NearCol, 3, "RT center = near red");
            failures += TestHarness.AssertPixel(rtPx, RTSize, (int)((0.6f + 1) / 2 * RTSize), RTSize / 2,
                FarCol, 3, "RT ring = far green");
            failures += TestHarness.AssertPixel(rtPx, RTSize, 5, 5,
                ClearCol, 3, "RT corner = clear");

            TestHarness.Report("DepthSampling", failures);
        }

        [STAThread]
        static void Main(string[] args)
        {
            TestHarness.ParseArgs(args);
            using var game = new DepthSamplingGame();
            game.Run();
        }
    }
}
