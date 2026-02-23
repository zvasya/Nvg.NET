using System.Buffers;
using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Collections.Pooled;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;

using SilkyNvg.Blending;
using SilkyNvg.Images;

namespace SilkyNvg.Rendering.Vulkan;

public class VkNvgRenderer(VkNvgContext createInfo, CreateFlags flags, Queue queue) : INvgRenderer
{
    bool _disposed;

    VkNvgShaderModule? _fillFragShader;
    VkNvgShaderModule? _fillVertShader;
    
    PhysicalDeviceProperties _gpuProperties;
    PhysicalDeviceMemoryProperties _memoryProperties;


    readonly DescriptorSetLayout[] _descLayout = new DescriptorSetLayout[2];
    PipelineLayout _pipelineLayout;
    VkNvgBuffer?[]? _vertexBuffer;
    VkNvgBuffer[]? _fragUniformBuffer;
    Pipeline? _currentPipeline;

    readonly VkNvgList<VkNvgTexture?> _textures = new VkNvgList<VkNvgTexture?>(4);

    DescriptorPool _descPool;
    DescriptorSet[][]? _uniformDescriptorSet;
    DescriptorSet[]? _ssboDescriptorSet;

    readonly VkNvgList<VkNvgFragUniforms> _uniforms = new VkNvgList<VkNvgFragUniforms>(128);
    
    readonly List<VkNvgCall> _calls = new List<VkNvgCall>(128);
    readonly VkNvgList<VkNvgPath> _paths = new VkNvgList<VkNvgPath>(128);
    readonly VkNvgList<Vertex> _verts = new VkNvgList<Vertex>(4096);
    

    readonly Dictionary<VkNvgCreatePipelineKey, Pipeline> _pipelines =
	    new Dictionary<VkNvgCreatePipelineKey, Pipeline>(128);

    VkNvgVertexConstants _vertexConstants;

    private uint CurrentFrame => createInfo.CurrentFrameProvider();
    private List<VkNvgTexture>[]? _texturesForRemove;
    private int _texturesForRemoveCurrentIndex;

    public void Dispose()
    {
        if (_disposed)
            return;

        Vk api = createInfo.Api;

        Device device = createInfo.Device;
        ref AllocationCallbacks allocator = ref createInfo.Allocator;

        var texturesData = _textures.Data;
        for (int i = 0; i < _textures.Length; i++)
        {
            if (texturesData[i]?.Image.Handle != 0)
            {
                texturesData[i]?.Dispose();
            }
        }

        if (_texturesForRemove != null)
        {
	        foreach (List<VkNvgTexture> texturesList in _texturesForRemove)
	        {
		        foreach (VkNvgTexture texture in texturesList)
		        {
			        texture.Dispose();
		        }
	        }
        }

        for (int i = 0; i < createInfo.SwapchainImageCount; i++)
        {
            _vertexBuffer?[i]?.Dispose();
            _fragUniformBuffer?[i].Dispose();
        }

        _fillVertShader?.Dispose();
        _fillFragShader?.Dispose();

        api.DestroyDescriptorPool(device, _descPool, allocator);
        api.DestroyDescriptorSetLayout(device, _descLayout[0], allocator);
        api.DestroyDescriptorSetLayout(device, _descLayout[1], allocator);
        api.DestroyPipelineLayout(device, _pipelineLayout, allocator);

        foreach ((VkNvgCreatePipelineKey key, Pipeline pipeline) in _pipelines)
        { 
            api.DestroyPipeline(device, pipeline, allocator);
        }
        _pipelines.Clear();

        _disposed = true;
    }


    public bool EdgeAntiAlias => (flags & CreateFlags.Antialias) == CreateFlags.Antialias;
    public unsafe bool Create()
    {
        Vk api = createInfo.Api;
        Device device = createInfo.Device;
        ref AllocationCallbacks allocator = ref createInfo.Allocator;

        var physicalDeviceMemoryProperties = _memoryProperties;
        api.GetPhysicalDeviceMemoryProperties(createInfo.Gpu, &physicalDeviceMemoryProperties);
        _memoryProperties = physicalDeviceMemoryProperties;
        api.GetPhysicalDeviceProperties(createInfo.Gpu, out _gpuProperties);
        byte[] fillVertShader = File.ReadAllBytes("fillVert.spv");
        

        byte[] fillFragShader = File.ReadAllBytes("fillFrag.spv");


        fixed (byte* pFillVertShader = &fillVertShader[0])
        {
            _fillVertShader = VkNvgShaderModule.Create(createInfo, pFillVertShader, (UIntPtr)(fillVertShader.Length), allocator);
        }

        fixed (byte* pFillFragShader = &fillFragShader[0])
        {
            _fillFragShader = VkNvgShaderModule.Create(createInfo, pFillFragShader, (UIntPtr)(fillFragShader.Length), allocator);
        }
        ulong align = _gpuProperties.Limits.MinUniformBufferOffsetAlignment;

        fixed (DescriptorSetLayout* ptr = _descLayout)
        {
            CreateDescriptorSetLayout(ptr);
        }

        _pipelineLayout = CreatePipelineLayout();

        PhysicalDeviceFeatures supportedFeatures;
        api.GetPhysicalDeviceFeatures(createInfo.Gpu, &supportedFeatures);
        
        _descPool = CreateDescriptorPool(1000);
        
        uint maxFramesInFlight = createInfo.SwapchainImageCount;
        _vertexBuffer = new VkNvgBuffer[maxFramesInFlight];
        _fragUniformBuffer = new VkNvgBuffer[maxFramesInFlight];
        
        InitSsboDescriptionSet(api, device);
        _uniformDescriptorSet = [new DescriptorSet[16], new DescriptorSet[16], new DescriptorSet[16]];

        _texturesForRemove = new List<VkNvgTexture>[maxFramesInFlight];
        for (int i = 0; i < maxFramesInFlight; i++)
	        _texturesForRemove[i] = new List<VkNvgTexture>();

        CreateTexture(Texture.Rgba, new Size(2, 2), 0, Span<byte>.Empty);
        return true;
    }

