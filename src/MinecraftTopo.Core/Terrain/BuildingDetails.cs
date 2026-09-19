using MinecraftTopo.Core.Water;

namespace MinecraftTopo.Core.Terrain;

/// <summary>Bits stored per cell in <see cref="ClassifiedTerrain.BuildingDetail"/>; the direction sits in bits 4-5 (0 N, 1 E, 2 S, 3 W).</summary>
public static class BuildingDetail
{
    public const byte Door = 1;
    public const byte Ladder = 2;
    public const byte Chimney = 4;
    public const byte Cellar = 8;
    public const int DirShift = 4;
}

/// <summary>
/// Adds the small things that make a house a house: a door on the side facing the street, a
/// ladder between the storeys, a chimney on the ridge, a cellar under larger houses, and
/// balconies on the street side of taller ones.
/// </summary>
public static class BuildingDetails
{
    private static readonly int[] Dx = [0, 1, 0, -1];
    private static readonly int[] Dz = [-1, 0, 1, 0];

    public static int StoreyBlocks(double metresPerBlock) => Math.Max(2, (int)Math.Round(3 / metresPerBlock));

    public static void Plan(ClassifiedTerrain t, LandCover cover, double metresPerBlock)
    {
        var ids = cover.BuildingId!;
        var height = t.BuildingHeight!;
        var floor = t.BuildingFloor!;
        var flags = t.BuildingFlags!;
        var kinds = t.BuildingKind;
        int w = t.Width, h = t.Height, n = w * h;
        var detail = new byte[n];
        var balconies = new ChunkIndex<(int X, int Z, int Y)>();
        int storey = StoreyBlocks(metresPerBlock);

        var cells = new Dictionary<int, List<int>>();
        for (int i = 0; i < n; i++)
        {
            if (ids[i] == 0 || height[i] == 0) continue;
            if (!cells.TryGetValue(ids[i], out var list)) cells[ids[i]] = list = new List<int>(32);
            list.Add(i);
        }

        foreach (var (id, list) in cells)
        {
            var kind = (BuildingKind)(kinds?[list[0]] ?? 0);
            byte f0 = flags[list[0]];
            bool church = (f0 & BuildingFlag.Church) != 0, industrial = (f0 & BuildingFlag.Industrial) != 0;
            bool house = kind is BuildingKind.House or BuildingKind.HighRise && !church;
            bool wantsDoor = kind is BuildingKind.House or BuildingKind.HighRise or BuildingKind.Tower or BuildingKind.Observatory or BuildingKind.CarPark or BuildingKind.Stadium or BuildingKind.Greenhouse;
            bool wantsLadder = kind is BuildingKind.House or BuildingKind.HighRise or BuildingKind.Tower or BuildingKind.CarPark or BuildingKind.Observatory;
            if (list.Count < 6) continue;

            // door: an edge cell whose outward neighbour is free, preferably towards a road
            int bestDoor = -1, bestDir = 2, bestScore = int.MinValue;
            if (wantsDoor)
            {
                foreach (int cell in list)
                {
                    if ((flags[cell] & BuildingFlag.Edge) == 0) continue;
                    int x = cell % w, z = cell / w;
                    for (int dir = 0; dir < 4; dir++)
                    {
                        int nx = x + Dx[dir], nz = z + Dz[dir];
                        if (nx < 0 || nz < 0 || nx >= w || nz >= h) continue;
                        int ncell = nz * w + nx;
                        if (ids[ncell] != 0 || t.Kind[ncell] == Surface.Water || t.WaterY[ncell] != ClassifiedTerrain.NoWater) continue;
                        // the door must open onto ground near the floor level
                        if (Math.Abs(t.TopY[ncell] - floor[cell]) > 2) continue;
                        int score = dir == 2 ? 1 : 0;
                        for (int k = 1; k <= 4; k++)
                        {
                            int mx = x + Dx[dir] * k, mz = z + Dz[dir] * k;
                            if (mx < 0 || mz < 0 || mx >= w || mz >= h) break;
                            int m = mz * w + mx;
                            if (ids[m] != 0) break;
                            if (t.Road is not null && t.Road[m] != 0) { score += 20 - k; break; }
                        }
                        if (score > bestScore) { bestScore = score; bestDoor = cell; bestDir = dir; }
                    }
                }
                if (bestDoor >= 0) detail[bestDoor] |= (byte)(BuildingDetail.Door | (bestDir << BuildingDetail.DirShift));
            }

            // ladder: an interior cell next to a wall
            bool hasStoreys = height[list[0]] >= storey + 2;
            if (wantsLadder && (hasStoreys || list.Count >= 16))
            {
                foreach (int cell in list)
                {
                    if ((flags[cell] & BuildingFlag.Edge) != 0) continue;
                    int x = cell % w, z = cell / w;
                    bool placed = false;
                    for (int dir = 0; dir < 4 && !placed; dir++)
                    {
                        int nx = x + Dx[dir], nz = z + Dz[dir];
                        if (nx < 0 || nz < 0 || nx >= w || nz >= h) continue;
                        int ncell = nz * w + nx;
                        if (ids[ncell] == id && (flags[ncell] & BuildingFlag.Edge) != 0)
                        {
                            detail[cell] |= (byte)(BuildingDetail.Ladder | (dir << BuildingDetail.DirShift));
                            placed = true;
                        }
                    }
                    if (placed) break;
                }
            }

            if (!house || industrial) continue;

            // chimney on the highest roof cell (interior when possible)
            int best = -1, bestRoof = int.MinValue;
            foreach (int cell in list)
            {
                int r = floor[cell] + height[cell] + ((flags[cell] & BuildingFlag.Edge) == 0 ? 1 : 0);
                if (r > bestRoof) { bestRoof = r; best = cell; }
            }
            if (best >= 0) detail[best] |= BuildingDetail.Chimney;

            // cellar under larger houses at fine scales
            if (list.Count >= 16 && metresPerBlock <= 2)
            {
                foreach (int cell in list) detail[cell] |= BuildingDetail.Cellar;
            }

            // balconies on the street side of houses with at least two storeys
            if (bestDoor >= 0 && height[list[0]] >= 2 * storey + 1)
            {
                foreach (int cell in list)
                {
                    if ((flags[cell] & BuildingFlag.Edge) == 0) continue;
                    int x = cell % w, z = cell / w;
                    if ((x + z) % 3 != 0) continue;
                    int nx = x + Dx[bestDir], nz = z + Dz[bestDir];
                    if (nx < 0 || nz < 0 || nx >= w || nz >= h) continue;
                    int ncell = nz * w + nx;
                    if (ids[ncell] != 0 || (t.Road is not null && t.Road[ncell] != 0) || t.WaterY[ncell] != ClassifiedTerrain.NoWater) continue;
                    if ((detail[cell] & BuildingDetail.Door) != 0) continue;
                    // only walls that really face outward in the door direction
                    int bx = x - Dx[bestDir], bz = z - Dz[bestDir];
                    if (bx < 0 || bz < 0 || bx >= w || bz >= h || ids[bz * w + bx] != id) continue;
                    int fl = floor[cell], top = fl + height[cell];
                    for (int y = fl + storey; y < top - 1; y += storey)
                    {
                        if (y <= t.TopY[ncell] + 1) continue;
                        balconies.Add((nx, nz, y), nx, nz, nx, nz);
                    }
                }
            }
        }
        t.BuildingDetail = detail;
        t.Balconies = balconies;
    }
}
