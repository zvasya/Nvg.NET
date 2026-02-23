using NvgNET.Blending;

namespace NvgNET.Rendering.Vulkan;

public readonly struct VkNvgCall
(
	CallType type,
	int image,
	int pathOffset,
	int pathCount,
	int triangleOffset,
	int triangleCount,
	int uniformOffset,
	CompositeOperationState compositeOperation
)
{
	public readonly CallType Type = type;
	public readonly int Image = image;
	public readonly int PathOffset = pathOffset;
	public readonly int PathCount = pathCount;
	public readonly int TriangleOffset = triangleOffset;
	public readonly int TriangleCount = triangleCount;
	public readonly int UniformOffset = uniformOffset;
	public readonly CompositeOperationState CompositeOperation = compositeOperation;
}