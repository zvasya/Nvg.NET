using System.Numerics;
using System.Runtime.InteropServices;

namespace SilkyNvg.Rendering.Vulkan;

[StructLayout(LayoutKind.Sequential)]
public struct VkNvgFragUniforms {
    public Matrix4x4 ScissorMat;
    public Matrix4x4 PaintMat;
    public Colour InnerCol;
    public Colour OuterCol;
    public Vector2 ScissorExt;
    public Vector2 ScissorScale;
    public Vector2 Extent;
    public float Radius;
    public float Feather;
    public float StrokeMult;
    public float StrokeThr;
    public int TexType;
    public ShaderType Type;
}
