using Silk.NET.Vulkan;

namespace SilkyNvg.Rendering.Vulkan
{
	public static class DebugUtils
	{
		public static void Check(Result res)
		{
			if (res != Result.Success)
				throw new InvalidOperationException(res.ToString());
		}
	}
}