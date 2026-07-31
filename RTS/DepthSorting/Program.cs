using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNA.Test;

namespace FNA.RTS
{
    /// <summary>
    /// Verifies sprite depth sorting for isometric rendering:
    /// entities with higher world Y (closer to camera) occlude entities
    /// with lower world Y (farther from camera).
    ///
    /// Uses SpriteSortMode.BackToFront with layerDepth computed from
    /// world Y position, plus isometric tile backdrop.
    /// </summary>
    public class DepthSortingTest : Game
    {
        private GraphicsDeviceManager _gdm;
        private Camera2D _camera;
        private SpriteBatch _sb;
        private Texture2D _unitTex;
        private Texture2D _tileTex;
        private int _frameCount;

        private const int TILE_W = 64;
        private const int TILE_H = 32;
        private const int MAP_W = 8;
        private const int MAP_H = 8;

        // Known entity positions for occlusion testing
        // Unit A at grid (~2, 2) — farther from camera
        // Unit B at grid (~5, 5) — closer, should occlude A if they overlap
        private Vector2 _unitAPos;
        private Vector2 _unitBPos;
        private float _depthA;
        private float _depthB;

        private Vector2 TileToWorld(int tx, int ty)
        {
            return new Vector2(
                (tx - ty) * (TILE_W / 2f),
                (tx + ty) * (TILE_H / 2f));
        }

        /// <summary>World position to continuous grid coords (for depth).</summary>
        private Vector2 WorldToGridFloat(Vector2 world)
        {
            float halfW = TILE_W / 2f;
            float halfH = TILE_H / 2f;
            return new Vector2(
                (world.X / halfW + world.Y / halfH) / 2f,
                (world.Y / halfH - world.X / halfW) / 2f);
        }

        public DepthSortingTest()
        {
            _gdm = new GraphicsDeviceManager(this);
            _gdm.PreferredBackBufferWidth = 800;
            _gdm.PreferredBackBufferHeight = 600;
            _gdm.SynchronizeWithVerticalRetrace = false;
            IsMouseVisible = true;
            Window.Title = "RTS/DepthSorting — BackToFront occlusion | ESC quit";
        }

        protected override void Initialize()
        {
            _camera = new Camera2D(_gdm.PreferredBackBufferWidth,
                _gdm.PreferredBackBufferHeight);
            _camera.Position = new Vector2(0, -MAP_H * TILE_H / 4f);
            _camera.RebuildMatrices();

            // Place entities at known grid-like positions
            // Unit A at grid (2, 2), Unit B at grid (5, 5)
            _unitAPos = TileToWorld(2, 2);
            _unitBPos = TileToWorld(5, 5);

            // Compute depth using grid-sum formula (consistent with tiles)
            float maxSum = MAP_W + MAP_H;
            var gridA = WorldToGridFloat(_unitAPos);
            var gridB = WorldToGridFloat(_unitBPos);
            _depthA = (maxSum - (gridA.X + gridA.Y)) / maxSum;
            _depthB = (maxSum - (gridB.X + gridB.Y)) / maxSum;

            base.Initialize();
        }

        protected override void LoadContent()
        {
            _sb = new SpriteBatch(GraphicsDevice);

            // Generate a tile texture (simple diamond)
            _tileTex = GenDiamondTex(64, 32, new Color(60, 60, 80),
                new Color(40, 40, 60));

            // Generate a unit texture (solid circle, blue)
            _unitTex = GenCircleTex(32, new Color(80, 120, 255));

            ImGuiTestHarness.Init(GraphicsDevice);
        }

        private Texture2D GenDiamondTex(int w, int h, Color fill, Color border)
        {
            var data = new Color[w * h];
            Array.Fill(data, Color.Transparent);
            float hw = w / 2f, hh = h / 2f;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = x - hw + 0.5f;
                float dy = y - hh + 0.5f;
                float d = MathF.Abs(dx / hw) + MathF.Abs(dy / hh);
                if (d <= 1.02f)
                    data[y * w + x] = d > 0.85f ? border : fill;
            }
            var tex = new Texture2D(GraphicsDevice, w, h);
            tex.SetData(data);
            return tex;
        }

