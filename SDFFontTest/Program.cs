using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNA.Test;

namespace SDFFontTest
{
    public class SDFFontTestGame : Game
    {
        private GraphicsDeviceManager _graphics;
        private SDFFont _cnFont;
        private SDFFont _enFont;
        private SDFTextRenderer _renderer;
        private SDFTextRenderer _enRenderer;

        // UI state
        private float _fontScale = 1.5f;
        private bool _showOutline;
        private float _outlineWidth = 0.15f;
        private float[] _outlineColor = { 0.0f, 0.0f, 0.0f };
        private float _weight = 0.0f;

        // Scroll
        private Vector2 _scrollOffset;
        private Point _lastMousePos;
        private bool _dragging;

        private const string CnSample =
            "你好世界有向离场中文支持思源体效果对比大小放\n" +
            "的一是在不了和人这中国以为大来们到地于出就\n" +
            "分成会可主发年动同工也能下过子说产种面方后\n" +
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ\n" +
            "abcdefghijklmnopqrstuvwxyz\n" +
            "0123456789";

        public SDFFontTestGame()
        {
            _graphics = new GraphicsDeviceManager(this)
            {
                PreferredBackBufferWidth = 1024,
                PreferredBackBufferHeight = 768,
                SynchronizeWithVerticalRetrace = false
            };
            Window.Title = "SDF Font Test | Drag to scroll | ESC=quit";
            IsMouseVisible = true;
        }

        protected override void LoadContent()
        {
            Console.WriteLine("[SDFFont] Loading fonts...");

            // Load Chinese font
            var cnPng = SDFFont.LoadEmbeddedBytes("SDFFontTest.cn_atlas.png");
            var cnJson = SDFFont.LoadEmbeddedString("SDFFontTest.cn_metrics.json");
            _cnFont = SDFFont.Load(GraphicsDevice, cnPng, cnJson);
            Console.WriteLine($"[SDFFont] CN font: {_cnFont.Glyphs.Count} glyphs, " +
                              $"atlas {_cnFont.AtlasWidth}×{_cnFont.AtlasHeight}");

            // Load English font
            var enPng = SDFFont.LoadEmbeddedBytes("SDFFontTest.en_atlas.png");
            var enJson = SDFFont.LoadEmbeddedString("SDFFontTest.en_metrics.json");
            _enFont = SDFFont.Load(GraphicsDevice, enPng, enJson);
            Console.WriteLine($"[SDFFont] EN font: {_enFont.Glyphs.Count} glyphs, " +
                              $"atlas {_enFont.AtlasWidth}×{_enFont.AtlasHeight}");

            // Create renderers (separate instances to avoid vertex buffer race)
            _renderer = new SDFTextRenderer(GraphicsDevice);
            _enRenderer = new SDFTextRenderer(GraphicsDevice);
            Console.WriteLine("[SDFFont] Renderers initialized");

            ImGuiTestHarness.Init(GraphicsDevice);
        }

        protected override void Update(GameTime gameTime)
        {
            var kb = Keyboard.GetState();
            if (kb.IsKeyDown(Keys.Escape))
                Exit();

            UpdateScroll();

            TestHarness.Tick(this, 3, () =>
            {
                var px = TestHarness.ReadBackbuffer(GraphicsDevice);
                int fails = 0;
                fails += TestHarness.AssertCoverage(px, Color.CornflowerBlue, 0.01f,
                    "sdf-text-coverage");
                TestHarness.Report("SDFFontTest", fails);
            });
        }

        private void UpdateScroll()
        {
            var ms = Mouse.GetState();
            bool overImGui = ms.X < 350 && ms.Y < 550;

            if (ms.LeftButton == ButtonState.Pressed && !overImGui)
            {
                if (!_dragging)
                {
                    _dragging = true;
                    _lastMousePos = new Point(ms.X, ms.Y);
                }
                else
                {
                    int dx = ms.X - _lastMousePos.X;
                    int dy = ms.Y - _lastMousePos.Y;
                    _scrollOffset.X -= dx;
                    _scrollOffset.Y -= dy;
                    _lastMousePos = new Point(ms.X, ms.Y);
                }
            }
            else
            {
                _dragging = false;
            }
        }

