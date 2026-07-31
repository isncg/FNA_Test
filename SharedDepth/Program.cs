using System;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNA.Test;

namespace SharedDepthTest
{
    /// <summary>
    /// Phase 3 test: two render targets sharing one DepthStencilBuffer.
    /// This is the core of UE5-style deferred rendering, where every pass
    /// reuses the depth produced by the GBuffer pass.
    ///
    /// Pass 1 → RT1: clear color+depth, draw a near quad (z=0.25).
    /// Pass 2 → RT2: clear color ONLY, draw a fullscreen "sky" quad (z=0.75)
    ///               with depth testing enabled.
    ///
    /// If the depth buffer is truly shared, the sky is rejected wherever pass 1
    /// wrote 0.25 — even though it is drawn into a different color target.
    /// </summary>
    public class SharedDepthGame : Game
    {
        private GraphicsDeviceManager graphics;
        private Effect effect;
        private DepthStencilBuffer sharedDepth;
        private RenderTarget2D rt1;
        private RenderTarget2D rt2;
        private VertexPositionColor[] geometryVerts;
        private VertexPositionColor[] skyVerts;

        private const int RTSize = 256;

        /// <summary>
        /// Negative control: give RT2 its own depth buffer instead of sharing.
        /// The assertions below must then FAIL, proving they really do detect
        /// depth sharing rather than passing vacuously.
        /// </summary>
        private static bool noShare;

        // RT1 background, also the depth-clear pass color
        private static readonly Color Rt1Clear = new Color(0, 0, 0, 255);
        // RT2 background: what remains where the sky is depth-rejected
        private static readonly Color Rt2Clear = new Color(32, 0, 32, 255);
        private static readonly Color GeometryCol = new Color(255, 0, 0, 255);
        private static readonly Color SkyCol = new Color(0, 128, 255, 255);

        public SharedDepthGame()
        {
            graphics = new GraphicsDeviceManager(this)
            {
                PreferredBackBufferWidth = 800,
                PreferredBackBufferHeight = 600,
                PreferredDepthStencilFormat = DepthFormat.Depth24Stencil8,
                SynchronizeWithVerticalRetrace = false
            };
            Window.Title = "SharedDepth — Phase 3 | ESC=quit";
        }

        protected override void LoadContent()
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("SharedDepthTest.Geometry.feb");
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            effect = new Effect(GraphicsDevice, ms.ToArray());

            // One depth buffer, two color targets
            sharedDepth = new DepthStencilBuffer(GraphicsDevice,
                RTSize, RTSize, DepthFormat.Depth24Stencil8);
            rt1 = new RenderTarget2D(GraphicsDevice, RTSize, RTSize, false,
                SurfaceFormat.Color, sharedDepth);
            rt2 = noShare
                ? new RenderTarget2D(GraphicsDevice, RTSize, RTSize, false,
                    SurfaceFormat.Color, DepthFormat.Depth24Stencil8, 0,
                    RenderTargetUsage.PreserveContents)
                : new RenderTarget2D(GraphicsDevice, RTSize, RTSize, false,
                    SurfaceFormat.Color, sharedDepth);

            Console.WriteLine(noShare
                ? "[SharedDepth] NEGATIVE CONTROL: RT2 has its own depth buffer."
                : "[SharedDepth] Shared D24S8 buffer + 2 render targets created.");

            // Near geometry: x,y in [-0.5, 0.5] at z=0.25 (Y-symmetric)
            geometryVerts = new VertexPositionColor[6];
            FillQuad(geometryVerts, 0.5f, 0.25f, GeometryCol);

            // Sky: fullscreen at z=0.75
            skyVerts = new VertexPositionColor[6];
            FillQuad(skyVerts, 1.0f, 0.75f, SkyCol);
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
            GraphicsDevice.RasterizerState = RasterizerState.CullNone;
            GraphicsDevice.BlendState = BlendState.Opaque;
            GraphicsDevice.DepthStencilState = DepthStencilState.Default;

            // ── Pass 1 → RT1: establish depth ───────────────────────────
            GraphicsDevice.SetRenderTarget(rt1);
            GraphicsDevice.Clear(
                ClearOptions.Target | ClearOptions.DepthBuffer | ClearOptions.Stencil,
                Rt1Clear, 1.0f, 0);
            effect.CurrentTechnique.Passes[0].Apply();
            GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, geometryVerts, 0, 2);

            // ── Pass 2 → RT2: reuse depth, clear color only ─────────────
            GraphicsDevice.SetRenderTarget(rt2);
            GraphicsDevice.Clear(ClearOptions.Target, Rt2Clear, 1.0f, 0);
            effect.CurrentTechnique.Passes[0].Apply();
            GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, skyVerts, 0, 2);

            // ── Show RT2 on the backbuffer ──────────────────────────────
            GraphicsDevice.SetRenderTarget(null);
            GraphicsDevice.Clear(Color.CornflowerBlue);

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

            // Pass 1 actually drew the geometry
            var px1 = new Color[RTSize * RTSize];
            rt1.GetData(px1);
            failures += TestHarness.AssertPixel(px1, RTSize, RTSize / 2, RTSize / 2,
                GeometryCol, 3, "RT1 center = geometry");

            // The shared depth rejected the sky where the geometry is,
            // so RT2 keeps its own clear color there.
            var px2 = new Color[RTSize * RTSize];
            rt2.GetData(px2);
            failures += TestHarness.AssertPixel(px2, RTSize, RTSize / 2, RTSize / 2,
                Rt2Clear, 3, "RT2 center = sky occluded by shared depth");

            // Outside the geometry the depth is still 1.0, so the sky passes.
            failures += TestHarness.AssertPixel(px2, RTSize,
                (int)((0.7f + 1) / 2 * RTSize), RTSize / 2,
                SkyCol, 3, "RT2 ring = sky drawn");
            failures += TestHarness.AssertPixel(px2, RTSize, 5, 5,
                SkyCol, 3, "RT2 corner = sky drawn");

            // Phase 2 interop: the shared buffer is sampleable
            if (sharedDepth.GetTexture() == null)
            {
                Console.WriteLine("FAIL [interop]: shared depth GetTexture() returned null");
                failures += 1;
            }

            TestHarness.Report("SharedDepth", failures);
        }

        protected override void UnloadContent()
        {
            // Render targets first: they alias the shared buffer
            rt1?.Dispose();
            rt2?.Dispose();
            sharedDepth?.Dispose();
            base.UnloadContent();
        }

        [STAThread]
        static void Main(string[] args)
        {
            TestHarness.ParseArgs(args);
            foreach (var a in args)
            {
                if (a == "--no-share") noShare = true;
            }
            using var game = new SharedDepthGame();
            game.Run();
        }
    }
}
