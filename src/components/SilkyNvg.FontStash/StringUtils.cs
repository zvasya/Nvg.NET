using System;

namespace FontStash.NET
{
	public static class StringUtils
	{
		public static bool Equal(ReadOnlyMemory<Char> strA, int indexA, ReadOnlyMemory<Char> strB, int indexB, int length)
		{
			return strA[indexA .. Math.Min(indexA + length, strA.Length)].Span.SequenceEqual(strB[indexB .. Math.Min(indexB + length, strB.Length)].Span);
		}
        
		public static bool Equal(ReadOnlyMemory<Char> strA, ReadOnlyMemory<Char> strB)
		{
			return strA.Span.SequenceEqual(strB.Span);
		}

	}
}