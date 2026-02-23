using System.Collections.Generic;

using StbTrueTypeSharp;

namespace FontStash.NET
{
    public class FonsTtImpl
    {

        public FontInfo font;

        public FonsTtImpl()
        {
            font = new FontInfo();
        }

        private readonly Dictionary<(int, int), int> _glyphKernAdvance = new Dictionary<(int, int), int>(); 

        public int GetGlyphKernAdvance(int prevGlyphIndex, int glyphIndex)
        {
	        if (!_glyphKernAdvance.TryGetValue((prevGlyphIndex, glyphIndex), out var advance))
	        {
		        advance = font.stbtt_GetGlyphKernAdvance(prevGlyphIndex, glyphIndex);
		        _glyphKernAdvance.Add((prevGlyphIndex, glyphIndex), advance);
	        }

	        return advance;
        }

    }
}
