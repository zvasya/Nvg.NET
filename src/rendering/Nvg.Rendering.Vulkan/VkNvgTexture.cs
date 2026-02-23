using Silk.NET.Vulkan;

using NvgNET.Images;

namespace NvgNET.Rendering.Vulkan;

public class VkNvgTexture : IDisposable 
{
	readonly VkNvgContext _vk;
    public Sampler Sampler;

    public Image Image;
    public ImageLayout ImageLayout;
    public ImageView View;

    public DeviceMemory Mem;
    public unsafe void *MappedMem;
    public ulong RowPitch;
    public bool Mapped;
    public int Width, Height;
    public Texture Type;
    public ImageFlags Flags;

    public VkNvgTexture(VkNvgContext vk)
    {
        this._vk = vk;
    }

    public unsafe void UpdateTexture(int dx, int dy, int w, int h, ReadOnlySpan<byte> data)
    {
        var api = _vk.Api;
        Device device =  _vk.Device;
        
        if (!Mapped)
        {
            MemoryRequirements memReqs;
            api.GetImageMemoryRequirements(device, Image, &memReqs);
            var mapMemoryResult = api.MapMemory(device, Mem, 0, memReqs.Size, 0, ref MappedMem);
            DebugUtils.Check(mapMemoryResult);
            Mapped = true;

            ImageSubresource subresource = new ImageSubresource
            {
                AspectMask = ImageAspectFlags.ColorBit,
                MipLevel = 0,
                ArrayLayer = 0,
            };
            SubresourceLayout layout;
            api.GetImageSubresourceLayout(device, Image, &subresource, &layout);
            RowPitch = layout.RowPitch;
        }

        int elementSize = (Type == Texture.Rgba) ? 4 : 1;
        for (int y = 0; y < h; ++y)
        {
            byte* dest = (byte*)MappedMem + ((dy + y) * (int)RowPitch) + dx;
            var compSize = w * elementSize;
            data.Slice(((dy + y) * (Width * elementSize)) + dx, compSize).CopyTo(new Span<byte>(dest, compSize));
        }
    }
    
    // call it after vknvg.UpdateTexture
    public unsafe void InitTexture(Vk api, CommandBuffer cmdBuffer, Queue queue)
    {
        CommandBufferBeginInfo beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };

        api.BeginCommandBuffer(cmdBuffer, &beginInfo);

        ImageSubresourceRange resourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1);

        ImageMemoryBarrier layoutTransitionBarrier = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = 0,
            DstAccessMask = 0,
            OldLayout = ImageLayout.Preinitialized,
            NewLayout = ImageLayout.ShaderReadOnlyOptimal,
            SrcQueueFamilyIndex = ~0u,
            DstQueueFamilyIndex = ~0u,
            Image = Image,
            SubresourceRange = resourceRange,
        };

        api.CmdPipelineBarrier(cmdBuffer, PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.TopOfPipeBit, 0, 0, null, 0, null, 1, &layoutTransitionBarrier);

        api.EndCommandBuffer(cmdBuffer);

        PipelineStageFlags* waitStageMash = stackalloc PipelineStageFlags[] { PipelineStageFlags.ColorAttachmentOutputBit };
        
        SubmitInfo submitInfo = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            WaitSemaphoreCount = 0,
            PWaitSemaphores = null,
            PWaitDstStageMask = waitStageMash,
            CommandBufferCount = 1,
            PCommandBuffers = &cmdBuffer,
            SignalSemaphoreCount = 0,
            PSignalSemaphores = null,
        };
        api.QueueSubmit(queue, 1, &submitInfo, default);
        api.QueueWaitIdle(queue);
        api.ResetCommandBuffer(cmdBuffer, 0);
        ImageLayout = ImageLayout.ShaderReadOnlyOptimal;
    }

    public void Dispose()
    {
        Device device = _vk.Device;
        var api = _vk.Api;
        ref AllocationCallbacks allocator = ref _vk.Allocator;
        
        if (Mapped)
        {
            api.UnmapMemory(_vk.Device, Mem);
            Mapped = false;
        }
            
        if (View.Handle != 0)
        {
            api.DestroyImageView(device, View, allocator);
            View = new ImageView();
        }

        if (Sampler.Handle != 0)
        {
            api.DestroySampler(device, Sampler, allocator);
            Sampler = new Sampler();
        }

        if (Image.Handle != 0)
        {
            api.DestroyImage(device, Image, allocator);
            Image.Handle = 0;
        }

        if (Mem.Handle != 0)
        {
            api.FreeMemory(device, Mem, allocator);
            Mem = new DeviceMemory();
            Mapped = false;
        }
    }
}