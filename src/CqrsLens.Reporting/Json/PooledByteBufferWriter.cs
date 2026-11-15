using System.Buffers;
using System.Text;

namespace CqrsLens;

/// <summary>ArrayPool-backed byte buffer writer for single-pass UTF-8 JSON report encoding.</summary>
internal sealed class PooledByteBufferWriter : IBufferWriter<byte>, IDisposable
{
    private byte[] _buffer;
    private int _index;

    internal PooledByteBufferWriter(int initialCapacity)
    {
        _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(256, initialCapacity));
    }

    public void Advance(int count) => _index += count;

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsMemory(_index);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsSpan(_index);
    }

    internal void AppendByte(byte value)
    {
        EnsureCapacity(1);
        _buffer[_index++] = value;
    }

    internal string ToUtf8String() => Encoding.UTF8.GetString(_buffer.AsSpan(0, _index));

    public void Dispose()
    {
        var buffer = _buffer;
        _buffer = Array.Empty<byte>();
        if (buffer.Length > 0)
            ArrayPool<byte>.Shared.Return(buffer);
    }

    private void EnsureCapacity(int sizeHint)
    {
        if (sizeHint <= 0)
            sizeHint = 256;

        var available = _buffer.Length - _index;
        if (available >= sizeHint)
            return;

        var needed = checked(_index + sizeHint);
        var nextSize = Math.Max(needed, _buffer.Length * 2);
        var next = ArrayPool<byte>.Shared.Rent(nextSize);
        _buffer.AsSpan(0, _index).CopyTo(next);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = next;
    }
}
