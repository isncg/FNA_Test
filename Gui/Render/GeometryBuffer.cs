using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace FNA.Gui
{
    /// <summary>
    /// Poolable list of <see cref="GraphicQuad"/> used by Graphic-derived
    /// widgets to emit their geometry. Rebuilt when the widget's content
    /// changes and cached between frames.
    /// </summary>
    public class GeometryBuffer
    {
        private readonly List<GraphicQuad> _quads = new();

        /// <summary>Number of quads in this buffer.</summary>
        public int Count => _quads.Count;

        /// <summary>Get the quad at the given index.</summary>
        public GraphicQuad this[int index] => _quads[index];

        /// <summary>Add a quad and return its index.</summary>
        public int Add(GraphicQuad quad)
        {
            int idx = _quads.Count;
            _quads.Add(quad);
            return idx;
        }

        /// <summary>Add a textured quad with full UV range.</summary>
        public void AddQuad(Vector2 pos, Vector2 size,
            Vector2 uv0, Vector2 uv1, Color color,
            Microsoft.Xna.Framework.Graphics.Texture2D? texture)
        {
            _quads.Add(new GraphicQuad(pos, size, uv0, uv1, color, texture));
        }

        /// <summary>Clear all quads for reuse.</summary>
        public void Clear() => _quads.Clear();

        /// <summary>Copy quads to a span for batch rendering.</summary>
        public ReadOnlySpan<GraphicQuad> AsSpan() =>
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_quads);

        /// <summary>Return this buffer to the pool.</summary>
        public void Return()
        {
            Clear();
            Pool.Return(this);
        }

        // ── Pool ──────────────────────────────────────────────────────

        private static class Pool
        {
            private static readonly Stack<GeometryBuffer> _pool = new();

            public static GeometryBuffer Rent()
            {
                if (_pool.TryPop(out var buf))
                    return buf;
                return new GeometryBuffer();
            }

            public static void Return(GeometryBuffer buf) => _pool.Push(buf);
        }

        /// <summary>Rent a buffer from the pool.</summary>
        public static GeometryBuffer Rent() => Pool.Rent();
    }
}