    public unsafe int CreateTexture(Texture type, Size size, ImageFlags imageFlags, ReadOnlySpan<byte> data)
    {
        Vk api = createInfo.Api;
        VkNvgTexture tex = AllocTexture();

        Device device = createInfo.Device;
        ref AllocationCallbacks allocator = ref createInfo.Allocator;

        ImageCreateInfo imageCreateInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            PNext = null,
            ImageType = ImageType.Type2D,
            Format = type == Texture.Rgba ? Format.R8G8B8A8Unorm : Format.R8Unorm,
            Extent = new Extent3D
            {
                Width = (uint)size.Width,
                Height = (uint)size.Height,
                Depth = 1,
            },
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Linear,
            InitialLayout = ImageLayout.Preinitialized,
            Usage = ImageUsageFlags.SampledBit,
            QueueFamilyIndexCount = 0,
            PQueueFamilyIndices = null,
            SharingMode = SharingMode.Exclusive,
            Flags = 0,
        };

        MemoryAllocateInfo memAlloc = new MemoryAllocateInfo
        {
	        SType = StructureType.MemoryAllocateInfo,
	        AllocationSize = 0,
        };

        Image mappableImage;
        DeviceMemory mappableMemory;

        var createImageResult = api.CreateImage(device, &imageCreateInfo, allocator, &mappableImage);
        DebugUtils.Check(createImageResult);

        MemoryRequirements memReqs;
        api.GetImageMemoryRequirements(device, mappableImage, &memReqs);

        memAlloc.AllocationSize = memReqs.Size;

        MemoryPropertyFlags memoryPropertyFlags = MemoryPropertyFlags.DeviceLocalBit | MemoryPropertyFlags.HostVisibleBit;
        Result res = _memoryProperties.GetMemoryType(memReqs.MemoryTypeBits, memoryPropertyFlags, out memAlloc.MemoryTypeIndex);
        DebugUtils.Check(res);

        var allocateMemoryResult = api.AllocateMemory(device, &memAlloc, allocator, &mappableMemory);
        DebugUtils.Check(allocateMemoryResult);

        var bindImageMemoryResult = api.BindImageMemory(device, mappableImage, mappableMemory, 0);
        DebugUtils.Check(bindImageMemoryResult);

        SamplerCreateInfo samplerCreateInfo = new SamplerCreateInfo { SType = StructureType.SamplerCreateInfo };
        if ((imageFlags & ImageFlags.Nearest) == ImageFlags.Nearest)
        {
            samplerCreateInfo.MagFilter = Filter.Nearest;
            samplerCreateInfo.MinFilter = Filter.Nearest;
        }
        else
        {
            samplerCreateInfo.MagFilter = Filter.Linear;
            samplerCreateInfo.MinFilter = Filter.Linear;
        }

        samplerCreateInfo.MipmapMode = SamplerMipmapMode.Nearest;
        if ((imageFlags & ImageFlags.RepeatX) == ImageFlags.RepeatX)
        {
            samplerCreateInfo.AddressModeU = SamplerAddressMode.MirroredRepeat;
            samplerCreateInfo.AddressModeV = SamplerAddressMode.MirroredRepeat;
            samplerCreateInfo.AddressModeW = SamplerAddressMode.MirroredRepeat;
        }
        else
        {
            samplerCreateInfo.AddressModeU = SamplerAddressMode.ClampToEdge;
            samplerCreateInfo.AddressModeV = SamplerAddressMode.ClampToEdge;
            samplerCreateInfo.AddressModeW = SamplerAddressMode.ClampToEdge;
        }

        samplerCreateInfo.MipLodBias = 0.0f;
        samplerCreateInfo.AnisotropyEnable = false;
        samplerCreateInfo.MaxAnisotropy = 1;
        samplerCreateInfo.CompareEnable = false;
        samplerCreateInfo.CompareOp = CompareOp.Never;
        samplerCreateInfo.MinLod = 0.0f;
        samplerCreateInfo.MaxLod = Vk.LodClampNone;
        samplerCreateInfo.BorderColor = BorderColor.FloatOpaqueWhite;

        /* create sampler */
        var samplerResult = api.CreateSampler(device, &samplerCreateInfo, allocator, out tex.Sampler);
        DebugUtils.Check(samplerResult);

        ImageViewCreateInfo viewInfo = new ImageViewCreateInfo { SType = StructureType.ImageViewCreateInfo,
	        PNext = null,
	        Image = mappableImage,
	        ViewType = ImageViewType.Type2D,
	        Format = imageCreateInfo.Format,
	        Components = new ComponentMapping
	        {
		        R = ComponentSwizzle.R,
		        G = ComponentSwizzle.G,
		        B = ComponentSwizzle.B,
		        A = ComponentSwizzle.A
	        },
	        SubresourceRange = new ImageSubresourceRange()
	        {
		        AspectMask = ImageAspectFlags.ColorBit,
		        BaseMipLevel = 0,
		        LevelCount = 1,
		        BaseArrayLayer = 0,
		        LayerCount = 1,
	        },
        };

        ImageView imageView;
        var imageViewResult = api.CreateImageView(device, &viewInfo, allocator, &imageView);
        DebugUtils.Check(imageViewResult);

        tex.Height = size.Height;
        tex.Width = size.Width;
        tex.Image = mappableImage;
        tex.View = imageView;
        tex.Mem = mappableMemory;
        tex.ImageLayout = ImageLayout.ShaderReadOnlyOptimal;
        tex.Type = type;
        tex.Flags = imageFlags;
        if (data.Length > 0)
        {
            tex.UpdateTexture(0, 0, size.Width, size.Height, data);
        }
        else
        {
            int txFormat = 1;
            if (type == Texture.Rgba)
                txFormat = 4;
            int textureSize = size.Width * size.Height * txFormat * sizeof(byte);


            byte[] generatedTexture = ArrayPool<byte>.Shared.Rent(textureSize);

            for (uint i = 0; i < (uint)size.Width; ++i)
            {
                for (uint j = 0; j < (uint)size.Height; ++j)
                {
                    var pixel = (i + j * size.Width) * txFormat * sizeof(byte);
                    if (type == Texture.Rgba)
                    {
                        generatedTexture[pixel + 0] = 0x00;
                        generatedTexture[pixel + 1] = 0x00;
                        generatedTexture[pixel + 2] = 0x00;
                        generatedTexture[pixel + 3] = 0x00;
                    }
                    else
                        generatedTexture[pixel + 0] = 0x00;
                }
            }

            tex.UpdateTexture(0, 0, size.Width, size.Height, generatedTexture.AsSpan(0, textureSize));

            ArrayPool<byte>.Shared.Return(generatedTexture);
        }

        uint currentFrame = CurrentFrame;
        tex.InitTexture(api, createInfo.CmdBuffer[currentFrame], queue);

