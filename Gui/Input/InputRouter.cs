using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace FNA.Gui
{
    /// <summary>
    /// Processes raw input into routed GUI events.
    /// Manages pointer state (hovered widget, pressed widget) and
    /// generates Enter/Leave/Down/Up/Click/Drag/Scroll events.
    /// </summary>
    public class InputRouter
    {
        // Pointer tracking
        private Widget? _hovered;
        private Widget? _pressed;
        private Vector2 _pressPosition;
        private Vector2 _lastPosition;
        private bool _isDragging;

        // Focus
        private Widget? _focused;

        /// <summary>The widget currently under the pointer (deepest hit).</summary>
        public Widget? HoveredWidget => _hovered;

        /// <summary>The widget currently capturing pointer input (pressed).</summary>
        public Widget? PressedWidget => _pressed;

        /// <summary>The currently focused widget.</summary>
        public Widget? FocusedWidget
        {
            get => _focused;
            set
            {
                if (_focused == value) return;

                if (_focused != null)
                {
                    _focused.SetFocusedInternal(false);
                    var evt = new GuiEvent(GuiEventType.FocusLost, Vector2.Zero);
                    Route(_focused, evt);
                }

                _focused = value;

                if (_focused != null)
                {
                    _focused.SetFocusedInternal(true);
                    var evt = new GuiEvent(GuiEventType.FocusGained, Vector2.Zero);
                    Route(_focused, evt);
                }
            }
        }

        /// <summary>Whether the GUI currently wants mouse capture.</summary>
        public bool WantsMouse { get; private set; }

        /// <summary>Whether the GUI currently wants keyboard capture.</summary>
        public bool WantsKeyboard { get; private set; }

        // ── Pointer Input ────────────────────────────────────────────

        /// <summary>
        /// Process pointer movement. Generates Enter/Leave/Drag events.
        /// Call every frame with the current pointer position.
        /// </summary>
        public void MovePointer(Widget root, Vector2 position)
        {
            _lastPosition = position;

            var hit = root.HitTestTree(position);

            // Handle Enter/Leave
            if (hit != _hovered)
            {
                if (_hovered != null)
                {
                    _hovered.SetHoveredInternal(false);
                    var leave = new GuiEvent(GuiEventType.PointerLeave, position);
                    Route(_hovered, leave);
                }

                _hovered = hit;

                if (_hovered != null)
                {
                    _hovered.SetHoveredInternal(true);
                    var enter = new GuiEvent(GuiEventType.PointerEnter, position);
                    Route(_hovered, enter);
                }
            }

            // Handle drag
            if (_pressed != null && !_isDragging)
            {
                float dist = Vector2.Distance(position, _pressPosition);
                if (dist > 3f)
                {
                    _isDragging = true;
                    var drag = new GuiEvent(GuiEventType.Drag, position);
                    Route(_pressed, drag);
                }
            }
            else if (_pressed != null && _isDragging)
            {
                var drag = new GuiEvent(GuiEventType.Drag, position);
                Route(_pressed, drag);
            }

            UpdateCaptureFlags();
        }

        /// <summary>
        /// Process pointer press (button down).
        /// </summary>
        public void PressPointer(Widget root, Vector2 position,
            MouseButton button = MouseButton.Left)
        {
            var hit = root.HitTestTree(position);
            _pressed = hit;
            _pressPosition = position;
            _isDragging = false;

            if (hit != null)
            {
                FocusedWidget = hit;

                hit.SetPressedInternal(true);
                var down = new GuiEvent(GuiEventType.PointerDown, position, button);
                Route(hit, down);
            }
            else
            {
                // Click outside GUI → lose focus
                FocusedWidget = null;
            }

            UpdateCaptureFlags();
        }

        /// <summary>
        /// Process pointer release (button up). Generates Up and potentially Click.
        /// </summary>
        public void ReleasePointer(Widget root, Vector2 position,
            MouseButton button = MouseButton.Left)
        {
            if (_pressed != null)
            {
                _pressed.SetPressedInternal(false);
                var up = new GuiEvent(GuiEventType.PointerUp, position, button);
                Route(_pressed, up);

                // Click: released on same widget that was pressed
                var hit = root.HitTestTree(position);
                if (hit == _pressed && !_isDragging)
                {
                    var click = new GuiEvent(GuiEventType.Click, position, button);
                    Route(_pressed, click);
                }

                _pressed = null;
                _isDragging = false;
            }

            UpdateCaptureFlags();
        }

        // ── Text Input ───────────────────────────────────────────────

        /// <summary>
        /// Process a text input event (from SDL TextInput / IME).
        /// Routes to the focused widget.
        /// </summary>
        public void TextInput(string text)
        {
            if (_focused != null && !string.IsNullOrEmpty(text))
            {
                var evt = new GuiEvent(GuiEventType.TextInput, Vector2.Zero, text: text);
                Route(_focused, evt);
            }
        }

        // ── Keyboard Input ───────────────────────────────────────────

        /// <summary>
        /// Process a key press. Routes to focused widget.
        /// </summary>
        public void PressKey(Keys key)
        {
            if (_focused != null)
            {
                var evt = new GuiEvent(GuiEventType.KeyDown, Vector2.Zero, key: key);
                Route(_focused, evt);
            }

            UpdateCaptureFlags();
        }

        /// <summary>
        /// Process a key release. Routes to focused widget.
        /// </summary>
        public void ReleaseKey(Keys key)
        {
            if (_focused != null)
            {
                var evt = new GuiEvent(GuiEventType.KeyUp, Vector2.Zero, key: key);
                Route(_focused, evt);
            }

            UpdateCaptureFlags();
        }

        // ── Scroll ───────────────────────────────────────────────────

        /// <summary>
        /// Process scroll wheel. Routes to hovered widget.
        /// </summary>
        public void Scroll(Widget root, Vector2 position, float delta)
        {
            var hit = root.HitTestTree(position);
            if (hit != null)
            {
                var evt = new GuiEvent(GuiEventType.Scroll, position, scrollDelta: delta);
                Route(hit, evt);
            }
        }

        // ── Tab Navigation ────────────────────────────────────────────

        /// <summary>
        /// Move focus to the next focusable widget in tree order.
        /// Wraps around to the first when reaching the end.
        /// </summary>
        public void FocusNext(Widget root)
        {
            var all = CollectFocusable(root);
            if (all.Length == 0) return;

            int idx = _focused != null ? Array.IndexOf(all, _focused) : -1;
            int next = (idx + 1) % all.Length;
            FocusedWidget = all[next];
        }

        /// <summary>
        /// Move focus to the previous focusable widget.
        /// </summary>
        public void FocusPrev(Widget root)
        {
            var all = CollectFocusable(root);
            if (all.Length == 0) return;

            int idx = _focused != null ? Array.IndexOf(all, _focused) : -1;
            int prev = idx <= 0 ? all.Length - 1 : idx - 1;
            FocusedWidget = all[prev];
        }

        // ── Directional (Gamepad) Navigation ─────────────────────────

        /// <summary>
        /// Navigate focus in a direction (Up/Down/Left/Right).
        /// Uses geometric nearest-neighbor: finds the closest focusable widget
        /// in the specified direction from the currently focused widget.
        /// If nothing is focused, focuses the first focusable widget.
        /// </summary>
        public void NavigateDirection(Widget root, Direction direction)
        {
            var all = CollectFocusable(root);
            if (all.Length == 0) return;

            // If nothing focused, pick first
            if (_focused == null)
            {
                FocusedWidget = all[0];
                return;
            }

            var currentCenter = CenterOf(_focused.Bounds);
            Widget? best = null;
            float bestScore = float.MaxValue;

            foreach (var candidate in all)
            {
                if (candidate == _focused) continue;

                var candidateCenter = CenterOf(candidate.Bounds);
                float dx = candidateCenter.X - currentCenter.X;
                float dy = candidateCenter.Y - currentCenter.Y;

                // Must be in the desired direction
                bool inDirection = direction switch
                {
                    Direction.Left => dx < -1,
                    Direction.Right => dx > 1,
                    Direction.Up => dy < -1,
                    Direction.Down => dy > 1,
                    _ => true,
                };
                if (!inDirection) continue;

                // Score: perpendicular distance (heavily weighted) + main-axis distance
                float perpDist, mainDist;
                if (direction is Direction.Left or Direction.Right)
                {
                    mainDist = Math.Abs(dx);
                    perpDist = Math.Abs(dy);
                }
                else
                {
                    mainDist = Math.Abs(dy);
                    perpDist = Math.Abs(dx);
                }

                // Heuristic: prefer candidates along the same axis, with shorter distance
                float score = mainDist + perpDist * 2.0f;

                // Overlap bonus: if the candidate overlaps on the perpendicular axis
                if (direction is Direction.Left or Direction.Right)
                {
                    bool overlaps = candidate.Bounds.Top < _focused.Bounds.Bottom &&
                                    candidate.Bounds.Bottom > _focused.Bounds.Top;
                    if (overlaps) score -= mainDist * 0.5f;
                }
                else
                {
                    bool overlaps = candidate.Bounds.Left < _focused.Bounds.Right &&
                                    candidate.Bounds.Right > _focused.Bounds.Left;
                    if (overlaps) score -= mainDist * 0.5f;
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            if (best != null)
                FocusedWidget = best;
        }

        /// <summary>
        /// Activate the currently focused widget (simulate Enter key press).
        /// Used for gamepad "confirm" (A button).
        /// </summary>
        public void ActivateFocused()
        {
            if (_focused != null)
            {
                PressKey(Keys.Enter);
            }
        }

        /// <summary>
        /// Send a "back" action. Currently routes Escape to the focused widget.
        /// </summary>
        public void BackFocused()
        {
            if (_focused != null)
            {
                PressKey(Keys.Escape);
            }
        }

        private static Vector2 CenterOf(Rectangle r)
        {
            return new Vector2(r.X + r.Width * 0.5f, r.Y + r.Height * 0.5f);
        }

        private static Widget[] CollectFocusable(Widget root)
        {
            var list = new System.Collections.Generic.List<Widget>();
            CollectFocusableRecursive(root, list);
            return list.ToArray();
        }

        private static void CollectFocusableRecursive(Widget w,
            System.Collections.Generic.List<Widget> list)
        {
            if (w.Visibility != Visibility.Visible)
                return;

            if (w.IsFocusable && w.Enabled)
                list.Add(w);

            foreach (var child in w.Children)
                CollectFocusableRecursive(child, list);
        }

        // ── Event Routing ─────────────────────────────────────────────

        /// <summary>
        /// Route an event through the widget tree: bubble from target to root.
        /// Sets e.Handled on any widget's OnEvent override to stop propagation.
        /// </summary>
        public static void Route(Widget target, GuiEvent evt)
        {
            evt.Handled = false;
            evt.Target = target;

            // Bubble: target → root
            var current = target;
            while (current != null && !evt.Handled)
            {
                current.OnEvent(evt);
                current = current.Parent;
            }
        }

        // ── Capture Flags ─────────────────────────────────────────────

        private void UpdateCaptureFlags()
        {
            WantsMouse = _hovered != null;
            WantsKeyboard = _focused != null;
        }

        /// <summary>Reset all pointer/focus state (e.g., on screen change).</summary>
        public void Reset()
        {
            _hovered = null;
            _pressed = null;
            _focused = null;
            _isDragging = false;
            WantsMouse = false;
            WantsKeyboard = false;
        }
    }
}
