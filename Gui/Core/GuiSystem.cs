using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNA.Gui
{
    /// <summary>
    /// Top-level entry point for the GUI system.
    /// Owns the root widget, renderer, input router, and per-frame orchestration.
    /// </summary>
    public class GuiSystem : IDisposable
    {
        private readonly IGuiRenderer _renderer;
        private readonly Widget _rootWidget;
        private readonly InputRouter _inputRouter = new();
        private readonly TweenSystem _tweens = new();

        /// <summary>The root widget that contains all GUI elements.</summary>
        public Widget Root => _rootWidget;

        /// <summary>The renderer used by this GUI system.</summary>
        public IGuiRenderer Renderer => _renderer;

        /// <summary>The input router (event dispatch, hit testing, focus).</summary>
        public InputRouter Input => _inputRouter;

        /// <summary>The tween system for animations.</summary>
        public TweenSystem Tweens => _tweens;

        /// <summary>
        /// The global theme for this GUI system. Setting this propagates
        /// the theme to the root widget and all children.
        /// </summary>
        public Theme? Theme
        {
            get => _rootWidget.Theme;
            set => _rootWidget.Theme = value;
        }

        /// <summary>Screen/viewport size in logical pixels.</summary>
        public Vector2 ScreenSize { get; set; }

        /// <summary>The transform matrix from logical to physical pixels.</summary>
        public Matrix Transform { get; set; } = Matrix.Identity;

        /// <summary>Whether the GUI wants to capture mouse input (for ImGui negotiation).</summary>
        public bool WantsMouse => _inputRouter.WantsMouse;

        /// <summary>Whether the GUI wants to capture keyboard input (for ImGui negotiation).</summary>
        public bool WantsKeyboard => _inputRouter.WantsKeyboard;

        public GuiSystem(GraphicsDevice device, Widget rootWidget)
            : this(new SpriteBatchGuiRenderer(device), rootWidget)
        {
        }

        /// <summary>Create a GuiSystem with a custom renderer (for testing).</summary>
        public GuiSystem(IGuiRenderer renderer, Widget rootWidget)
        {
            _renderer = renderer;
            _rootWidget = rootWidget;
        }

        /// <summary>
        /// Per-frame update: input collection, event routing, binding sync,
        /// animation stepping, and layout convergence — in that order.
        /// </summary>
        public void Update(GameTime gameTime)
        {
            // Step animations (Phase 5)
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (dt > 0 && dt < 1.0f) // sanity clamp
            {
                _tweens.Update(dt);
            }

            // Layout convergence: if dirty, run measure + arrange pass
            _rootWidget.Layout(ScreenSize);

            // TODO (Phase 4+): binding sync
        }

        /// <summary>
        /// Process real FNA input state. Call in Game.Update before Update().
        /// </summary>
        public void ProcessInput(
            Microsoft.Xna.Framework.Input.MouseState mouse,
            Microsoft.Xna.Framework.Input.KeyboardState keyboard,
            Microsoft.Xna.Framework.Input.MouseState prevMouse,
            Microsoft.Xna.Framework.Input.KeyboardState prevKeyboard)
        {
            var mousePos = new Vector2(mouse.X, mouse.Y);

            // Pointer move
            if (mouse.X != prevMouse.X || mouse.Y != prevMouse.Y)
            {
                _inputRouter.MovePointer(_rootWidget, mousePos);
            }

            // Mouse buttons
            if (mouse.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed &&
                prevMouse.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Released)
            {
                _inputRouter.PressPointer(_rootWidget, mousePos, MouseButton.Left);
            }
            if (mouse.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Released &&
                prevMouse.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed)
            {
                _inputRouter.ReleasePointer(_rootWidget, mousePos, MouseButton.Left);
            }

            // Scroll (wheel value difference)
            int scrollDelta = mouse.ScrollWheelValue - prevMouse.ScrollWheelValue;
            if (scrollDelta != 0)
            {
                _inputRouter.Scroll(_rootWidget, mousePos, scrollDelta / 120f);
            }

            // Keyboard — Tab for focus navigation
            var pressedKeys = keyboard.GetPressedKeys();
            var prevPressedKeys = prevKeyboard.GetPressedKeys();
            foreach (var key in pressedKeys)
            {
                bool wasDown = false;
                foreach (var pk in prevPressedKeys)
                    if (pk == key) { wasDown = true; break; }
                if (!wasDown)
                {
                    if (key == Microsoft.Xna.Framework.Input.Keys.Tab)
                    {
                        _inputRouter.FocusNext(_rootWidget);
                    }
                    else
                    {
                        _inputRouter.PressKey(key);
                    }
                }
            }
        }

        /// <summary>
        /// Inject a pointer move for headless testing.
        /// </summary>
        public void InjectPointerMove(Vector2 position) =>
            _inputRouter.MovePointer(_rootWidget, position);

        /// <summary>
        /// Inject a pointer press for headless testing.
        /// </summary>
        public void InjectPointerDown(Vector2 position, MouseButton button = MouseButton.Left) =>
            _inputRouter.PressPointer(_rootWidget, position, button);

        /// <summary>
        /// Inject a pointer release for headless testing.
        /// </summary>
        public void InjectPointerUp(Vector2 position, MouseButton button = MouseButton.Left) =>
            _inputRouter.ReleasePointer(_rootWidget, position, button);

        /// <summary>
        /// Process gamepad state for directional navigation and confirm/back.
        /// Call in Game.Update after ProcessInput.
        /// </summary>
        public void ProcessGamePad(
            Microsoft.Xna.Framework.Input.GamePadState gamepad,
            Microsoft.Xna.Framework.Input.GamePadState prevGamepad)
        {
            // D-pad and left thumbstick navigation
            float threshold = 0.5f;

            // D-pad (digital)
            if (gamepad.DPad.Up == Microsoft.Xna.Framework.Input.ButtonState.Pressed &&
                prevGamepad.DPad.Up == Microsoft.Xna.Framework.Input.ButtonState.Released)
            {
                _inputRouter.NavigateDirection(_rootWidget, Direction.Up);
            }
            if (gamepad.DPad.Down == Microsoft.Xna.Framework.Input.ButtonState.Pressed &&
                prevGamepad.DPad.Down == Microsoft.Xna.Framework.Input.ButtonState.Released)
            {
                _inputRouter.NavigateDirection(_rootWidget, Direction.Down);
            }
            if (gamepad.DPad.Left == Microsoft.Xna.Framework.Input.ButtonState.Pressed &&
                prevGamepad.DPad.Left == Microsoft.Xna.Framework.Input.ButtonState.Released)
            {
                _inputRouter.NavigateDirection(_rootWidget, Direction.Left);
            }
            if (gamepad.DPad.Right == Microsoft.Xna.Framework.Input.ButtonState.Pressed &&
                prevGamepad.DPad.Right == Microsoft.Xna.Framework.Input.ButtonState.Released)
            {
                _inputRouter.NavigateDirection(_rootWidget, Direction.Right);
            }

            // Left thumbstick (analog) — only trigger on edge crossing
            bool prevLeft = prevGamepad.ThumbSticks.Left.X < -threshold;
            bool prevRight = prevGamepad.ThumbSticks.Left.X > threshold;
            bool prevUp = prevGamepad.ThumbSticks.Left.Y > threshold;
            bool prevDown = prevGamepad.ThumbSticks.Left.Y < -threshold;

            bool curLeft = gamepad.ThumbSticks.Left.X < -threshold;
            bool curRight = gamepad.ThumbSticks.Left.X > threshold;
            bool curUp = gamepad.ThumbSticks.Left.Y > threshold;
            bool curDown = gamepad.ThumbSticks.Left.Y < -threshold;

            if (curUp && !prevUp) _inputRouter.NavigateDirection(_rootWidget, Direction.Up);
            if (curDown && !prevDown) _inputRouter.NavigateDirection(_rootWidget, Direction.Down);
            if (curLeft && !prevLeft) _inputRouter.NavigateDirection(_rootWidget, Direction.Left);
            if (curRight && !prevRight) _inputRouter.NavigateDirection(_rootWidget, Direction.Right);

            // A button → confirm (Enter)
            if (gamepad.Buttons.A == Microsoft.Xna.Framework.Input.ButtonState.Pressed &&
                prevGamepad.Buttons.A == Microsoft.Xna.Framework.Input.ButtonState.Released)
            {
                _inputRouter.ActivateFocused();
            }

            // B button → back (Escape)
            if (gamepad.Buttons.B == Microsoft.Xna.Framework.Input.ButtonState.Pressed &&
                prevGamepad.Buttons.B == Microsoft.Xna.Framework.Input.ButtonState.Released)
            {
                _inputRouter.BackFocused();
            }
        }

        /// <summary>
        /// Inject directional navigation for headless testing.
        /// </summary>
        public void InjectNavigate(Direction direction) =>
            _inputRouter.NavigateDirection(_rootWidget, direction);

        /// <summary>
        /// Inject text input for headless testing.
        /// </summary>
        public void InjectTextInput(string text) =>
            _inputRouter.TextInput(text);

        /// <summary>
        /// Inject a key press for headless testing.
        /// </summary>
        public void InjectKeyPress(Microsoft.Xna.Framework.Input.Keys key)
        {
            if (key == Microsoft.Xna.Framework.Input.Keys.Tab)
                _inputRouter.FocusNext(_rootWidget);
            else
                _inputRouter.PressKey(key);
        }

        /// <summary>
        /// Per-frame draw: render the entire widget tree.
        /// </summary>
        public void Draw()
        {
            _renderer.Begin(Transform);
            _rootWidget.Draw(_renderer);
            _renderer.End();
        }

        /// <summary>
        /// Create a GuiSystem with a default Panel as root.
        /// </summary>
        public static GuiSystem CreateDefault(GraphicsDevice device)
        {
            var root = new Panel();
            return new GuiSystem(device, root);
        }

        public void Dispose()
        {
            (_renderer as IDisposable)?.Dispose();
        }
    }
}
