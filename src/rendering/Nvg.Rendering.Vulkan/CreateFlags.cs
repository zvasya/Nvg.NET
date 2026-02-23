namespace NvgNET.Rendering.Vulkan
{
	[Flags]
	public enum CreateFlags
	{
		// Flag indicating if geometry based anti-aliasing is used (may not be needed
		// when using MSAA).
		Antialias = 1 << 0,

		// Flag indicating if strokes should be drawn using stencil buffer. The
		// rendering will be a little
		// slower, but path overlaps (i.e. self-intersecting or sharp turns) will be
		// drawn just once.
		StencilStrokes = 1 << 1,

		// Flag indicating that additional debug checks are done.
		Debug = 1 << 2,
	}
}