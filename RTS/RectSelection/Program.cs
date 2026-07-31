using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNA.Test;

namespace FNA.RTS
{
    /// <summary>
    /// Verifies rectangle drag-selection: mouse drag draws a semi-transparent
    /// selection rectangle, entities within the rectangle are highlighted.
    ///
    /// This is the core RTS selection UX: click+drag → rect → select units inside.
    /// </summary>
    public class RectSelectionTest : Game
    {
        private GraphicsDeviceManager _gdm;
        private Camera2D _camera;
        private SpriteBatch _sb;
        private Texture2D _whiteTex;
        private Texture2D _unitTex;
        private int _frameCount;

        // Selection state
        private bool _isDragging;
        private Vector2 _dragStart;
        private Vector2 _dragEnd;
        private bool _selectionComplete;

        // Simulated entities (world positions)
        private Vector2[] _entityPositions;
        private bool[] _entitySelected;

        // For headless: pre-programmed drag
        private bool _headlessDragDone;

        public RectSelectionTest()
        {
            _gdm = new GraphicsDeviceManager(this);
            _gdm.PreferredBackBufferWidth = 800;
            _gdm.PreferredBackBufferHeight = 600;
            _gdm.SynchronizeWithVerticalRetrace = false;
            IsMouseVisible = true;
            Window.Title = "RTS/RectSelection — click+drag to select | ESC quit";
        }

        protected override void Initialize()
        {
            _camera = new Camera2D(_gdm.PreferredBackBufferWidth,
                _gdm.PreferredBackBufferHeight);
            _camera.Position = new Vector2(400, 300);
            _camera.RebuildMatrices();

            // Place entities in a grid-like pattern
            _entityPositions = new Vector2[]
            {
                new Vector2(100, 100), new Vector2(200, 100),
                new Vector2(300, 100), new Vector2(100, 200),
                new Vector2(200, 200), new Vector2(300, 200),
                new Vector2(400, 300), new Vector2(500, 400),
            };
            _entitySelected = new bool[_entityPositions.Length];

            base.Initialize();
        }

        protected override void LoadContent()
        {
            _sb = new SpriteBatch(GraphicsDevice);
            _whiteTex = TextureGen.White(GraphicsDevice);

            // Circle unit texture
            int size = 24;
            var data = new Color[size * size];
            float center = size / 2f, radius = size / 2f - 2;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - center + 0.5f, dy = y - center + 0.5f;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                if (d <= radius)
                    data[y * size + x] = d > radius - 2
                        ? new Color(60, 60, 60, 255)
                        : new Color(150, 150, 150, 255);
            }
            _unitTex = new Texture2D(GraphicsDevice, size, size);
            _unitTex.SetData(data);
            ImGuiTestHarness.Init(GraphicsDevice);
        }

        /// <summary>Check if a world position is inside a screen-space rectangle.</summary>
        private bool IsInRect(Vector2 worldPos, Rectangle rect)
        {
            Vector2 screen = _camera.WorldToScreen(worldPos);
            return rect.Contains((int)screen.X, (int)screen.Y);
        }

        private Rectangle GetDragRect()
        {
            int x = (int)Math.Min(_dragStart.X, _dragEnd.X);
            int y = (int)Math.Min(_dragStart.Y, _dragEnd.Y);
            int w = (int)Math.Abs(_dragEnd.X - _dragStart.X);
            int h = (int)Math.Abs(_dragEnd.Y - _dragStart.Y);
            return new Rectangle(x, y, w, h);
        }

