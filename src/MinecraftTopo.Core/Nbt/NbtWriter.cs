using System.Buffers.Binary;
using System.Text;

namespace MinecraftTopo.Core.Nbt;

public enum TagType : byte
{
    End = 0, Byte = 1, Short = 2, Int = 3, Long = 4, Float = 5, Double = 6,
    ByteArray = 7, String = 8, List = 9, Compound = 10, IntArray = 11, LongArray = 12,
}

/// <summary>
/// Streaming big-endian NBT writer. Named tags are written with a type byte and name; inside a
/// list, elements are written without header (pass <c>null</c> as the name).
/// </summary>
public sealed class NbtWriter
{
    private readonly Stream _s;
    private readonly byte[] _buf = new byte[8];

    public NbtWriter(Stream s)
    {
        _s = s;
    }

    private void Header(TagType type, string? name)
    {
        if (name is null) return; // list element: payload only
        _s.WriteByte((byte)type);
        WriteStringPayload(name);
    }

    public void BeginCompound(string? name)
    {
        Header(TagType.Compound, name);
    }

    public void EndCompound()
    {
        _s.WriteByte((byte)TagType.End);
    }

    public void BeginList(string? name, TagType elementType, int count)
    {
        Header(TagType.List, name);
        _s.WriteByte((byte)(count == 0 ? TagType.End : elementType));
        WriteInt32(count);
    }

    public void WriteByte(string? name, sbyte value)
    {
        Header(TagType.Byte, name);
        _s.WriteByte((byte)value);
    }

    public void WriteBool(string? name, bool value) => WriteByte(name, (sbyte)(value ? 1 : 0));

    public void WriteShort(string? name, short value)
    {
        Header(TagType.Short, name);
        BinaryPrimitives.WriteInt16BigEndian(_buf, value);
        _s.Write(_buf, 0, 2);
    }

    public void WriteInt(string? name, int value)
    {
        Header(TagType.Int, name);
        WriteInt32(value);
    }

    public void WriteLong(string? name, long value)
    {
        Header(TagType.Long, name);
        BinaryPrimitives.WriteInt64BigEndian(_buf, value);
        _s.Write(_buf, 0, 8);
    }

    public void WriteFloat(string? name, float value)
    {
        Header(TagType.Float, name);
        BinaryPrimitives.WriteSingleBigEndian(_buf, value);
        _s.Write(_buf, 0, 4);
    }

    public void WriteDouble(string? name, double value)
    {
        Header(TagType.Double, name);
        BinaryPrimitives.WriteDoubleBigEndian(_buf, value);
        _s.Write(_buf, 0, 8);
    }

    public void WriteString(string? name, string value)
    {
        Header(TagType.String, name);
        WriteStringPayload(value);
    }

    public void WriteByteArray(string? name, ReadOnlySpan<byte> value)
    {
        Header(TagType.ByteArray, name);
        WriteInt32(value.Length);
        _s.Write(value);
    }

    public void WriteIntArray(string? name, ReadOnlySpan<int> value)
    {
        Header(TagType.IntArray, name);
        WriteInt32(value.Length);
        foreach (int v in value)
        {
            BinaryPrimitives.WriteInt32BigEndian(_buf, v);
            _s.Write(_buf, 0, 4);
        }
    }

    public void WriteLongArray(string? name, ReadOnlySpan<long> value)
    {
        Header(TagType.LongArray, name);
        WriteInt32(value.Length);
        var chunk = new byte[Math.Min(value.Length, 512) * 8];
        int offset = 0;
        while (offset < value.Length)
        {
            int n = Math.Min(512, value.Length - offset);
            for (int i = 0; i < n; i++)
            {
                BinaryPrimitives.WriteInt64BigEndian(chunk.AsSpan(i * 8), value[offset + i]);
            }
            _s.Write(chunk, 0, n * 8);
            offset += n;
        }
    }

    /// <summary>Convenience: a list of strings.</summary>
    public void WriteStringList(string? name, IReadOnlyList<string> values)
    {
        BeginList(name, TagType.String, values.Count);
        foreach (var v in values) WriteStringPayload(v);
    }

    private void WriteInt32(int value)
    {
        BinaryPrimitives.WriteInt32BigEndian(_buf, value);
        _s.Write(_buf, 0, 4);
    }

    private void WriteStringPayload(string value)
    {
        // Minecraft uses Java "modified UTF-8"; identical to UTF-8 for the strings we write.
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > ushort.MaxValue) throw new ArgumentException("NBT string too long.");
        BinaryPrimitives.WriteUInt16BigEndian(_buf, (ushort)bytes.Length);
        _s.Write(_buf, 0, 2);
        _s.Write(bytes);
    }
}
