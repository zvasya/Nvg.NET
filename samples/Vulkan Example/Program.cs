// See https://aka.ms/new-console-template for more information
// #define DEMO_VULKAN_VALIDATON_LAYER
// #define MOLTEN_VK_NEW
using System.Diagnostics;
using System.Runtime.InteropServices;
using NvgExample;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using SilkyNvg;
using SilkyNvg.Rendering.Vulkan;
using VulkanExample2;

using Image = Silk.NET.Vulkan.Image;
using Semaphore = Silk.NET.Vulkan.Semaphore;

internal class Program
{
#if !MOLTEN_VK_NEW
    const string KhrPortabilityEnumerationExtensionName = "VK_KHR_portability_enumeration";
    const string LayerKhronosValidationExtensionName = "VK_LAYER_KHRONOS_validation";
#endif

    private static Vk api;
    private static KhrSurface khrSurface;
    private static ExtDebugUtils debugUtils;
    private static ExtExtendedDynamicState3? apiExt3;
    
    private static FrameBuffers fb;

    static VkNvgContext create_info;
    static Instance instance;
    static VulkanDevice device;
    static VkNvgRenderer nvgRenderer;
    static Nvg nvg;
    static Demo demo;
    static PerformanceGraph frameGraph;
    static PerformanceGraph cpuGraph;
    static CommandBuffer[] cmd_buffer => create_info.CmdBuffer;
    private static SurfaceKHR surface;

    public static KhrSwapchain KhrSwapchain;

    private static IWindow window;
    private static Extent2D windowExtent = new(1000, 600);

    static Queue executionQueue;
    static Queue presentQueue;

    static int winWidth;
    static int winHeight;

    static bool isApplePlatform => OperatingSystem.IsMacOS();
    
    static ImageMemoryBarrier defaultImageBarrier = new ImageMemoryBarrier()
    {
	    SType = StructureType.ImageMemoryBarrier,
	    SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
	    DstAccessMask = 0,
	    OldLayout = ImageLayout.ColorAttachmentOptimal,
	    NewLayout = ImageLayout.PresentSrcKhr,
	    SrcQueueFamilyIndex = ~0u,
	    DstQueueFamilyIndex = ~0u,
	    SubresourceRange = new ImageSubresourceRange()
	    {
		    AspectMask = ImageAspectFlags.ColorBit,
		    BaseMipLevel = 0,
		    LevelCount = 1,
		    BaseArrayLayer = 0,
		    LayerCount = 1,
	    },
    };

    private static void LoadInput()
    {
        IInputContext input = window.CreateInput();
        foreach (IKeyboard keyboard in input.Keyboards)
        {
            keyboard.KeyDown += key;
        }

        foreach (IMouse mouse in input.Mice)
        {
            mouse.MouseMove += MouseMove;
        }
    }

    private static void MouseMove(IMouse _, System.Numerics.Vector2 mousePosition)
    {
        mx = mousePosition.X;
        my = mousePosition.Y;
    }


    public static unsafe void Main(string[] args)
    {
        api = Vk.GetApi();
        Silk.NET.Windowing.Sdl.SdlWindowing.Use();
        frameGraph = new PerformanceGraph(PerformanceGraph.GraphRenderStyle.Fps, "Frame Time");
        cpuGraph = new PerformanceGraph(PerformanceGraph.GraphRenderStyle.Ms, "CPU Time");

        WindowOptions windowOptions = WindowOptions.DefaultVulkan;
        windowOptions.FramesPerSecond = -1;
        windowOptions.Size = new Vector2D<int>((int)windowExtent.Width, (int)windowExtent.Height);
        windowOptions.Title = "SilkyNvg + Silk.NET.Vulkan";

        window = Window.Create(windowOptions);
        window.Load += Load;
        window.FramebufferResize += FramebufferResize;
        window.Render += Render;
        window.Closing += Close;
        window.Run();
    }

    static void FramebufferResize(Vector2D<int> obj)
    {
        resize_event = true;
    }

    unsafe static void Load()
    {
        LoadInput();

        bool enableValidationLayer = false;
#if DEMO_VULKAN_VALIDATON_LAYER
        enableValidationLayer = true;
#endif

        instance = createVkInstance(enableValidationLayer);
        if (!api.TryGetInstanceExtension(instance, out khrSurface))
        {
            throw new NotSupportedException($"{KhrSurface.ExtensionName} extension not found.");
        }

        Result res;
        surface = window.VkSurface!.Create<AllocationCallbacks>(instance.ToHandle(), null).ToSurface();

        uint gpu_count = 0;

        res = api.EnumeratePhysicalDevices(instance, &gpu_count, null);
        if (Result.Success != res && res != Result.Incomplete)
        {
            throw new Exception($"vkEnumeratePhysicalDevices failed {res} ");
        }

        if (gpu_count < 1)
        {
            throw new Exception("No Vulkan device found.");
        }

        PhysicalDevice* gpu = stackalloc PhysicalDevice[32];
        res = api.EnumeratePhysicalDevices(instance, &gpu_count, gpu);
        if (res != Result.Success && res != Result.Incomplete)
        {
            throw new Exception($"vkEnumeratePhysicalDevices failed {res}");
        }

        uint idx = 0;
        bool use_idx = false;
        bool discrete_idx = false;
        for (uint i = 0; i < gpu_count && (!discrete_idx); i++)
        {
            uint qfc = 0;
            api.GetPhysicalDeviceQueueFamilyProperties(gpu[i], &qfc, null);
            if (qfc < 1)
                continue;

            QueueFamilyProperties[] queue_family_properties = new QueueFamilyProperties[qfc];

            api.GetPhysicalDeviceQueueFamilyProperties(gpu[i], &qfc, queue_family_properties);

            for (uint j = 0; j < qfc; j++)
            {
                khrSurface.GetPhysicalDeviceSurfaceSupport(gpu[i], j, surface, out var supports_present);

                if (queue_family_properties[j].QueueFlags.HasFlag(QueueFlags.GraphicsBit) && supports_present)
                {
                    api.GetPhysicalDeviceProperties(gpu[i], out var pr);
                    idx = i;
                    use_idx = true;
                    if (pr.DeviceType == PhysicalDeviceType.DiscreteGpu)
                    {
                        discrete_idx = true;
                    }

                    break;
                }
            }
        }

        if (!use_idx)
        {
            throw new Exception("Not found suitable queue which supports graphics.");
        }

        Console.WriteLine($"Using GPU device {(ulong)idx}");

        device = createVulkanDevice(gpu[idx], surface, out var extQuery);
        if (!api.TryGetDeviceExtension(instance, device.device, out KhrSwapchain))
        {
            throw new NotSupportedException($"{KhrSwapchain.ExtensionName} extension not found.");
        }

        winWidth = window.Size.X;
        winHeight = window.Size.Y;

        api.GetDeviceQueue(device.device, device.graphicsQueueFamilyIndex, 0, out executionQueue);
        api.GetDeviceQueue(device.device, device.graphicsQueueFamilyIndex, 0, out presentQueue);

        fb = createFrameBuffers(device, surface, executionQueue, winWidth, winHeight, default);

        CommandBuffer[] cmd_buffer = createCmdBuffer(device.device, device.commandPool, fb.swapchain_image_count);

        VkNvgExt ext = new VkNvgExt();
        ext.DynamicState = apiExt3 != null && extQuery.DynamicState;
        ext.ColorBlendEquation = extQuery.ColorBlendEquation;
        ext.ColorWriteMask = extQuery.ColorWriteMask;
        
        create_info = new VkNvgContext(api, apiExt3, device.gpu, device.device, fb.render_pass, fb.swapchain_image_count, ext, cmd_buffer, null, () => fb.current_frame);
        

        CreateFlags flags = 0;
#if NDEBUG
        flags |= СreateFlags.Debug; // unused in nanovg_vk
#endif
#if DEMO_ANTIALIAS
  flags |= NVGcreateFlags.NVG_ANTIALIAS;
#endif
#if DEMO_STENCIL_STROKES
  flags |= NVGcreateFlags.NVG_STENCIL_STROKES;
#endif

        nvgRenderer = new VkNvgRenderer(create_info, CreateFlags.Antialias | CreateFlags.StencilStrokes | CreateFlags.Debug, executionQueue);
        nvg = Nvg.Create(nvgRenderer);

        demo = new Demo(nvg);
    }

