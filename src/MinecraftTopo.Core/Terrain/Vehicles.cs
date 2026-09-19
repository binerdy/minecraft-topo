using MinecraftTopo.Core.Water;

namespace MinecraftTopo.Core.Terrain;

/// <summary>Boats at jetties and boat stops, minecarts at railway stations.</summary>
public static class VehiclePlanner
{
    public static List<MobSpawn> Plan(ClassifiedTerrain t, IReadOnlyList<PointOfInterest> points, double metresPerBlock, long seed)
    {
        var result = new List<MobSpawn>();
        int w = t.Width, h = t.Height;
        int reach = Math.Clamp((int)Math.Round(120 / metresPerBlock), 3, 120);

        // stations: a minecart on the nearest rail
        foreach (var p in points.Where(p => p.Kind == "station"))
        {
            if (t.Rail is null) break;
            if (Nearest(w, h, p.X, p.Z, reach, i => (t.Rail[i] & RailFlag.Track) != 0) is { } cell)
            {
                int x = cell % w, z = cell / w;
                int surface = t.WaterY[cell] != ClassifiedTerrain.NoWater && t.WaterY[cell] >= t.TopY[cell] ? t.WaterY[cell] + 1 : t.TopY[cell];
                result.Add(new MobSpawn("minecraft:minecart", x, surface + 1, z, 0));
            }
        }
        // boat stops: a boat on the nearest water
        foreach (var p in points.Where(p => p.Kind == "pier"))
        {
            if (Nearest(w, h, p.X, p.Z, reach, i => t.WaterY[i] != ClassifiedTerrain.NoWater && t.WaterY[i] - t.TopY[i] >= 1) is { } cell)
            {
                int x = cell % w, z = cell / w;
                result.Add(new MobSpawn("minecraft:oak_boat", x, t.WaterY[cell], z, TreePlanner.Hash(x, z, seed) % 360));
            }
        }
        // jetties: about one boat in three at the water end of a jetty
        if (t.Wall is not null)
        {
            var seen = new HashSet<int>();
            for (int z = 1; z < h - 1; z++)
                for (int x = 1; x < w - 1; x++)
                {
                    int i = z * w + x;
                    if (t.Wall[i] != Anvil.Blocks.Jetty || seen.Contains(i)) continue;
                    // flood the jetty so each one counts once
                    var stack = new Stack<int>();
                    stack.Push(i);
                    seen.Add(i);
                    int best = -1;
                    while (stack.Count > 0)
                    {
                        int c = stack.Pop();
                        foreach (int n in new[] { c - 1, c + 1, c - w, c + w })
                        {
                            if (n < 0 || n >= w * h) continue;
                            if (t.Wall[n] == Anvil.Blocks.Jetty) { if (seen.Add(n)) stack.Push(n); }
                            else if (t.WaterY[n] != ClassifiedTerrain.NoWater && t.WaterY[n] - t.TopY[n] >= 1 && best < 0) best = n;
                        }
                    }
                    if (best >= 0 && TreePlanner.Hash(x, z, seed ^ 0xB0A7) % 3 == 0)
                        result.Add(new MobSpawn("minecraft:oak_boat", best % w, t.WaterY[best], best / w, TreePlanner.Hash(z, x, seed) % 360));
                }
        }
        return result;
    }

    private static int? Nearest(int w, int h, int x, int z, int radius, Func<int, bool> ok)
    {
        for (int r = 0; r <= radius; r++)
            for (int dz = -r; dz <= r; dz++)
            {
                int step = Math.Abs(dz) == r ? 1 : 2 * r;
                for (int dx = -r; dx <= r; dx += Math.Max(1, step))
                {
                    int cx = x + dx, cz = z + dz;
                    if (cx < 0 || cz < 0 || cx >= w || cz >= h) continue;
                    int i = cz * w + cx;
                    if (ok(i)) return i;
                }
            }
        return null;
    }
}
