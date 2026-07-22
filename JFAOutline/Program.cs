using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNA.Test;

namespace JFAOutlineDemo
{
    public class JFAOutlineGame : Game
    {
        private GraphicsDeviceManager graphics;
        private OutlineRenderer _renderer;

        // ── Geometry ──────────────────────────────────────────────────
        private VertexBuffer _sphereVB, _cubeVB, _cylinderVB, _coneVB, _pyramidVB, _groundVB;
        private int _spherePrims, _cubePrims, _cylinderPrims, _conePrims, _pyramidPrims, _groundPrims;

        // ── Camera ────────────────────────────────────────────────────
        private float _orbitYaw   = MathHelper.ToRadians(45);
        private float _orbitPitch = MathHelper.ToRadians(25);
        private float _orbitDist  = 8.0f;
        private Vector3 _orbitTarget = new Vector3(0, -0.5f, 0);
        private Point _lastMousePos;
        private bool _dragging;
        private int _lastScrollValue;

        // ── Outline toggles ───────────────────────────────────────────
        private bool _sphereOutline   = true;
        private bool _cubeOutline     = true;
        private bool _cylinderOutline = true;
        private bool _coneOutline     = true;
        private bool _pyramidOutline  = false; // default OFF — acts as occluder
        private bool _groundOutline   = false;

        // ── Outline parameters ────────────────────────────────────────
        private float _outlineWidth = 3.0f;
        private float[] _outlineColor = { 1.0f, 1.0f, 0.0f };

        // ── X-Ray parameters ──────────────────────────────────────────
        private bool _xRayEnabled = true;
        private float[] _xRayColor = { 0.0f, 0.8f, 1.0f };
        private float _xRayAlpha = 0.5f;

        // ── Resize tracking ───────────────────────────────────────────
        private int _lastW, _lastH;

        public JFAOutlineGame()
        {
            graphics = new GraphicsDeviceManager(this)
            {
                PreferredBackBufferWidth = 800,
                PreferredBackBufferHeight = 600,
                SynchronizeWithVerticalRetrace = false
            };
            Window.Title = "JFA Outline + X-Ray | Drag to orbit | ESC=quit";
            IsMouseVisible = true;
        }

        protected override void LoadContent()
        {
            int w = graphics.PreferredBackBufferWidth;
            int h = graphics.PreferredBackBufferHeight;
            _lastW = w; _lastH = h;

            _renderer = new OutlineRenderer(GraphicsDevice, w, h);
            Console.WriteLine($"[JFAOutline] Renderer ready: {w}x{h}");

            CreateGeometry();
            ImGuiTestHarness.Init(GraphicsDevice);
        }

        private void CreateGeometry()
        {
            var sphereVerts   = GeometryGen.Sphere(24, 32);
            var cubeVerts     = GeometryGen.Cube();
            var cylinderVerts = GeometryGen.Cylinder(16, 32);
            var coneVerts     = GeometryGen.Cone(16, 32);
            var pyramidVerts  = GeometryGen.Pyramid(2.0f, 3.0f); // 4×4 base, height 3

            _sphereVB   = MakeVB(sphereVerts);
            _cubeVB     = MakeVB(cubeVerts);
            _cylinderVB = MakeVB(cylinderVerts);
            _coneVB     = MakeVB(coneVerts);
            _pyramidVB  = MakeVB(pyramidVerts);

            _spherePrims   = sphereVerts.Length / 3;
            _cubePrims     = cubeVerts.Length / 3;
            _cylinderPrims = cylinderVerts.Length / 3;
            _conePrims     = coneVerts.Length / 3;
            _pyramidPrims  = pyramidVerts.Length / 3;

            var groundVerts = MakeGroundPlane(5.0f, -1.5f);
            _groundVB = MakeVB(groundVerts);
            _groundPrims = groundVerts.Length / 3;

            Console.WriteLine($"[JFAOutline] Pyramid={_pyramidPrims}tris, total geo loaded.");
        }

        private VertexBuffer MakeVB(VertexPositionNormalTexture[] verts)
        {
            var vb = new VertexBuffer(GraphicsDevice, typeof(VertexPositionNormalTexture),
                verts.Length, BufferUsage.WriteOnly);
            vb.SetData(verts);
            return vb;
        }

