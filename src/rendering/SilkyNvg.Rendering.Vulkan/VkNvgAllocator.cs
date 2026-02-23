using Silk.NET.Vulkan;

namespace SilkyNvg.Rendering.Vulkan;

public class VkNvgAllocator
{
    AllocationCallbacks _allocator;

    public VkNvgAllocator(AllocationCallbacks allocator)
    {
        this._allocator = allocator;
    }

    public ref AllocationCallbacks Allocator => ref _allocator;
}