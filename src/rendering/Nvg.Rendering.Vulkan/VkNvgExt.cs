namespace NvgNET.Rendering.Vulkan;

public struct VkNvgExt {
    public bool DynamicState; //Requires VK_EXT_EXTENDED_DYNAMIC_STATE_EXTENSION_NAME
    public bool ColorBlendEquation; //Requires VK_EXT_EXTENDED_DYNAMIC_STATE_3_EXTENSION_NAME
    public bool ColorWriteMask; //Requires VK_EXT_EXTENDED_DYNAMIC_STATE_3_EXTENSION_NAME
}