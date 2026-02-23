using System.Runtime.CompilerServices;

namespace SilkyNvg.Rendering.Vulkan
{
	public static class ListUtils
	{
		public static Span<T> AsSpan<T>(this List<T> list)
		{
			return Unsafe.As<StrongBox<T[]>>(list).Value.AsSpan(0, list.Count);
		}
    
		public static Span<T> AsSpan<T>(this List<T> list, int start, int length)
		{
			if (length + start > list.Count)
				throw new ArgumentOutOfRangeException(nameof(length));
			return Unsafe.As<StrongBox<T[]>>(list).Value.AsSpan(start, length);
		}
    
		public static Span<T> AsSpan<T>(this List<T> list, int start)
		{
			return Unsafe.As<StrongBox<T[]>>(list).Value.AsSpan(start, list.Count);
		}
	}
}