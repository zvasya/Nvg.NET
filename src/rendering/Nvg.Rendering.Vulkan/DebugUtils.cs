using Silk.NET.Vulkan;

namespace NvgNET.Rendering.Vulkan
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