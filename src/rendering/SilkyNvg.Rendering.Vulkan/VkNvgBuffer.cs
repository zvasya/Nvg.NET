using Silk.NET.Vulkan;

using Buffer = Silk.NET.Vulkan.Buffer;

namespace SilkyNvg.Rendering.Vulkan;

public class VkNvgBuffer : IDisposable
{
	readonly VkNvgContext _context;
	public readonly Buffer Buffer;
	bool _disposed;
	bool _initialised;
	IntPtr _mapped;

	DeviceMemory _mem;
	ulong _size;

	VkNvgBuffer(VkNvgContext context, Buffer buffer)
	{
		Buffer = buffer;
		_context = context;
	}

	Vk Api => _context.Api;
	Device Device => _context.Device;
	ref AllocationCallbacks Allocator => ref _context.Allocator;

	public void Dispose()
	{
		if (_disposed)
			return;

		if (_initialised)
		{
			Api.UnmapMemory(Device, this._mem);
		}

		Api.DestroyBuffer(Device, Buffer, Allocator);
		Api.FreeMemory(Device, _mem, Allocator);
		_disposed = true;
	}

	public static unsafe void Update(
		VkNvgContext context,
		ref VkNvgBuffer? buffer,
		PhysicalDeviceMemoryProperties memoryProperties,
		BufferUsageFlags usage,
		MemoryPropertyFlags memoryType,
		ReadOnlySpan<byte> data
	)
	{
		if (buffer == null || buffer._size < (ulong)data.Length)
		{
			buffer?.Dispose();
			buffer = Create(context, memoryProperties, usage, memoryType, data);
		}
		else
		{
			data.CopyTo(new Span<byte>((void*)buffer._mapped, data.Length));
		}
	}

	public static unsafe VkNvgBuffer Create(
		VkNvgContext context,
		PhysicalDeviceMemoryProperties memoryProperties,
		BufferUsageFlags usage,
		MemoryPropertyFlags memoryType,
		ReadOnlySpan<byte> data
	)
	{
		Vk api = context.Api;
		Device device = context.Device;
		ref AllocationCallbacks allocator = ref context.Allocator;

		BufferCreateInfo bufCreateInfo = new BufferCreateInfo
		{
			SType = StructureType.BufferCreateInfo,
			PNext = null,
			Flags = 0,
			Size = (ulong)data.Length,
			Usage = usage,
		};

		Buffer buffer;
		var result = api.CreateBuffer(device, &bufCreateInfo, allocator, &buffer);
		DebugUtils.Check(result);
		MemoryRequirements memReqs = default;

		api.GetBufferMemoryRequirements(device, buffer, &memReqs);

		Result res = memoryProperties.GetMemoryType(memReqs.MemoryTypeBits, memoryType, out var memoryTypeIndex);
		DebugUtils.Check(res);
		MemoryAllocateInfo memAlloc = new MemoryAllocateInfo
		{
			SType = StructureType.MemoryAllocateInfo,
			PNext = null,
			AllocationSize = memReqs.Size,
			MemoryTypeIndex = memoryTypeIndex,
		};

		DeviceMemory mem;
		var allocateMemoryResult = api.AllocateMemory(device, &memAlloc, null, &mem);
		DebugUtils.Check(allocateMemoryResult);

		void* mapped;
		var mapMemoryResult = api.MapMemory(device, mem, 0, memAlloc.AllocationSize, 0, &mapped);
		DebugUtils.Check(mapMemoryResult);
		data.CopyTo(new Span<byte>(mapped, data.Length));
		var bindBufferMemoryResult = api.BindBufferMemory(device, buffer, mem, 0);
		DebugUtils.Check(bindBufferMemoryResult);
		VkNvgBuffer buf = new VkNvgBuffer(context, buffer)
		{
			_mem = mem,
			_size = memAlloc.AllocationSize,
			_mapped = (IntPtr)mapped,
			_initialised = true,
		};
		return buf;
	}
}