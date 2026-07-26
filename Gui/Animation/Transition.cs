using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace FNA.Gui
{
    /// <summary>
    /// Describes an animated transition triggered by widget state changes.
    /// When the widget enters the specified state, the target property is
    /// animated from its current value to <see cref="ToValue"/>.
    /// When leaving, it animates back to <see cref="FromValue"/>.
    ///
    /// Example:
    /// <code>
    /// widget.AddTransition(new TransitionFloat(WidgetState.Hover, "scale", 1.0f, 1.05f, 0.1f)
    ///     { Easing = EasingType.QuadOut });
    /// </code>
    /// </summary>
    public abstract class Transition
    {
        /// <summary>The widget state that triggers this transition.</summary>
        public WidgetState State { get; }

        /// <summary>Duration of the enter transition (seconds).</summary>
        public float EnterDuration { get; set; } = 0.15f;

        /// <summary>Duration of the leave transition (seconds).</summary>
        public float LeaveDuration { get; set; } = 0.15f;

        /// <summary>Easing for the enter transition.</summary>
        public EasingType EnterEasing { get; set; } = EasingType.QuadOut;

        /// <summary>Easing for the leave transition.</summary>
        public EasingType LeaveEasing { get; set; } = EasingType.QuadIn;

        /// <summary>Whether the enter transition is currently playing.</summary>
        public bool IsEntering { get; private set; }

        /// <summary>Whether the leave transition is currently playing.</summary>
        public bool IsLeaving { get; private set; }

        /// <summary>The currently active tween, if any.</summary>
        public Tween? ActiveTween { get; private set; }

        /// <summary>The owning widget (set when transition is added).</summary>
        public Widget? Owner { get; internal set; }

        protected Transition(WidgetState state)
        {
            State = state;
        }

        /// <summary>Called by the widget when entering the target state.</summary>
        public void Enter()
        {
            IsEntering = true;
            IsLeaving = false;
            ActiveTween?.Complete(); // cancel any in-progress leave
            ActiveTween = CreateEnterTween();
            if (ActiveTween != null)
            {
                ActiveTween.Ease = EnterEasing;
                var captured = ActiveTween;
                ActiveTween.OnComplete = () =>
                {
                    IsEntering = false;
                    if (ActiveTween == captured)
                        ActiveTween = null;
                };
            }
        }

        /// <summary>Called by the widget when leaving the target state.</summary>
        public void Leave()
        {
            IsLeaving = true;
            IsEntering = false;
            ActiveTween?.Complete(); // cancel any in-progress enter
            ActiveTween = CreateLeaveTween();
            if (ActiveTween != null)
            {
                ActiveTween.Ease = LeaveEasing;
                var captured = ActiveTween;
                ActiveTween.OnComplete = () =>
                {
                    IsLeaving = false;
                    if (ActiveTween == captured)
                        ActiveTween = null;
                };
            }
        }

        /// <summary>Create the tween for entering the state. Override in subclasses.</summary>
        protected abstract Tween? CreateEnterTween();

        /// <summary>Create the tween for leaving the state. Override in subclasses.</summary>
        protected abstract Tween? CreateLeaveTween();

        /// <summary>Step the active tween. Called by the widget or system.</summary>
        public void Step(float dt)
        {
            ActiveTween?.Step(dt);
            if (ActiveTween?.IsComplete == true)
            {
                ActiveTween = null;
                IsEntering = false;
                IsLeaving = false;
            }
        }
    }

    /// <summary>
    /// A float-valued transition (e.g., scale, opacity, width).
    /// </summary>
    public class TransitionFloat : Transition
    {
        public float FromValue { get; }
        public float ToValue { get; }
        public Action<float>? OnUpdate { get; set; }

        private float _currentValue;

        public TransitionFloat(WidgetState state, float fromValue, float toValue,
            float enterDuration = 0.15f, float leaveDuration = 0.15f) : base(state)
        {
            FromValue = fromValue;
            ToValue = toValue;
            _currentValue = fromValue;
            EnterDuration = enterDuration;
            LeaveDuration = leaveDuration;
        }

        public float CurrentValue => _currentValue;

        protected override Tween? CreateEnterTween()
        {
            return TweenFloat.Animate(_currentValue, ToValue, EnterDuration, v =>
            {
                _currentValue = v;
                OnUpdate?.Invoke(v);
            });
        }

        protected override Tween? CreateLeaveTween()
        {
            return TweenFloat.Animate(_currentValue, FromValue, LeaveDuration, v =>
            {
                _currentValue = v;
                OnUpdate?.Invoke(v);
            });
        }
    }

    /// <summary>
    /// A Color-valued transition (e.g., background color fade).
    /// </summary>
    public class TransitionColor : Transition
    {
        public Color FromColor { get; }
        public Color ToColor { get; }
        public Action<Color>? OnUpdate { get; set; }

        private Color _currentColor;

        public TransitionColor(WidgetState state, Color fromColor, Color toColor,
            float enterDuration = 0.15f, float leaveDuration = 0.15f) : base(state)
        {
            FromColor = fromColor;
            ToColor = toColor;
            _currentColor = fromColor;
            EnterDuration = enterDuration;
            LeaveDuration = leaveDuration;
        }

        public Color CurrentColor => _currentColor;

        protected override Tween? CreateEnterTween()
        {
            return TweenColor.Animate(_currentColor, ToColor, EnterDuration, c =>
            {
                _currentColor = c;
                OnUpdate?.Invoke(c);
            });
        }

        protected override Tween? CreateLeaveTween()
        {
            return TweenColor.Animate(_currentColor, FromColor, LeaveDuration, c =>
            {
                _currentColor = c;
                OnUpdate?.Invoke(c);
            });
        }
    }
}
