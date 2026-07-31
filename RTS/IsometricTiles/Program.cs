using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNA.Test;

namespace FNA.RTS
{
    /// <summary>
    /// Verifies isometric tile map rendering with procedural diamond tiles,
    /// depth sorting, and camera integration.
    /// </summary>
    public class IsometricTilesTest : Game
    {
        private GraphicsDeviceManager _gdm;
        private Camera2D _camera;
        private SpriteBatch _sb;

        // Tileset atlas
        private Texture2D _tileset;
        private const int TILE_W = 64;
        private const int TILE_H = 32;
        private const int TILESET_COLS = 4;  // Grass, Water, Cliff, Impassable

        // Map
        private const int MAP_W = 10;
        private const int MAP_H = 10;
        private TileType[,] _map;

        private int _frameCount;

        // Pre-computed world position for each tile center (for depth/ordering)
        private enum TileType { Grass = 0, Water = 1, Cliff = 2, Impassable = 3 }

        public IsometricTilesTest()
        {
            _gdm = new GraphicsDeviceManager(this);
            _gdm.PreferredBackBufferWidth = 800;
            _gdm.PreferredBackBufferHeight = 600;
            _gdm.SynchronizeWithVerticalRetrace = false;
            IsMouseVisible = true;
            Window.Title = "RTS/IsometricTiles — 10x10 diamond map | ESC quit";
        }

        protected override void Initialize()
        {
            _camera = new Camera2D(_gdm.PreferredBackBufferWidth,
                _gdm.PreferredBackBufferHeight);
            // Center camera on the isometric map
            _camera.Position = new Vector2(0, -MAP_H * TILE_H / 4f);
            _camera.RebuildMatrices();
            base.Initialize();
        }

        protected override void LoadContent()
        {
            _sb = new SpriteBatch(GraphicsDevice);
            _tileset = GenerateTileset();
            _map = GenerateTestMap();
            ImGuiTestHarness.Init(GraphicsDevice);
        }

        private Texture2D GenerateTileset()
        {
            int atlasW = TILESET_COLS * TILE_W;
            int atlasH = TILE_H;
            var data = new Color[atlasW * atlasH];
            Array.Fill(data, Color.Transparent);

            for (int t = 0; t < TILESET_COLS; t++)
            {
                int ox = t * TILE_W;
                Color fill = t switch
                {
                    0 => new Color(76, 153, 0),     // Grass: green
                    1 => new Color(51, 102, 255),   // Water: blue
                    2 => new Color(160, 160, 160),  // Cliff: gray
                    3 => new Color(180, 60, 60),    // Impassable: red
                    _ => Color.Magenta
                };
                Color border = new Color(
                    (byte)(fill.R * 0.6f),
                    (byte)(fill.G * 0.6f),
                    (byte)(fill.B * 0.6f),
                    255);

                float halfW = TILE_W / 2f;
                float halfH = TILE_H / 2f;
                for (int py = 0; py < TILE_H; py++)
                for (int px = 0; px < TILE_W; px++)
                {
                    float dx = px - halfW + 0.5f;
                    float dy = py - halfH + 0.5f;
                    float dist = MathF.Abs(dx / halfW) + MathF.Abs(dy / halfH);
                    if (dist <= 1.02f)
                    {
                        data[(py) * atlasW + (ox + px)] = dist > 0.85f ? border : fill;
                    }
                }
            }

            var tex = new Texture2D(GraphicsDevice, atlasW, atlasH);
            tex.SetData(data);
            return tex;
        }

        private TileType[,] GenerateTestMap()
        {
            var map = new TileType[MAP_W, MAP_H];
            for (int x = 0; x < MAP_W; x++)
            for (int y = 0; y < MAP_H; y++)
            {
                // Water pond in the center
                if (x >= 4 && x <= 6 && y >= 4 && y <= 6)
                    map[x, y] = TileType.Water;
                // Cliff edge on the right
                else if (x >= 8)
                    map[x, y] = TileType.Cliff;
                // Impassable obstacles
                else if ((x == 2 && y == 3) || (x == 7 && y == 2))
                    map[x, y] = TileType.Impassable;
                else
                    map[x, y] = TileType.Grass;
            }
            return map;
        }

        /// <summary>Convert grid coordinates to world position (tile top-left corner).</summary>
        public static Vector2 GridToWorld(int gx, int gy)
        {
            return new Vector2(
                (gx - gy) * (TILE_W / 2f),
                (gx + gy) * (TILE_H / 2f)
            );
        }

