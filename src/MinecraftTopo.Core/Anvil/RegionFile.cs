using System.Buffers.Binary;

namespace MinecraftTopo.Core.Anvil;

/// <summary>
/// Writes an Anvil region file (r.x.z.mca): 4 KiB sectors, a 1024-entry location table and a
/// 1024-entry timestamp table, then zlib-compressed chunk payloads.
/// </summary>
public sealed class RegionFile
{
    public const int SectorSize = 4096;
    private const byte CompressionZlib = 2;

    private readonly byte[]?[] _chunks = new byte[]?[1024];

    public int RegionX { get; }
    public int RegionZ { get; }

    public RegionFile(int regionX, int regionZ)
    {
        RegionX = regionX;
        RegionZ = regionZ;
    }

    public static string FileName(int regionX, int regionZ) => $"r.{regionX}.{regionZ}.mca";

    public int ChunkCount => _chunks.Count(c => c is not null);

    /// <summary>Stores an already zlib-compressed chunk at local coordinates (0..31).</summary>
    public void SetChunk(int localX, int localZ, byte[] zlibData)
    {
        if (localX is < 0 or > 31 || localZ is < 0 or > 31) throw new ArgumentOutOfRangeException(nameof(localX));
        int sectors = (zlibData.Length + 5 + SectorSize - 1) / SectorSize;
        if (sectors > 255) throw new InvalidOperationException($"Chunk ({localX},{localZ}) is too large for a region file ({zlibData.Length} bytes).");
        _chunks[localZ * 32 + localX] = zlibData;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
        Write(fs);
    }

    public void Write(Stream s)
    {
        var header = new byte[SectorSize * 2];
        int timestamp = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        int nextSector = 2;
        var order = new List<int>(1024);

        for (int i = 0; i < 1024; i++)
        {
            var data = _chunks[i];
            if (data is null) continue;
            int sectors = (data.Length + 5 + SectorSize - 1) / SectorSize;
            int location = (nextSector << 8) | (sectors & 0xFF);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(i * 4), location);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(SectorSize + i * 4), timestamp);
            nextSector += sectors;
            order.Add(i);
        }

        s.Write(header);
        var pad = new byte[SectorSize];
        var lenBuf = new byte[5];
        foreach (int i in order)
        {
            var data = _chunks[i]!;
            BinaryPrimitives.WriteInt32BigEndian(lenBuf, data.Length + 1);
            lenBuf[4] = CompressionZlib;
            s.Write(lenBuf);
            s.Write(data);
            int used = (data.Length + 5) % SectorSize;
            if (used != 0) s.Write(pad, 0, SectorSize - used);
        }
    }
}
