using Silk.NET.Vulkan;

namespace NvgNET.Rendering.Vulkan;

public class VkNvgShaderModule(VkNvgContext vk, ShaderModule shaderModule) : IDisposable
{
    public ShaderModule ShaderModule => shaderModule;
    
    public static unsafe VkNvgShaderModule Create(VkNvgContext vk, void* code, nuint size, in AllocationCallbacks allocator)
    {
        ShaderModuleCreateInfo moduleCreateInfo = new ShaderModuleCreateInfo
        {
            SType = StructureType.ShaderModuleCreateInfo,
            PNext = null,
            Flags = 0,
            CodeSize = size,
            PCode = (uint*)code
        };
        ShaderModule module;
        var shaderModuleResult = vk.Api.CreateShaderModule(vk.Device, &moduleCreateInfo, allocator, &module);
        DebugUtils.Check(shaderModuleResult);
        return new VkNvgShaderModule(vk, module);
    }

    public void Dispose()
    {
        vk.Api.DestroyShaderModule(vk.Device, shaderModule, in vk.Allocator);
    }
}