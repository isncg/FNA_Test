using System;

namespace FNA.Gui
{
    /// <summary>
    /// Standard easing functions for tween interpolation.
    /// Each takes normalized time t ∈ [0,1] and returns eased progress ∈ [0,1].
    /// </summary>
    public enum EasingType
    {
        Linear,
        QuadIn, QuadOut, QuadInOut,
        CubicIn, CubicOut, CubicInOut,
        QuartIn, QuartOut, QuartInOut,
        QuintIn, QuintOut, QuintInOut,
        SineIn, SineOut, SineInOut,
        ExpoIn, ExpoOut, ExpoInOut,
        CircIn, CircOut, CircInOut,
        BackIn, BackOut, BackInOut,
        ElasticIn, ElasticOut, ElasticInOut,
        BounceIn, BounceOut, BounceInOut,
    }

    /// <summary>
    /// Static easing function library.
    /// All functions clamp t to [0,1] and return eased value ∈ [0,1].
    /// </summary>
    public static class Easing
    {
        public static float Apply(EasingType type, float t)
        {
            t = Math.Clamp(t, 0f, 1f);

            switch (type)
            {
                case EasingType.Linear: return t;

                // Quad
                case EasingType.QuadIn: return t * t;
                case EasingType.QuadOut: return 1 - (1 - t) * (1 - t);
                case EasingType.QuadInOut: return t < 0.5f ? 2 * t * t : 1 - MathF.Pow(-2 * t + 2, 2) / 2;

                // Cubic
                case EasingType.CubicIn: return t * t * t;
                case EasingType.CubicOut: return 1 - MathF.Pow(1 - t, 3);
                case EasingType.CubicInOut: return t < 0.5f ? 4 * t * t * t : 1 - MathF.Pow(-2 * t + 2, 3) / 2;

                // Quart
                case EasingType.QuartIn: return t * t * t * t;
                case EasingType.QuartOut: return 1 - MathF.Pow(1 - t, 4);
                case EasingType.QuartInOut: return t < 0.5f ? 8 * t * t * t * t : 1 - MathF.Pow(-2 * t + 2, 4) / 2;

                // Quint
                case EasingType.QuintIn: return t * t * t * t * t;
                case EasingType.QuintOut: return 1 - MathF.Pow(1 - t, 5);
                case EasingType.QuintInOut: return t < 0.5f ? 16 * t * t * t * t * t : 1 - MathF.Pow(-2 * t + 2, 5) / 2;

                // Sine
                case EasingType.SineIn: return 1 - MathF.Cos(t * MathF.PI / 2);
                case EasingType.SineOut: return MathF.Sin(t * MathF.PI / 2);
                case EasingType.SineInOut: return -(MathF.Cos(MathF.PI * t) - 1) / 2;

                // Expo
                case EasingType.ExpoIn: return t == 0 ? 0 : MathF.Pow(2, 10 * t - 10);
                case EasingType.ExpoOut: return t == 1 ? 1 : 1 - MathF.Pow(2, -10 * t);
                case EasingType.ExpoInOut:
                    if (t == 0) return 0;
                    if (t == 1) return 1;
                    return t < 0.5f
                        ? MathF.Pow(2, 20 * t - 10) / 2
                        : (2 - MathF.Pow(2, -20 * t + 10)) / 2;

                // Circ
                case EasingType.CircIn: return 1 - MathF.Sqrt(1 - t * t);
                case EasingType.CircOut: return MathF.Sqrt(1 - (t - 1) * (t - 1));
                case EasingType.CircInOut:
                    return t < 0.5f
                        ? (1 - MathF.Sqrt(1 - 4 * t * t)) / 2
                        : (MathF.Sqrt(1 - (-2 * t + 2) * (-2 * t + 2)) + 1) / 2;

                // Back (overshoot)
                case EasingType.BackIn:
                    { const float c1 = 1.70158f; const float c3 = c1 + 1; return c3 * t * t * t - c1 * t * t; }
                case EasingType.BackOut:
                    { const float c1 = 1.70158f; const float c3 = c1 + 1; return 1 + c3 * MathF.Pow(t - 1, 3) + c1 * MathF.Pow(t - 1, 2); }
                case EasingType.BackInOut:
                    {
                        const float c1 = 1.70158f;
                        const float c2 = c1 * 1.525f;
                        return t < 0.5f
                            ? MathF.Pow(2 * t, 2) * ((c2 + 1) * 2 * t - c2) / 2
                            : (MathF.Pow(2 * t - 2, 2) * ((c2 + 1) * (t * 2 - 2) + c2) + 2) / 2;
                    }

                // Elastic
                case EasingType.ElasticIn:
                    if (t == 0) return 0;
                    if (t == 1) return 1;
                    return -MathF.Pow(2, 10 * t - 10) * MathF.Sin((t * 10 - 10.75f) * (2 * MathF.PI) / 3);
                case EasingType.ElasticOut:
                    if (t == 0) return 0;
                    if (t == 1) return 1;
                    return MathF.Pow(2, -10 * t) * MathF.Sin((t * 10 - 0.75f) * (2 * MathF.PI) / 3) + 1;
                case EasingType.ElasticInOut:
                    if (t == 0) return 0;
                    if (t == 1) return 1;
                    return t < 0.5f
                        ? -(MathF.Pow(2, 20 * t - 10) * MathF.Sin((20 * t - 11.125f) * (2 * MathF.PI) / 4.5f)) / 2
                        : MathF.Pow(2, -20 * t + 10) * MathF.Sin((20 * t - 11.125f) * (2 * MathF.PI) / 4.5f) / 2 + 1;

                // Bounce
                case EasingType.BounceIn: return 1 - BounceOut(1 - t);
                case EasingType.BounceOut: return BounceOut(t);
                case EasingType.BounceInOut:
                    return t < 0.5f
                        ? (1 - BounceOut(1 - 2 * t)) / 2
                        : (1 + BounceOut(2 * t - 1)) / 2;

                default: return t;
            }
        }

        private static float BounceOut(float t)
        {
            const float n1 = 7.5625f;
            const float d1 = 2.75f;
            if (t < 1 / d1)
                return n1 * t * t;
            else if (t < 2 / d1)
                return n1 * (t -= 1.5f / d1) * t + 0.75f;
            else if (t < 2.5 / d1)
                return n1 * (t -= 2.25f / d1) * t + 0.9375f;
            else
                return n1 * (t -= 2.625f / d1) * t + 0.984375f;
        }
    }
}
