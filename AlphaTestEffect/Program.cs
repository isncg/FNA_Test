using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNA.Test;

namespace AlphaTestDemo
{
    /// <summary>
    /// Demonstrates AlphaTestEffect: all 8 CompareFunction modes, auto-oscillating
    /// ReferenceAlpha, fog toggling. Alpha gradient texture from left (transparent)
    /// to right (opaque) shows the clip boundary moving.
    /// </summary>
    public class AlphaDemo : Game
    {
        private GraphicsDeviceManager graphics;
        private AlphaTestEffect effect;
        private VertexBuffer quad;
        private Texture2D gradientTex;
        private float time;
        private int funcIndex;
        private int refAlpha = 128;
        private bool autoOsc = true;
        private bool fogEnabled;
        private static readonly CompareFunction[] Funcs = {
            CompareFunction.Greater, CompareFunction.Less,
            CompareFunction.Equal, CompareFunction.NotEqual,
            CompareFunction.GreaterEqual, CompareFunction.LessEqual,
            CompareFunction.Never, CompareFunction.Always
        };
        private static readonly string[] FuncNames = {
            "Greater","Less","Equal","NotEqual","GreaterEqual","LessEqual","Never","Always"
        };

        public AlphaDemo()
        {
            graphics = new GraphicsDeviceManager(this) {
                PreferredBackBufferWidth = 800, PreferredBackBufferHeight = 600,
                SynchronizeWithVerticalRetrace = false
            };
            Window.Title = "AlphaTestEffect Demo — ImGUI panel | ESC=quit";
        }

        protected override void LoadContent()
        {
            effect = new AlphaTestEffect(GraphicsDevice);
            gradientTex = TextureGen.AlphaGradient(GraphicsDevice, 512, 512, Color.White);
            quad = new VertexBuffer(GraphicsDevice, typeof(VertexPositionTexture), 6, BufferUsage.WriteOnly);
            quad.SetData(GeometryGen.Quad());
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
                // Default: Greater, refAlpha=128, alpha gradient left→right
                // Left side (alpha<128) should be black, right side (alpha>128) non-black
                // Background is Black, so check that right portion has coverage
                fails += TestHarness.AssertCoverage(px, Color.Black, 0.15f, "alpha-coverage");
                TestHarness.Report("AlphaTestEffect", fails);
            });
        }

        protected override void Draw(GameTime gt)
        {
            ImGuiTestHarness.NewFrame(GraphicsDevice);
            GraphicsDevice.Clear(Color.Black);
            int ra = autoOsc ? (int)(127.5f + 127.5f * Math.Sin(time * 2f)) : refAlpha;
            effect.World = Matrix.Identity;
            effect.View = Matrix.Identity;
            effect.Projection = Matrix.Identity;
            effect.Texture = gradientTex;
            effect.AlphaFunction = Funcs[funcIndex];
            effect.ReferenceAlpha = ra;
            effect.FogEnabled = fogEnabled;
            if (fogEnabled) { effect.FogStart = 50; effect.FogEnd = 200; }
            effect.CurrentTechnique.Passes[0].Apply();
            GraphicsDevice.SetVertexBuffer(quad);
            GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, 2);
            if (!TestHarness.Headless)
            {
                ImGuiBindings.BeginPanel("AlphaTestEffect");
                ImGuiBindings.Combo("Alpha Func", ref funcIndex, FuncNames);
                if (autoOsc) ImGuiBindings.ImGui_BeginDisabled(true);
                float raf = refAlpha;
                ImGuiBindings.ImGui_SliderFloat("Ref Alpha", ref raf, 0f, 255f);
                refAlpha = (int)raf;
                if (autoOsc) ImGuiBindings.ImGui_EndDisabled();
                ImGuiBindings.ImGui_Checkbox("Auto-Oscillate", ref autoOsc);
                ImGuiBindings.ImGui_Checkbox("Fog", ref fogEnabled);
                ImGuiBindings.EndPanel();
            }
        }

        static void Main(string[] args)
        {
            TestHarness.ParseArgs(args);
            using var g = new AlphaDemo();
            g.Run();
        }
    }
}