        protected override void Draw(GameTime gameTime)
        {
            ImGuiTestHarness.NewFrame(GraphicsDevice);

            GraphicsDevice.Clear(Color.CornflowerBlue);

            // ── SDF text rendering ──────────────────────────────────────
            GraphicsDevice.BlendState = BlendState.AlphaBlend;
            GraphicsDevice.DepthStencilState = DepthStencilState.None;
            GraphicsDevice.RasterizerState = RasterizerState.CullNone;

            int vpW = GraphicsDevice.Viewport.Width;
            int vpH = GraphicsDevice.Viewport.Height;
            var baseProj = Matrix.CreateOrthographicOffCenter(
                0, vpW, vpH, 0, 0, 1);
            var scrollProj = Matrix.CreateTranslation(
                -_scrollOffset.X, -_scrollOffset.Y, 0);
            var proj = scrollProj * baseProj;

            _renderer.OutlineColor = _showOutline
                ? new Color(_outlineColor[0], _outlineColor[1], _outlineColor[2])
                : Color.Transparent;
            _renderer.OutlineWidth = _showOutline ? _outlineWidth : 0.0f;

            _renderer.OutlineColor = _showOutline
                ? new Color(_outlineColor[0], _outlineColor[1], _outlineColor[2])
                : Color.Transparent;
            _renderer.OutlineWidth = _showOutline ? _outlineWidth : 0.0f;
            _renderer.Weight = _weight;

            // Sync EN renderer params
            _enRenderer.OutlineColor = _renderer.OutlineColor;
            _enRenderer.OutlineWidth = _renderer.OutlineWidth;
            _enRenderer.Weight = _renderer.Weight;

            GraphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;

            _renderer.DrawString(_cnFont, CnSample,
                new Vector2(50, 50), Color.White, _fontScale * 64.0f);
            _renderer.End(proj, _cnFont.Atlas);

            string scaleText = $"Scale: {_fontScale:F2}x | Weight: {_weight:F3}";
            var measure = _enFont.MeasureString(scaleText, 24.0f);
            _enRenderer.DrawString(_enFont, scaleText,
                new Vector2(vpW - measure.X - 20, vpH - 30),
                new Color(255, 255, 255, 180), 24.0f);
            _enRenderer.End(proj, _enFont.Atlas);

            if (!TestHarness.Headless)
            {
                GraphicsDevice.BlendState = BlendState.AlphaBlend;
                DrawImGui();
            }
        }

        private void DrawImGui()
        {
            ImGuiBindings.BeginPanel("SDF Font Controls");

            ImGuiBindings.ImGui_Text("Font Settings:");
            ImGuiBindings.ImGui_SliderFloat("Scale", ref _fontScale, 0.25f, 3.0f);
            ImGuiBindings.ImGui_SliderFloat("Weight", ref _weight, -0.12f, 0.12f);
            ImGuiBindings.ImGui_Separator();

            ImGuiBindings.ImGui_Text("Outline:");
            ImGuiBindings.ImGui_Checkbox("Show Outline", ref _showOutline);
            if (_showOutline)
            {
                ImGuiBindings.ImGui_SliderFloat("Width", ref _outlineWidth, 0.02f, 0.5f);
                ImGuiBindings.ImGui_ColorEdit3("Color", _outlineColor, 0);
            }
            ImGuiBindings.ImGui_Separator();

            ImGuiBindings.ImGui_Text($"CN Font: {_cnFont.Glyphs.Count} glyphs");
            ImGuiBindings.ImGui_Text($"EN Font: {_enFont.Glyphs.Count} glyphs");
            ImGuiBindings.ImGui_Text("Drag to scroll | ESC to quit");

            ImGuiBindings.EndPanel();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _renderer?.Dispose();
                _enRenderer?.Dispose();
                _cnFont?.Dispose();
                _enFont?.Dispose();
            }
            base.Dispose(disposing);
        }

        static void Main(string[] args)
        {
            TestHarness.ParseArgs(args);
            using var g = new SDFFontTestGame();
            g.Run();
        }
    }
}
