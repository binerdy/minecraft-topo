using System.Buffers.Binary;
using System.Text;

namespace MinecraftTopo.Core.Nbt;

/// <summary>A parsed NBT tag (used for verification and debugging only).</summary>
public sealed class NbtTag
{
    public TagType Type { get; init; }
    public string Name { get; init; } = "";
    public object? Value { get; init; }

    public IReadOnlyList<NbtTag> Children => Value as IReadOnlyList<NbtTag> ?? [];

    public NbtTag? this[string name] => Children.FirstOrDefault(c => c.Name == name);

    public override string ToString() => Type switch
    {
        TagType.Compound => $"{Name}: {{{Children.Count} entries}}",
        TagType.List => $"{Name}: [{Children.Count} items]",
        TagType.ByteArray => $"{Name}: byte[{((byte[])Value!).Length}]",
        TagType.IntArray => $"{Name}: int[{((int[])Value!).Length}]",
        TagType.LongArray => $"{Name}: long[{((long[])Value!).Length}]",
        _ => $"{Name}: {Value}",
    };

    /// <summary>Renders the tag tree as indented text, truncating long lists.</summary>
    public string Dump(int maxListItems = 8)
    {
        var sb = new StringBuilder();
        Dump(this, sb, 0, maxListItems);
        return sb.ToString();
    }

    private static void Dump(NbtTag t, StringBuilder sb, int depth, int maxListItems)
    {
        sb.Append(' ', depth * 2).Append(t.Type).Append(' ').AppendLine(t.ToString());
        if (t.Type is TagType.Compound or TagType.List)
        {
            int i = 0;
            foreach (var c in t.Children)
            {
                if (i++ >= maxListItems && t.Type == TagType.List)
                {
                    sb.Append(' ', (depth + 1) * 2).AppendLine($"... {t.Children.Count - maxListItems} more");
                    break;
                }
                Dump(c, sb, depth + 1, maxListItems);
            }
        }
    }
}

/// <summary>Reads uncompressed big-endian NBT.</summary>
public sealed class NbtReader
{
    private readonly Stream _s;
    private readonly byte[] _buf = new byte[8];

    public NbtReader(Stream s)
    {
        _s = s;
    }

    public NbtTag ReadRoot()
    {
        var type = (TagType)ReadByte();
        if (type == TagType.End) return new NbtTag { Type = TagType.End };
        string name = ReadString();
        return new NbtTag { Type = type, Name = name, Value = ReadPayload(type) };
    }

    private object? ReadPayload(TagType type) => type switch
    {
        TagType.End => null,
        TagType.Byte => (sbyte)ReadByte(),
        TagType.Short => ReadInt16(),
        TagType.Int => ReadInt32(),
        TagType.Long => ReadInt64(),
        TagType.Float => BitConverter.Int32BitsToSingle(ReadInt32()),
        TagType.Double => BitConverter.Int64BitsToDouble(ReadInt64()),
        TagType.ByteArray => ReadBytes(ReadInt32()),
        TagType.String => ReadString(),
        TagType.List => ReadList(),
        TagType.Compound => ReadCompound(),
        TagType.IntArray => ReadIntArray(),
        TagType.LongArray => ReadLongArray(),
        _ => throw new InvalidDataException($"Unknown NBT tag type {(int)type}."),
    };

    private List<NbtTag> ReadCompound()
    {
        var list = new List<NbtTag>();
        while (true)
        {
            var type = (TagType)ReadByte();
            if (type == TagType.End) return list;
            string name = ReadString();
            list.Add(new NbtTag { Type = type, Name = name, Value = ReadPayload(type) });
        }
    }

    private List<NbtTag> ReadList()
    {
        var elemType = (TagType)ReadByte();
        int count = ReadInt32();
        var list = new List<NbtTag>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(new NbtTag { Type = elemType, Name = "", Value = ReadPayload(elemType) });
        }
        return list;
    }

    private int[] ReadIntArray()
    {
        int n = ReadInt32();
        var arr = new int[n];
        for (int i = 0; i < n; i++) arr[i] = ReadInt32();
        return arr;
    }

    private long[] ReadLongArray()
    {
        int n = ReadInt32();
        var arr = new long[n];
        for (int i = 0; i < n; i++) arr[i] = ReadInt64();
        return arr;
    }

    private byte ReadByte()
    {
        int b = _s.ReadByte();
        if (b < 0) throw new EndOfStreamException();
        return (byte)b;
    }

    private byte[] ReadBytes(int n)
    {
        var arr = new byte[n];
        _s.ReadExactly(arr);
        return arr;
    }

    private short ReadInt16() { _s.ReadExactly(_buf, 0, 2); return BinaryPrimitives.ReadInt16BigEndian(_buf); }
    private int ReadInt32() { _s.ReadExactly(_buf, 0, 4); return BinaryPrimitives.ReadInt32BigEndian(_buf); }
    private long ReadInt64() { _s.ReadExactly(_buf, 0, 8); return BinaryPrimitives.ReadInt64BigEndian(_buf); }

    private string ReadString()
    {
        _s.ReadExactly(_buf, 0, 2);
        int len = BinaryPrimitives.ReadUInt16BigEndian(_buf);
        return Encoding.UTF8.GetString(ReadBytes(len));
    }
}