        private static VertexPositionNormalTexture[] MakeGroundPlane(float halfSize, float y)
        {
            float s = halfSize;
            return new VertexPositionNormalTexture[]
            {
                new(new Vector3(-s, y, -s), Vector3.Up, new Vector2(0, 0)),
                new(new Vector3( s, y, -s), Vector3.Up, new Vector2(1, 0)),
                new(new Vector3(-s, y,  s), Vector3.Up, new Vector2(0, 1)),
                new(new Vector3(-s, y,  s), Vector3.Up, new Vector2(0, 1)),
                new(new Vector3( s, y, -s), Vector3.Up, new Vector2(1, 0)),
                new(new Vector3( s, y,  s), Vector3.Up, new Vector2(1, 1)),
            };
        }

        // ── Mouse camera ──────────────────────────────────────────────

        private void UpdateCamera()
        {
            var ms = Mouse.GetState();
            var kb = Keyboard.GetState();

            if (kb.IsKeyDown(Keys.R))
            {
                _orbitYaw   = MathHelper.ToRadians(45);
                _orbitPitch = MathHelper.ToRadians(25);
                _orbitDist  = 8.0f;
            }

            int scrollDelta = ms.ScrollWheelValue - _lastScrollValue;
            if (scrollDelta != 0)
            {
                _orbitDist -= scrollDelta * 0.005f;
                _orbitDist = MathHelper.Clamp(_orbitDist, 2.0f, 20.0f);
            }
            _lastScrollValue = ms.ScrollWheelValue;

            bool overImGui = ms.X < 300 && ms.Y < 450;

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
                    const float sensitivity = 0.005f;
                    _orbitYaw   -= dx * sensitivity;
                    _orbitPitch += dy * sensitivity;
                    _orbitPitch = MathHelper.Clamp(_orbitPitch,
                        -MathHelper.PiOver2 + 0.05f, MathHelper.PiOver2 - 0.05f);
                    _lastMousePos = new Point(ms.X, ms.Y);
                }
            }
            else
            {
                _dragging = false;
            }
        }

        private Matrix GetViewMatrix()
        {
            float cosP = (float)Math.Cos(_orbitPitch);
            float sinP = (float)Math.Sin(_orbitPitch);
            float cosY = (float)Math.Cos(_orbitYaw);
            float sinY = (float)Math.Sin(_orbitYaw);
            var camPos = new Vector3(
                _orbitTarget.X + cosP * sinY * _orbitDist,
                _orbitTarget.Y + sinP * _orbitDist,
                _orbitTarget.Z + cosP * cosY * _orbitDist);
            return Matrix.CreateLookAt(camPos, _orbitTarget, Vector3.Up);
        }

        // ── Update / Draw ─────────────────────────────────────────────

        protected override void Update(GameTime gameTime)
        {
            var kb = Keyboard.GetState();
            if (kb.IsKeyDown(Keys.Escape)) Exit();

            UpdateCamera();

            int w = GraphicsDevice.Viewport.Width;
            int h = GraphicsDevice.Viewport.Height;
            if (w > 0 && h > 0 && (w != _lastW || h != _lastH))
            {
                _renderer.Resize(w, h);
                _lastW = w; _lastH = h;
            }

            TestHarness.Tick(this, 3, () =>
            {
                var px = TestHarness.ReadBackbuffer(GraphicsDevice);
                int fails = 0;
                fails += TestHarness.AssertCoverage(px, Color.CornflowerBlue, 0.05f, "jfa-bg-coverage");
                TestHarness.Report("JFAOutline", fails);
            });
        }

        protected override void Draw(GameTime gameTime)
        {
            ImGuiTestHarness.NewFrame(GraphicsDevice);

            var view = GetViewMatrix();
            float aspect = GraphicsDevice.Viewport.AspectRatio;
            var proj = Matrix.CreatePerspectiveFieldOfView(
                MathHelper.PiOver4, aspect, 0.1f, 100f);

            // ═══════════════════════════════════════════════════════════
            // Phase 1: Scene RT
            // ═══════════════════════════════════════════════════════════
            GraphicsDevice.SetRenderTarget(_renderer.SceneRT);
            GraphicsDevice.Clear(Color.CornflowerBlue);
            GraphicsDevice.BlendState = BlendState.Opaque;
            GraphicsDevice.DepthStencilState = DepthStencilState.Default;
            GraphicsDevice.RasterizerState = RasterizerState.CullNone;

            DrawAllSceneObjects(view, proj);

            GraphicsDevice.SetRenderTarget(null);

            // ═══════════════════════════════════════════════════════════
            // Phase 2a: FullMask RT (NO depth — full silhouettes)
            // ═══════════════════════════════════════════════════════════
            GraphicsDevice.SetRenderTarget(_renderer.FullMaskRT);
            GraphicsDevice.Clear(ClearOptions.Target, Color.Black, 1.0f, 0);
            GraphicsDevice.BlendState = BlendState.Opaque;
            GraphicsDevice.DepthStencilState = DepthStencilState.None;
            GraphicsDevice.RasterizerState = RasterizerState.CullNone;

            DrawOutlineObjectsOnly(view, proj);

            GraphicsDevice.SetRenderTarget(null);

            // ═══════════════════════════════════════════════════════════
            // Phase 2b: VisibleMask RT (WITH depth — nearest outline surface)
            // ═══════════════════════════════════════════════════════════
            GraphicsDevice.SetRenderTarget(_renderer.VisibleMaskRT);
            GraphicsDevice.Clear(ClearOptions.Target | ClearOptions.DepthBuffer,
                Color.Black, 1.0f, 0);
            GraphicsDevice.BlendState = BlendState.Opaque;
            GraphicsDevice.DepthStencilState = DepthStencilState.Default;
            GraphicsDevice.RasterizerState = RasterizerState.CullNone;

            // Step 1: ALL objects as BLACK → records every visible surface's depth
            DrawAllObjectsBlack(view, proj);

            // Step 2: Outline objects as WHITE → overwrites black only where visible
            DrawOutlineObjectsOnly(view, proj);

            GraphicsDevice.SetRenderTarget(null);

            // ═══════════════════════════════════════════════════════════
            // Phase 3-4: JFA
            // ═══════════════════════════════════════════════════════════
            GraphicsDevice.BlendState = BlendState.Opaque;
            GraphicsDevice.DepthStencilState = DepthStencilState.None;
            GraphicsDevice.RasterizerState = RasterizerState.CullNone;

            _renderer.RunJFA();

            // ═══════════════════════════════════════════════════════════
            // Phase 5: Composite → backbuffer
            // ═══════════════════════════════════════════════════════════
            _renderer.Composite(
                _outlineWidth,
                new Vector3(_outlineColor[0], _outlineColor[1], _outlineColor[2]),
                new Vector3(_xRayColor[0], _xRayColor[1], _xRayColor[2]),
                _xRayAlpha, _xRayEnabled);

            // ═══════════════════════════════════════════════════════════
            // ImGui
            // ═══════════════════════════════════════════════════════════
            if (!TestHarness.Headless)
            {
                GraphicsDevice.BlendState = BlendState.AlphaBlend;
                DrawImGui();
            }
        }

        // ── Object drawing helpers ────────────────────────────────────

        private void DrawAllSceneObjects(Matrix view, Matrix proj)
        {
            DrawScene(_sphereVB, _spherePrims,
                Matrix.CreateTranslation(-2.0f, -0.5f, 0.0f), view, proj,
                new Vector3(0.3f, 0.5f, 1.0f));
            DrawScene(_cubeVB, _cubePrims,
                Matrix.CreateRotationY(0.3f) * Matrix.CreateTranslation(1.5f, -1.0f, 1.5f), view, proj,
                new Vector3(1.0f, 0.3f, 0.3f));
            DrawScene(_cylinderVB, _cylinderPrims,
                Matrix.CreateTranslation(2.5f, -0.5f, -1.2f), view, proj,
                new Vector3(0.3f, 1.0f, 0.3f));
            DrawScene(_coneVB, _conePrims,
                Matrix.CreateTranslation(-1.5f, -0.5f, 1.8f), view, proj,
                new Vector3(1.0f, 0.6f, 0.1f));
            DrawScene(_pyramidVB, _pyramidPrims,
                Matrix.CreateTranslation(-0.5f, -0.5f, -0.3f), view, proj,
                new Vector3(0.7f, 0.65f, 0.55f));
            DrawScene(_groundVB, _groundPrims,
                Matrix.Identity, view, proj,
                new Vector3(0.55f, 0.55f, 0.55f));
        }

        private void DrawOutlineObjectsOnly(Matrix view, Matrix proj)
        {
            // Only objects with outline enabled go into the mask
            if (_sphereOutline)
                DrawMask(_sphereVB, _spherePrims,
                    Matrix.CreateTranslation(-2.0f, -0.5f, 0.0f), view, proj);
            if (_cubeOutline)
                DrawMask(_cubeVB, _cubePrims,
                    Matrix.CreateRotationY(0.3f) * Matrix.CreateTranslation(1.5f, -1.0f, 1.5f), view, proj);
            if (_cylinderOutline)
                DrawMask(_cylinderVB, _cylinderPrims,
                    Matrix.CreateTranslation(2.5f, -0.5f, -1.2f), view, proj);
            if (_coneOutline)
                DrawMask(_coneVB, _conePrims,
                    Matrix.CreateTranslation(-1.5f, -0.5f, 1.8f), view, proj);
            if (_pyramidOutline)
                DrawMask(_pyramidVB, _pyramidPrims,
                    Matrix.CreateTranslation(-0.5f, -0.5f, -0.3f), view, proj);
            if (_groundOutline)
                DrawMask(_groundVB, _groundPrims, Matrix.Identity, view, proj);
        }

        private void DrawAllObjectsBlack(Matrix view, Matrix proj)
        {
            // Every object writes black + depth — any frontmost surface
            // blocks outline objects behind it in the VisibleMask.
            _renderer.DrawOccluderGeometry(_sphereVB, _spherePrims,
                Matrix.CreateTranslation(-2.0f, -0.5f, 0.0f), view, proj);
            _renderer.DrawOccluderGeometry(_cubeVB, _cubePrims,
                Matrix.CreateRotationY(0.3f) * Matrix.CreateTranslation(1.5f, -1.0f, 1.5f), view, proj);
            _renderer.DrawOccluderGeometry(_cylinderVB, _cylinderPrims,
                Matrix.CreateTranslation(2.5f, -0.5f, -1.2f), view, proj);
            _renderer.DrawOccluderGeometry(_coneVB, _conePrims,
                Matrix.CreateTranslation(-1.5f, -0.5f, 1.8f), view, proj);
            _renderer.DrawOccluderGeometry(_pyramidVB, _pyramidPrims,
                Matrix.CreateTranslation(-0.5f, -0.5f, -0.3f), view, proj);
            _renderer.DrawOccluderGeometry(_groundVB, _groundPrims, Matrix.Identity, view, proj);
        }

        private void DrawScene(VertexBuffer vb, int prims, Matrix world,
            Matrix view, Matrix proj, Vector3 color)
        {
            _renderer.DrawSceneGeometry(vb, prims, world, view, proj, color);
        }

        private void DrawMask(VertexBuffer vb, int prims, Matrix world,
            Matrix view, Matrix proj)
        {
            _renderer.DrawMaskGeometry(vb, prims, world, view, proj);
        }

        // ── ImGui ─────────────────────────────────────────────────────

        private void DrawImGui()
        {
            ImGuiBindings.BeginPanel("JFA Outline + X-Ray");

            ImGuiBindings.ImGui_Text("Per-Object Outlines:");
            ImGuiBindings.ImGui_Checkbox("Sphere",    ref _sphereOutline);
            ImGuiBindings.ImGui_Checkbox("Cube",      ref _cubeOutline);
            ImGuiBindings.ImGui_Checkbox("Cylinder",  ref _cylinderOutline);
            ImGuiBindings.ImGui_Checkbox("Cone",      ref _coneOutline);
            ImGuiBindings.ImGui_Checkbox("Pyramid",   ref _pyramidOutline);
            ImGuiBindings.ImGui_Checkbox("Ground",    ref _groundOutline);
            ImGuiBindings.ImGui_Separator();

            ImGuiBindings.ImGui_Text("Outline:");
            ImGuiBindings.ImGui_SliderFloat("Width", ref _outlineWidth, 0.5f, 20.0f);
            ImGuiBindings.ImGui_ColorEdit3("Color", _outlineColor, 0);
            ImGuiBindings.ImGui_Separator();

            ImGuiBindings.ImGui_Text("X-Ray Occlusion:");
            ImGuiBindings.ImGui_Checkbox("Enabled", ref _xRayEnabled);
            if (_xRayEnabled)
            {
                ImGuiBindings.ImGui_ColorEdit3("X-Ray Color", _xRayColor, 0);
                ImGuiBindings.ImGui_SliderFloat("X-Ray Alpha", ref _xRayAlpha, 0.1f, 1.0f);
            }
            ImGuiBindings.ImGui_Separator();

            ImGuiBindings.ImGui_Text("Camera:");
            ImGuiBindings.ImGui_Text($"  Dist={_orbitDist:F1} Yaw={MathHelper.ToDegrees(_orbitYaw):F0}°  Pitch={MathHelper.ToDegrees(_orbitPitch):F0}°");
            ImGuiBindings.ImGui_Text("  Press R to reset view");

            ImGuiBindings.EndPanel();
        }

        // ── Cleanup ───────────────────────────────────────────────────

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _renderer?.Dispose();
                _sphereVB?.Dispose();
                _cubeVB?.Dispose();
                _cylinderVB?.Dispose();
                _coneVB?.Dispose();
                _pyramidVB?.Dispose();
                _groundVB?.Dispose();
            }
            base.Dispose(disposing);
        }

        static void Main(string[] args)
        {
            TestHarness.ParseArgs(args);
            using var g = new JFAOutlineGame();
            g.Run();
        }
    }
}
