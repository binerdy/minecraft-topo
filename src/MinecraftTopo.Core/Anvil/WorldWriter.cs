using System.IO.Compression;
using MinecraftTopo.Core.Nbt;

namespace MinecraftTopo.Core.Anvil;

/// <summary>
/// Creates the Java Edition 26.x world folder layout and the world-level files. Existing
/// level.dat / world_gen_settings.dat are never overwritten, so the tool can also fill a world
/// that was created in-game (the recommended fallback if 26.3 rejects the generated files).
/// </summary>
public static class WorldWriter
{
    public const string TallWorldPackName = "tall_world";

    /// <summary>Writes icon.png, shown in the world list.</summary>
    public static void WriteIcon(string worldDir, byte[] png) => File.WriteAllBytes(Path.Combine(worldDir, "icon.png"), png);

    /// <summary>True once the game has saved the world (a player data folder with content or session.lock).</summary>
    public static bool WasPlayed(string worldDir)
    {
        string players = Path.Combine(worldDir, "playerdata");
        return (Directory.Exists(players) && Directory.EnumerateFiles(players).Any()) || File.Exists(Path.Combine(worldDir, "session.lock"));
    }

    /// <summary>
    /// Writes the data pack that overrides the overworld dimension type with the tall height
    /// (Y -2032..2031). The JSON is the vanilla 26.3 overworld definition with min_y, height,
    /// logical_height and the cloud height changed; level.dat enables the pack as "file/tall_world".
    /// </summary>
    public static void WriteTallWorldDataPack(string worldDir, int minY, int height, int cloudHeight)
    {
        string packDir = Path.Combine(worldDir, "datapacks", TallWorldPackName);
        Directory.CreateDirectory(Path.Combine(packDir, "data", "minecraft", "dimension_type"));
        File.WriteAllText(Path.Combine(packDir, "pack.mcmeta"), "{\n  \"pack\": {\n    \"description\": \"Minecraft Topo: tall overworld (Y -2032..2031) for 1 m per block relief\",\n    \"min_format\": 121,\n    \"max_format\": 200\n  }\n}\n");
        string json = OverworldDimensionType
            .Replace("__MIN_Y__", minY.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("__HEIGHT__", height.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("__CLOUDS__", cloudHeight.ToString(System.Globalization.CultureInfo.InvariantCulture));
        File.WriteAllText(Path.Combine(packDir, "data", "minecraft", "dimension_type", "overworld.json"), json);
    }

    // Copied from the 26.3 jar (data/minecraft/dimension_type/overworld.json) with placeholders.
    private const string OverworldDimensionType = """
        {
          "ambient_light": 0.0,
          "attributes": {
            "minecraft:audio/ambient_sounds": {
              "mood": { "block_search_extent": 8, "offset": 2.0, "sound": "minecraft:ambient.cave", "tick_delay": 6000 }
            },
            "minecraft:audio/background_music": {
              "creative": { "max_delay": 24000, "min_delay": 12000, "sound": "minecraft:music.creative" },
              "default": { "max_delay": 24000, "min_delay": 12000, "sound": "minecraft:music.game" }
            },
            "minecraft:gameplay/bed_rule": { "can_set_spawn": "always", "can_sleep": "when_dark", "error_message": { "translate": "block.minecraft.bed.no_sleep" } },
            "minecraft:gameplay/nether_portal_spawns_piglin": true,
            "minecraft:gameplay/respawn_anchor_works": false,
            "minecraft:gameplay/straw_bed_rule": { "can_set_spawn": "never", "can_sleep": "when_dark", "destroy_on_leave": true, "error_message": { "translate": "block.minecraft.bed.no_sleep" } },
            "minecraft:visual/ambient_light_color": "#0a0a0a",
            "minecraft:visual/cloud_color": "#ccffffff",
            "minecraft:visual/cloud_height": __CLOUDS__,
            "minecraft:visual/fog_color": "#c0d8ff",
            "minecraft:visual/sky_color": "#78a7ff"
          },
          "coordinate_scale": 1.0,
          "default_clock": "minecraft:overworld",
          "has_ceiling": false,
          "has_ender_dragon_fight": false,
          "has_skylight": true,
          "height": __HEIGHT__,
          "infiniburn": "#minecraft:infiniburn_overworld",
          "logical_height": __HEIGHT__,
          "min_y": __MIN_Y__,
          "monster_spawn_block_light_limit": 0,
          "monster_spawn_light_level": { "type": "minecraft:uniform", "max_inclusive": 7, "min_inclusive": 0 },
          "timelines": "#minecraft:in_overworld"
        }
        """;
    public const string VersionName = "26.3";
    public const int DataVersion = ChunkBuilder.DataVersion;

    public static string OverworldRegionDir(string worldDir) =>
        Path.Combine(worldDir, "dimensions", "minecraft", "overworld", "region");

    public static string LevelDatPath(string worldDir) => Path.Combine(worldDir, "level.dat");

    public static string WorldGenSettingsPath(string worldDir) =>
        Path.Combine(worldDir, "data", "minecraft", "world_gen_settings.dat");

    public static void EnsureLayout(string worldDir)
    {
        Directory.CreateDirectory(OverworldRegionDir(worldDir));
        Directory.CreateDirectory(Path.Combine(worldDir, "dimensions", "minecraft", "overworld", "entities"));
        Directory.CreateDirectory(Path.Combine(worldDir, "dimensions", "minecraft", "overworld", "poi"));
        Directory.CreateDirectory(Path.Combine(worldDir, "data", "minecraft"));
        Directory.CreateDirectory(Path.Combine(worldDir, "players", "data"));
        Directory.CreateDirectory(Path.Combine(worldDir, "datapacks"));
    }

    /// <summary>Returns true when the folder looks like an existing world (level.dat or region files).</summary>
    public static bool Exists(string worldDir) =>
        File.Exists(LevelDatPath(worldDir)) || HasRegions(worldDir);

    /// <summary>
    /// Removes the world's own files (dimensions, data, players, level.dat and friends) so the
    /// folder can be regenerated from scratch. Other files in the folder are left alone.
    /// </summary>
    public static void RemoveWorldFiles(string worldDir)
    {
        if (!Directory.Exists(worldDir)) return;
        foreach (var sub in new[] { "dimensions", "data", "players", "region", "entities", "poi", "DIM-1", "DIM1", "playerdata", "advancements", "stats" })
        {
            string dir = Path.Combine(worldDir, sub);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        foreach (var file in new[] { "level.dat", "level.dat_old", "level.dat_new", "session.lock" })
        {
            string path = Path.Combine(worldDir, file);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>Returns true when the folder already has region files in the 26.x overworld location.</summary>
    public static bool HasRegions(string worldDir)
    {
        string dir = OverworldRegionDir(worldDir);
        return Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*.mca").Any();
    }

    /// <summary>Writes level.dat unless it exists. Returns true when written.</summary>
    public static bool WriteLevelDatIfMissing(string worldDir, string levelName, int spawnX, int spawnY, int spawnZ, long seed,
        GameMode gameMode = GameMode.Creative, Difficulty difficulty = Difficulty.Peaceful, bool tallWorld = false, bool overwrite = false)
    {
        string path = LevelDatPath(worldDir);
        if (File.Exists(path) && !overwrite) return false;

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var gz = new GZipStream(fs, CompressionLevel.Optimal);
        var w = new NbtWriter(gz);
        w.BeginCompound("");
        w.BeginCompound("Data");

        w.WriteInt("DataVersion", DataVersion);
        w.WriteInt("version", 19133); // NBT version of level.dat (Anvil)
        w.BeginCompound("Version");
        w.WriteInt("Id", DataVersion);
        w.WriteString("Name", VersionName);
        w.WriteString("Series", "main");
        w.WriteBool("Snapshot", false);
        w.EndCompound();

        w.WriteString("LevelName", levelName);
        w.WriteInt("GameType", gameMode switch { GameMode.Creative => 1, GameMode.Adventure => 2, _ => 0 });
        w.WriteBool("allowCommands", gameMode == GameMode.Creative);
        w.BeginCompound("difficulty_settings");
        w.WriteString("difficulty", gameMode == GameMode.Hardcore ? "hard" : difficulty.ToString().ToLowerInvariant());
        w.WriteBool("hardcore", gameMode == GameMode.Hardcore);
        w.WriteBool("locked", false);
        w.EndCompound();

        w.WriteBool("initialized", true);
        w.WriteLong("LastPlayed", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        w.WriteLong("Time", 0);
        w.WriteLong("DayTime", 6000);
        w.WriteBool("WasModded", false);

        w.BeginCompound("spawn");
        w.WriteString("dimension", "minecraft:overworld");
        w.WriteIntArray("pos", [spawnX, spawnY, spawnZ]);
        w.WriteFloat("yaw", 0f);
        w.WriteFloat("pitch", 0f);
        w.EndCompound();

        w.BeginCompound("DataPacks");
        w.WriteStringList("Enabled", tallWorld ? ["vanilla", "file/" + TallWorldPackName] : ["vanilla"]);
        w.WriteStringList("Disabled", []);
        w.EndCompound();

        w.WriteStringList("enabled_features", ["minecraft:vanilla"]);
        w.WriteStringList("ServerBrands", ["vanilla"]);

        w.EndCompound(); // Data
        w.EndCompound(); // root
        return true;
    }

    /// <summary>
    /// Writes data/minecraft/world_gen_settings.dat unless it exists: a flat generator with no
    /// layers so that chunks the tool did not write stay void. Returns true when written.
    /// </summary>
    public static bool WriteWorldGenSettingsIfMissing(string worldDir, long seed)
    {
        string path = WorldGenSettingsPath(worldDir);
        if (File.Exists(path)) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var gz = new GZipStream(fs, CompressionLevel.Optimal);
        var w = new NbtWriter(gz);
        w.BeginCompound("");
        w.WriteInt("DataVersion", DataVersion);
        w.BeginCompound("data");
        w.WriteLong("seed", seed);
        w.WriteBool("generate_structures", false);
        w.WriteBool("bonus_chest", false);

        w.BeginCompound("dimensions");

        w.BeginCompound("minecraft:overworld");
        w.WriteString("type", "minecraft:overworld");
        w.BeginCompound("generator");
        w.WriteString("type", "minecraft:flat");
        w.BeginCompound("settings");
        w.BeginList("layers", TagType.Compound, 0);
        w.WriteString("biome", "minecraft:plains");
        w.WriteBool("features", false);
        w.WriteBool("lakes", false);
        w.WriteStringList("structure_overrides", []);
        w.EndCompound(); // settings
        w.EndCompound(); // generator
        w.EndCompound(); // overworld

        w.BeginCompound("minecraft:the_nether");
        w.WriteString("type", "minecraft:the_nether");
        w.BeginCompound("generator");
        w.WriteString("type", "minecraft:noise");
        w.WriteString("settings", "minecraft:nether");
        w.BeginCompound("biome_source");
        w.WriteString("type", "minecraft:multi_noise");
        w.WriteString("preset", "minecraft:nether");
        w.EndCompound();
        w.EndCompound();
        w.EndCompound();

        w.BeginCompound("minecraft:the_end");
        w.WriteString("type", "minecraft:the_end");
        w.BeginCompound("generator");
        w.WriteString("type", "minecraft:noise");
        w.WriteString("settings", "minecraft:end");
        w.BeginCompound("biome_source");
        w.WriteString("type", "minecraft:the_end");
        w.EndCompound();
        w.EndCompound();
        w.EndCompound();

        w.EndCompound(); // dimensions
        w.EndCompound(); // data
        w.EndCompound(); // root
        return true;
    }
}
