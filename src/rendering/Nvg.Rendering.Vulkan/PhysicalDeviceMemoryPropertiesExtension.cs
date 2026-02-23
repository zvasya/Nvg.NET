using Silk.NET.Vulkan;

namespace NvgNET.Rendering.Vulkan;

public static class PhysicalDeviceMemoryPropertiesExtension
{
	public static Result GetMemoryType
	(
		this PhysicalDeviceMemoryProperties memoryProperties,
		uint typeBits, 
		MemoryPropertyFlags requirementsMask,
		out uint typeIndex
	)
	{
		// Search memtypes to find first index with those properties
		for (int i = 0; i < memoryProperties.MemoryTypeCount; i++)
		{
			if ((typeBits & 1) == 1)
			{
				// Type is available, does it match user properties?
				if ((memoryProperties.MemoryTypes[i].PropertyFlags & requirementsMask) == requirementsMask)
				{
					typeIndex = (uint)i;
					return Result.Success;
				}
			}

			typeBits >>= 1;
		}

		// No memory types matched, return failure
		typeIndex = 0;
		return Result.ErrorFormatNotSupported;
	}
}