        protected override void Update(GameTime gameTime)
        {
            _frameCount++;

            int fails = 0;
            TestHarness.Tick(this, 5, () =>
            {
                var px = TestHarness.ReadBackbuffer(GraphicsDevice);
                var clearColor = new Color(20, 20, 30);

                // Coverage: isometric tiles should cover a significant portion
                fails += TestHarness.AssertCoverage(px, clearColor, 0.10f,
                    "tile-coverage");

                // Verify a grass tile pixel exists.
                // Tile (1, 1): world pos ~ (0, 32). After camera transform,
                // screen center maps world center. We check a region of the
                // diamond's approximate screen bounding box.
                Vector2 worldPos = GridToWorld(1, 1);
                Vector2 screenPos = _camera.WorldToScreen(worldPos);
                int sx = (int)screenPos.X + TILE_W / 2;  // center of diamond
                int sy = (int)screenPos.Y + TILE_H / 2;

                if (sx >= 0 && sx < _gdm.PreferredBackBufferWidth &&
                    sy >= 0 && sy < _gdm.PreferredBackBufferHeight)
                {
                    var got = px[sy * _gdm.PreferredBackBufferWidth + sx];
                    // Grass should be green-ish
                    bool isGrass = got.G > 100 && got.G > got.R && got.G > got.B;
                    if (!isGrass)
                    {
                        Console.WriteLine(
                            $"FAIL [grass-tile]: at screen ({sx},{sy}) expected greenish got {got}");
                        fails++;
                    }
                }

                // Verify a water tile pixel exists
                worldPos = GridToWorld(5, 5);
                screenPos = _camera.WorldToScreen(worldPos);
                sx = (int)screenPos.X + TILE_W / 2;
                sy = (int)screenPos.Y + TILE_H / 2;
                if (sx >= 0 && sx < _gdm.PreferredBackBufferWidth &&
                    sy >= 0 && sy < _gdm.PreferredBackBufferHeight)
                {
                    var got = px[sy * _gdm.PreferredBackBufferWidth + sx];
                    bool isWater = got.B > 150 && got.B > got.R && got.B > got.G;
                    if (!isWater)
                    {
                        Console.WriteLine(
                            $"FAIL [water-tile]: at screen ({sx},{sy}) expected blueish got {got}");
                        fails++;
                    }
                }

                TestHarness.Report("IsometricTiles", fails);
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

            // Render isometric tiles with depth sorting
            // BackToFront: higher depth = drawn first (behind)
            // layerDepth: (maxGridSum - (gx+gy)) / maxGridSum → far tiles depth~1, near tiles depth~0
            float maxSum = MAP_W + MAP_H;
            _sb.Begin(SpriteSortMode.BackToFront, BlendState.AlphaBlend,
                SamplerState.LinearClamp, DepthStencilState.None,
                RasterizerState.CullCounterClockwise, null,
                _camera.ViewMatrix);

            for (int gy = 0; gy < MAP_H; gy++)
            {
                for (int gx = 0; gx < MAP_W; gx++)
                {
                    TileType tile = _map[gx, gy];
                    Vector2 pos = GridToWorld(gx, gy);
                    Rectangle srcRect = new Rectangle(
                        (int)tile * TILE_W, 0, TILE_W, TILE_H);

                    // depth = 1.0 (far/behind) to 0.0 (near/front)
                    float depth = (maxSum - (gx + gy)) / maxSum;

                    _sb.Draw(_tileset, pos, srcRect, Color.White,
                        0f, Vector2.Zero, 1f, SpriteEffects.None, depth);
                }
            }

            _sb.End();

            if (!TestHarness.Headless) DrawImGui();

            // Draw coordinate reference overlay
            _sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
                SamplerState.LinearClamp, DepthStencilState.None,
                RasterizerState.CullCounterClockwise, null,
                _camera.ViewMatrix);
            _sb.End();
        }

        private void DrawImGui()
        {
            ImGuiBindings.BeginPanel("IsometricTiles Controls");
            ImGuiBindings.ImGui_Text($"Map: {MAP_W}x{MAP_H}");
            ImGuiBindings.ImGui_Text($"Tiles: Grass(grn) Water(blu) Cliff(gry) Impass(red)");
            ImGuiBindings.ImGui_Text($"Camera: ({_camera.Position.X:F0}, {_camera.Position.Y:F0})");
            ImGuiBindings.ImGui_Text($"Zoom: {_camera.Zoom:F2}x");
            ImGuiBindings.ImGui_Text($"Frame: {_frameCount}");
            ImGuiBindings.ImGui_Separator();
            ImGuiBindings.ImGui_Text("WASD: pan  |  Scroll: zoom  |  ESC: quit");
            ImGuiBindings.EndPanel();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ImGuiTestHarness.Shutdown(GraphicsDevice);
                _sb?.Dispose();
                _tileset?.Dispose();
            }
            base.Dispose(disposing);
        }

        [STAThread]
        static void Main(string[] args)
        {
            TestHarness.ParseArgs(args);
            using var game = new IsometricTilesTest();
            game.Run();
        }
    }
}