    static bool closed = false;
    static unsafe void Close()
    {
	    if (closed)
		    return;
	    closed = true;
	    
        api.QueueWaitIdle(executionQueue);
        api.QueueWaitIdle(presentQueue);

        demo.Dispose();

        nvgRenderer.Dispose();
        nvg.Dispose();

        destroyFrameBuffers(device, fb, executionQueue);

        destroyVulkanDevice(device);

        destroyDebugCallback(instance);

        khrSurface.DestroySurface(instance, surface, null);
        api.DestroyInstance(instance, null);

        // glfwDestroyWindow(window);

        // free(cmd_buffer);
    }

    static double t = 0;
    static double mx = 0, my = 0;

    static void Render(double ddt)
    {
        float dt = (float)ddt;
        t += dt;
        float pxRatio;


        int cwinWidth = window.Size.X;
        int cwinHeight = window.Size.Y;
        if ((resize_event) || (winWidth != cwinWidth || winHeight != cwinHeight))
        {
            winWidth = cwinWidth;
            winHeight = cwinHeight;
            destroyFrameBuffers(device, fb, executionQueue);
            fb = createFrameBuffers(device, surface, executionQueue, winWidth, winHeight, default);
            resize_event = false;
        }
        else
        {
            prepareFrame(device.device, fb);
            if (resize_event)
                return;

            frameGraph.Update(dt);
            cpuGraph.Update(dt);

            pxRatio = fb.buffer_size.Width / (float)winWidth;

            nvg.BeginFrame(winWidth, winHeight, pxRatio);

	        demo.Render((float)mx, (float)my, winWidth, winHeight, (float)t, blowup);

            frameGraph.Render(5.0f, 5.0f, nvg);
            cpuGraph.Render(5.0f + 200.0f + 5.0f, 5.0f, nvg);

            BeginCmdBuffer(cmd_buffer[fb.current_frame], fb);
            nvg.EndFrame();

            SubmitFrame(executionQueue, presentQueue, cmd_buffer[fb.current_frame], fb);
        }
    }

    static bool blowup = false;
    static bool screenshot = false;
    static bool premult = false;
    static bool resize_event = false;

    static void key(IKeyboard keyboard, Key key, int _)
    {
        if (key == Key.Escape)
            window.Close();
        if (key == Key.Space)
            blowup = !blowup;
        if (key == Key.S)
            screenshot = true;
        if (key == Key.P)
            premult = !premult;
    }

    static unsafe void prepareFrame(Device device, FrameBuffers fb)
    {
	    var fence = fb.flight_fence[fb.current_frame];
        api.WaitForFences(device, 1, &fence, true, ulong.MaxValue);
        api.ResetFences(device, 1, &fence);
        // Get the index of the next available swapchain image:
        Result res = KhrSwapchain.AcquireNextImage(device, fb.swap_chain, ulong.MaxValue, fb.present_complete_semaphore[fb.current_frame],
	        default, ref fb.current_buffer);

        if (res == Result.ErrorOutOfDateKhr) 
	        resize_event = true;
    }

    private static unsafe void BeginCmdBuffer(CommandBuffer cmd_buffer, FrameBuffers fb)
    {
	    CommandBufferBeginInfo cmd_buf_info = new CommandBufferBeginInfo
	    {
		    SType = StructureType.CommandBufferBeginInfo
	    };
	    api.BeginCommandBuffer(cmd_buffer, &cmd_buf_info);

	    ClearValue* clear_values = stackalloc ClearValue[2];
	    clear_values[0].Color.Float32_0 = 0.3f;
	    clear_values[0].Color.Float32_1 = 0.3f;
	    clear_values[0].Color.Float32_2 = 0.32f;
	    clear_values[0].Color.Float32_3 = 1.0f;
	    clear_values[1].DepthStencil.Depth = 1.0f;
	    clear_values[1].DepthStencil.Stencil = 0;

	    RenderPassBeginInfo rp_begin = new RenderPassBeginInfo();
	    rp_begin.SType = StructureType.RenderPassBeginInfo;
	    rp_begin.PNext = null;
	    rp_begin.RenderPass = fb.render_pass;
	    rp_begin.Framebuffer = fb.framebuffers[fb.current_buffer];
	    rp_begin.RenderArea.Offset.X = 0;
	    rp_begin.RenderArea.Offset.Y = 0;
	    rp_begin.RenderArea.Extent.Width = fb.buffer_size.Width;
	    rp_begin.RenderArea.Extent.Height = fb.buffer_size.Height;
	    rp_begin.ClearValueCount = 2;
	    rp_begin.PClearValues = clear_values;

	    api.CmdBeginRenderPass(cmd_buffer, &rp_begin, SubpassContents.Inline);

	    Viewport viewport = new Viewport();
	    viewport.Width = (float)fb.buffer_size.Width;
	    viewport.Height = (float)fb.buffer_size.Height;
	    viewport.MinDepth = 0.0f;
	    viewport.MaxDepth = 1.0f;
	    viewport.X = (float)rp_begin.RenderArea.Offset.X;
	    viewport.Y = (float)rp_begin.RenderArea.Offset.Y;
	    api.CmdSetViewport(cmd_buffer, 0, 1, &viewport);

	    Rect2D scissor = rp_begin.RenderArea;
	    api.CmdSetScissor(cmd_buffer, 0, 1, &scissor);
    }

