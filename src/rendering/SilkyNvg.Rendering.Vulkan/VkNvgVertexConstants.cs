using System.Numerics;
using System.Runtime.InteropServices;

namespace SilkyNvg.Rendering.Vulkan;

[StructLayout(LayoutKind.Sequential)]
public struct VkNvgVertexConstants {
    public Vector2 ViewSize;
    public uint UniformOffset;
}