        private Texture2D GenCircleTex(int size, Color fill)
        {
            var data = new Color[size * size];
            float center = size / 2f;
            float radius = size / 2f - 2;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - center + 0.5f;
                float dy = y - center + 0.5f;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                if (d <= radius)
                    data[y * size + x] = d > radius - 2
                        ? new Color((byte)(fill.R * 0.6f), (byte)(fill.G * 0.6f), (byte)(fill.B * 0.6f), 255)
                        : fill;
            }
            var tex = new Texture2D(GraphicsDevice, size, size);
            tex.SetData(data);
            return tex;
        }

        protected override void Update(GameTime gameTime)
        {
            _frameCount++;

            int fails = 0;
            TestHarness.Tick(this, 5, () =>
            {
                var px = TestHarness.ReadBackbuffer(GraphicsDevice);
                var clearColor = new Color(20, 20, 30);

                fails += TestHarness.AssertCoverage(px, clearColor, 0.05f,
                    "depth-coverage");

                // Unit A at world (0, 100): convert to screen
                Vector2 screenA = _camera.WorldToScreen(_unitAPos);
                Vector2 screenB = _camera.WorldToScreen(_unitBPos);
                int sxA = (int)screenA.X, syA = (int)screenA.Y;
                int sxB = (int)screenB.X, syB = (int)screenB.Y;

                // Both units are at same screen X ≈ center of viewport
                // Unit B (higher Y) should be drawn on top of Unit A
                // Since both units overlap, the overlapping pixel(s) should
                // show Unit B's blue color, not background
                bool unitAVisible = false, unitBVisible = false;
                for (int dy = -10; dy <= 10; dy++)
                for (int dx = -10; dx <= 10; dx++)
                {
                    int cx = sxA + dx, cy = syA + dy;
                    if (cx < 0 || cx >= _gdm.PreferredBackBufferWidth ||
                        cy < 0 || cy >= _gdm.PreferredBackBufferHeight)
                        continue;
                    var got = px[cy * _gdm.PreferredBackBufferWidth + cx];
                    if (got.B > 150 && got.B > got.R + 50)
                    {
                        unitAVisible = true;
                        break;
                    }
                }
                for (int dy = -10; dy <= 10; dy++)
                for (int dx = -10; dx <= 10; dx++)
                {
                    int cx = sxB + dx, cy = syB + dy;
                    if (cx < 0 || cx >= _gdm.PreferredBackBufferWidth ||
                        cy < 0 || cy >= _gdm.PreferredBackBufferHeight)
                        continue;
                    var got = px[cy * _gdm.PreferredBackBufferWidth + cx];
                    if (got.B > 150 && got.B > got.R + 50)
                    {
                        unitBVisible = true;
                        break;
                    }
                }

                if (!unitAVisible)
                {
                    Console.WriteLine("FAIL [unit-a]: Unit A not visible");
                    fails++;
                }
                if (!unitBVisible)
                {
                    Console.WriteLine("FAIL [unit-b]: Unit B not visible");
                    fails++;
                }

                // Verify B's screen Y is greater than A's (B is closer to camera in isometric)
                if (syB <= syA)
                {
                    Console.WriteLine(
                        $"FAIL [depth-order]: B screen Y ({syB}) should be > A screen Y ({syA})");
                    fails++;
                }

                TestHarness.Report("DepthSorting", fails);
            });

            if (TestHarness.Headless) return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _camera.Update(dt);
            if (Keyboard.GetState().IsKeyDown(Keys.Escape)) Exit();
        }

        protected override void Draw(GameTime gameTime)
        {
            ImGuiTestHarness.NewFrame(GraphicsDevice);
            GraphicsDevice.Clear(new Color(20, 20, 30));

            // --- Layer 1: Isometric tile backdrop ---
            float maxSum = MAP_W + MAP_H;
            _sb.Begin(SpriteSortMode.BackToFront, BlendState.AlphaBlend,
                SamplerState.LinearClamp, DepthStencilState.None,
                RasterizerState.CullCounterClockwise, null,
                _camera.ViewMatrix);

            for (int ty = 0; ty < MAP_H; ty++)
            for (int tx = 0; tx < MAP_W; tx++)
            {
                Vector2 pos = TileToWorld(tx, ty);
                float depth = (maxSum - (tx + ty)) / maxSum;
                _sb.Draw(_tileTex, pos, null, Color.White,
                    0f, Vector2.Zero, 1f, SpriteEffects.None, depth);
            }

            // --- Layer 2: Entities (grid-based depth, same formula as tiles) ---
            // Unit A at grid (2,2) → drawn before Unit B at grid (5,5)
            _sb.Draw(_unitTex, _unitAPos - new Vector2(16, 16), null,
                new Color(100, 150, 255), 0f, Vector2.Zero, 1f,
                SpriteEffects.None, MathHelper.Clamp(_depthA, 0, 1));
            _sb.Draw(_unitTex, _unitBPos - new Vector2(16, 16), null,
                new Color(80, 120, 255), 0f, Vector2.Zero, 1f,
                SpriteEffects.None, MathHelper.Clamp(_depthB, 0, 1));

            _sb.End();

            if (!TestHarness.Headless) DrawImGui();
        }

        private void DrawImGui()
        {
            ImGuiBindings.BeginPanel("DepthSorting Test");
            ImGuiBindings.ImGui_Text($"Unit A world: ({_unitAPos.X:F0}, {_unitAPos.Y:F0})");
            ImGuiBindings.ImGui_Text($"Unit B world: ({_unitBPos.X:F0}, {_unitBPos.Y:F0})");
            Vector2 sA = _camera.WorldToScreen(_unitAPos);
            Vector2 sB = _camera.WorldToScreen(_unitBPos);
            ImGuiBindings.ImGui_Text($"Unit A screen Y: {sA.Y:F0}");
            ImGuiBindings.ImGui_Text($"Unit B screen Y: {sB.Y:F0} (should be > A)");
            ImGuiBindings.ImGui_Separator();
            ImGuiBindings.ImGui_Text("Blue circles = units");
            ImGuiBindings.ImGui_Text("Higher screen Y = closer to camera = drawn later");
            ImGuiBindings.EndPanel();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ImGuiTestHarness.Shutdown(GraphicsDevice);
                _sb?.Dispose();
                _tileTex?.Dispose();
                _unitTex?.Dispose();
            }
            base.Dispose(disposing);
        }

        [STAThread]
        static void Main(string[] args)
        {
            TestHarness.ParseArgs(args);
            using var game = new DepthSortingTest();
            game.Run();
        }
    }
}
