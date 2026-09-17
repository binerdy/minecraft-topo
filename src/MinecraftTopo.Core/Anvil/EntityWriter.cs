using System.IO.Compression;
using MinecraftTopo.Core.Nbt;
using MinecraftTopo.Core.Terrain;

namespace MinecraftTopo.Core.Anvil;

/// <summary>
/// Writes entity region files (dimensions/minecraft/overworld/entities/r.x.z.mca). Each chunk holds
/// {Position [cx, cz], DataVersion, Entities [...]}, the layout observed in a 26.3 world.
/// </summary>
public static class EntityWriter
{
    public static string EntitiesDir(string worldDir) => Path.Combine(worldDir, "dimensions", "minecraft", "overworld", "entities");

    public static void WriteVillagers(string worldDir, IReadOnlyList<VillagerSpawn> villagers, int seed)
    {
        var byRegion = new Dictionary<(int rx, int rz), Dictionary<(int cx, int cz), List<VillagerSpawn>>>();
        foreach (var v in villagers)
        {
            int cx = v.X >> 4, cz = v.Z >> 4;
            var regionKey = (cx >> 5, cz >> 5);
            if (!byRegion.TryGetValue(regionKey, out var chunks)) byRegion[regionKey] = chunks = new();
            if (!chunks.TryGetValue((cx, cz), out var list)) chunks[(cx, cz)] = list = new();
            list.Add(v);
        }

        string dir = EntitiesDir(worldDir);
        Directory.CreateDirectory(dir);
        int n = 0;
        foreach (var ((rx, rz), chunks) in byRegion)
        {
            var region = new RegionFile(rx, rz);
            foreach (var ((cx, cz), list) in chunks)
            {
                region.SetChunk(cx & 31, cz & 31, BuildChunk(cx, cz, list, seed, ref n));
            }
            region.Save(Path.Combine(dir, RegionFile.FileName(rx, rz)));
        }
    }

    private static byte[] BuildChunk(int cx, int cz, List<VillagerSpawn> villagers, int seed, ref int counter)
    {
        using var raw = new MemoryStream(1024);
        var w = new NbtWriter(raw);
        w.BeginCompound("");
        w.WriteIntArray("Position", [cx, cz]);
        w.WriteInt("DataVersion", ChunkBuilder.DataVersion);
        w.BeginList("Entities", TagType.Compound, villagers.Count);
        foreach (var v in villagers)
        {
            counter++;
            w.BeginCompound(null);
            w.WriteString("id", "minecraft:villager");
            // Deterministic version-4-style UUID from the seed and a running number.
            uint a = TreePlanner.Hash(counter, 1, seed), b = TreePlanner.Hash(counter, 2, seed), c = TreePlanner.Hash(counter, 3, seed), d = TreePlanner.Hash(counter, 4, seed);
            b = (b & 0xFFFF0FFFu) | 0x00004000u;
            c = (c & 0x3FFFFFFFu) | 0x80000000u;
            w.WriteIntArray("UUID", [(int)a, (int)b, (int)c, (int)d]);
            w.BeginList("Pos", TagType.Double, 3);
            w.WriteDouble(null, v.X + 0.5);
            w.WriteDouble(null, v.Y);
            w.WriteDouble(null, v.Z + 0.5);
            w.BeginList("Motion", TagType.Double, 3);
            w.WriteDouble(null, 0); w.WriteDouble(null, 0); w.WriteDouble(null, 0);
            w.BeginList("Rotation", TagType.Float, 2);
            w.WriteFloat(null, v.Yaw); w.WriteFloat(null, 0f);
            w.WriteFloat("Health", 20f);
            w.WriteShort("Air", 300);
            w.WriteShort("Fire", 0);
            w.WriteBool("OnGround", true);
            w.WriteBool("Invulnerable", false);
            w.WriteBool("PersistenceRequired", true);
            w.WriteInt("PortalCooldown", 0);
            w.EndCompound();
        }
        w.EndCompound();

        using var output = new MemoryStream();
        using (var z = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(z);
        }
        return output.ToArray();
    }
}