    static unsafe void SubmitFrame(Queue graphicsQueue, Queue presentQueue, CommandBuffer cmdBuffer, FrameBuffers frameBuffers)
    {
	    api.CmdEndRenderPass(cmdBuffer);
        var fbCurrentBuffer = frameBuffers.current_buffer;
        ImageMemoryBarrier imageBarrier = defaultImageBarrier with
        {
	        Image = frameBuffers.swap_chain_buffers[fbCurrentBuffer].image,
        };
        api.CmdPipelineBarrier(cmdBuffer, PipelineStageFlags.ColorAttachmentOutputBit, PipelineStageFlags.BottomOfPipeBit,
            0, 0, null, 0, null, 1, &imageBarrier);

        api.EndCommandBuffer(cmdBuffer);

        PipelineStageFlags pipeStageFlags = PipelineStageFlags.ColorAttachmentOutputBit;

        var semaphorePresent = frameBuffers.present_complete_semaphore[frameBuffers.current_frame];
        var semaphoreRender = frameBuffers.render_complete_semaphore[frameBuffers.current_frame];
        
        SubmitInfo submitInfo = new SubmitInfo { 
	        SType = StructureType.SubmitInfo,
	        PNext = null,
	        WaitSemaphoreCount = 1,
	        PWaitSemaphores = &semaphorePresent,
	        PWaitDstStageMask = &pipeStageFlags,
	        CommandBufferCount = 1,
	        PCommandBuffers = &cmdBuffer,
	        SignalSemaphoreCount = 1,
	        PSignalSemaphores = &semaphoreRender,
        };

        var fence = frameBuffers.flight_fence[frameBuffers.current_frame];
        api.QueueSubmit(presentQueue, 1, &submitInfo, fence);

        /* Now present the image in the window */

        var fbSwapChain = frameBuffers.swap_chain;
        PresentInfoKHR present = new PresentInfoKHR { SType = StructureType.PresentInfoKhr,
	        PNext = null,
	        SwapchainCount = 1,
	        PSwapchains = &fbSwapChain,
	        PImageIndices = &fbCurrentBuffer,
	        WaitSemaphoreCount = 1,
	        PWaitSemaphores = &semaphoreRender,
        };

        Result res = KhrSwapchain.QueuePresent(presentQueue, &present);
        if (res == Result.ErrorOutOfDateKhr)
        {
            api.QueueWaitIdle(graphicsQueue);
            api.QueueWaitIdle(presentQueue);
            resize_event = true;
            return;
        }

        frameBuffers.current_frame = (frameBuffers.current_frame + 1) % frameBuffers.swapchain_image_count;
        frameBuffers.num_swaps++;
    }


    unsafe static VulkanDevice createVulkanDevice(PhysicalDevice gpu, SurfaceKHR surface, out VkNvgExt ext)
    {
        ext = new VkNvgExt();
        VulkanDevice device = new VulkanDevice();

        device.gpu = gpu;
        api.GetPhysicalDeviceMemoryProperties(gpu, out device.memoryProperties);
        api.GetPhysicalDeviceProperties(gpu, out device.gpuProperties);

        api.GetPhysicalDeviceQueueFamilyProperties(gpu, ref device.queueFamilyPropertiesCount, null);
        Debug.Assert(device.queueFamilyPropertiesCount >= 1);

        device.queueFamilyProperties = new QueueFamilyProperties[device.queueFamilyPropertiesCount];

        api.GetPhysicalDeviceQueueFamilyProperties(gpu, new Span<uint>(ref device.queueFamilyPropertiesCount), device.queueFamilyProperties);
        Debug.Assert(device.queueFamilyPropertiesCount >= 1);

        device.graphicsQueueFamilyIndex = uint.MaxValue;
        device.presentIndex = uint.MaxValue;
        for (uint i = 0; i < device.queueFamilyPropertiesCount; ++i)
        {
            if (device.queueFamilyProperties[i].QueueFlags.HasFlag(QueueFlags.GraphicsBit))
            {
                device.graphicsQueueFamilyIndex = i;
            }

            Bool32 presentSupport = false;
            khrSurface.GetPhysicalDeviceSurfaceSupport(gpu, i, surface, &presentSupport);
            if (presentSupport)
            {
                device.presentIndex = i;
            }

            if (device.presentIndex != uint.MaxValue && device.graphicsQueueFamilyIndex != uint.MaxValue)
            {
                break;
            }
        }

        float* queuePriorities = stackalloc float[] { 0.0f };
        DeviceQueueCreateInfo queue_info = new DeviceQueueCreateInfo { SType = StructureType.DeviceQueueCreateInfo };
        queue_info.QueueCount = 1;
        queue_info.PQueuePriorities = queuePriorities;
        queue_info.QueueFamilyIndex = device.graphicsQueueFamilyIndex;

        PhysicalDeviceExtendedDynamicStateFeaturesEXT extendedDynamicStateFeatures = new PhysicalDeviceExtendedDynamicStateFeaturesEXT
        {
            SType = StructureType.PhysicalDeviceExtendedDynamicStateFeaturesExt
        };
        PhysicalDeviceExtendedDynamicState3FeaturesEXT extendedDynamicState3Features = new PhysicalDeviceExtendedDynamicState3FeaturesEXT
        {
            SType = StructureType.PhysicalDeviceExtendedDynamicState3FeaturesExt
        };

        extendedDynamicStateFeatures.PNext = &extendedDynamicState3Features;

        //Provided by VK_VERSION_1_1 or VK_KHR_get_physical_device_properties2/VK_KHR_GET_PHYSICAL_DEVICE_PROPERTIES_2_EXTENSION_NAME
        PhysicalDeviceFeatures2 physicalDeviceFeatures2 = new PhysicalDeviceFeatures2();
        physicalDeviceFeatures2.SType = StructureType.PhysicalDeviceFeatures2;
        physicalDeviceFeatures2.PNext = &extendedDynamicStateFeatures;
        api.GetPhysicalDeviceFeatures2(gpu, &physicalDeviceFeatures2);
        physicalDeviceFeatures2.Features.RobustBufferAccess = false;

        // bool enableDynamicState = false;
        // bool enableDynamicState3 = false;

        uint count = 0;

        api.EnumerateDeviceExtensionProperties(gpu, (byte*)null, ref count, null);
        ExtensionProperties* extensions = stackalloc ExtensionProperties[(int)count];
        api.EnumerateDeviceExtensionProperties(gpu, (byte*)null, ref count, extensions);

        physicalDeviceFeatures2.PNext = null;
        extendedDynamicStateFeatures.PNext = null;
        extendedDynamicState3Features.PNext = null;

        List<string> enabledExtension = new List<string>(16)
        {
            KhrSwapchain.ExtensionName
        };
        Console.WriteLine("vkEnumerateDeviceExtensionProperties:");
        for (uint i = 0; i < count; i++)
        {
            string extensionName = SilkMarshal.PtrToString((IntPtr)extensions[i].ExtensionName, NativeStringEncoding.LPTStr) ?? throw new Exception("Can't get ExtensionName");
	        Console.WriteLine(extensionName);
	        // TODO
            // if (!isApplePlatform && string.Compare(ExtExtendedDynamicState.ExtensionName, extensionName, StringComparison.OrdinalIgnoreCase) == 0)
            // {
            //     enabledExtension.Add(ExtExtendedDynamicState.ExtensionName);
            //     physicalDeviceFeatures2.PNext = &extendedDynamicStateFeatures;
            //     ext.DynamicState = extendedDynamicStateFeatures.ExtendedDynamicState;
            // }
            //
            // if (string.Compare(ExtExtendedDynamicState3.ExtensionName, extensionName, StringComparison.OrdinalIgnoreCase) == 0)
            // {
            //     enabledExtension.Add(ExtExtendedDynamicState3.ExtensionName);
            //     extendedDynamicStateFeatures.PNext = &extendedDynamicState3Features;
            //     ext.ColorBlendEquation = extendedDynamicState3Features.ExtendedDynamicState3ColorBlendEquation;
            //     ext.ColorWriteMask = extendedDynamicState3Features.ExtendedDynamicState3ColorWriteMask;
            // }
        }

#if !MOLTEN_VK_NEW
        if (isApplePlatform) 
            enabledExtension.Add("VK_KHR_portability_subset");
#endif

        IntPtr enabledExtensionPtr = SilkMarshal.StringArrayToPtr(enabledExtension);
        DeviceCreateInfo deviceInfo = new DeviceCreateInfo() { SType = StructureType.DeviceCreateInfo };
        deviceInfo.QueueCreateInfoCount = 1;
        deviceInfo.PQueueCreateInfos = &queue_info;
        deviceInfo.EnabledExtensionCount = (uint)enabledExtension.Count;
        deviceInfo.PpEnabledExtensionNames = (byte**)enabledExtensionPtr;
        deviceInfo.PEnabledFeatures = null;
        deviceInfo.PNext = &physicalDeviceFeatures2;
        Result res = api.CreateDevice(gpu, &deviceInfo, null, out device.device);
        Debug.Assert(res == Result.Success);
        SilkMarshal.Free(enabledExtensionPtr);

        api.TryGetDeviceExtension(instance, device.device, out apiExt3);

        /* Create a command pool to allocate our command buffer from */
        CommandPoolCreateInfo cmd_pool_info = new CommandPoolCreateInfo() { SType = StructureType.CommandPoolCreateInfo };
        cmd_pool_info.QueueFamilyIndex = device.graphicsQueueFamilyIndex;
        cmd_pool_info.Flags = CommandPoolCreateFlags.ResetCommandBufferBit;
        res = api.CreateCommandPool(device.device, &cmd_pool_info, null, out device.commandPool);
        Debug.Assert(res == Result.Success);

        return device;
    }

