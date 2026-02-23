using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using SilkyNvg.Blending;
using BlendFactor = Silk.NET.Vulkan.BlendFactor;

namespace SilkyNvg.Rendering.Vulkan;

public readonly struct VkNvgCreatePipelineKey : IEquatable<VkNvgCreatePipelineKey>
{
    readonly VkNvgContext _vkContext;
    public readonly StencilSetting StencilStroke;
    public readonly bool StencilFill;
    public readonly bool StencilTest;
    public readonly bool EdgeAa;
    public readonly PrimitiveTopology Topology;
    public readonly CompositeOperationState CompositeOperation;
    public readonly ColorComponentFlags ColorWriteMask; // set and compare independently
    readonly int _hashCode;

    public VkNvgCreatePipelineKey(VkNvgContext vkContext,
        StencilSetting stencilStroke = StencilSetting.StencilStrokeUndefined,
        bool stencilFill = false,
        bool stencilTest = false,
        bool edgeAa = false,
        PrimitiveTopology topology = PrimitiveTopology.PointList,
        CompositeOperationState compositeOperation = default)
    {
        _vkContext = vkContext;
        StencilStroke = stencilStroke;
        StencilFill = stencilFill;
        StencilTest = stencilTest;
        EdgeAa = edgeAa;
        Topology = topology;
        CompositeOperation = compositeOperation;
        ColorWriteMask = StencilStroke == StencilSetting.StencilStrokeClear || StencilFill
            ? ColorComponentFlags.None
            : ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit;
        
        _hashCode = CalcHashCode();
    }

    int CalcHashCode()
    {
	    int hash = 0;
	    if (!_vkContext.Ext.DynamicState) 
		    hash += (int)Topology + (StencilTest ? 1 << 5: 0) + (StencilFill ? 1 << 6 : 0) + ((int)StencilStroke << 7);

	    if (!_vkContext.Ext.ColorWriteMask)
		    hash += ((int)ColorWriteMask << 8);

	    if (!_vkContext.Ext.ColorBlendEquation)
		    return HashCode.Combine(hash, EdgeAa, CompositeOperation);

	    return hash.GetHashCode();
    }

    public VkNvgCreatePipelineKey With(
        StencilSetting? stencilStroke = null,
        bool? stencilFill = null,
        bool? stencilTest = null,
        bool? edgeAa = null,
        PrimitiveTopology? topology = null,
        CompositeOperationState? compositeOperation = null)
    {
        return new VkNvgCreatePipelineKey
        (
            _vkContext,
            stencilStroke ?? this.StencilStroke,
            stencilFill ?? this.StencilFill,
            stencilTest ?? this.StencilTest,
            edgeAa ?? this.EdgeAa,
            topology ?? this.Topology,
            compositeOperation ?? this.CompositeOperation
            );
    }

    public CullModeFlags CullMode => StencilFill ? CullModeFlags.None : CullModeFlags.BackBit;

    public PipelineColorBlendAttachmentState CompositeOperationToColorBlendAttachmentState
    {
        get
        {
            PipelineColorBlendAttachmentState state = new PipelineColorBlendAttachmentState
            {
                BlendEnable = true,
                ColorBlendOp = BlendOp.Add,
                AlphaBlendOp = BlendOp.Add,
                ColorWriteMask = ColorWriteMask,
            };

            try
            {
                state.SrcColorBlendFactor = CompositeOperation.SrcRgb.ToVkBlendFactor();
                state.SrcAlphaBlendFactor = CompositeOperation.SrcAlpha.ToVkBlendFactor();
                state.DstColorBlendFactor = CompositeOperation.DstRgb.ToVkBlendFactor();
                state.DstAlphaBlendFactor = CompositeOperation.DstAlpha.ToVkBlendFactor();
            }
            catch (Exception)
            {
                state.SrcColorBlendFactor = BlendFactor.One;
                state.SrcAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha;
                state.DstColorBlendFactor = BlendFactor.One;
                state.DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha;
            }

            return state;
        }
    }

    public PipelineDepthStencilStateCreateInfo GetDepthStencilCreateInfo()
    {
        PipelineDepthStencilStateCreateInfo ds = new PipelineDepthStencilStateCreateInfo
        {
            SType = StructureType.PipelineDepthStencilStateCreateInfo,
            DepthWriteEnable = false,
            DepthTestEnable = true,
            DepthCompareOp = CompareOp.LessOrEqual,
            DepthBoundsTestEnable = false,
        };

        if (StencilStroke != StencilSetting.StencilStrokeUndefined)
        {
            // enables
            ds.StencilTestEnable = true;
            ds.Front.FailOp = StencilOp.Keep;
            ds.Front.DepthFailOp = StencilOp.Keep;
            ds.Front.PassOp = StencilOp.Keep;
            ds.Front.CompareOp = CompareOp.Equal;
            ds.Front.Reference = 0x00;
            ds.Front.CompareMask = 0xff;
            ds.Front.WriteMask = 0xff;
            ds.Back = ds.Front;
            ds.Back.PassOp = StencilOp.DecrementAndClamp;

            switch (StencilStroke)
            {
                case StencilSetting.StencilStrokeFill:
                    ds.Front.PassOp = StencilOp.IncrementAndClamp;
                    ds.Back.PassOp = StencilOp.DecrementAndClamp;
                    break;
                case StencilSetting.StencilStrokeDrawAA:
                    ds.Front.PassOp = StencilOp.Keep;
                    ds.Back.PassOp = StencilOp.Keep;
                    break;
                case StencilSetting.StencilStrokeClear:
                    ds.Front.FailOp = StencilOp.Zero;
                    ds.Front.DepthFailOp = StencilOp.Zero;
                    ds.Front.PassOp = StencilOp.Zero;
                    ds.Front.CompareOp = CompareOp.Always;
                    ds.Back = ds.Front;
                    break;
            }

            return ds;
        }

        ds.StencilTestEnable = false;
        ds.Back.FailOp = StencilOp.Keep;
        ds.Back.PassOp = StencilOp.Keep;
        ds.Back.CompareOp = CompareOp.Always;

        if (StencilFill)
        {
            ds.StencilTestEnable = true;
            ds.Front.CompareOp = CompareOp.Always;
            ds.Front.FailOp = StencilOp.Keep;
            ds.Front.DepthFailOp = StencilOp.Keep;
            ds.Front.PassOp = StencilOp.IncrementAndWrap;
            ds.Front.Reference = 0x0;
            ds.Front.CompareMask = 0xff;
            ds.Front.WriteMask = 0xff;
            ds.Back = ds.Front;
            ds.Back.PassOp = StencilOp.DecrementAndWrap;
        }
        else if (StencilTest)
        {
            ds.StencilTestEnable = true;
            if (EdgeAa)
            {
                ds.Front.CompareOp = CompareOp.Equal;
                ds.Front.Reference = 0x0;
                ds.Front.CompareMask = 0xff;
                ds.Front.WriteMask = 0xff;
                ds.Front.FailOp = StencilOp.Keep;
                ds.Front.DepthFailOp = StencilOp.Keep;
                ds.Front.PassOp = StencilOp.Keep;
            }
            else
            {
                ds.Front.CompareOp = CompareOp.NotEqual;
                ds.Front.Reference = 0x0;
                ds.Front.CompareMask = 0xff;
                ds.Front.WriteMask = 0xff;
                ds.Front.FailOp = StencilOp.Zero;
                ds.Front.DepthFailOp = StencilOp.Zero;
                ds.Front.PassOp = StencilOp.Zero;
            }

            ds.Back = ds.Front;
        }

        return ds;
    }

    public unsafe Pipeline CreatePipeline(PipelineLayout pipelineLayout, ShaderModule vertShader, ShaderModule fragShader, bool antialias)
    {
	    Vk api = _vkContext.Api;
        Device device = _vkContext.Device;
        RenderPass renderPass = _vkContext.RenderPass;
        ref AllocationCallbacks allocator = ref _vkContext.Allocator;

        VertexInputBindingDescription* viBindings = stackalloc VertexInputBindingDescription[1];
        {
            viBindings[0].Binding = 0;
            viBindings[0].Stride = (uint)sizeof(Vertex);
            viBindings[0].InputRate = VertexInputRate.Vertex;
        }

        VertexInputAttributeDescription* viAttrs = stackalloc VertexInputAttributeDescription[2];
        {
            viAttrs[0].Binding = 0;
            viAttrs[0].Location = 0;
            viAttrs[0].Format = Format.R32G32Sfloat;
            viAttrs[0].Offset = 0;
            viAttrs[1].Binding = 0;
            viAttrs[1].Location = 1;
            viAttrs[1].Format = Format.R32G32Sfloat;
            viAttrs[1].Offset = (2 * sizeof(float));
        }

        PipelineVertexInputStateCreateInfo vi = new PipelineVertexInputStateCreateInfo
        {
            SType = StructureType.PipelineVertexInputStateCreateInfo,
            VertexBindingDescriptionCount = 1,
            PVertexBindingDescriptions = viBindings,
            VertexAttributeDescriptionCount = 2,
            PVertexAttributeDescriptions = viAttrs,
        };

        PipelineInputAssemblyStateCreateInfo ia = new PipelineInputAssemblyStateCreateInfo
        {
            SType = StructureType.PipelineInputAssemblyStateCreateInfo,
            Topology = Topology,
        };

        PipelineRasterizationStateCreateInfo rs = new PipelineRasterizationStateCreateInfo
        {
            SType = StructureType.PipelineRasterizationStateCreateInfo,
            PolygonMode = PolygonMode.Fill,
            CullMode = CullMode,
            FrontFace = FrontFace.CounterClockwise,
            DepthClampEnable = false,
            RasterizerDiscardEnable = false,
            DepthBiasEnable = false,
            LineWidth = 1.0f,
        };

        PipelineColorBlendAttachmentState colorblend = CompositeOperationToColorBlendAttachmentState;
        
        PipelineColorBlendStateCreateInfo cb = new PipelineColorBlendStateCreateInfo
        {
            SType = StructureType.PipelineColorBlendStateCreateInfo,
            AttachmentCount = 1,
            PAttachments = &colorblend,
        };

        PipelineViewportStateCreateInfo vp = new PipelineViewportStateCreateInfo
        {
            SType = StructureType.PipelineViewportStateCreateInfo,
            ViewportCount = 1,
            ScissorCount = 1,
        };


        DynamicState* dynamicStateEnables = stackalloc DynamicState[16];

        uint numDynamicStates = 0;
        dynamicStateEnables[numDynamicStates++] = DynamicState.Viewport;
        dynamicStateEnables[numDynamicStates++] = DynamicState.Scissor;
        if (_vkContext.Ext.DynamicState)
        {
            dynamicStateEnables[numDynamicStates++] = DynamicState.PrimitiveTopology;
            dynamicStateEnables[numDynamicStates++] = DynamicState.StencilTestEnable;
            dynamicStateEnables[numDynamicStates++] = DynamicState.StencilOp;
        }

        if (_vkContext.Ext.ColorBlendEquation)
        {
            dynamicStateEnables[numDynamicStates++] = DynamicState.ColorBlendEquationExt;
        }

        if (_vkContext.Ext.ColorWriteMask)
        {
            dynamicStateEnables[numDynamicStates++] = DynamicState.ColorWriteMaskExt;
        }

        PipelineDynamicStateCreateInfo dynamicState = new PipelineDynamicStateCreateInfo
        {
            SType = StructureType.PipelineDynamicStateCreateInfo,
            DynamicStateCount = numDynamicStates,
            PDynamicStates = dynamicStateEnables,
        };

        PipelineDepthStencilStateCreateInfo ds = GetDepthStencilCreateInfo();

        PipelineMultisampleStateCreateInfo ms = new PipelineMultisampleStateCreateInfo
        {
            SType = StructureType.PipelineMultisampleStateCreateInfo,
            PSampleMask = null,
            RasterizationSamples = SampleCountFlags.Count1Bit,
        };

        uint edgeAA = antialias ? 1u : 0u;

        SpecializationMapEntry entry = new SpecializationMapEntry
        {
            Offset = 0,
            ConstantID = 0,
            Size = sizeof(uint),
        };

        SpecializationInfo specializationInfo = new SpecializationInfo
        {
            MapEntryCount = 1,
            PMapEntries = &entry,
            DataSize = entry.Size,
            PData = &edgeAA,
        };
        
        PipelineShaderStageCreateInfo* shaderStages = stackalloc PipelineShaderStageCreateInfo[2]
        {
            new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo, 
                Stage = ShaderStageFlags.VertexBit,
                Module = vertShader,
                PName = (byte*)SilkMarshal.StringToPtr("main"),
            },
            new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = fragShader,
                PName = (byte*)SilkMarshal.StringToPtr("main"),
                PSpecializationInfo = &specializationInfo,
            },
        };

        GraphicsPipelineCreateInfo pipelineCreateInfo = new GraphicsPipelineCreateInfo
        {
            SType = StructureType.GraphicsPipelineCreateInfo,
            Layout = pipelineLayout,
            StageCount = 2,
            PStages = shaderStages,
            PVertexInputState = &vi,
            PInputAssemblyState = &ia,
            PRasterizationState = &rs,
            PColorBlendState = &cb,
            PMultisampleState = &ms,
            PViewportState = &vp,
            PDepthStencilState = &ds,
            RenderPass = renderPass,
            PDynamicState = &dynamicState,
        };

        Pipeline pipeline;
        var graphicsPipelinesResult = api.CreateGraphicsPipelines(device, default, 1, in pipelineCreateInfo, allocator, &pipeline);
        DebugUtils.Check(graphicsPipelinesResult);

        return pipeline;
    }

    public override int GetHashCode() => _hashCode;

    public bool Equals(VkNvgCreatePipelineKey other)
    {
	    if (!_vkContext.Ext.DynamicState)
        {
            if (Topology != other.Topology)
                return false;

            if (StencilTest != other.StencilTest)
                return false;

            if (StencilFill != other.StencilFill)
                return false;

            if (StencilStroke != other.StencilStroke)
                return false;
        }

        if (!_vkContext.Ext.ColorWriteMask)
        {
            if (ColorWriteMask != other.ColorWriteMask)
                return false;
        }

        if (!_vkContext.Ext.ColorBlendEquation)
        {
            if (EdgeAa != other.EdgeAa)
                return false;

            if (CompositeOperation != other.CompositeOperation)
                return false;
        }

        return true;
    }
}