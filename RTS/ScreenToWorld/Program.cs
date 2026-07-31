using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNA.Test;

namespace FNA.RTS
{
    /// <summary>
    /// Verifies the complete screen→world→grid coordinate transform chain
    /// for isometric mouse picking: screen pixel → InverseViewMatrix → world
    /// position → inverse isometric projection → grid coordinate.
    /// </summary>
    public class ScreenToWorldTest : Game
    {
        private GraphicsDeviceManager _gdm;
        private Camera2D _camera;
        private SpriteBatch _sb;
        private Texture2D _whiteTex;
        private int _frameCount;

        private const int TILE_W = 64;
        private const int TILE_H = 32;
        private const int MAP_W = 10;
        private const int MAP_H = 10;

        public ScreenToWorldTest()
        {
            _gdm = new GraphicsDeviceManager(this);
            _gdm.PreferredBackBufferWidth = 800;
            _gdm.PreferredBackBufferHeight = 600;
            _gdm.SynchronizeWithVerticalRetrace = false;
            IsMouseVisible = true;
            Window.Title = "RTS/ScreenToWorld — click tiles to test | ESC quit";
        }

        protected override void Initialize()
        {
            _camera = new Camera2D(_gdm.PreferredBackBufferWidth,
                _gdm.PreferredBackBufferHeight);
            _camera.Position = new Vector2(0, -MAP_H * TILE_H / 4f);
            _camera.RebuildMatrices();
            base.Initialize();
        }

        protected override void LoadContent()
        {
            _sb = new SpriteBatch(GraphicsDevice);
            _whiteTex = TextureGen.White(GraphicsDevice);
            ImGuiTestHarness.Init(GraphicsDevice);
        }

        /// <summary>Grid coords to world position (tile top-left).</summary>
        public static Vector2 GridToWorld(int gx, int gy)
        {
            return new Vector2(
                (gx - gy) * (TILE_W / 2f),
                (gx + gy) * (TILE_H / 2f)
            );
        }

        /// <summary>World position to grid coords (floor).</summary>
        public static Point WorldToGrid(Vector2 world)
        {
            float halfW = TILE_W / 2f;
            float halfH = TILE_H / 2f;
            float fx = (world.X / halfW + world.Y / halfH) / 2f;
            float fy = (world.Y / halfH - world.X / halfW) / 2f;
            return new Point((int)MathF.Floor(fx), (int)MathF.Floor(fy));
        }

        /// <summary>Screen pixel to grid coord (full pipeline).</summary>
        public Point ScreenToGrid(Vector2 screenPos)
        {
            Vector2 world = _camera.ScreenToWorld(screenPos);
            return WorldToGrid(world);
        }

