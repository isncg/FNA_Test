using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNA.Test;

namespace FNA.RTS
{
    public class Camera2DTest : Game
    {
        private GraphicsDeviceManager _gdm;
        private SpriteBatch _sb;
        private Camera2D _camera;
        private Texture2D _whiteTex;
        private int _frameCount;

        // Grid parameters
        private const int GridCols = 20;
        private const int GridRows = 15;
        private const int CellSize = 64;
        private const int WorldW = GridCols * CellSize;
        private const int WorldH = GridRows * CellSize;

        public Camera2DTest()
        {
            _gdm = new GraphicsDeviceManager(this);
            _gdm.PreferredBackBufferWidth = 800;
            _gdm.PreferredBackBufferHeight = 600;
            _gdm.SynchronizeWithVerticalRetrace = false;
            IsMouseVisible = true;
            Window.Title = "RTS/Camera2D — WASD pan | scroll zoom | ESC quit";
        }

        protected override void Initialize()
        {
            _camera = new Camera2D(_gdm.PreferredBackBufferWidth,
                _gdm.PreferredBackBufferHeight);
            // Set world bounds to the grid extent
            _camera.WorldBoundMin = Vector2.Zero;
            _camera.WorldBoundMax = new Vector2(WorldW, WorldH);
            // Center camera on the grid
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
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _frameCount++;

            // Headless assertion on frame 5 (give camera time to settle)
            int fails = 0;
            TestHarness.Tick(this, 5, () =>
            {
                // At default position (center of world), verify ViewMatrix properties:
                // 1. World origin should project to a specific screen position
                Vector2 screenOrigin = _camera.WorldToScreen(Vector2.Zero);
                // World origin (0,0) relative to camera center (WorldW/2, WorldH/2)
                // at zoom 1.0 should be at screen center - (WorldW/2, WorldH/2)
                float expectedX = _gdm.PreferredBackBufferWidth / 2f - WorldW / 2f;
                float expectedY = _gdm.PreferredBackBufferHeight / 2f - WorldH / 2f;

                if (Math.Abs(screenOrigin.X - expectedX) > 2f ||
                    Math.Abs(screenOrigin.Y - expectedY) > 2f)
                {
                    Console.WriteLine($"FAIL [world_to_screen]: expected ({expectedX:F1},{expectedY:F1}) got ({screenOrigin.X:F1},{screenOrigin.Y:F1})");
                    fails++;
                }

                // 2. Screen center should map back to camera position (WorldW/2, WorldH/2)
                var screenCenter = new Vector2(
                    _gdm.PreferredBackBufferWidth / 2f,
                    _gdm.PreferredBackBufferHeight / 2f);
                Vector2 worldCenter = _camera.ScreenToWorld(screenCenter);
                if (Vector2.Distance(worldCenter, _camera.Position) > 2f)
                {
                    Console.WriteLine($"FAIL [screen_to_world]: expected {_camera.Position} got {worldCenter}");
                    fails++;
                }

                // 3. Round-trip: world → screen → world
                var testPoint = new Vector2(100f, 200f);
                Vector2 roundTrip = _camera.ScreenToWorld(
                    _camera.WorldToScreen(testPoint));
                if (Vector2.Distance(roundTrip, testPoint) > 1f)
                {
                    Console.WriteLine($"FAIL [round_trip]: expected {testPoint} got {roundTrip}");
                    fails++;
                }

                TestHarness.Report("Camera2D", fails);
            });

            if (TestHarness.Headless) return;

            // Interactive: update camera normally
            _camera.Update(dt);

            // ESC to quit
            if (Keyboard.GetState().IsKeyDown(Keys.Escape))
                Exit();
        }

        protected override void Draw(GameTime gameTime)
        {
            ImGuiTestHarness.NewFrame(GraphicsDevice);
            GraphicsDevice.Clear(new Color(20, 20, 30));

            _sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
                SamplerState.LinearClamp, DepthStencilState.None,
                RasterizerState.CullCounterClockwise, null,
                _camera.ViewMatrix);

            // Draw a checkerboard grid
            for (int row = 0; row < GridRows; row++)
            {
                for (int col = 0; col < GridCols; col++)
                {
                    bool light = (col + row) % 2 == 0;
                    Color cellColor = light
                        ? new Color(60, 60, 80)
                        : new Color(45, 45, 60);
                    var rect = new Rectangle(col * CellSize, row * CellSize,
                        CellSize, CellSize);
                    _sb.Draw(_whiteTex, rect, cellColor);
                }
            }

            // Draw grid lines (using white texture stretched thin)
            Color lineColor = new Color(100, 100, 120);
            for (int col = 0; col <= GridCols; col++)
            {
                var lineRect = new Rectangle(col * CellSize, 0, 1, WorldH);
                _sb.Draw(_whiteTex, lineRect, lineColor);
            }
            for (int row = 0; row <= GridRows; row++)
            {
                var lineRect = new Rectangle(0, row * CellSize, WorldW, 1);
                _sb.Draw(_whiteTex, lineRect, lineColor);
            }

            // Draw world origin marker (red square)
            var originRect = new Rectangle(0, 0, 8, 8);
            _sb.Draw(_whiteTex, originRect, Color.Red);

            _sb.End();

            if (!TestHarness.Headless)
            {
                DrawImGui();
            }
        }

        private void DrawImGui()
        {
            ImGuiBindings.BeginPanel("Camera2D Controls");
            ImGuiBindings.ImGui_Text($"Camera: ({_camera.Position.X:F0}, {_camera.Position.Y:F0})");
            ImGuiBindings.ImGui_Text($"Zoom: {_camera.Zoom:F2}x");
            ImGuiBindings.ImGui_Text($"Frame: {_frameCount}");
            ImGuiBindings.ImGui_Text($"Viewport: {_gdm.PreferredBackBufferWidth}x{_gdm.PreferredBackBufferHeight}");

            bool boundsClamp = _camera.WorldBoundMin.HasValue;
            if (ImGuiBindings.ImGui_Checkbox("Bounds Clamp", ref boundsClamp))
            {
                if (boundsClamp)
                {
                    _camera.WorldBoundMin = Vector2.Zero;
                    _camera.WorldBoundMax = new Vector2(WorldW, WorldH);
                }
                else
                {
                    _camera.WorldBoundMin = null;
                    _camera.WorldBoundMax = null;
                }
            }

            float zoom = _camera.Zoom;
            ImGuiBindings.ImGui_SliderFloat("Zoom", ref zoom,
                _camera.MinZoom, _camera.MaxZoom);
            if (Math.Abs(zoom - _camera.Zoom) > 0.001f)
            {
                _camera.Zoom = zoom;
                _camera.RebuildMatrices();
            }

            ImGuiBindings.ImGui_Separator();
            ImGuiBindings.ImGui_Text("WASD / Arrows: pan");
            ImGuiBindings.ImGui_Text("Scroll wheel: zoom");
            ImGuiBindings.ImGui_Text("Mouse edge: pan");
            ImGuiBindings.ImGui_Text("ESC: quit");
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
            using var game = new Camera2DTest();
            game.Run();
        }
    }
}
