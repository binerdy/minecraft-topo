namespace MinecraftTopo.Core.Terrain;

/// <summary>An entity to write: vanilla id, block position (feet), heading and an id-specific variant.</summary>
public readonly record struct MobSpawn(string Id, int X, int Y, int Z, float Yaw, int Variant = 0, string? Tag = null);

/// <summary>
/// Places animals and fish where they belong in the Swiss landscape: cows, sheep, pigs, chickens
/// and horses on pastures and near farms, goats and rabbits in the alpine zone, foxes and wolves
/// in the forests, frogs in wetlands, salmon in rivers and lakes and squid in deep lakes.
/// Everything is deterministic from the world seed.
/// </summary>
public static class WildlifePlanner
{
    public const int MaxMobs = 12000;

    /// <summary>Max health per entity so the written value is not clamped.</summary>
    public static float Health(string id) => id switch
    {
        "minecraft:cow" or "minecraft:pig" or "minecraft:goat" or "minecraft:fox" or "minecraft:frog" or "minecraft:squid" => 10f,
        "minecraft:sheep" or "minecraft:wolf" => 8f,
        "minecraft:chicken" => 4f,
        "minecraft:rabbit" => 3f,
        "minecraft:salmon" => 3f,
        "minecraft:horse" => 22f,
        _ => 20f,
    };