    static unsafe void destroyVulkanDevice(VulkanDevice device)
    {
        device.queueFamilyProperties = null;

        if (device.commandPool.Handle != 0)
        {
            api.DestroyCommandPool(device.device, device.commandPool, null);
        }

        if (device.device.Handle != 0)
        {
            api.DestroyDevice(device.device, null);
        }
    }

    public struct SwapchainBuffers
    {
        public Image image;
        public ImageView view;
    }

    public struct DepthBuffer
    {
        public Format format;

        public Image image;
        public DeviceMemory mem;
        public ImageView view;
    }

    public class FrameBuffers
    {
        public SwapchainKHR swap_chain;
        public SwapchainBuffers[] swap_chain_buffers;
        public uint swapchain_image_count;
        public Framebuffer[] framebuffers;

        public uint current_buffer;
        public uint current_frame;
        public ulong num_swaps;

        public Extent2D buffer_size;

        public RenderPass render_pass;

        public Format format;
        public DepthBuffer depth;
        public Semaphore[] present_complete_semaphore;
        public Semaphore[] render_complete_semaphore;
        public Fence[] flight_fence;
    }

    static unsafe uint DebugCallback(DebugUtilsMessageSeverityFlagsEXT messageSeverity, DebugUtilsMessageTypeFlagsEXT messageTypes, DebugUtilsMessengerCallbackDataEXT* pCallbackData, void* pUserData)
    {
        if (messageSeverity > DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt)
        {
            string message = Marshal.PtrToStringAnsi((nint)pCallbackData->PMessage) ?? string.Empty;
            Console.WriteLine($"{messageSeverity} {messageTypes} {message}");
// #if DEBUG && BREAK_ON_ERROR
            if (messageSeverity.HasFlag(DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt))
            {
                if (System.Diagnostics.Debugger.IsAttached)
                {
                    System.Diagnostics.Debugger.Break();
                }
            }
// #endif
        }

        return Vk.False;
    }

    static DebugUtilsMessengerEXT? debugMessenger = null;