        return GetTextureId(tex);
    }

    public bool DeleteTexture(int image)
    {
        VkNvgTexture? tex = FindTexture(image);

        if (tex == null)
            return false;

        _textures.Data[image - 1] = null;
        
        _texturesForRemove![_texturesForRemoveCurrentIndex].Add(tex);
        return true;
    }

    public bool UpdateTexture(int image, Rectangle bounds, ReadOnlySpan<byte> data)
    {
        VkNvgTexture tex = FindTexture(image)!;
        tex.UpdateTexture(bounds.X, bounds.Y, bounds.Width, bounds.Height, data);
        return true;
    }

    public bool GetTextureSize(int image, out Size size)
    {
        size = new Size();
        int r;
        VkNvgTexture? tex = FindTexture(image);
        if (tex != null)
        {
            size.Width = tex.Width;
            size.Height = tex.Height;
            return true;
        }

        return false;
    }

    public void Viewport(SizeF size, float devicePixelRatio)
    {
        _vertexConstants.ViewSize = new Vector2(size.Width, size.Height);
    }

    public void Cancel()
    {
        _verts.Clear();
        _paths.Clear();
        _calls.Clear();
        _uniforms.Clear();
    }

    public unsafe void Flush()
    {
        Vk api = createInfo.Api;
        Device device = createInfo.Device;
        uint currentFrame = CurrentFrame;
        PhysicalDeviceMemoryProperties memoryProperties = _memoryProperties;
        ref AllocationCallbacks allocator = ref createInfo.Allocator;

        if (_calls.Count > 0)
        {
            int i;
            MemoryPropertyFlags flags = MemoryPropertyFlags.DeviceLocalBit | MemoryPropertyFlags.HostVisibleBit;

            ReadOnlySpan<byte> vertsData = MemoryMarshal.AsBytes(_verts.Data);
            VkNvgBuffer.Update(createInfo, ref _vertexBuffer[currentFrame], memoryProperties, BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit, flags, vertsData);

            ReadOnlySpan<byte> uniformsData =  MemoryMarshal.AsBytes(_uniforms.Data);
            VkNvgBuffer.Update(createInfo, ref _fragUniformBuffer![currentFrame], memoryProperties, BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit, flags, uniformsData);

            var offsets = stackalloc ulong[] { 0 };
            api.CmdBindVertexBuffers(createInfo.CmdBuffer[currentFrame], 0, 1, _vertexBuffer![currentFrame]!.Buffer, offsets);

            _currentPipeline = null;
            
            UpdateUniformDescriptorSets(api, device, currentFrame);

            UpdateSsboDescriptionSet(currentFrame, uniformsData, api, device);

            RemoveTextures();

            CommandBuffer cmdBuffer = createInfo.CmdBuffer[currentFrame];
            DescriptorSet ssboDescriptorSet = _ssboDescriptorSet![currentFrame];
	        DescriptorSet[] uniformDescriptor = _uniformDescriptorSet[currentFrame];
            for (i = 0; i < _calls.Count; i++)
            {
	            VkNvgCall call = _calls[i];
	            switch (call.Type)
	            {
		            case CallType.Fill:
			            FillInternal(call, cmdBuffer, ssboDescriptorSet, uniformDescriptor);
			            break;
		            case CallType.ConvexFill:
			            ConvexFillInternal(call, cmdBuffer, ssboDescriptorSet, uniformDescriptor);
			            break;
		            case CallType.Stroke:
			            StrokeInternal(call, cmdBuffer, ssboDescriptorSet, uniformDescriptor);
			            break;
		            case CallType.Triangles:
			            TrianglesInternal(call, cmdBuffer, ssboDescriptorSet, uniformDescriptor);
			            break;
		            case CallType.None:
			            break;
		            default:
			            throw new ArgumentOutOfRangeException();
	            }
            }
        }

        // Reset calls
        _verts.Clear();
        _paths.Clear();
        _calls.Clear();
        _uniforms.Clear();
    }

    private void RemoveTextures()
    {
	    _texturesForRemoveCurrentIndex = (_texturesForRemoveCurrentIndex + 1) % _texturesForRemove!.Length;
	    var list = _texturesForRemove![_texturesForRemoveCurrentIndex];
	    foreach (VkNvgTexture texture in list)
	    {
		    texture.Dispose();
	    }
	    list.Clear();
    }

    private unsafe void UpdateUniformDescriptorSets(Vk api, Device device, uint currentFrame)
    {
	    api.FreeDescriptorSets(device, _descPool, _uniformDescriptorSet[currentFrame]);
            
	    {
		    Span<DescriptorSetLayout> descLayout = stackalloc DescriptorSetLayout[_textures.Length];
		    descLayout.Fill(_descLayout[1]);
		    fixed (DescriptorSetLayout* descSet = &descLayout[0])
		    {
			    Array.Resize(ref _uniformDescriptorSet[currentFrame], _textures.Length);
			    DescriptorSetAllocateInfo allocInfo1 = new DescriptorSetAllocateInfo(StructureType.DescriptorSetAllocateInfo, null, _descPool, (uint)_textures.Length, descSet);
			    var allocateDescriptorSetsResult = api.AllocateDescriptorSets(device, &allocInfo1, _uniformDescriptorSet[currentFrame]);
			    DebugUtils.Check(allocateDescriptorSetsResult);
		    }
	    }
            
	    for (int j = 0; j < _textures.Data.Length; j++)
	    {
		    VkNvgTexture tex = _textures.Data[j];
		    DescriptorSet descSet = _uniformDescriptorSet[currentFrame][j];
		    if (tex != null) 
			    UpdateTextureDescriptorSet(descSet, tex!, api, device);
	    }
    }

    private unsafe void UpdateSsboDescriptionSet(uint currentFrame, ReadOnlySpan<byte> uniformsData, Vk api, Device device)
    {
	    DescriptorBufferInfo bufferInfo = new DescriptorBufferInfo
	    {
		    Buffer = _fragUniformBuffer![currentFrame].Buffer,
		    Offset = 0,
		    Range = (ulong)uniformsData.Length,
	    };

	    WriteDescriptorSet writeFragData = new WriteDescriptorSet { SType = StructureType.WriteDescriptorSet,
		    DstSet = _ssboDescriptorSet![currentFrame],
		    DescriptorCount = 1,
		    DescriptorType = DescriptorType.StorageBuffer,
		    PBufferInfo = &bufferInfo,
		    DstBinding = 0,
	    };
	    api.UpdateDescriptorSets(device, 1, &writeFragData, 0, null);
    }

    private unsafe void InitSsboDescriptionSet(Vk api, Device device)
    {
	    int i;
	    _ssboDescriptorSet = new DescriptorSet[createInfo.SwapchainImageCount];


	    fixed (DescriptorSetLayout* descLayout = &_descLayout[0])
	    fixed (DescriptorSet* ssboDescriptorSetPtr = &_ssboDescriptorSet[0])
	    {
		    DescriptorSetAllocateInfo allocInfo0 = new DescriptorSetAllocateInfo(StructureType.DescriptorSetAllocateInfo, null, _descPool, 1, descLayout);
		    for (i = 0; i < createInfo.SwapchainImageCount; i++)
		    {
			    DescriptorSet* ptr = (DescriptorSet*)Unsafe.Add<DescriptorSet>(ssboDescriptorSetPtr, i);
			    var allocateDescriptorSetsResult = api.AllocateDescriptorSets(device, &allocInfo0, ptr);
			    DebugUtils.Check(allocateDescriptorSetsResult);
		    }
	    }
    }

    public void Fill(Paint paint, CompositeOperationState compositeOperation, Scissor scissor, float fringe, RectangleF bounds, ReadOnlySpan<Path> paths1)
    {
        int i, maxverts, offset;

        var type = CallType.Fill;
        var triangleCount = 4;
        var pathOffset = _paths.EnsureCapacity(paths1.Length);
        if (pathOffset == -1)
        {
            return;
        }

        var pathCount = paths1.Length;
        var image = paint.Image;

        if (paths1.Length == 1 && paths1[0].Convex)
        {
            type = CallType.ConvexFill;
            triangleCount = 0; // Bounding box fill quad not needed for convex fill
        }

        // Allocate vertices for all the paths.
#if !USE_TOPOLOGY_TRIANGLE_FAN
        maxverts = MaxVertCountList(paths1) + triangleCount;
#else
        maxverts = MaxVertCount(paths1) + triangleCount;
#endif
        offset = _verts.EnsureCapacity(maxverts);
        if (offset == -1)
        {
            return;
        }

        var vertsData = _verts.Data;
        for (i = 0; i < paths1.Length; i++)
        {
            ref VkNvgPath copy = ref _paths.Data[pathOffset + i];
            Path path = paths1[i];
            copy = new VkNvgPath();
            if (path.FillCount > 0)
            {
	            var pathFill = path.Fill;
                copy.FillOffset = (uint)offset;
#if !USE_TOPOLOGY_TRIANGLE_FAN
                copy.FillCount = (pathFill.Length - 2) * 3;
                int j;
                for (j = 0; j < pathFill.Length - 2; j++)
                {
	                vertsData[offset] = pathFill[0];
                    vertsData[offset + 1] = pathFill[j + 1];
                    vertsData[offset + 2] = pathFill[j + 2];
                    offset += 3;
                }
#else
				copy.FillCount = path.FillCount;
				((List<Vertex>)path.Fill).AsSpan(0, path.FillCount).CopyTo(vertsData.Slice( offset, path.FillCount));
				// memcpy(&vk.verts[offset], path.Fill, sizeof(Vertex) * path.FillCount);
				offset += path.FillCount;
#endif
            }

            if (path.StrokeCount > 0)
            {
                copy.StrokeOffset = offset;
                copy.StrokeCount = path.StrokeCount;
                path.Stroke.CopyTo(vertsData.Slice(offset, path.StrokeCount));
                offset += path.StrokeCount;
            }
        }

        // Setup uniforms for draw calls
        int uniformOffset;
        int triangleOffset = 0;
        if (type == CallType.Fill)
        {
            // Quad
            triangleOffset = offset;
            vertsData[triangleOffset] = new Vertex(bounds.Right, bounds.Bottom, 0.5f, 1.0f);
            vertsData[triangleOffset + 1] = new Vertex(bounds.Right, bounds.Top, 0.5f, 1.0f);
            vertsData[triangleOffset + 2] = new Vertex(bounds.Left, bounds.Bottom, 0.5f, 1.0f);
            vertsData[triangleOffset + 3] = new Vertex(bounds.Left, bounds.Top, 0.5f, 1.0f);

            uniformOffset = _uniforms.EnsureCapacity(2);
            if (uniformOffset == -1)
            {
                return;
            }

            // Simple shader for stencil
            ref VkNvgFragUniforms frag = ref _uniforms.Data[uniformOffset];
            frag = new VkNvgFragUniforms();
            frag.StrokeThr = -1.0f;
            frag.Type = ShaderType.Simple;
            // Fill shader
            ConvertPaint(out _uniforms.Data[uniformOffset + 1], paint, scissor, fringe, fringe, -1.0f);
        }
        else
        {
            uniformOffset = _uniforms.EnsureCapacity(1);
            if (uniformOffset == -1)
            {
                return;
            }

            // Fill shader
            ConvertPaint(out _uniforms.Data[uniformOffset], paint, scissor, fringe, fringe, -1.0f);
        }

        VkNvgCall call = new VkNvgCall(type, image, pathOffset, pathCount, triangleOffset, triangleCount, uniformOffset, compositeOperation);
        _calls.Add(call);
    }

    public void Stroke(Paint paint, CompositeOperationState compositeOperation, Scissor scissor, float fringe, float strokeWidth, ReadOnlySpan<Path> paths1)
    {
        var pathCount = paths1.Length;
        int i;

        var pathOffset = _paths.EnsureCapacity(pathCount);
        if (pathOffset == -1)
        {
            return;
        }

        var image = paint.Image;

        // Allocate vertices for all the paths.
        var maxVerts = MaxVertCount(paths1);
        var offset = _verts.EnsureCapacity(maxVerts);
        if (offset == -1)
        {
            return;
        }

        for (i = 0; i < pathCount; i++)
        {
            ref VkNvgPath copy = ref _paths.Data[pathOffset + i];
            Path path = paths1[i];
            copy = new VkNvgPath();
            if (path.StrokeCount > 0)
            {
                copy.StrokeOffset = offset;
                copy.StrokeCount = path.StrokeCount;
                path.Stroke.CopyTo(_verts.Data.Slice(offset, path.StrokeCount));
                offset += path.StrokeCount;
            }
        }

        int uniformOffset;
        if ((flags & CreateFlags.StencilStrokes) == CreateFlags.StencilStrokes)
        {
            // Fill shader
            uniformOffset = _uniforms.EnsureCapacity(2);
            if (uniformOffset == -1)
            {
                return;
            }

            ConvertPaint(out _uniforms.Data[uniformOffset], paint, scissor, strokeWidth, fringe, -1.0f);
            int i1 = uniformOffset + 1;
            ConvertPaint(out _uniforms.Data[i1], paint, scissor, strokeWidth, fringe, 1.0f - 0.5f / 255.0f);
        }
        else
        {
            // Fill shader
            uniformOffset = _uniforms.EnsureCapacity(1);
            if (uniformOffset == -1)
            {
                return;
            }

            ConvertPaint(out _uniforms.Data[uniformOffset], paint, scissor, strokeWidth, fringe, -1.0f);
        }

        VkNvgCall call = new VkNvgCall(CallType.Stroke, image, pathOffset, pathCount, 0, 0, uniformOffset, compositeOperation);
        _calls.Add(call);
    }

    public void Triangles(Paint paint, CompositeOperationState compositeOperation, Scissor scissor, ReadOnlySpan<Vertex> vertices, float fringeWidth)
    {
	    int nverts = vertices.Length;

        var image = paint.Image;

        // Allocate vertices for all the paths.
        var triangleOffset = _verts.EnsureCapacity(nverts);
        if (triangleOffset == -1)
        {
            return;
        }

        vertices.CopyTo(_verts.Data.Slice(triangleOffset, nverts));
        
        // Fill shader
        var uniformOffset = _uniforms.EnsureCapacity(1);
        if (uniformOffset == -1)
        {
            return;
        }

        ref VkNvgFragUniforms frag = ref _uniforms.Data[uniformOffset];
        ConvertPaint(out frag, paint, scissor, 1.0f, fringeWidth, -1.0f);
        frag.Type = ShaderType.Image;

        VkNvgCall call = new VkNvgCall(CallType.Triangles, image, 0, 0, triangleOffset, nverts, uniformOffset, compositeOperation);
        _calls.Add(call);
    }

    unsafe void FillInternal(in VkNvgCall call, CommandBuffer cmdBuffer, DescriptorSet ssboDescriptorSet, DescriptorSet[] uniformDescriptor)
    {
        Vk api = createInfo.Api;

        Span<VkNvgPath> paths = _paths.Data.Slice(call.PathOffset, call.PathCount);
        int npaths = paths.Length;

        var compositeOperation = call.CompositeOperation;
#if !USE_TOPOLOGY_TRIANGLE_FAN
        var topology = PrimitiveTopology.TriangleList;
#else
        var topology = PrimitiveTopology.TriangleFan;
#endif
        var stencilFill = true;

        VkNvgCreatePipelineKey pipelineKey = new VkNvgCreatePipelineKey(
            createInfo,
            compositeOperation: compositeOperation,
            topology: topology,
            stencilFill: stencilFill
        );

        BindPipeline(cmdBuffer, ref pipelineKey);
        SetDynamicState(cmdBuffer, ref pipelineKey);
        SetUniforms(cmdBuffer, call.UniformOffset);
        DescriptorSet* sets = stackalloc DescriptorSet[2] { ssboDescriptorSet, uniformDescriptor[call.Image != 0 ? call.Image - 1 : 0] };
        api.CmdBindDescriptorSets(cmdBuffer, PipelineBindPoint.Graphics, _pipelineLayout, 0, 2, sets, 0, null);

        for (int i = 0; i < npaths; i++)
        {
            api.CmdDraw(cmdBuffer, (uint)paths[i].FillCount, 1, paths[i].FillOffset, 0);
        }

        SetUniforms( cmdBuffer, call.UniformOffset + 1);
        api.CmdBindDescriptorSets(cmdBuffer, PipelineBindPoint.Graphics, _pipelineLayout, 0, 2, sets, 0, null);

        if (EdgeAntiAlias)
        {
            pipelineKey = pipelineKey.With
            (
                compositeOperation: call.CompositeOperation,
                topology: PrimitiveTopology.TriangleStrip,
                stencilFill: false,
                stencilTest: true,
                edgeAa: true
            );
            BindPipeline(cmdBuffer, ref pipelineKey);
            SetDynamicState(cmdBuffer, ref pipelineKey);
            // Draw fringes
            for (int i = 0; i < npaths; ++i)
            {
                api.CmdDraw(cmdBuffer, (uint)paths[i].StrokeCount, 1, (uint)paths[i].StrokeOffset, 0);
            }
        }

        pipelineKey = pipelineKey.With(
            compositeOperation: call.CompositeOperation,
            topology: PrimitiveTopology.TriangleStrip,
            stencilFill: false,
            stencilTest: true,
            edgeAa: false
        );
        BindPipeline(cmdBuffer, ref pipelineKey);
        SetDynamicState(cmdBuffer, ref pipelineKey);
        api.CmdDraw(cmdBuffer, (uint)call.TriangleCount, 1, (uint)call.TriangleOffset, 0);
    }


    unsafe void ConvexFillInternal(in VkNvgCall call, CommandBuffer cmdBuffer, DescriptorSet ssboDescriptorSet, DescriptorSet[] uniformDescriptor)
    {
        Vk api = createInfo.Api;
        Span<VkNvgPath> paths = _paths.Data.Slice(call.PathOffset, call.PathCount);
        int npaths = paths.Length;

        
        var compositeOperation = call.CompositeOperation;
#if !USE_TOPOLOGY_TRIANGLE_FAN
        var topology = PrimitiveTopology.TriangleList;
#else
        var topology = PrimitiveTopology.TriangleFan;
#endif

        VkNvgCreatePipelineKey pipelineKey = new VkNvgCreatePipelineKey(createInfo,
            compositeOperation: compositeOperation,
            topology: topology
        );
        
        BindPipeline(cmdBuffer, ref pipelineKey);
        SetDynamicState(cmdBuffer, ref pipelineKey);
        SetUniforms(cmdBuffer, call.UniformOffset);
        DescriptorSet* sets = stackalloc DescriptorSet[] { ssboDescriptorSet, uniformDescriptor[call.Image != 0 ? call.Image - 1 : 0] };
        api.CmdBindDescriptorSets(cmdBuffer, PipelineBindPoint.Graphics, _pipelineLayout, 0, 2, sets, 0, null);

        for (int i = 0; i < npaths; ++i)
        {
            api.CmdDraw(cmdBuffer, (uint)paths[i].FillCount, 1, paths[i].FillOffset, 0);
        }

        if (EdgeAntiAlias)
        {
            pipelineKey = pipelineKey.With(topology: PrimitiveTopology.TriangleStrip);
            BindPipeline(cmdBuffer, ref pipelineKey);
            SetDynamicState(cmdBuffer, ref pipelineKey);
            // Draw fringes
            for (int i = 0; i < npaths; ++i)
            {
                api.CmdDraw(cmdBuffer, (uint)paths[i].StrokeCount, 1, (uint)paths[i].StrokeOffset, 0);
            }
        }
    }

    unsafe void StrokeInternal(in VkNvgCall call, CommandBuffer cmdBuffer, DescriptorSet ssboDescriptorSet, DescriptorSet[] uniformDescriptor)
    {
        Vk api = createInfo.Api;

        Span<VkNvgPath> paths = _paths.Data.Slice(call.PathOffset, call.PathCount);
        int npaths = paths.Length;

        if ((flags & CreateFlags.StencilStrokes) == CreateFlags.StencilStrokes)
        {
            VkNvgCreatePipelineKey pipelineKey = new VkNvgCreatePipelineKey(createInfo,
                compositeOperation: call.CompositeOperation,
                topology: PrimitiveTopology.TriangleStrip,
                // Fill stencil with 1 if stencil EQUAL passes
                stencilStroke: StencilSetting.StencilStrokeFill
            );


            BindPipeline(cmdBuffer, ref pipelineKey);
            SetDynamicState(cmdBuffer, ref pipelineKey);
            SetUniforms(cmdBuffer, call.UniformOffset + 1);
            DescriptorSet* sets = stackalloc DescriptorSet[] { ssboDescriptorSet, uniformDescriptor[call.Image != 0 ? call.Image - 1 : 0] };
            api.CmdBindDescriptorSets(cmdBuffer, PipelineBindPoint.Graphics, _pipelineLayout, 0, 2, sets, 0, null);

            for (int i = 0; i < npaths; ++i)
            {
                api.CmdDraw(cmdBuffer, (uint)paths[i].StrokeCount, 1, (uint)paths[i].StrokeOffset, 0);
            }

            SetUniforms(cmdBuffer, call.UniformOffset);
            // //Draw AA shape if stencil EQUAL passes
            pipelineKey = pipelineKey.With(stencilStroke: StencilSetting.StencilStrokeDrawAA);
            BindPipeline(cmdBuffer, ref pipelineKey);
            SetDynamicState(cmdBuffer, ref pipelineKey);
            api.CmdBindDescriptorSets(cmdBuffer, PipelineBindPoint.Graphics, _pipelineLayout, 0, 2, sets, 0, null);

            for (int i = 0; i < npaths; ++i)
            {
                api.CmdDraw(cmdBuffer, (uint)paths[i].StrokeCount, 1, (uint)paths[i].StrokeOffset, 0);
            }

            // Fill stencil with 0, always
            pipelineKey = pipelineKey.With(stencilStroke: StencilSetting.StencilStrokeClear);
            BindPipeline(cmdBuffer, ref pipelineKey);
            SetDynamicState(cmdBuffer, ref pipelineKey);
            api.CmdBindDescriptorSets(cmdBuffer, PipelineBindPoint.Graphics, _pipelineLayout, 0, 2, sets, 0, null);

            for (int i = 0; i < npaths; ++i)
            {
                api.CmdDraw(cmdBuffer, (uint)paths[i].StrokeCount, 1, (uint)paths[i].StrokeOffset, 0);
            }
        }
        else
        {
            VkNvgCreatePipelineKey pipelineKey = new VkNvgCreatePipelineKey(createInfo,
                compositeOperation: call.CompositeOperation,
                stencilFill: false,
                topology: PrimitiveTopology.TriangleStrip);

            BindPipeline(cmdBuffer, ref pipelineKey);
            SetDynamicState(cmdBuffer, ref pipelineKey);
            SetUniforms(cmdBuffer, call.UniformOffset);
            DescriptorSet* sets = stackalloc DescriptorSet[] { ssboDescriptorSet, uniformDescriptor[call.Image != 0 ? call.Image - 1 : 0] };
            api.CmdBindDescriptorSets(cmdBuffer, PipelineBindPoint.Graphics, _pipelineLayout, 0, 2, sets, 0, null);
            // Draw Strokes

            for (int i = 0; i < npaths; ++i)
            {
                api.CmdDraw(cmdBuffer, (uint)paths[i].StrokeCount, 1, (uint)paths[i].StrokeOffset, 0);
            }
        }
    }

    unsafe void TrianglesInternal(in VkNvgCall call, CommandBuffer cmdBuffer, DescriptorSet ssboDescriptorSet, DescriptorSet[] uniformDescriptor)
    {
        Vk api = createInfo.Api;
        if (call.TriangleCount == 0)
        {
            return;
        }

        VkNvgCreatePipelineKey pipelineKey = new VkNvgCreatePipelineKey(createInfo,
            compositeOperation: call.CompositeOperation,
            topology: PrimitiveTopology.TriangleList,
            stencilFill: false);

        BindPipeline(cmdBuffer, ref pipelineKey);
        SetDynamicState(cmdBuffer, ref pipelineKey);
        SetUniforms(cmdBuffer, call.UniformOffset);
        DescriptorSet* sets = stackalloc DescriptorSet[] { ssboDescriptorSet, uniformDescriptor[call.Image != 0 ? call.Image - 1: 0 ] };
        api.CmdBindDescriptorSets(cmdBuffer, PipelineBindPoint.Graphics, _pipelineLayout, 0, 2, sets, 0, null);

        api.CmdDraw(cmdBuffer, (uint)call.TriangleCount, 1, (uint)call.TriangleOffset, 0);
    }
    
    VkNvgTexture AllocTexture()
    {
        int? texIndex = null;

        var texturesData = _textures.Data;
        for (int i = 0; i < _textures.Length; i++)
        {
            if (texturesData[i] == null || texturesData[i]!.Image.Handle == 0)
            {
                texIndex = i;
                break;
            }
        }

        if (texIndex == null)
        {
            texIndex = _textures.EnsureCapacity(1);
            texturesData = _textures.Data;
        }

        var texture = new VkNvgTexture(createInfo);
        texturesData[texIndex.Value] = texture;
        return texture;
    }

    int GetTextureId(VkNvgTexture tex)
    {
        return _textures.IndexOf(tex) + 1;
    }

    public unsafe void CreateDescriptorSetLayout(DescriptorSetLayout* layouts)
    {
	    Vk api = createInfo.Api;
	    Device device = createInfo.Device;
	    ref AllocationCallbacks allocator = ref createInfo.Allocator;
	    
	    DescriptorSetLayoutBinding binding0 = new DescriptorSetLayoutBinding
	    {
		    Binding = 0,
		    DescriptorType = DescriptorType.StorageBuffer,
		    DescriptorCount = 1,
		    StageFlags = ShaderStageFlags.FragmentBit,
		    PImmutableSamplers = null,
	    };
	    DescriptorSetLayoutCreateInfo createInfo0 = new DescriptorSetLayoutCreateInfo
	    {
		    SType = StructureType.DescriptorSetLayoutCreateInfo,
		    PNext = null,
		    Flags = 0,
		    BindingCount = 1,
		    PBindings = &binding0,
	    };
	    var descriptorSetLayoutResult = api.CreateDescriptorSetLayout(device, &createInfo0, allocator, &layouts[0]);
	    DebugUtils.Check(descriptorSetLayoutResult);


	    DescriptorSetLayoutBinding binding1 = new DescriptorSetLayoutBinding
	    {
		    Binding = 1,
		    DescriptorType = DescriptorType.CombinedImageSampler,
		    DescriptorCount = 1,
		    StageFlags = ShaderStageFlags.FragmentBit,
		    PImmutableSamplers = null,
	    };
	    DescriptorSetLayoutCreateInfo createInfo1 = new DescriptorSetLayoutCreateInfo
	    {
		    SType = StructureType.DescriptorSetLayoutCreateInfo,
		    PNext = null,
		    Flags = 0,
		    BindingCount = 1,
		    PBindings = &binding1,
	    };
	    descriptorSetLayoutResult = api.CreateDescriptorSetLayout(device, &createInfo1, allocator, &layouts[1]);
	    DebugUtils.Check(descriptorSetLayoutResult);
    }

    static int MaxVertCount(ReadOnlySpan<Path> paths)
    {
	    int i, count = 0;
	    for (i = 0; i < paths.Length; i++)
	    {
		    count += paths[i].FillCount;
		    count += paths[i].StrokeCount;
	    }

	    return count;
    }

    static int MaxVertCountList(ReadOnlySpan<Path> paths)
    {
	    int i, count = 0;
	    for (i = 0; i < paths.Length; i++)
	    {
		    count += (paths[i].FillCount - 2) * 3;
		    count += paths[i].StrokeCount;
	    }

	    return count;
    }

    private VkNvgTexture? FindTexture(int id)
    {
	    if (id > _textures.Length || id <= 0)
		    return null;

	    VkNvgTexture? tex = _textures.Data[id - 1];
	    return tex;
    }

    private void ConvertPaint(
	    out VkNvgFragUniforms frag,
	    Paint paint,
	    Scissor scissor,
	    float width,
	    float fringe,
	    float strokeThr
    )
    {
	    Matrix3x2 invTransform;

	    frag = new VkNvgFragUniforms
	    {
		    InnerCol = paint.InnerColour.Premult(),
		    OuterCol = paint.OuterColour.Premult(),
	    };

	    if (scissor.Extent.Width < -0.5f || scissor.Extent.Height < -0.5f)
	    {
		    frag.ScissorMat = new Matrix4x4();
		    frag.ScissorExt.X = 1.0f;
		    frag.ScissorExt.Y = 1.0f;
		    frag.ScissorScale.X = 1.0f;
		    frag.ScissorScale.Y = 1.0f;
	    }
	    else
	    {
		    Matrix3x2 scissorMat = scissor.Transform;
		    Matrix3x2.Invert(scissorMat, out invTransform);
		    frag.ScissorMat = new Matrix4x4(invTransform);
		    frag.ScissorExt.X = scissor.Extent.Width;
		    frag.ScissorExt.Y = scissor.Extent.Height;

		    frag.ScissorScale = new Vector2(
			    MathF.Sqrt(scissorMat.M11 * scissorMat.M11 + scissorMat.M21 * scissorMat.M21) / fringe,
			    MathF.Sqrt(scissorMat.M21 * scissorMat.M21 + scissorMat.M22 * scissorMat.M22) / fringe
		    );
	    }

	    frag.Extent = paint.Extent.ToVector2();
	    frag.StrokeMult = (width * 0.5f + fringe * 0.5f) / fringe;
	    frag.StrokeThr = strokeThr;

	    if (paint.Image != 0)
	    {
		    VkNvgTexture? tex = FindTexture(paint.Image);
		    if (tex == null) return;
		    if ((tex.Flags & ImageFlags.FlipY) != 0)
		    {
			    Matrix3x2 m1, m2;
			    m1 = Matrix3x2.CreateTranslation(0.0f, frag.Extent.Y * 0.5f);
			    m1 = Transforms.NvgTransforms.Multiply(m1, paint.Transform);
			    m2 = Matrix3x2.CreateScale(1.0f, -1.0f);
			    m2 = Transforms.NvgTransforms.Multiply(m2, m1);
			    m1 = Matrix3x2.CreateTranslation(0.0f, -frag.Extent.Y * 0.5f);
			    m1 = Transforms.NvgTransforms.Multiply(m1, m2);
			    Matrix3x2.Invert(m1, out invTransform);
		    }
		    else
		    {
			    Matrix3x2.Invert(paint.Transform, out invTransform);
		    }

		    frag.Type = ShaderType.FillImage;

		    if (tex.Type == Texture.Rgba)
			    frag.TexType = (tex.Flags & ImageFlags.Premultiplied) == ImageFlags.Premultiplied ? 0 : 1;
		    else
			    frag.TexType = 2;
	    }
	    else
	    {
		    frag.Type = ShaderType.FillGradient;
		    frag.Radius = paint.Radius;
		    frag.Feather = paint.Feather;
		    Matrix3x2.Invert(paint.Transform, out invTransform);
	    }

	    frag.PaintMat = new Matrix4x4(invTransform);
    }

    private unsafe DescriptorPool CreateDescriptorPool(uint count)
    {
	    Vk api = createInfo.Api;
	    Device device = createInfo.Device;
	    ref AllocationCallbacks allocator = ref createInfo.Allocator;
		
	    DescriptorPoolSize* typeCount = stackalloc DescriptorPoolSize[3]
	    {
		    new DescriptorPoolSize { Type = DescriptorType.InputAttachment, DescriptorCount = 4 * count },
		    new DescriptorPoolSize { Type = DescriptorType.StorageBuffer, DescriptorCount = 4 * count },
		    new DescriptorPoolSize { Type = DescriptorType.CombinedImageSampler, DescriptorCount = 4 * count },
	    };
	    DescriptorPoolCreateInfo descriptorPool = new DescriptorPoolCreateInfo
	    {
		    SType = StructureType.DescriptorPoolCreateInfo,
		    PNext = null,
		    Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit,
		    MaxSets = count * 2,
		    PoolSizeCount = 3,
		    PPoolSizes = typeCount,
	    };
	    DescriptorPool descPool;
	    var descriptorPoolResult = api.CreateDescriptorPool(device, &descriptorPool, allocator, &descPool);
	    DebugUtils.Check(descriptorPoolResult);
	    return descPool;
    }

    private unsafe PipelineLayout CreatePipelineLayout()
    {
	    Vk api = createInfo.Api;
	    ref AllocationCallbacks allocator = ref createInfo.Allocator;
	    PushConstantRange pushConstantRange = new PushConstantRange
	    {
		    Offset = 0,
		    Size = (uint)sizeof(VkNvgVertexConstants),
		    StageFlags = ShaderStageFlags.VertexBit,
	    };

	    PipelineLayout pipelineLayout;
	    fixed (DescriptorSetLayout* ptr = &_descLayout[0])
	    {
		    PipelineLayoutCreateInfo pipelineLayoutCreateInfo = new PipelineLayoutCreateInfo
		    {
			    SType = StructureType.PipelineLayoutCreateInfo,
			    SetLayoutCount = (uint)_descLayout.Length,
			    PSetLayouts = ptr,
			    PushConstantRangeCount = 1,
			    PPushConstantRanges = &pushConstantRange,
		    };


		    var layoutResult = api.CreatePipelineLayout(createInfo.Device, &pipelineLayoutCreateInfo, allocator, &pipelineLayout);
		    DebugUtils.Check(layoutResult);
	    }

	    return pipelineLayout;
    }

    private Pipeline BindPipeline(CommandBuffer cmdBuffer, ref VkNvgCreatePipelineKey pipelineKey)
    {
	    Vk api = createInfo.Api;
	    if (!_pipelines.TryGetValue(pipelineKey, out var pipeline))
	    {
		    pipeline = pipelineKey.CreatePipeline(_pipelineLayout,  _fillVertShader!.ShaderModule, _fillFragShader!.ShaderModule, EdgeAntiAlias);
		    _pipelines.Add(pipelineKey, pipeline);
	    }

	    if (!_currentPipeline.HasValue || pipeline.Handle != _currentPipeline.Value.Handle)
	    {
		    api.CmdBindPipeline(cmdBuffer, PipelineBindPoint.Graphics, pipeline);
		    _currentPipeline = pipeline;
	    }

	    return pipeline;
    }

    private unsafe void SetUniforms(CommandBuffer cmdBuffer, int uniformOffset)
    {
	    Vk api = createInfo.Api;
	    Device device = createInfo.Device;

	    VkNvgVertexConstants vertexConstants = _vertexConstants;
	    vertexConstants.UniformOffset = (uint)uniformOffset;
	    api.CmdPushConstants(cmdBuffer, _pipelineLayout, ShaderStageFlags.VertexBit, 0u,
		    (uint)sizeof(VkNvgVertexConstants), &vertexConstants);
    }
    

    private static unsafe void UpdateTextureDescriptorSet(DescriptorSet descSet, VkNvgTexture tex, Vk api, Device device)
    {
	    DescriptorImageInfo imageInfo = new DescriptorImageInfo
	    {
		    ImageLayout = tex.ImageLayout,
		    ImageView = tex.View,
		    Sampler = tex.Sampler,
	    };

	    WriteDescriptorSet write = new WriteDescriptorSet
	    {
		    SType = StructureType.WriteDescriptorSet,
		    DstSet = descSet,
		    DstBinding = 1,
		    DescriptorCount = 1,
		    DescriptorType = DescriptorType.CombinedImageSampler,
		    PImageInfo = &imageInfo,
	    };
	    api.UpdateDescriptorSets(device, 1, &write, 0, null);
    }

    private unsafe void SetDynamicState(CommandBuffer cmd, ref readonly VkNvgCreatePipelineKey pipelineKey)
    {
	    Vk api = createInfo.Api;
	    ExtExtendedDynamicState3? apiExt3 = createInfo.ApiExt3;
	    var pipelineKeyColorWriteMask = pipelineKey.ColorWriteMask;
	    if (createInfo.Ext.DynamicState)
	    {
		    api.CmdSetPrimitiveTopology(cmd, pipelineKey.Topology);
	    }

	    if (createInfo.Ext.ColorWriteMask && apiExt3 != null)
	    {
		    apiExt3.CmdSetColorWriteMask(cmd, 0, 1, &pipelineKeyColorWriteMask);
	    }

	    if (createInfo.Ext.ColorBlendEquation && apiExt3 != null)
	    {
		    PipelineColorBlendAttachmentState colorBlendAttachment = pipelineKey.CompositeOperationToColorBlendAttachmentState;
		    ColorBlendEquationEXT colorBlendEquation = new ColorBlendEquationEXT
		    {
			    SrcColorBlendFactor = colorBlendAttachment.SrcColorBlendFactor,
			    DstColorBlendFactor = colorBlendAttachment.DstColorBlendFactor,
			    ColorBlendOp = colorBlendAttachment.ColorBlendOp,
			    SrcAlphaBlendFactor = colorBlendAttachment.SrcAlphaBlendFactor,
			    DstAlphaBlendFactor = colorBlendAttachment.DstAlphaBlendFactor,
			    AlphaBlendOp = colorBlendAttachment.AlphaBlendOp,
		    };
		    apiExt3.CmdSetColorBlendEquation(cmd, 0, 1, &colorBlendEquation);
	    }

	    if (createInfo.Ext.DynamicState)
	    {
		    PipelineDepthStencilStateCreateInfo ds = pipelineKey.GetDepthStencilCreateInfo();
		    api.CmdSetStencilTestEnable(cmd, ds.StencilTestEnable);
		    if (ds.StencilTestEnable)
		    {
			    api.CmdSetStencilOp(cmd, StencilFaceFlags.FaceFrontBit, ds.Front.FailOp, ds.Front.PassOp,
				    ds.Front.DepthFailOp, ds.Front.CompareOp);
			    api.CmdSetStencilOp(cmd, StencilFaceFlags.FaceBackBit, ds.Back.FailOp, ds.Back.PassOp,
				    ds.Back.DepthFailOp, ds.Back.CompareOp);
		    }
	    }
    }
}