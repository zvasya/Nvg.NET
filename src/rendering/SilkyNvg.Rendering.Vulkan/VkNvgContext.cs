using System.Runtime.CompilerServices;

using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;

namespace SilkyNvg.Rendering.Vulkan;

public class VkNvgContext
(
	Vk api,
	ExtExtendedDynamicState3? apiExt3,
	PhysicalDevice gpu,
	Device device,
	RenderPass renderPass,
	uint swapchainImageCount,
	VkNvgExt ext,
	CommandBuffer[] cmdBuffer,
	VkNvgAllocator? allocator,
	Func<uint> currentFrameProvider
)
{
	public readonly Vk Api = api;
	public readonly ExtExtendedDynamicState3? ApiExt3 = apiExt3;
	public readonly PhysicalDevice Gpu = gpu;
	public readonly Device Device = device;
	public readonly RenderPass RenderPass = renderPass;
	public readonly uint SwapchainImageCount = swapchainImageCount;
	public readonly VkNvgExt Ext = ext;
	public readonly CommandBuffer[] CmdBuffer = cmdBuffer;
	public readonly Func<uint> CurrentFrameProvider = currentFrameProvider;
	public ref AllocationCallbacks Allocator
	{
		get
		{
			if (allocator == null)
				return ref Unsafe.NullRef<AllocationCallbacks>();
			return ref allocator.Allocator;
		}
	}
}