    unsafe static Instance createVkInstance(bool enable_debug_layer)
    {
        // initialize the VkApplicationInfo structure
        var applicationName = SilkMarshal.StringToPtr("NanoVG");

        ApplicationInfo app_info = new ApplicationInfo() { SType = StructureType.ApplicationInfo };
        app_info.PApplicationName = (byte*)applicationName;
        app_info.ApplicationVersion = 1;
        app_info.PEngineName = (byte*)applicationName;
        app_info.EngineVersion = 1;
        app_info.ApiVersion = Vk.Version10;

        List<string> extensions = new List<string>()
        {
            KhrGetPhysicalDeviceProperties2.ExtensionName
        };

        if (enable_debug_layer)
        {
            extensions.Add(ExtDebugUtils.ExtensionName);
        }

        if (isApplePlatform)
        {
#if !MOLTEN_VK_NEW
            extensions.Add(KhrPortabilityEnumerationExtensionName);
#endif
        }

        Version32 loader_version = Vk.Version10;
        uint version;
        if (api.EnumerateInstanceVersion(&version) == Result.Success)
            loader_version = (Version32)version;
        Console.WriteLine($"Vulkan loader API version: {loader_version.Major}.{loader_version.Minor}");

        var windowExtensionsPtr = window.VkSurface.GetRequiredExtensions(out var extensionsCount);
        var windowExtensions = SilkMarshal.PtrToStringArray((IntPtr)windowExtensionsPtr, (int)extensionsCount);
        extensions.AddRange(windowExtensions);

        IntPtr extensionsPtr = SilkMarshal.StringArrayToPtr(extensions);
        // initialize the VkInstanceCreateInfo structure
        InstanceCreateInfo inst_info = new InstanceCreateInfo() { SType = StructureType.InstanceCreateInfo };
        inst_info.PApplicationInfo = &app_info;
        inst_info.EnabledExtensionCount = (uint)extensions.Count;
        inst_info.PpEnabledExtensionNames = (byte**)extensionsPtr;
#if !MOLTEN_VK_NEW
        if (isApplePlatform)
        {
            inst_info.Flags = InstanceCreateFlags.EnumeratePortabilityBitKhr;
        }
#endif

        var enabledLayerNamesPtr = IntPtr.Zero;
        if (enable_debug_layer)
        {
#if MOLTEN_VK_NEW
            string[] enabledLayerNames = ["MoltenVK"];
#else
            string[] enabledLayerNames = [LayerKhronosValidationExtensionName];
#endif
            enabledLayerNamesPtr = SilkMarshal.StringArrayToPtr(enabledLayerNames);
            inst_info.EnabledLayerCount = (uint)enabledLayerNames.Length;
            inst_info.PpEnabledLayerNames = (byte**)enabledLayerNamesPtr;
        }

        uint layerCount = 0;
        api.EnumerateInstanceLayerProperties(ref layerCount, null);
        LayerProperties* layerprop = stackalloc LayerProperties[(int)layerCount];
        api.EnumerateInstanceLayerProperties(&layerCount, layerprop);
        Console.WriteLine("vkEnumerateInstanceLayerProperties:");
        for (uint i = 0; i < layerCount; ++i)
        {
            Console.WriteLine(SilkMarshal.PtrToString((IntPtr)layerprop[i].LayerName, NativeStringEncoding.LPTStr));
        }


        Instance inst;
        Result res;
        res = api.CreateInstance(in inst_info, null, out inst);

        SilkMarshal.Free(extensionsPtr);
        if (enabledLayerNamesPtr != IntPtr.Zero)
            SilkMarshal.Free(enabledLayerNamesPtr);

        if (res == Result.ErrorIncompatibleDriver)
        {
            throw new Exception("cannot find a compatible Vulkan ICD");
        }

        if (res != Result.Success)
        {
            switch (res)
            {
                case (Result.ErrorOutOfHostMemory):
                    throw new Exception("VK_ERROR_OUT_OF_HOST_MEMORY");
                case (Result.ErrorOutOfDeviceMemory):
                    throw new Exception("VK_ERROR_OUT_OF_DEVICE_MEMORY");
                case (Result.ErrorInitializationFailed):
                    throw new Exception("VK_ERROR_INITIALIZATION_FAILED");
                case (Result.ErrorLayerNotPresent):
                    throw new Exception("VK_ERROR_LAYER_NOT_PRESENT");
                case (Result.ErrorExtensionNotPresent):
                    throw new Exception("VK_ERROR_EXTENSION_NOT_PRESENT");
                default:
                    throw new Exception($"unknown error {res}");
            }
        }

        if (enable_debug_layer)
        {
            DebugUtilsMessengerCreateInfoEXT createInfo = new DebugUtilsMessengerCreateInfoEXT();
            createInfo.SType = StructureType.DebugUtilsMessengerCreateInfoExt;
            createInfo.MessageSeverity =
                DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt | DebugUtilsMessageSeverityFlagsEXT.InfoBitExt |
                DebugUtilsMessageSeverityFlagsEXT.WarningBitExt | DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt;
            createInfo.MessageType = DebugUtilsMessageTypeFlagsEXT.GeneralBitExt |
                                     DebugUtilsMessageTypeFlagsEXT.ValidationBitExt |
                                     DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt;
            createInfo.PfnUserCallback = new PfnDebugUtilsMessengerCallbackEXT(DebugCallback);

            if (!api.TryGetInstanceExtension(inst, out debugUtils))
            {
                Console.WriteLine($"{ExtDebugUtils.ExtensionName} extension not found.");
            }
            else
            {
                var result = debugUtils.CreateDebugUtilsMessenger(inst, &createInfo, null, out var debugUtilsMessenger);
                if (result != Result.Success)
                {
                    debugMessenger = null;
                    Console.WriteLine($"CreateDebugUtilsMessengerEXT failed ({result})");
                }
                else
                {
                    debugMessenger = debugUtilsMessenger;
                }
            }
        }

        return inst;
    }

    static unsafe void destroyDebugCallback(Instance instance)
    {
        if (!debugMessenger.HasValue)
            return;
        debugUtils.DestroyDebugUtilsMessenger(instance, debugMessenger.Value, null);
    }

    unsafe CommandPool createCmdPool(VulkanDevice device)
    {
        Result res;
        /* Create a command pool to allocate our command buffer from */
        CommandPoolCreateInfo cmd_pool_info = new CommandPoolCreateInfo() { SType = StructureType.CommandPoolCreateInfo };
        cmd_pool_info.QueueFamilyIndex = device.graphicsQueueFamilyIndex;
        cmd_pool_info.Flags = CommandPoolCreateFlags.ResetCommandBufferBit;
        CommandPool cmd_pool;
        res = api.CreateCommandPool(device.device, in cmd_pool_info, null, out cmd_pool);
        Debug.Assert(res == Result.Success);
        return cmd_pool;
    }

    static unsafe CommandBuffer[] createCmdBuffer(Device device, CommandPool cmd_pool, uint command_buffer_count)
    {
        Result res;
        CommandBufferAllocateInfo cmd = new CommandBufferAllocateInfo { SType = StructureType.CommandBufferAllocateInfo };
        cmd.CommandPool = cmd_pool;
        cmd.Level = CommandBufferLevel.Primary;
        cmd.CommandBufferCount = command_buffer_count;

        CommandBuffer[] cmd_buffer = new CommandBuffer[command_buffer_count];
        res = api.AllocateCommandBuffers(device, &cmd, cmd_buffer);
        Debug.Assert(res == Result.Success);
        return cmd_buffer;
    }

