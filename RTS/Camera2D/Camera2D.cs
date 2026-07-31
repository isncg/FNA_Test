using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace FNA.RTS
{
    /// <summary>
    /// 2D camera for isometric RTS. Provides ViewMatrix for SpriteBatch
    /// transformMatrix and ScreenToWorld for mouse picking.
    /// </summary>
    public class Camera2D
    {
        // --- Configuration ---
        public float PanSpeed { get; set; } = 600f;          // pixels/sec at zoom 1.0
        public float EdgeScrollThreshold { get; set; } = 20f; // px from window edge
        public float EdgeScrollSpeed { get; set; } = 400f;
        public float ZoomSpeed { get; set; } = 0.1f;          // multiplier per scroll tick
        public float MinZoom { get; set; } = 0.25f;
        public float MaxZoom { get; set; } = 4.0f;

        // --- State ---
        public Vector2 Position { get; set; }
        public float Zoom { get; set; } = 1.0f;

        // --- Matrices (rebuilt each Update) ---
        public Matrix ViewMatrix { get; private set; } = Matrix.Identity;
        public Matrix InverseViewMatrix { get; private set; } = Matrix.Identity;

        // --- Bounds ---
        public Vector2? WorldBoundMin { get; set; }
        public Vector2? WorldBoundMax { get; set; }

        private int _viewportW, _viewportH;
        private int _prevScrollWheel;

        public Camera2D(int viewportWidth, int viewportHeight)
        {
            _viewportW = viewportWidth;
            _viewportH = viewportHeight;
            _prevScrollWheel = Mouse.GetState().ScrollWheelValue;
        }

        public void Resize(int viewportWidth, int viewportHeight)
        {
            _viewportW = viewportWidth;
            _viewportH = viewportHeight;
        }

        /// <summary>Update camera position and zoom from input. Call once per frame.</summary>
        public void Update(float dt, bool keyboardEnabled = true, bool edgeScroll = true,
            bool mouseZoom = true)
        {
            var mouse = Mouse.GetState();
            var kb = Keyboard.GetState();
            Vector2 pan = Vector2.Zero;

            // --- Keyboard pan ---
            if (keyboardEnabled)
            {
                if (kb.IsKeyDown(Keys.W) || kb.IsKeyDown(Keys.Up))
                    pan.Y -= 1;
                if (kb.IsKeyDown(Keys.S) || kb.IsKeyDown(Keys.Down))
                    pan.Y += 1;
                if (kb.IsKeyDown(Keys.A) || kb.IsKeyDown(Keys.Left))
                    pan.X -= 1;
                if (kb.IsKeyDown(Keys.D) || kb.IsKeyDown(Keys.Right))
                    pan.X += 1;
            }

            // --- Edge scroll ---
            if (edgeScroll && mouse.X >= 0 && mouse.X <= _viewportW &&
                mouse.Y >= 0 && mouse.Y <= _viewportH)
            {
                if (mouse.X <= EdgeScrollThreshold) pan.X -= 1;
                if (mouse.X >= _viewportW - EdgeScrollThreshold) pan.X += 1;
                if (mouse.Y <= EdgeScrollThreshold) pan.Y -= 1;
                if (mouse.Y >= _viewportH - EdgeScrollThreshold) pan.Y += 1;
            }

            // Apply keyboard + edge pan (speed adjusted for zoom so pan feels consistent)
            if (pan != Vector2.Zero)
            {
                pan.Normalize();
                Position += pan * PanSpeed / Zoom * dt;
            }

            // --- Mouse wheel zoom ---
            if (mouseZoom)
            {
                int scroll = mouse.ScrollWheelValue;
                int delta = scroll - _prevScrollWheel;
                _prevScrollWheel = scroll;

                if (delta != 0)
                {
                    // Zoom toward mouse cursor: convert the zoom step,
                    // then adjust Position to keep the world point under cursor fixed.
                    float zoomDelta = delta > 0 ? ZoomSpeed : -ZoomSpeed;
                    float oldZoom = Zoom;
                    Zoom = MathHelper.Clamp(Zoom + zoomDelta, MinZoom, MaxZoom);

                    if (Zoom != oldZoom)
                    {
                        // World point under mouse before zoom
                        Vector2 mouseWorldBefore = ScreenToWorld(
                            new Vector2(mouse.X, mouse.Y));
                        // After changing zoom, adjust Position so the same world point
                        // stays under the mouse
                        Vector2 mouseWorldAfter = ScreenToWorld(
                            new Vector2(mouse.X, mouse.Y));
                        Position += mouseWorldBefore - mouseWorldAfter;
                    }
                }
            }

            // --- Clamp to world bounds ---
            ClampPosition();

            // --- Rebuild matrices ---
            BuildMatrices();
        }

        /// <summary>Convert screen pixel coordinates to world coordinates.</summary>
        public Vector2 ScreenToWorld(Vector2 screenPos)
        {
            return Vector2.Transform(screenPos, InverseViewMatrix);
        }

        /// <summary>Convert world coordinates to screen pixel coordinates.</summary>
        public Vector2 WorldToScreen(Vector2 worldPos)
        {
            return Vector2.Transform(worldPos, ViewMatrix);
        }

        private void ClampPosition()
        {
            if (WorldBoundMin.HasValue && WorldBoundMax.HasValue)
            {
                var halfViewW = _viewportW / (2f * Zoom);
                var halfViewH = _viewportH / (2f * Zoom);
                var min = WorldBoundMin.Value;
                var max = WorldBoundMax.Value;

                Position = new Vector2(
                    MathHelper.Clamp(Position.X, min.X + halfViewW, max.X - halfViewW),
                    MathHelper.Clamp(Position.Y, min.Y + halfViewH, max.Y - halfViewH)
                );
            }
        }

        private void BuildMatrices()
        {
            ViewMatrix =
                Matrix.CreateTranslation(-Position.X, -Position.Y, 0) *
                Matrix.CreateScale(Zoom, Zoom, 1f) *
                Matrix.CreateTranslation(_viewportW / 2f, _viewportH / 2f, 0);

            InverseViewMatrix = Matrix.Invert(ViewMatrix);
        }

        /// <summary>Force immediate matrix rebuild (call after manual Position/Zoom changes).</summary>
        public void RebuildMatrices()
        {
            BuildMatrices();
        }
    }
}
