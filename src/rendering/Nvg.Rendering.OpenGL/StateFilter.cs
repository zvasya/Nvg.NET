using NvgNET.Rendering.OpenGL.Blending;

using Silk.NET.OpenGL;

namespace NvgNET.Rendering.OpenGL
{
    internal class StateFilter
    {

        public uint BoundTexture { get; set; }

        public uint StencilMask { get; set; }

        public StencilFunction StencilFunc { get; set; }

        public int StencilFuncRef { get; set; }

        public uint StencilFuncMask { get; set; }

        public Blend BlendFunc { get; set; }

    }
}