    static unsafe DepthBuffer createDepthBuffer(VulkanDevice device, int width, int height)
    {
        Result res;
        DepthBuffer depth;
        depth.format = Format.D24UnormS8Uint;

        Format[] depth_formats =
        [
            Format.D32SfloatS8Uint,
            Format.D24UnormS8Uint,
            Format.D16UnormS8Uint,
        ];
        int dformats = depth_formats.Length;

        ImageTiling image_tilling = default;
        for (int i = 0; i < dformats; i++)
        {
            FormatProperties fprops;
            api.GetPhysicalDeviceFormatProperties(device.gpu, depth_formats[i], &fprops);

            if (fprops.LinearTilingFeatures.HasFlag(FormatFeatureFlags.DepthStencilAttachmentBit))
            {
                depth.format = depth_formats[i];
                image_tilling = ImageTiling.Linear;
                break;
            }

            if (fprops.OptimalTilingFeatures.HasFlag(FormatFeatureFlags.DepthStencilAttachmentBit))
            {
                depth.format = depth_formats[i];
                image_tilling = ImageTiling.Optimal;
                break;
            }

            if (i == dformats - 1)
            {
                throw new Exception("Failed to find supported depth format!");
            }
        }

        Format depth_format = depth.format;

        ImageCreateInfo image_info = new ImageCreateInfo { SType = StructureType.ImageCreateInfo };
        image_info.ImageType = ImageType.Type2D;
        image_info.Format = depth_format;
        image_info.Tiling = image_tilling;
        image_info.Extent.Width = (uint)width;
        image_info.Extent.Height = (uint)height;
        image_info.Extent.Depth = 1;
        image_info.MipLevels = 1;
        image_info.ArrayLayers = 1;
        image_info.Samples = SampleCountFlags.Count1Bit;
        image_info.InitialLayout = ImageLayout.Undefined;
        image_info.QueueFamilyIndexCount = 0;
        image_info.PQueueFamilyIndices = null;
        image_info.SharingMode = SharingMode.Exclusive;
        image_info.Usage = ImageUsageFlags.DepthStencilAttachmentBit;
        MemoryAllocateInfo mem_alloc = new MemoryAllocateInfo { SType = StructureType.MemoryAllocateInfo };

        ImageViewCreateInfo view_info = new ImageViewCreateInfo { SType = StructureType.ImageViewCreateInfo };
        view_info.Format = depth_format;
        view_info.Components.R = ComponentSwizzle.R;
        view_info.Components.G = ComponentSwizzle.G;
        view_info.Components.B = ComponentSwizzle.B;
        view_info.Components.A = ComponentSwizzle.A;
        view_info.SubresourceRange.AspectMask = ImageAspectFlags.DepthBit;
        view_info.SubresourceRange.BaseMipLevel = 0;
        view_info.SubresourceRange.LevelCount = 1;
        view_info.SubresourceRange.BaseArrayLayer = 0;
        view_info.SubresourceRange.LayerCount = 1;
        view_info.ViewType = ImageViewType.Type2D;

        if (depth_format == Format.D16UnormS8Uint || depth_format == Format.D24UnormS8Uint ||
            depth_format == Format.D32SfloatS8Uint)
        {
            view_info.SubresourceRange.AspectMask |= ImageAspectFlags.StencilBit;
        }

        MemoryRequirements mem_reqs;

        /* Create image */
        res = api.CreateImage(device.device, &image_info, null, &depth.image);
        Debug.Assert(res == Result.Success);

        api.GetImageMemoryRequirements(device.device, depth.image, &mem_reqs);

        mem_alloc.AllocationSize = mem_reqs.Size;
        /* Use the memory properties to determine the type of memory required */

        bool pass = device.memoryProperties.GetMemoryType(mem_reqs.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit, out mem_alloc.MemoryTypeIndex) == Result.Success;
        Debug.Assert(pass);

        /* Allocate memory */
        res = api.AllocateMemory(device.device, &mem_alloc, null, &depth.mem);
        Debug.Assert(res == Result.Success);

        /* Bind memory */
        res = api.BindImageMemory(device.device, depth.image, depth.mem, 0);
        Debug.Assert(res == Result.Success);

        /* Create image view */
        view_info.Image = depth.image;
        res = api.CreateImageView(device.device, &view_info, null, &depth.view);
        Debug.Assert(res == Result.Success);

        return depth;
    }

    unsafe static void setupImageLayout(
        CommandBuffer cmdbuffer, Image image, ImageAspectFlags aspectMask,
        ImageLayout old_image_layout, ImageLayout new_image_layout
    )
    {
        ImageMemoryBarrier image_memory_barrier = new ImageMemoryBarrier { SType = StructureType.ImageMemoryBarrier };
        image_memory_barrier.OldLayout = old_image_layout;
        image_memory_barrier.NewLayout = new_image_layout;
        image_memory_barrier.Image = image;

        ImageSubresourceRange subresourceRange = new ImageSubresourceRange(aspectMask, 0, 1, 0, 1);
        image_memory_barrier.SubresourceRange = subresourceRange;

        if (new_image_layout == ImageLayout.TransferDstOptimal)
        {
            /* Make sure anything that was copying from this image has completed */
            image_memory_barrier.DstAccessMask = AccessFlags.TransferReadBit;
        }

        if (new_image_layout == ImageLayout.ColorAttachmentOptimal)
        {
            image_memory_barrier.DstAccessMask = AccessFlags.ColorAttachmentWriteBit;
        }

        if (new_image_layout == ImageLayout.DepthStencilAttachmentOptimal)
        {
            image_memory_barrier.DstAccessMask = AccessFlags.DepthStencilAttachmentWriteBit;
        }

        if (new_image_layout == ImageLayout.ShaderReadOnlyOptimal)
        {
            /* Make sure any Copy or CPU writes to image are flushed */
            image_memory_barrier.DstAccessMask = AccessFlags.ShaderReadBit | AccessFlags.InputAttachmentReadBit;
        }

        ImageMemoryBarrier* pmemory_barrier = &image_memory_barrier;

        PipelineStageFlags src_stages = PipelineStageFlags.TopOfPipeBit;
        PipelineStageFlags dest_stages = PipelineStageFlags.TopOfPipeBit;

        api.CmdPipelineBarrier(cmdbuffer, src_stages, dest_stages, 0, 0, null, 0, null, 1, pmemory_barrier);
    }

    static unsafe SwapchainBuffers createSwapchainBuffers(VulkanDevice device, Format format, CommandBuffer cmdbuffer, Image image)
    {
        Result res;
        SwapchainBuffers buffer;
        ImageViewCreateInfo color_attachment_view = new ImageViewCreateInfo { SType = StructureType.ImageViewCreateInfo };
        color_attachment_view.Format = format;
        color_attachment_view.Components.R = ComponentSwizzle.R;
        color_attachment_view.Components.G = ComponentSwizzle.G;
        color_attachment_view.Components.B = ComponentSwizzle.B;
        color_attachment_view.Components.A = ComponentSwizzle.A;
        ImageSubresourceRange subresourceRange = new ImageSubresourceRange();
        subresourceRange.AspectMask = ImageAspectFlags.ColorBit;
        subresourceRange.BaseMipLevel = 0;
        subresourceRange.LevelCount = 1;
        subresourceRange.BaseArrayLayer = 0;
        subresourceRange.LayerCount = 1;

        color_attachment_view.SubresourceRange = subresourceRange;
        color_attachment_view.ViewType = ImageViewType.Type2D;

        buffer.image = image;

        setupImageLayout(cmdbuffer, image, ImageAspectFlags.ColorBit, ImageLayout.Undefined,
            ImageLayout.PresentSrcKhr);

        color_attachment_view.Image = buffer.image;

        res = api.CreateImageView(device.device, &color_attachment_view, null, &buffer.view);
        Debug.Assert(res == Result.Success);
        return buffer;
    }

