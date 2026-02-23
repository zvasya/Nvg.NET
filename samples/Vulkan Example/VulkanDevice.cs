using Silk.NET.Vulkan;

namespace Vulkan_Example;

public class VulkanDevice {
    public PhysicalDevice gpu;
    public PhysicalDeviceProperties gpuProperties;
    public PhysicalDeviceMemoryProperties memoryProperties;

    public QueueFamilyProperties[] queueFamilyProperties;
    public uint queueFamilyPropertiesCount;

    public uint graphicsQueueFamilyIndex;
    public uint presentIndex;

    public Device device;

    public CommandPool commandPool;
}