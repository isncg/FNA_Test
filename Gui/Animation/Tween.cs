using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace FNA.Gui
{
    /// <summary>
    /// Base class for all tweens. Manages elapsed time, completion, and callbacks.
    /// Concrete subclasses: <see cref="TweenFloat"/>, <see cref="TweenColor"/>.
    /// </summary>
    public abstract class Tween
    {
        /// <summary>Duration in seconds.</summary>
        public float Duration { get; }

        /// <summary>Elapsed time in seconds.</summary>
        public float Elapsed { get; private set; }

        /// <summary>Normalized progress [0, 1] after easing is applied.</summary>
        public float Progress => IsComplete ? 1f : Easing.Apply(Ease, Math.Clamp(Elapsed / Duration, 0f, 1f));

        /// <summary>Raw un-eased progress [0, 1].</summary>
        public float RawProgress => IsComplete ? 1f : Math.Clamp(Elapsed / Duration, 0f, 1f);

        /// <summary>Whether the tween has finished.</summary>
        public bool IsComplete => Elapsed >= Duration;

        /// <summary>Easing function to apply.</summary>
        public EasingType Ease { get; set; } = EasingType.Linear;

        /// <summary>Called when the tween completes (after the final update).</summary>
        public Action? OnComplete { get; set; }

        /// <summary>
        /// If true, the tween loops: on completion it resets and starts again.
        /// </summary>
        public bool Loop { get; set; }

        /// <summary>
        /// If true, the tween ping-pongs: reverses direction on completion.
        /// </summary>
        public bool PingPong { get; set; }
        private bool _pingPongReverse;

        /// <summary>
        /// Number of times to repeat after the first play.
        /// 0 = play once, 1 = play twice, -1 = infinite loop.
        /// </summary>
        public int RepeatCount { get; set; }
        private int _repeatIndex;

        protected Tween(float duration)
        {
            Duration = Math.Max(duration, 0.001f); // prevent division by zero
        }

        /// <summary>Advance the tween by dt seconds. Calls OnUpdate and OnComplete.</summary>
        public void Step(float dt)
        {
            if (IsComplete && !Loop && !PingPong && _repeatIndex >= RepeatCount)
                return;

            Elapsed += dt;

            ApplyUpdate();

            if (Elapsed >= Duration)
            {
                if (Loop)
                {
                    Elapsed -= Duration;
                    _pingPongReverse = false;
                    ApplyUpdate();
                }
                else if (PingPong)
                {
                    Elapsed -= Duration;
                    _pingPongReverse = !_pingPongReverse;
                    ApplyUpdate();
                }
                else if (RepeatCount > 0 && _repeatIndex < RepeatCount)
                {
                    _repeatIndex++;
                    if (_repeatIndex < RepeatCount)
                    {
                        Elapsed -= Duration;
                        ApplyUpdate();
                    }
                    else
                    {
                        Elapsed = Duration; // clamp
                        ApplyUpdate();
                        OnComplete?.Invoke();
                    }
                }
                else if (RepeatCount == -1) // infinite
                {
                    Elapsed -= Duration;
                    ApplyUpdate();
                }
                else
                {
                    Elapsed = Duration; // clamp
                    ApplyUpdate();
                    OnComplete?.Invoke();
                }
            }
        }

        /// <summary>Called each step to apply the interpolated value.</summary>
        protected abstract void ApplyUpdate();

        /// <summary>Reset the tween to its initial state.</summary>
        public void Reset()
        {
            Elapsed = 0;
            _repeatIndex = 0;
            _pingPongReverse = false;
        }

        /// <summary>Force-complete the tween immediately (snaps to end value).</summary>
        public void Complete()
        {
            Elapsed = Duration;
            ApplyUpdate();
            OnComplete?.Invoke();
        }

        /// <summary>Get the effective progress, accounting for ping-pong reversal.</summary>
        protected float EffectiveProgress => _pingPongReverse ? 1f - Progress : Progress;
    }

    /// <summary>
    /// Interpolates a float value from <see cref="From"/> to <see cref="To"/>.
    /// </summary>
    public class TweenFloat : Tween
    {
        public float From { get; }
        public float To { get; }
        public float CurrentValue { get; private set; }

        /// <summary>Called on each step with the new interpolated value.</summary>
        public Action<float>? OnUpdate { get; set; }

        public TweenFloat(float from, float to, float duration) : base(duration)
        {
            From = from;
            To = to;
            CurrentValue = from;
        }

        protected override void ApplyUpdate()
        {
            float t = EffectiveProgress;
            CurrentValue = From + (To - From) * t;
            OnUpdate?.Invoke(CurrentValue);
        }

        /// <summary>Create a tween that animates a float property.</summary>
        public static TweenFloat Animate(float from, float to, float duration,
            Action<float> onUpdate, EasingType easing = EasingType.Linear)
        {
            return new TweenFloat(from, to, duration)
            {
                OnUpdate = onUpdate,
                Ease = easing,
            };
        }
    }

    /// <summary>
    /// Interpolates a <see cref="Color"/> value from <see cref="From"/> to <see cref="To"/>.
    /// Each RGBA channel is interpolated independently.
    /// </summary>
    public class TweenColor : Tween
    {
        public Color From { get; }
        public Color To { get; }
        public Color CurrentValue { get; private set; }

        /// <summary>Called on each step with the new interpolated color.</summary>
        public Action<Color>? OnUpdate { get; set; }

        public TweenColor(Color from, Color to, float duration) : base(duration)
        {
            From = from;
            To = to;
            CurrentValue = from;
        }

        protected override void ApplyUpdate()
        {
            float t = EffectiveProgress;
            CurrentValue = new Color(
                (int)(From.R + (To.R - From.R) * t),
                (int)(From.G + (To.G - From.G) * t),
                (int)(From.B + (To.B - From.B) * t),
                (int)(From.A + (To.A - From.A) * t));
            OnUpdate?.Invoke(CurrentValue);
        }

        /// <summary>Create a tween that animates a Color property.</summary>
        public static TweenColor Animate(Color from, Color to, float duration,
            Action<Color> onUpdate, EasingType easing = EasingType.Linear)
        {
            return new TweenColor(from, to, duration)
            {
                OnUpdate = onUpdate,
                Ease = easing,
            };
        }
    }

    /// <summary>
    /// Manages a collection of active tweens. Owned by <see cref="GuiSystem"/>.
    /// Steps all tweens each frame and removes completed ones.
    /// </summary>
    public class TweenSystem
    {
        private readonly List<Tween> _tweens = new();
        private readonly List<Tween> _pending = new();
        private bool _isUpdating;

        /// <summary>Number of currently active tweens.</summary>
        public int ActiveCount => _tweens.Count;

        /// <summary>Add a tween and start it immediately.</summary>
        public void Add(Tween tween)
        {
            if (_isUpdating)
                _pending.Add(tween);
            else
                _tweens.Add(tween);
        }

        /// <summary>Step all active tweens by dt. Removes completed ones.</summary>
        public void Update(float dt)
        {
            _isUpdating = true;

            for (int i = _tweens.Count - 1; i >= 0; i--)
            {
                var tween = _tweens[i];
                tween.Step(dt);

                if (tween.IsComplete && tween.Loop == false && tween.PingPong == false
                    && tween.RepeatCount == 0)
                {
                    _tweens.RemoveAt(i);
                }
            }

            _isUpdating = false;

            // Flush pending
            if (_pending.Count > 0)
            {
                _tweens.AddRange(_pending);
                _pending.Clear();
            }
        }

        /// <summary>Remove all active tweens without completing them.</summary>
        public void Clear()
        {
            _tweens.Clear();
            _pending.Clear();
        }
    }
}
