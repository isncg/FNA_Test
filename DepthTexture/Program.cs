using System;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNA.Test;

namespace DepthTextureTest
{
    /// <summary>
    /// Phase 2 test: RenderTarget2D.DepthStencilTexture wraps the depth
    /// renderbuffer as a sampleable Texture2D (FNA3D_GetDepthStencilTexture).
    ///
    /// Pass 1 (to RT): draw a quad at z=0.25 into a D24S8 render target.
    /// Pass 2 (to backbuffer): fullscreen quad samples rt.DepthStencilTexture
    /// and writes raw depth as grayscale.
    ///
    /// Assertions: center pixel = depth 0.25 (~64 gray), region outside the
    /// quad = clear depth 1.0 (white).
    /// </summary>
    public class DepthTextureGame : Game
    {
        private GraphicsDeviceManager graphics;
        private Effect fillEffect;
        private Effect viewEffect;
        private RenderTarget2D rt;
        private Texture2D depthTexture;
        private VertexPositionColor[] fillVerts;
        private VertexPositionTexture[] fullscreenVerts;

        private const int RTSize = 256;
        private const float QuadDepth = 0.25f;

        public DepthTextureGame()
        {
            graphics = new GraphicsDeviceManager(this)
            {
                PreferredBackBufferWidth = 800,
                PreferredBackBufferHeight = 600,
                PreferredDepthStencilFormat = DepthFormat.Depth24Stencil8,
                SynchronizeWithVerticalRetrace = false
            };
            Window.Title = "DepthTexture — Phase 2 | ESC=quit";
        }

        private static Effect LoadFeb(GraphicsDevice device, string name)
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(name);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return new Effect(device, ms.ToArray());
        }

        protected override void LoadContent()
        {
            fillEffect = LoadFeb(GraphicsDevice, "DepthTextureTest.DepthFill.feb");
            viewEffect = LoadFeb(GraphicsDevice, "DepthTextureTest.DepthView.feb");

            rt = new RenderTarget2D(GraphicsDevice, RTSize, RTSize, false,
                SurfaceFormat.Color, DepthFormat.Depth24Stencil8);

            // Phase 2 API: sampleable view of the RT's depth buffer
            depthTexture = rt.DepthStencilTexture;
            Console.WriteLine(depthTexture != null
                ? "[DepthTexture] DepthStencilTexture acquired."
                : "[DepthTexture] DepthStencilTexture is NULL!");

            // Quad at z=0.25, x,y in [-0.5, 0.5] (Y-symmetric)
            fillVerts = new VertexPositionColor[6];
            FillQuad(fillVerts, 0.5f, QuadDepth, Color.Red);

            // Fullscreen quad with UVs covering the whole depth texture
            fullscreenVerts = new VertexPositionTexture[]
            {
                new VertexPositionTexture(new Vector3(-1, -1, 0), new Vector2(0, 1)),
                new VertexPositionTexture(new Vector3( 1, -1, 0), new Vector2(1, 1)),
                new VertexPositionTexture(new Vector3( 1,  1, 0), new Vector2(1, 0)),
                new VertexPositionTexture(new Vector3(-1, -1, 0), new Vector2(0, 1)),
                new VertexPositionTexture(new Vector3( 1,  1, 0), new Vector2(1, 0)),
                new VertexPositionTexture(new Vector3(-1,  1, 0), new Vector2(0, 0))
            };
        }

        private static void FillQuad(VertexPositionColor[] v,
            float half, float z, Color color)
        {
            var bl = new Vector3(-half, -half, z);
            var br = new Vector3(half, -half, z);
            var tr = new Vector3(half, half, z);
            var tl = new Vector3(-half, half, z);
            v[0] = new VertexPositionColor(bl, color);
            v[1] = new VertexPositionColor(br, color);
            v[2] = new VertexPositionColor(tr, color);
            v[3] = new VertexPositionColor(bl, color);
            v[4] = new VertexPositionColor(tr, color);
            v[5] = new VertexPositionColor(tl, color);
        }

        protected override void Draw(GameTime gameTime)
        {
            // ── Pass 1: fill depth in the RT ────────────────────────────
            GraphicsDevice.SetRenderTarget(rt);
            GraphicsDevice.Clear(
                ClearOptions.Target | ClearOptions.DepthBuffer | ClearOptions.Stencil,
                Color.Black, 1.0f, 0);
            GraphicsDevice.DepthStencilState = DepthStencilState.Default;
            GraphicsDevice.RasterizerState = RasterizerState.CullNone;
            GraphicsDevice.BlendState = BlendState.Opaque;

            fillEffect.CurrentTechnique.Passes[0].Apply();
            GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, fillVerts, 0, 2);

            // ── Pass 2: visualize the depth texture on the backbuffer ──
            GraphicsDevice.SetRenderTarget(null);
            GraphicsDevice.Clear(Color.CornflowerBlue);
            GraphicsDevice.DepthStencilState = DepthStencilState.None;

            GraphicsDevice.Textures[0] = depthTexture;
            GraphicsDevice.SamplerStates[0] = SamplerState.PointClamp;

            viewEffect.CurrentTechnique.Passes[0].Apply();
            GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, fullscreenVerts, 0, 2);

            // Unbind so the RT depth can be written again next frame
            GraphicsDevice.Textures[0] = null;

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

            if (depthTexture == null)
            {
                Console.WriteLine("FAIL [api]: DepthStencilTexture returned null");
                TestHarness.Report("DepthTexture", 1);
                return;
            }

            var px = TestHarness.ReadBackbuffer(GraphicsDevice);
            int w = GraphicsDevice.PresentationParameters.BackBufferWidth;
            int h = GraphicsDevice.PresentationParameters.BackBufferHeight;

            // Quad covers UV [0.25, 0.75]; depth there is 0.25 → gray ~64.
            byte quadGray = (byte)Math.Round(QuadDepth * 255.0f);
            failures += TestHarness.AssertPixel(px, w, w / 2, h / 2,
                new Color(quadGray, quadGray, quadGray, (byte)255), 3,
                "center = quad depth 0.25");

            // NDC x=0.7 → UV 0.85, outside the quad → clear depth 1.0 → white.
            failures += TestHarness.AssertPixel(px, w, (int)((0.7f + 1) / 2 * w), h / 2,
                Color.White, 3, "ring = clear depth 1.0");

            // Corner (UV ~0) → also clear depth.
            failures += TestHarness.AssertPixel(px, w, 10, 10,
                Color.White, 3, "corner = clear depth 1.0");

            TestHarness.Report("DepthTexture", failures);
        }

        [STAThread]
        static void Main(string[] args)
        {
            TestHarness.ParseArgs(args);
            using var game = new DepthTextureGame();
            game.Run();
        }
    }
}