    public static List<MobSpawn> Plan(ClassifiedTerrain t, float[] elevation, double metresPerBlock, long seed, CancellationToken ct)
    {
        var result = new List<MobSpawn>();
        int w = t.Width, h = t.Height;
        int spacing = Math.Clamp((int)Math.Round(40 / metresPerBlock), 2, 40);
        int near = Math.Clamp((int)Math.Round(40 / metresPerBlock), 1, 40);
        // "farm" = a building nearby but not a dense settlement (less than 8 % of the surroundings built over)
        bool[]? nearBuilding = t.BuildingHeight is null ? null : GridStats.Near(t.BuildingHeight, w, h, near, ct);
        float[]? builtFraction = t.BuildingHeight is null ? null : GridStats.BoxFraction(t.BuildingHeight, w, h, Math.Clamp((int)Math.Round(80 / metresPerBlock), 2, 80), ct);
        var taken = new HashSet<int>();
        var counts = new Dictionary<string, int>();
        // Densities are tuned for a few km²; very large worlds get proportionally fewer groups so the cap is spread evenly.
        double samples = (double)(w / spacing + 1) * (h / spacing + 1);
        double scale = Math.Min(1.0, 400_000.0 / samples);
        bool Roll(uint r, int perTenThousand) => r < perTenThousand * scale;

        void Group(string id, int x, int z, int count, int radius, Func<int, int> variant, bool water = false)
        {
            if (result.Count >= MaxMobs) return;
            int placed = 0;
            for (int attempt = 0; attempt < count * 4 && placed < count; attempt++)
            {
                uint hh = TreePlanner.Hash(x * 7 + attempt, z * 13 + placed, seed ^ 0x600D);
                int dx = (int)(hh % (uint)(2 * radius + 1)) - radius, dz = (int)((hh >> 8) % (uint)(2 * radius + 1)) - radius;
                int cx = x + dx, cz = z + dz;
                if (cx < 0 || cz < 0 || cx >= w || cz >= h) continue;
                int i = cz * w + cx;
                if (taken.Contains(i)) continue;
                bool isWater = t.Kind[i] == Surface.Water || t.WaterY[i] != ClassifiedTerrain.NoWater;
                if (water != isWater) continue;
                if (!water && (t.IsOccupied(i) || (t.Wall is not null && t.Wall[i] != 0))) continue;
                int y;
                if (water)
                {
                    int bottom = t.TopY[i] + 1, top = t.WaterY[i];
                    if (top - bottom < 1) continue;
                    y = bottom + (int)((hh >> 16) % (uint)(top - bottom + 1));
                }
                else y = t.TopY[i] + 1;
                if (y > t.WorldMaxY - 2) continue;
                taken.Add(i);
                result.Add(new MobSpawn(id, cx, y, cz, (hh >> 20) % 360, variant(placed)));
                counts[id] = counts.GetValueOrDefault(id) + 1;
                placed++;
            }
        }

        for (int gz = 0; gz < h; gz += spacing)
        {
            ct.ThrowIfCancellationRequested();
            for (int gx = 0; gx < w; gx += spacing)
            {
                uint hash = TreePlanner.Hash(gx, gz, seed ^ 0xA11FE);
                int x = gx + (int)(hash % (uint)spacing), z = gz + (int)((hash >> 6) % (uint)spacing);
                if (x >= w || z >= h) continue;
                int i = z * w + x;
                uint r = (hash >> 12) % 10000;
                var kind = t.Kind[i];
                float elev = elevation[i];
                bool forest = t.IsForest(x, z);
                bool farm = nearBuilding is not null && nearBuilding[i] && builtFraction![i] < 0.08f;
                int sheepColour(int k) { int c = (int)(TreePlanner.Hash(x + k, z, seed ^ 0x5EE9) % 100); return c < 70 ? 0 : c < 80 ? 8 : c < 88 ? 7 : c < 96 ? 15 : 12; }

                if (kind == Surface.Water || t.WaterY[i] != ClassifiedTerrain.NoWater)
                {
                    int depth = t.WaterY[i] - t.TopY[i];
                    if (depth < 1) continue;
                    if (Roll(r, 1400)) Group("minecraft:salmon", x, z, 2 + (int)(hash % 3), 3, k => k == 0 && depth >= 4 ? 1 : 0, water: true);
                    else if (Roll(r, 1600) && depth >= 6) Group("minecraft:squid", x, z, 1 + (int)(hash % 2), 2, _ => 0, water: true);
                    continue;
                }
                if (t.IsOccupied(i)) continue;

                if (kind == Surface.Mud)
                {
                    if (Roll(r, 900)) Group("minecraft:frog", x, z, 1 + (int)(hash % 2), 3, _ => 0);
                    continue;
                }
                bool alpine = elev >= 1800 || (elev >= 1500 && kind is Surface.Stone or Surface.Gravel);
                if (alpine && kind is Surface.Grass or Surface.Stone or Surface.Gravel)
                {
                    if (Roll(r, 600)) Group("minecraft:goat", x, z, 2 + (int)(hash % 3), 4, _ => 0);
                    else if (Roll(r, 900) && kind == Surface.Grass) Group("minecraft:rabbit", x, z, 2, 3, _ => 0);
                    else if (Roll(r, 1400) && kind == Surface.Grass && elev < 2300) Group("minecraft:sheep", x, z, 3 + (int)(hash % 3), 4, sheepColour);
                    continue;
                }
                if (kind != Surface.Grass && kind != Surface.Vineyard) continue;

                if (forest)
                {
                    if (Roll(r, 150)) Group("minecraft:fox", x, z, 1, 2, _ => elev > 1800 ? 1 : 0);
                    else if (Roll(r, 180) && elev > 1200) Group("minecraft:wolf", x, z, 1 + (int)(hash % 2), 3, _ => 0);
                    else if (Roll(r, 400)) Group("minecraft:rabbit", x, z, 2, 3, _ => 0);
                    continue;
                }

                // open pasture
                if (Roll(r, 500) && elev < 1600) Group("minecraft:cow", x, z, 2 + (int)(hash % 3), 4, _ => 0);
                else if (Roll(r, elev > 1200 ? 1500 : 1000)) Group("minecraft:sheep", x, z, 3 + (int)(hash % 4), 4, sheepColour);
                else if (Roll(r, 1700) && farm) Group("minecraft:pig", x, z, 2 + (int)(hash % 2), 3, _ => 0);
                else if (Roll(r, 2400) && farm) Group("minecraft:chicken", x, z, 3 + (int)(hash % 3), 3, _ => 0);
                else if (Roll(r, 2700) && farm && elev < 1200) Group("minecraft:horse", x, z, 2, 4, k => (int)(TreePlanner.Hash(x, z + k, seed ^ 0x40) % 7) | ((int)(TreePlanner.Hash(x + k, z, seed ^ 0x41) % 4) << 8));
                else if (Roll(r, 1200) && elev > 1400) Group("minecraft:rabbit", x, z, 2, 3, _ => 0);
            }
        }
        return result;
    }

    /// <summary>Summary such as "412 cows, 980 sheep, …" in descending order.</summary>
    public static string Summary(IReadOnlyList<MobSpawn> mobs)
    {
        var counts = mobs.GroupBy(m => m.Id).OrderByDescending(g => g.Count()).Select(g => $"{g.Count():N0} {g.Key.Replace("minecraft:", "")}{(g.Count() == 1 ? "" : "s")}".Replace("foxs", "foxes").Replace("wolfs", "wolves").Replace("sheeps", "sheep").Replace("salmons", "salmon").Replace("squids", "squid"));
        return string.Join(", ", counts);
    }

}