        protected override void Update(GameTime gameTime)
        {
            _frameCount++;

            // Setup headless drag BEFORE the assertion frame so it's visible
            if (TestHarness.Headless && _frameCount == 4 && !_headlessDragDone)
            {
                _dragStart = new Vector2(80, 80);
                _dragEnd = new Vector2(320, 220);
                _isDragging = false;
                _selectionComplete = true;
                _headlessDragDone = true;

                var dragRect = GetDragRect();
                for (int i = 0; i < _entityPositions.Length; i++)
                    _entitySelected[i] = IsInRect(_entityPositions[i], dragRect);
            }

            int fails = 0;
            TestHarness.Tick(this, 5, () =>
            {
                var px = TestHarness.ReadBackbuffer(GraphicsDevice);
                var clearColor = new Color(20, 20, 30);

                // Verify selection rect + entities rendered
                fails += TestHarness.AssertCoverage(px, clearColor, 0.003f,
                    "selection-coverage");

                // Verify entities in drag rect are selected (highlighted green)
                int selected = 0;
                for (int i = 0; i < _entitySelected.Length; i++)
                    if (_entitySelected[i]) selected++;

                if (selected == 0)
                {
                    Console.WriteLine("FAIL [selection]: no entities selected");
                    fails++;
                }

                TestHarness.Report("RectSelection", fails);
            });

            if (TestHarness.Headless) return;

            var mouse = Mouse.GetState();
            var kb = Keyboard.GetState();
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            _camera.Update(dt);
            if (kb.IsKeyDown(Keys.Escape)) Exit();

            // Interactive selection
            if (mouse.LeftButton == ButtonState.Pressed)
            {
                if (!_isDragging)
                {
                    _isDragging = true;
                    _dragStart = new Vector2(mouse.X, mouse.Y);
                    _selectionComplete = false;
                }
                _dragEnd = new Vector2(mouse.X, mouse.Y);
            }
            else if (_isDragging)
            {
                _isDragging = false;
                _selectionComplete = true;
                _dragEnd = new Vector2(mouse.X, mouse.Y);

                // Apply selection
                var dragRect = GetDragRect();
                bool shiftHeld = kb.IsKeyDown(Keys.LeftShift) ||
                                 kb.IsKeyDown(Keys.RightShift);
                for (int i = 0; i < _entityPositions.Length; i++)
                {
                    bool inRect = IsInRect(_entityPositions[i], dragRect);
                    _entitySelected[i] = shiftHeld
                        ? (_entitySelected[i] || inRect)
                        : inRect;
                }
            }
        }

        protected override void Draw(GameTime gameTime)
        {
            ImGuiTestHarness.NewFrame(GraphicsDevice);
            GraphicsDevice.Clear(new Color(20, 20, 30));

            // Draw entities in world space
            _sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
                SamplerState.LinearClamp, DepthStencilState.None,
                RasterizerState.CullCounterClockwise, null,
                _camera.ViewMatrix);

            for (int i = 0; i < _entityPositions.Length; i++)
            {
                Color tint = _entitySelected[i]
                    ? new Color(100, 255, 100, 255)  // Selected: green tint
                    : new Color(200, 200, 200, 255);  // Normal: gray
                Vector2 pos = _entityPositions[i] - new Vector2(12, 12);
                _sb.Draw(_unitTex, pos, tint);
            }

            _sb.End();

            // Draw drag rectangle in screen space (no camera)
            if (_isDragging || _selectionComplete)
            {
                var dragRect = GetDragRect();
                _sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
                // Semi-transparent fill
                _sb.Draw(_whiteTex, dragRect, new Color(0, 255, 0, 40));
                // Border
                int bw = 1;
                _sb.Draw(_whiteTex, new Rectangle(dragRect.X, dragRect.Y, dragRect.Width, bw), Color.Lime);
                _sb.Draw(_whiteTex, new Rectangle(dragRect.X, dragRect.Y + dragRect.Height - bw, dragRect.Width, bw), Color.Lime);
                _sb.Draw(_whiteTex, new Rectangle(dragRect.X, dragRect.Y, bw, dragRect.Height), Color.Lime);
                _sb.Draw(_whiteTex, new Rectangle(dragRect.X + dragRect.Width - bw, dragRect.Y, bw, dragRect.Height), Color.Lime);
                _sb.End();
            }

            if (!TestHarness.Headless) DrawImGui();
        }

        private void DrawImGui()
        {
            int selCount = 0;
            for (int i = 0; i < _entitySelected.Length; i++)
                if (_entitySelected[i]) selCount++;

            ImGuiBindings.BeginPanel("RectSelection Test");
            ImGuiBindings.ImGui_Text($"Entities: {_entityPositions.Length}");
            ImGuiBindings.ImGui_Text($"Selected: {selCount}");
            ImGuiBindings.ImGui_Text($"Dragging: {_isDragging}");
            ImGuiBindings.ImGui_Separator();
            ImGuiBindings.ImGui_Text("Click+drag to select entities");
            ImGuiBindings.ImGui_Text("Hold Shift to add to selection");
            ImGuiBindings.ImGui_Text("Green = selected | Gray = unselected");
            ImGuiBindings.EndPanel();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ImGuiTestHarness.Shutdown(GraphicsDevice);
                _sb?.Dispose();
                _whiteTex?.Dispose();
                _unitTex?.Dispose();
            }
            base.Dispose(disposing);
        }

        [STAThread]
        static void Main(string[] args)
        {
            TestHarness.ParseArgs(args);
            using var game = new RectSelectionTest();
            game.Run();
        }
    }
}
