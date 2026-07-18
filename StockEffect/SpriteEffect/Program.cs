using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNA.Test;

namespace SpriteEffectDemo
{
    /// <summary>
    /// Demonstrates SpriteBatch (which uses SpriteEffect internally):
    /// rotation, scaling, color tint, alpha blending, layer ordering, sort modes.
    /// </summary>
    public class SpriteDemo : Game
    {
        private GraphicsDeviceManager graphics;
        private SpriteBatch spriteBatch;
        private Texture2D checkerTex;
        private float time;
        private SpriteSortMode sortMode;
        private bool rotating = true;
        private bool scaling = true;

        public SpriteDemo()
        {
            graphics = new GraphicsDeviceManager(this);
            graphics.PreferredBackBufferWidth = 800;
            graphics.PreferredBackBufferHeight = 600;
            graphics.SynchronizeWithVerticalRetrace = false;
            IsMouseVisible = true;
            Window.Title = "SpriteEffect Demo — ImGUI panel | ESC=quit";
        }

        protected override void LoadContent()
        {
            spriteBatch = new SpriteBatch(GraphicsDevice);
            checkerTex = TextureGen.Checkerboard(GraphicsDevice, 64, 8, Color.Red, Color.White);
            ImGuiTestHarness.Init(GraphicsDevice);
        }

        protected override void Update(GameTime gameTime)
        {
            var kb = Keyboard.GetState();
            if (kb.IsKeyDown(Keys.Escape)) Exit();
            time += (float)gameTime.ElapsedGameTime.TotalSeconds;

            TestHarness.Tick(this, 3, () =>
            {
                var px = TestHarness.ReadBackbuffer(GraphicsDevice);
                int fails = 0;
                fails += TestHarness.AssertCoverage(px, Color.CornflowerBlue, 0.05f, "sprite-coverage");
                TestHarness.Report("SpriteEffect", fails);
            });
        }

        protected override void Draw(GameTime gameTime)
        {
            ImGuiTestHarness.NewFrame(GraphicsDevice);
            GraphicsDevice.Clear(Color.CornflowerBlue);

            spriteBatch.Begin(sortMode, BlendState.AlphaBlend);

            var center = new Vector2(400, 300);
            Color[] colors = { Color.White, Color.Red, Color.Lime, Color.Blue, Color.Yellow, Color.Cyan };
            float[] alphas = { 1f, 0.8f, 0.6f, 0.4f, 0.2f };
            float[] scales = { 1.5f, 1.2f, 0.9f, 0.6f, 0.3f };

            for (int i = 0; i < 5; i++)
            {
                float r = rotating ? time * 0.8f + i * 1.2f : i * 0.5f;
                float s = scaling ? 1f + 0.3f * (float)Math.Sin(time * 1.5f + i) : 1f;
                float radius = 150f + i * 20f;
                var pos = center + new Vector2(
                    (float)Math.Cos(time * 0.7f + i * 2f) * radius,
                    (float)Math.Sin(time * 0.9f + i * 2f) * radius * 0.6f
                );

                spriteBatch.Draw(checkerTex, pos, null, colors[i] * alphas[i],
                    r, new Vector2(32, 32), scales[i] * s, SpriteEffects.None, (float)i / 5f);
            }

            spriteBatch.End();

            if (!TestHarness.Headless)
            {
                ImGuiBindings.BeginPanel("SpriteEffect");
                ImGuiBindings.ImGui_Checkbox("Rotating", ref rotating);
                ImGuiBindings.ImGui_Checkbox("Scaling", ref scaling);
                string[] sortNames = { "Deferred", "Immediate", "Texture", "BackToFront", "FrontToBack" };
                int sm = (int)sortMode;
                ImGuiBindings.Combo("Sort Mode", ref sm, sortNames);
                sortMode = (SpriteSortMode)sm;
                ImGuiBindings.EndPanel();
            }
        }

        static void Main(string[] args)
        {
            TestHarness.ParseArgs(args);
            using var g = new SpriteDemo();
            g.Run();
        }
    }
}