    static unsafe RenderPass createRenderPass(Device device, Format color_format, Format depth_format)
    {
        AttachmentDescription* attachments = stackalloc AttachmentDescription[2];
        attachments[0].Format = color_format;
        attachments[0].Samples = SampleCountFlags.Count1Bit;
        attachments[0].LoadOp = AttachmentLoadOp.Clear;
        attachments[0].StoreOp = AttachmentStoreOp.Store;
        attachments[0].StencilLoadOp = AttachmentLoadOp.Clear;
        attachments[0].StencilStoreOp = AttachmentStoreOp.DontCare;
        attachments[0].InitialLayout = ImageLayout.Undefined;
        attachments[0].FinalLayout = ImageLayout.ColorAttachmentOptimal;

        attachments[1].Format = depth_format;
        attachments[1].Samples = SampleCountFlags.Count1Bit;
        attachments[1].LoadOp = AttachmentLoadOp.Clear;
        attachments[1].StoreOp = AttachmentStoreOp.DontCare;
        attachments[1].StencilLoadOp = AttachmentLoadOp.Clear;
        attachments[1].StencilStoreOp = AttachmentStoreOp.DontCare;
        attachments[1].InitialLayout = ImageLayout.Undefined;
        attachments[1].FinalLayout = ImageLayout.DepthStencilAttachmentOptimal;

        AttachmentReference color_reference = new AttachmentReference();
        color_reference.Attachment = 0;
        color_reference.Layout = ImageLayout.ColorAttachmentOptimal;

        AttachmentReference depth_reference = new AttachmentReference();
        depth_reference.Attachment = 1;
        depth_reference.Layout = ImageLayout.DepthStencilAttachmentOptimal;

        SubpassDescription subpass = new SubpassDescription();
        subpass.PipelineBindPoint = PipelineBindPoint.Graphics;
        subpass.Flags = 0;
        subpass.InputAttachmentCount = 0;
        subpass.PInputAttachments = null;
        subpass.ColorAttachmentCount = 1;
        subpass.PColorAttachments = &color_reference;
        subpass.PResolveAttachments = null;
        subpass.PDepthStencilAttachment = &depth_reference;
        subpass.PreserveAttachmentCount = 0;
        subpass.PPreserveAttachments = null;

        RenderPassCreateInfo rp_info = new RenderPassCreateInfo { SType = StructureType.RenderPassCreateInfo };
        rp_info.AttachmentCount = 2;
        rp_info.PAttachments = attachments;
        rp_info.SubpassCount = 1;
        rp_info.PSubpasses = &subpass;
        RenderPass render_pass;
        Result res;
        res = api.CreateRenderPass(device, &rp_info, null, &render_pass);
        Debug.Assert(res == Result.Success);
        return render_pass;
    }

