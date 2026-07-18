using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNA.Test;

namespace DualTextureDemo
{
    /// <summary>
    /// Demonstrates DualTextureEffect: two textures blended multiplicatively,
    /// pulsing DiffuseColor, fog toggling. Texture1=checkerboard, Texture2=radial gradient.
    /// </summary>
    public class DualDemo : Game
    {
        private GraphicsDeviceManager graphics;
        private DualTextureEffect effect;
        private VertexBuffer quad;
        private Texture2D checkerTex, radialTex;
        private float time;
        private bool fogEnabled;

        public DualDemo()
        {
            graphics = new GraphicsDeviceManager(this) {
                PreferredBackBufferWidth = 800, PreferredBackBufferHeight = 600,
                SynchronizeWithVerticalRetrace = false
            };
            Window.Title = "DualTextureEffect Demo — ImGUI panel | ESC=quit";
        }

        protected override void LoadContent()
        {
            effect = new DualTextureEffect(GraphicsDevice);
            checkerTex = TextureGen.Checkerboard(GraphicsDevice, 256, 32, Color.Red, Color.Cyan);
            radialTex = TextureGen.RadialGradient(GraphicsDevice, 256);
            var verts = GeometryGen.DualTextureQuad();
            quad = new VertexBuffer(GraphicsDevice, typeof(DualTextureVertex), verts.Length, BufferUsage.WriteOnly);
            quad.SetData(verts);
            ImGuiTestHarness.Init(GraphicsDevice);
        }

        protected override void Update(GameTime gt)
        {
            var kb = Keyboard.GetState();
            if (kb.IsKeyDown(Keys.Escape)) Exit();
            time += (float)gt.ElapsedGameTime.TotalSeconds;

            TestHarness.Tick(this, 3, () =>
            {
                var px = TestHarness.ReadBackbuffer(GraphicsDevice);
                int fails = 0;
                // Full-screen quad with checker+radial, non-black pixels expected
                fails += TestHarness.AssertCoverage(px, Color.Black, 0.50f, "dual-coverage");
                TestHarness.Report("DualTextureEffect", fails);
            });
        }

        protected override void Draw(GameTime gt)
        {
            ImGuiTestHarness.NewFrame(GraphicsDevice);
            GraphicsDevice.Clear(Color.Black);
            // Pulsing diffuse color
            float r = 0.5f + 0.5f * (float)Math.Sin(time * 1.3f);
            float g = 0.5f + 0.5f * (float)Math.Sin(time * 1.7f + 1f);
            float b = 0.5f + 0.5f * (float)Math.Sin(time * 1.1f + 2f);
            effect.World = Matrix.Identity;
            effect.View = Matrix.Identity;
            effect.Projection = Matrix.Identity;
            effect.Texture = checkerTex;
            effect.Texture2 = radialTex;
            effect.DiffuseColor = new Vector3(r, g, b);
            effect.FogEnabled = fogEnabled;
            if (fogEnabled) { effect.FogStart = 0.3f; effect.FogEnd = 0.8f; }
            effect.CurrentTechnique.Passes[0].Apply();
            GraphicsDevice.SetVertexBuffer(quad);
            GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, 2);
            if (!TestHarness.Headless)
            {
                ImGuiBindings.BeginPanel("DualTextureEffect");
                ImGuiBindings.ImGui_Checkbox("Fog", ref fogEnabled);
                float[] col = { r, g, b };
                ImGuiBindings.ImGui_ColorEdit3("Diffuse Color", col, 0);
                ImGuiBindings.EndPanel();
            }
        }

        static void Main(string[] args)
        {
            TestHarness.ParseArgs(args);
            using var g = new DualDemo();
            g.Run();
        }
    }
}
