using System;

namespace FontStash.NET
{
    public struct FonsTextIter
    {
        public float x, y, nextx, nexty, scale, spacing;
        public uint codepoint;
        public short isize, iblur;
        public FonsFont font;
        public int prevGlyphIndex;
        public ReadOnlyMemory<Char> str;
        public ReadOnlyMemory<Char> next;
        public ReadOnlyMemory<Char> end;
        public uint utf8state;
        public FonsGlyphBitmap bitmapOption;

    }
}