    static unsafe FrameBuffers createFrameBuffers(
        VulkanDevice device, SurfaceKHR surface, Queue queue, int winWidth,
        int winHeight, SwapchainKHR oldSwapchain
    )
    {
        Result res;

        Bool32 supportsPresent;
        khrSurface.GetPhysicalDeviceSurfaceSupport(device.gpu, device.graphicsQueueFamilyIndex, surface, &supportsPresent);
        if (!supportsPresent)
        {
            throw new Exception("does not supported.");
        }

        CommandBuffer[] setup_cmd_buffer = createCmdBuffer(device.device, device.commandPool, 1);

        CommandBufferBeginInfo cmd_buf_info = new CommandBufferBeginInfo()
        {
            SType = StructureType.CommandBufferBeginInfo,
        };
        api.BeginCommandBuffer(setup_cmd_buffer[0], &cmd_buf_info);

        Format colorFormat = Format.B8G8R8A8Unorm;
        ColorSpaceKHR colorSpace;
        {
            // Get the list of VkFormats that are supported:
            uint formatCount;
            res = khrSurface.GetPhysicalDeviceSurfaceFormats(device.gpu, surface, &formatCount, null);
            Debug.Assert(res == Result.Success);
            SurfaceFormatKHR* surfFormats = stackalloc SurfaceFormatKHR[(int)formatCount];
            res = khrSurface.GetPhysicalDeviceSurfaceFormats(device.gpu, surface, &formatCount, surfFormats);
            Debug.Assert(res == Result.Success);
            // If the format list includes just one entry of Format.Undefined,
            // the surface has no preferred format.  Otherwise, at least one
            // supported format will be returned.
            if (formatCount == 1 && surfFormats[0].Format == Format.Undefined)
            {
                colorFormat = Format.B8G8R8A8Unorm;
            }
            else
            {
                Debug.Assert(formatCount >= 1);
                colorFormat = surfFormats[0].Format;
            }

            colorSpace = surfFormats[0].ColorSpace;
        }
        colorFormat = Format.B8G8R8A8Unorm;

        // Check the surface capabilities and formats
        SurfaceCapabilitiesKHR surfCapabilities;
        res = khrSurface.GetPhysicalDeviceSurfaceCapabilities(device.gpu, surface, &surfCapabilities);
        Debug.Assert(res == Result.Success);

        Extent2D buffer_size;
        // width and height are either both -1, or both not -1.
        if (surfCapabilities.CurrentExtent.Width == uint.MaxValue)
        {
            buffer_size.Width = (uint)winWidth;
            buffer_size.Height = (uint)winHeight;
        }
        else
        {
            // If the surface size is defined, the swap chain size must match
            buffer_size = surfCapabilities.CurrentExtent;
        }

        DepthBuffer depth = createDepthBuffer(device, (int)buffer_size.Width, (int)buffer_size.Height);

        RenderPass render_pass = createRenderPass(device.device, colorFormat, depth.format);

        PresentModeKHR swapchainPresentMode = PresentModeKHR.FifoKhr;

        uint presentModeCount;
        khrSurface.GetPhysicalDeviceSurfacePresentModes(device.gpu, surface, &presentModeCount, null);
        Debug.Assert(presentModeCount > 0);

        PresentModeKHR* presentModes = stackalloc PresentModeKHR[(int)presentModeCount];
        khrSurface.GetPhysicalDeviceSurfacePresentModes(device.gpu, surface, &presentModeCount, presentModes);

        for (int i = 0; i < presentModeCount; i++)
        {
            if (presentModes[i] == PresentModeKHR.MailboxKhr)
            {
                swapchainPresentMode = PresentModeKHR.MailboxKhr;
                break;
            }

            if ((swapchainPresentMode != PresentModeKHR.MailboxKhr) && (presentModes[i] == PresentModeKHR.ImmediateKhr))
            {
                swapchainPresentMode = PresentModeKHR.ImmediateKhr;
            }
        }

        SurfaceTransformFlagsKHR preTransform;
        if (surfCapabilities.SupportedTransforms.HasFlag(SurfaceTransformFlagsKHR.IdentityBitKhr))
        {
            preTransform = SurfaceTransformFlagsKHR.IdentityBitKhr;
        }
        else
        {
            preTransform = surfCapabilities.CurrentTransform;
        }

        // Determine the number of Image's to use in the swap chain (we desire to
        // own only 1 image at a time, besides the images being displayed and
        // queued for display):
        uint desiredNumberOfSwapchainImages = Math.Max(surfCapabilities.MinImageCount + 1, 3);
        if (surfCapabilities.MaxImageCount > 0 && desiredNumberOfSwapchainImages > surfCapabilities.MaxImageCount)
        {
            // Application must settle for fewer images than desired:
            desiredNumberOfSwapchainImages = surfCapabilities.MaxImageCount;
        }

        SwapchainCreateInfoKHR swapchainInfo = new SwapchainCreateInfoKHR() { SType = StructureType.SwapchainCreateInfoKhr };
        swapchainInfo.Surface = surface;
        swapchainInfo.MinImageCount = desiredNumberOfSwapchainImages;
        swapchainInfo.ImageFormat = colorFormat;
        swapchainInfo.ImageColorSpace = colorSpace;
        swapchainInfo.ImageExtent = buffer_size;
        swapchainInfo.ImageUsage = ImageUsageFlags.ColorAttachmentBit;
        swapchainInfo.PreTransform = preTransform;
        swapchainInfo.CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr;
        swapchainInfo.ImageArrayLayers = 1;
        swapchainInfo.ImageSharingMode = SharingMode.Exclusive;
        swapchainInfo.PresentMode = swapchainPresentMode;
        swapchainInfo.OldSwapchain = oldSwapchain;
        swapchainInfo.Clipped = true;

        SwapchainKHR swap_chain;
        res = KhrSwapchain.CreateSwapchain(device.device, &swapchainInfo, null, &swap_chain);
        Debug.Assert(res == Result.Success);

        if (oldSwapchain.Handle != 0)
        {
            KhrSwapchain.DestroySwapchain(device.device, oldSwapchain, null);
        }

        uint swapchain_image_count;
        res = KhrSwapchain.GetSwapchainImages(device.device, swap_chain, &swapchain_image_count, null);
        Debug.Assert(res == Result.Success);

        Image* swapchainImages = stackalloc Image[(int)swapchain_image_count];

        res = KhrSwapchain.GetSwapchainImages(device.device, swap_chain, &swapchain_image_count, swapchainImages);
        Debug.Assert(res == Result.Success);

        SwapchainBuffers[] swap_chain_buffers = new SwapchainBuffers[swapchain_image_count];
        for (uint i = 0; i < swapchain_image_count; i++)
        {
            swap_chain_buffers[i] = createSwapchainBuffers(device, colorFormat, setup_cmd_buffer[0], swapchainImages[i]);
        }

        ImageView* attachments = stackalloc ImageView[2];
        attachments[1] = depth.view;

        FramebufferCreateInfo fb_info = new FramebufferCreateInfo { SType = StructureType.FramebufferCreateInfo };
        fb_info.RenderPass = render_pass;
        fb_info.AttachmentCount = 2;
        fb_info.PAttachments = attachments;
        fb_info.Width = buffer_size.Width;
        fb_info.Height = buffer_size.Height;
        fb_info.Layers = 1;
        uint i2;


        Framebuffer[] framebuffers = new Framebuffer[swapchain_image_count];
        
        for (i2 = 0; i2 < swapchain_image_count; i2++)
        {
            attachments[0] = swap_chain_buffers[i2].view;
            res = api.CreateFramebuffer(device.device, &fb_info, null, out framebuffers[i2]);
            Debug.Assert(res == Result.Success);
        }

        api.EndCommandBuffer(setup_cmd_buffer[0]);
        fixed (CommandBuffer* setupCmdBufferPtr = &setup_cmd_buffer[0])
        {
            SubmitInfo submitInfo = new SubmitInfo { SType = StructureType.SubmitInfo };
            submitInfo.CommandBufferCount = 1;
            submitInfo.PCommandBuffers = setupCmdBufferPtr;

            api.QueueSubmit(queue, 1, &submitInfo, default);
        }

        api.QueueWaitIdle(queue);

        api.FreeCommandBuffers(device.device, device.commandPool, 1, setup_cmd_buffer);
        
        FrameBuffers buffer = new FrameBuffers();
        buffer.swap_chain = swap_chain;
        buffer.swap_chain_buffers = swap_chain_buffers;
        buffer.swapchain_image_count = swapchain_image_count;
        buffer.framebuffers = framebuffers;
        buffer.current_buffer = 0;
        buffer.format = colorFormat;
        buffer.buffer_size = buffer_size;
        buffer.render_pass = render_pass;
        buffer.depth = depth;
        buffer.present_complete_semaphore = new Semaphore[swapchain_image_count];
        buffer.render_complete_semaphore = new Semaphore[swapchain_image_count];
        buffer.flight_fence = new Fence[swapchain_image_count];

        SemaphoreCreateInfo presentCompleteSemaphoreCreateInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
        FenceCreateInfo fenceCreateInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo, Flags = FenceCreateFlags.SignaledBit};
        for (i2 = 0; i2 < swapchain_image_count; i2++)
        {
            res = api.CreateSemaphore(device.device, &presentCompleteSemaphoreCreateInfo, null,
                out buffer.present_complete_semaphore[i2]);
            Debug.Assert(res == Result.Success);

            res = api.CreateSemaphore(device.device, &presentCompleteSemaphoreCreateInfo, null,
                out buffer.render_complete_semaphore[i2]);
            Debug.Assert(res == Result.Success);

            res = api.CreateFence(device.device, &fenceCreateInfo, null, out buffer.flight_fence[i2]);
            Debug.Assert(res == Result.Success);
        }

        return buffer;
    }

    static unsafe void destroyFrameBuffers(VulkanDevice device, FrameBuffers buffer, Queue queue)
    {
        Result res = api.QueueWaitIdle(queue);
        Debug.Assert(res == Result.Success);

        for (uint i = 0; i < buffer.swapchain_image_count; ++i)
        {
            if (buffer.present_complete_semaphore[i].Handle != 0)
            {
                api.DestroySemaphore(device.device, buffer.present_complete_semaphore[i], null);
            }

            if (buffer.render_complete_semaphore[i].Handle != 0)
            {
                api.DestroySemaphore(device.device, buffer.render_complete_semaphore[i], null);
            }

            if (buffer.flight_fence[i].Handle != 0)
            {
                api.DestroyFence(device.device, buffer.flight_fence[i], null);
            }
        }

        for (uint i = 0; i < buffer.swapchain_image_count; ++i)
        {
            api.DestroyImageView(device.device, buffer.swap_chain_buffers[i].view, null);
            api.DestroyFramebuffer(device.device, buffer.framebuffers[i], null);
        }

        api.DestroyImageView(device.device, buffer.depth.view, null);
        api.DestroyImage(device.device, buffer.depth.image, null);
        api.FreeMemory(device.device, buffer.depth.mem, null);

        api.DestroyRenderPass(device.device, buffer.render_pass, null);
        KhrSwapchain.DestroySwapchain(device.device, buffer.swap_chain, null);

        buffer.framebuffers = null;
        buffer.swap_chain_buffers = null;
        buffer.present_complete_semaphore = null;
        buffer.render_complete_semaphore = null;
        buffer.flight_fence = null;
    }
}