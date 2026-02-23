using System;

namespace NvgNET.Text
{
    public struct GlyphPosition
    {

        /// <summary>
        /// Position of, and the rest of, the glyph in the input string.
        /// </summary>
        public ReadOnlyMemory<Char> Str { get; }

        /// <summary>
        /// The X-coordinate of the logical glyph position.
        /// </summary>
        public float X { get; }

        /// <summary>
        /// The smallest X-bound of the glyph shape.
        /// </summary>
        public float MinX { get; }

        /// <summary>
        /// The largest X-bound of the glyph shape.
        /// </summary>
        public float MaxX { get; }

        internal GlyphPosition(ReadOnlyMemory<Char> str, float x, float minX, float maxX)
        {
            Str = str;
            X = x;
            MinX = minX;
            MaxX = maxX;
        }

    }
}