        protected override void Update(GameTime gameTime)
        {
            _frameCount++;

            int fails = 0;
            TestHarness.Tick(this, 5, () =>
            {
                // Test 1: Round-trip grid→world→grid for all tiles
                for (int gx = 0; gx < MAP_W; gx++)
                {
                    for (int gy = 0; gy < MAP_H; gy++)
                    {
                        Vector2 world = GridToWorld(gx, gy);
                        Point grid = WorldToGrid(world);
                        if (grid.X != gx || grid.Y != gy)
                        {
                            Console.WriteLine(
                                $"FAIL [roundtrip]: Grid({gx},{gy}) → World({world.X:F1},{world.Y:F1}) → Grid({grid.X},{grid.Y})");
                            fails++;
                            if (fails > 5) goto doneRoundtrip;
                        }
                    }
                }
                doneRoundtrip:

                // Test 2: Camera.ScreenToWorld round-trip
                // Pick a world point, convert to screen, convert back
                var w0 = new Vector2(100f, 200f);
                Vector2 s = _camera.WorldToScreen(w0);
                Vector2 w1 = _camera.ScreenToWorld(s);
                if (Vector2.Distance(w0, w1) > 1f)
                {
                    Console.WriteLine(
                        $"FAIL [camera-roundtrip]: World({w0}) → Screen({s}) → World({w1})");
                    fails++;
                }

                // Test 3: Screen pixel → grid for a known screen position
                // Camera at (0, -80): center of viewport maps to world (0, -80)
                // GridToWorld(5,5) = (0, 160). World(0,160) with camera at (0,-80):
                // screen = (400, 300) + (0-0, 160-(-80)) = (400, 540)
                // Wait, zoom 1.0: ViewMatrix = T(0,80) * S(1) * T(400,300)
                // screen = (0+400, 160+80+300) = (400, 540)
                // So grid (5,5) should be around screen (400, 540)
                Point g = ScreenToGrid(new Vector2(400, 540));
                // grid should be (5,5) or nearby
                if (Math.Abs(g.X - 5) > 1 || Math.Abs(g.Y - 5) > 1)
                {
                    Console.WriteLine(
                        $"FAIL [screen-to-grid]: Screen(400,540) → Grid({g.X},{g.Y}), expected near (5,5)");
                    fails++;
                }

                TestHarness.Report("ScreenToWorld", fails);
            });

            if (TestHarness.Headless) return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _camera.Update(dt);

            // Show grid coord under mouse
            var mouse = Mouse.GetState();
            Point gridUnderMouse = ScreenToGrid(new Vector2(mouse.X, mouse.Y));
            Window.Title = $"RTS/ScreenToWorld — Grid({gridUnderMouse.X},{gridUnderMouse.Y}) | ESC quit";

            if (Keyboard.GetState().IsKeyDown(Keys.Escape)) Exit();
        }

        protected override void Draw(GameTime gameTime)
        {
            ImGuiTestHarness.NewFrame(GraphicsDevice);
            GraphicsDevice.Clear(new Color(20, 20, 30));

            // Draw isometric grid reference
            _sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
                SamplerState.LinearClamp, DepthStencilState.None,
                RasterizerState.CullCounterClockwise, null,
                _camera.ViewMatrix);

            for (int gx = 0; gx < MAP_W; gx++)
            for (int gy = 0; gy < MAP_H; gy++)
            {
                Vector2 pos = GridToWorld(gx, gy);
                bool isCenter = (gx == 5 && gy == 5);
                Color col = isCenter ? Color.Yellow : new Color(60, 60, 80, 100);
                // Draw tile outline as diamond
                float hw = TILE_W / 2f, hh = TILE_H / 2f;
                _sb.Draw(_whiteTex, new Rectangle((int)(pos.X + hw - 1), (int)pos.Y, 2, (int)hh), col);
                _sb.Draw(_whiteTex, new Rectangle((int)(pos.X + hw - 1), (int)(pos.Y + hh), 2, (int)hh), col);
                _sb.Draw(_whiteTex, new Rectangle((int)pos.X, (int)(pos.Y + hh - 1), (int)hw, 2), col);
                _sb.Draw(_whiteTex, new Rectangle((int)(pos.X + hw), (int)(pos.Y + hh - 1), (int)hw, 2), col);
            }

            _sb.End();

            if (!TestHarness.Headless) DrawImGui();
        }

        private void DrawImGui()
        {
            var mouse = Mouse.GetState();
            Point grid = ScreenToGrid(new Vector2(mouse.X, mouse.Y));
            Vector2 world = _camera.ScreenToWorld(new Vector2(mouse.X, mouse.Y));

            ImGuiBindings.BeginPanel("ScreenToWorld Test");
            ImGuiBindings.ImGui_Text($"Mouse screen: ({mouse.X}, {mouse.Y})");
            ImGuiBindings.ImGui_Text($"Mouse world: ({world.X:F1}, {world.Y:F1})");
            ImGuiBindings.ImGui_Text($"Mouse grid: ({grid.X}, {grid.Y})");
            ImGuiBindings.ImGui_Separator();
            ImGuiBindings.ImGui_Text($"Camera: ({_camera.Position.X:F0}, {_camera.Position.Y:F0})");
            ImGuiBindings.ImGui_Text($"Zoom: {_camera.Zoom:F2}x");
            ImGuiBindings.ImGui_Text("Move mouse over grid to test picking");
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
            using var game = new ScreenToWorldTest();
            game.Run();
        }
    }
}
