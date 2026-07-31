using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNA.Test;

namespace FNA.RTS
{
    /// <summary>
    /// Verifies line/primitive rendering using SpriteBatch with a 1x1 white texture
    /// stretched into lines and shapes. This is the standard 2D game approach for
    /// debug grids, selection rectangles, and path overlays.
    /// </summary>
    public class PrimitiveLinesTest : Game
    {
        private GraphicsDeviceManager _gdm;
        private Camera2D _camera;
        private SpriteBatch _sb;
        private Texture2D _whiteTex;
        private int _frameCount;

        private const int WorldW = 800;
        private const int WorldH = 600;
        private const int GridSpacing = 80;

        public PrimitiveLinesTest()
        {
            _gdm = new GraphicsDeviceManager(this);
            _gdm.PreferredBackBufferWidth = 800;
            _gdm.PreferredBackBufferHeight = 600;
            _gdm.SynchronizeWithVerticalRetrace = false;
            IsMouseVisible = true;
            Window.Title = "RTS/PrimitiveLines — SpriteBatch lines | ESC quit";
        }

        protected override void Initialize()
        {
            _camera = new Camera2D(_gdm.PreferredBackBufferWidth,
                _gdm.PreferredBackBufferHeight);
            _camera.Position = new Vector2(WorldW / 2f, WorldH / 2f);
            _camera.RebuildMatrices();
            base.Initialize();
        }

        protected override void LoadContent()
        {
            _sb = new SpriteBatch(GraphicsDevice);
            _whiteTex = TextureGen.White(GraphicsDevice);
            ImGuiTestHarness.Init(GraphicsDevice);
        }

        protected override void Update(GameTime gameTime)
        {
            _frameCount++;

            int fails = 0;
            TestHarness.Tick(this, 5, () =>
            {
                var px = TestHarness.ReadBackbuffer(GraphicsDevice);
                var clearColor = new Color(20, 20, 30);

                fails += TestHarness.AssertCoverage(px, clearColor, 0.01f,
                    "lines-coverage");

                // Camera at (400,300) zoom 1.0: screen = world coords.
                // Grid line at x=160 spans from y=0 to y=600.
                // Check pixel near vertical grid line x=160, y=300
                var got = px[300 * _gdm.PreferredBackBufferWidth + 160];
                bool foundGrid = got.R > 60 && got.G > 60 && got.B > 80;
                if (!foundGrid)
                {
                    Console.WriteLine($"FAIL [grid-line]: at (160,300) expected grid color got {got}");
                    fails++;
                }

                // Green selection rect: top edge at y=100, x=150..350
                got = px[100 * _gdm.PreferredBackBufferWidth + 250];
                bool foundGreen = got.G > 150 && got.R < 100;
                if (!foundGreen)
                {
                    Console.WriteLine($"FAIL [rect-green]: at (250,100) expected green got {got}");
                    fails++;
                }

                // Yellow path line: zigzag from (400,100)→(550,200)→(500,350)→(650,400)→(600,500)
                // Check midpoint of first segment at ~(475,150)
                got = px[150 * _gdm.PreferredBackBufferWidth + 475];
                bool foundYellow = got.R > 150 && got.G > 150 && got.B < 100;
                if (!foundYellow)
                {
                    Console.WriteLine($"FAIL [path-yellow]: at (475,150) expected yellow got {got}");
                    fails++;
                }

                TestHarness.Report("PrimitiveLines", fails);
            });

            if (TestHarness.Headless) return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _camera.Update(dt);
            if (Keyboard.GetState().IsKeyDown(Keys.Escape)) Exit();
        }

        private void DrawLine(Vector2 start, Vector2 end, int thickness, Color color)
        {
            Vector2 dir = end - start;
            float len = dir.Length();
            if (len < 0.5f) return;
            float angle = MathF.Atan2(dir.Y, dir.X);
            var rect = new Rectangle(
                (int)start.X, (int)(start.Y - thickness / 2f),
                (int)len, thickness);
            _sb.Draw(_whiteTex, rect, null, color, angle,
                Vector2.Zero, SpriteEffects.None, 0);
        }

        protected override void Draw(GameTime gameTime)
        {
            ImGuiTestHarness.NewFrame(GraphicsDevice);
            GraphicsDevice.Clear(new Color(20, 20, 30));

            _sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
                SamplerState.LinearClamp, DepthStencilState.None,
                RasterizerState.CullCounterClockwise, null,
                _camera.ViewMatrix);

            // --- Grid lines ---
            int cols = WorldW / GridSpacing + 1;
            int rows = WorldH / GridSpacing + 1;
            Color gridColor = new Color(80, 80, 100, 120);
            for (int col = 0; col < cols; col++)
            {
                int x = col * GridSpacing;
                _sb.Draw(_whiteTex, new Rectangle(x, 0, 1, WorldH), gridColor);
            }
            for (int row = 0; row < rows; row++)
            {
                int y = row * GridSpacing;
                _sb.Draw(_whiteTex, new Rectangle(0, y, WorldW, 1), gridColor);
            }

            // --- Selection rectangle (green outline) ---
            float sl = 150, st = 100, sr = 350, sb = 250;
            int borderW = 2;
            _sb.Draw(_whiteTex, new Rectangle((int)sl, (int)st, (int)(sr - sl), borderW), Color.Lime);
            _sb.Draw(_whiteTex, new Rectangle((int)sl, (int)sb - borderW, (int)(sr - sl), borderW), Color.Lime);
            _sb.Draw(_whiteTex, new Rectangle((int)sl, (int)st, borderW, (int)(sb - st)), Color.Lime);
            _sb.Draw(_whiteTex, new Rectangle((int)sr - borderW, (int)st, borderW, (int)(sb - st)), Color.Lime);

            // --- Path lines (yellow zigzag) ---
            var pathPts = new Vector2[]
            {
                new Vector2(400, 100), new Vector2(550, 200),
                new Vector2(500, 350), new Vector2(650, 400),
                new Vector2(600, 500)
            };
            for (int i = 0; i < pathPts.Length - 1; i++)
                DrawLine(pathPts[i], pathPts[i + 1], 2, Color.Yellow);

            // --- Axis markers at origin ---
            _sb.Draw(_whiteTex, new Rectangle(0, 0, 60, 2), Color.Red);
            _sb.Draw(_whiteTex, new Rectangle(0, 0, 2, 60), Color.Red);

            _sb.End();

            if (!TestHarness.Headless) DrawImGui();
        }

        private void DrawImGui()
        {
            ImGuiBindings.BeginPanel("PrimitiveLines Controls");
            ImGuiBindings.ImGui_Text($"Frame: {_frameCount}");
            ImGuiBindings.ImGui_Text($"Camera: ({_camera.Position.X:F0}, {_camera.Position.Y:F0})");
            ImGuiBindings.ImGui_Separator();
            ImGuiBindings.ImGui_Text("Gray: debug grid lines");
            ImGuiBindings.ImGui_Text("Green rect: selection box");
            ImGuiBindings.ImGui_Text("Yellow: path line");
            ImGuiBindings.EndPanel();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ImGuiTestHarness.Shutdown(GraphicsDevice);
                _sb?.Dispose();
                _whiteTex?.Dispose();
            }
            base.Dispose(disposing);
        }

        [STAThread]
        static void Main(string[] args)
        {
            TestHarness.ParseArgs(args);
            using var game = new PrimitiveLinesTest();
            game.Run();
        }
    }
}
