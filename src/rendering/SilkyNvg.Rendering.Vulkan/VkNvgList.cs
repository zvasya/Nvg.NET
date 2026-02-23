namespace SilkyNvg.Rendering.Vulkan;

public class VkNvgList<T>(int minAlloc)
{
    public Span<T> Data => _data.AsSpan(0, Length);
    public int Length { get; private set; }

    T[]? _data;


    public int EnsureCapacity(int n)
    {
        int ret = 0;
        var pathsDataLength = (_data?.Length ?? 0);
        if (Length + n > pathsDataLength)
        {
            int cpaths = Math.Max(Length + n, minAlloc) + pathsDataLength / 2; // 1.5x Overallocate
            Array.Resize(ref _data, cpaths);
        }

        ret = Length;
        Length += n;
        return ret;
    }

    public void Clear()
    {
        Length = 0;
    }

    public int IndexOf(T item)
    {
        if (_data != null)
            return Array.IndexOf(_data, item);
        return -1;
    }
}