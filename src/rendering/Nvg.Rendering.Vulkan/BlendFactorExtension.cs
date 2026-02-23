using Silk.NET.Vulkan;

namespace NvgNET.Rendering.Vulkan
{
	public static class BlendFactorExtension
	{
		public static BlendFactor ToVkBlendFactor(this Blending.BlendFactor factor) =>
			factor switch
			{
				Blending.BlendFactor.Zero => BlendFactor.Zero,
				Blending.BlendFactor.One => BlendFactor.One,
				Blending.BlendFactor.SrcColour => BlendFactor.SrcColor,
				Blending.BlendFactor.OneMinusSrcColour => BlendFactor.OneMinusSrcColor,
				Blending.BlendFactor.DstColour => BlendFactor.DstColor,
				Blending.BlendFactor.OneMinusDstColour => BlendFactor.OneMinusDstColor,
				Blending.BlendFactor.SrcAlpha => BlendFactor.SrcAlpha,
				Blending.BlendFactor.OneMinusSrcAlpha => BlendFactor.OneMinusSrcAlpha,
				Blending.BlendFactor.DstAlpha => BlendFactor.DstAlpha,
				Blending.BlendFactor.OneMinusDstAlpha => BlendFactor.OneMinusDstAlpha,
				Blending.BlendFactor.SrcAlphaSaturate => BlendFactor.SrcAlphaSaturate,
				_ => throw new ArgumentOutOfRangeException(nameof(factor), factor, null),
			};
	}
}