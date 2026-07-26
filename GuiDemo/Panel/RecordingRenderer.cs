using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNA.Gui
{
    /// <summary>Recorded draw call type for assertion.</summary>
    public enum DrawCallType
    {
        Begin,
        End,
        PushClip,
        PopClip,
        DrawRect,
        DrawTexture,
        DrawNineSlice,
        DrawSdfText,
        DrawGeometry,
    }

    /// <summary>A single recorded draw call for test assertion.</summary>
    public struct RecordedCall
    {
        public DrawCallType Type;
        public Rectangle Rect;
        public Color Color;
        public int GeometryQuadCount;
        public object? Tag; // For additional context (e.g., "nine-slice", "texture-quad")

        public override string ToString() =>
            $"{Type} rect=({Rect.X},{Rect.Y},{Rect.Width}x{Rect.Height}) color={Color} quads={GeometryQuadCount}";
    }

    /// <summary>
    /// IGuiRenderer implementation that records all calls for headless testing.
    /// Does not actually render anything — purely for assertion.
    /// </summary>
    public class RecordingRenderer : IGuiRenderer
    {
        private readonly List<RecordedCall> _calls = new();
        private int _beginCount;
        private int _endCount;

        public IReadOnlyList<RecordedCall> Calls => _calls;
        public Rectangle CurrentClip { get; private set; }

        public void Begin(Matrix transform)
        {
            _beginCount++;
            _calls.Add(new RecordedCall { Type = DrawCallType.Begin });
        }

        public void End()
        {
            _endCount++;
            _calls.Add(new RecordedCall { Type = DrawCallType.End });
        }

        public void PushClip(Rectangle rect)
        {
            CurrentClip = rect;
            _calls.Add(new RecordedCall { Type = DrawCallType.PushClip, Rect = rect });
        }

        public void PopClip()
        {
            _calls.Add(new RecordedCall { Type = DrawCallType.PopClip });
        }

        public void DrawRect(Rectangle rect, Color color)
        {
            _calls.Add(new RecordedCall
            {
                Type = DrawCallType.DrawRect,
                Rect = rect,
                Color = color,
            });
        }

        public void DrawTexture(Texture2D texture, Rectangle destination,
            Rectangle? source, Color tint)
        {
            _calls.Add(new RecordedCall
            {
                Type = DrawCallType.DrawTexture,
                Rect = destination,
                Color = tint,
                Tag = source,
            });
        }

        public void DrawNineSlice(NineSlice slice, Rectangle destination, Color tint)
        {
            _calls.Add(new RecordedCall
            {
                Type = DrawCallType.DrawNineSlice,
                Rect = destination,
                Color = tint,
                Tag = "nine-slice",
            });
        }

        public void DrawSdfText(SdfFont font, string text,
            Vector2 position, Color color, float scale)
        {
            _calls.Add(new RecordedCall
            {
                Type = DrawCallType.DrawSdfText,
                Rect = new Rectangle((int)position.X, (int)position.Y,
                    (int)(font.MeasureString(text, scale).X),
                    (int)(font.MeasureString(text, scale).Y)),
                Color = color,
                Tag = text,
            });
        }

        public void DrawGeometry(GeometryBuffer geometry, Color tint)
        {
            _calls.Add(new RecordedCall
            {
                Type = DrawCallType.DrawGeometry,
                Color = tint,
                GeometryQuadCount = geometry.Count,
            });
        }

        /// <summary>Clear all recorded calls for reuse.</summary>
        public void Reset()
        {
            _calls.Clear();
            _beginCount = 0;
            _endCount = 0;
        }

        /// <summary>Verify Begin/End are balanced.</summary>
        public bool IsBalanced => _beginCount == _endCount && _beginCount > 0;

        /// <summary>Find calls of a specific type.</summary>
        public List<RecordedCall> FindCalls(DrawCallType type)
        {
            var result = new List<RecordedCall>();
            foreach (var c in _calls)
                if (c.Type == type)
                    result.Add(c);
            return result;
        }

        /// <summary>Count calls of a specific type.</summary>
        public int CountCalls(DrawCallType type)
        {
            int count = 0;
            foreach (var c in _calls)
                if (c.Type == type)
                    count++;
            return count;
        }
    }
}
