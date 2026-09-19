namespace MinecraftTopo.Core.Terrain;

public enum Surface : byte
{
    Grass = 0,
    Stone = 1,
    Snow = 2,
    Sand = 3,
    Water = 4,
    Gravel = 5,   // scree
    Glacier = 6,  // snow over packed ice
    Mud = 7,      // wetland
    Vineyard = 8, // grass with bush rows
}

public enum GeologySource
{
    None,
    /// <summary>GK500, 1:500 000, whole country (map service).</summary>
    Gk500,
    /// <summary>GeoCover 1:25 000 sheets where published (per-sheet download), GK500 elsewhere.</summary>
    GeoCover,
}

public enum SurfaceStyle
{
    None,
    /// <summary>swissIMAGE orthophoto classified into meadow, cropland, bare soil, rock, sand and snow.</summary>
    Photo,
    /// <summary>swissIMAGE colours draped over the ground with the closest coloured blocks.</summary>
    PhotoBlocks,
    /// <summary>Siegfried map (1870-1949) draped over the ground.</summary>
    Siegfried,
    /// <summary>Dufour map (1845-1865) draped over the ground.</summary>
    Dufour,
    /// <summary>Today's national map draped over the ground.</summary>
    NationalMap,
}

/// <summary>Extra per-cell inputs to the classifier from optional data sources.</summary>
public sealed class ClassifyExtras
{
    /// <summary>Lake floor elevation (m) per cell, NaN where unknown.</summary>
    public float[]? LakeBed { get; init; }
    /// <summary>Ice mask and bed; the grid already carries the ice surface.</summary>
    public Glaciers.IceModel? Ice { get; init; }
    /// <summary>Vegetation/canopy height (m) per cell from the surface model.</summary>
    public float[]? Canopy { get; init; }
}

/// <summary>Options that control how real elevations map to block columns.</summary>
public sealed record TerrainOptions
{
    /// <summary>Lowest terrain block Y (the lowest real elevation lands here).</summary>
    public int BaseY { get; init; } = -60;

    /// <summary>Blocks per metre of real height; null = auto-fit into the world height.</summary>
    public double? VerticalScale { get; init; }

    /// <summary>Real elevation (m) below which everything is flooded; null = no global flooding.</summary>
    public double? WaterLevel { get; init; }

    /// <summary>Place lakes and rivers from swisstopo water surfaces.</summary>
    public bool WaterBodies { get; init; } = true;

    /// <summary>Plant trees in swisstopo forest areas.</summary>
    public bool Trees { get; init; } = true;

    /// <summary>Scatter grass, ferns and flowers on grass blocks.</summary>
    public bool Vegetation { get; init; } = true;

    /// <summary>Ores inside the terrain, bee nests, berries, mushrooms, pumpkins, clay and seagrass.</summary>
    public bool Resources { get; init; } = true;

    /// <summary>Roads from the landscape model laid onto the terrain surface.</summary>
    public bool Roads { get; init; }

    /// <summary>Railways from the landscape model: gravel bed with rails.</summary>
    public bool Rails { get; init; }

    /// <summary>Building footprints from the landscape model as simple walled boxes with a roof.</summary>
    public bool Buildings { get; init; }

    /// <summary>Power lines (pylons, poles, wires) and wind turbines from the landscape model.</summary>
    public bool PowerLines { get; init; }

    /// <summary>Villagers standing next to houses (needs buildings).</summary>
    public bool Villagers { get; init; }

    /// <summary>Standing signs with the street name along named roads (needs roads).</summary>
    public bool StreetSigns { get; init; }

    /// <summary>Rock types underground and on bare rock from the geological maps.</summary>
    public GeologySource Geology { get; init; } = GeologySource.Gk500;

    /// <summary>Which glacier state to build; years before today raise the surface to the historic ice.</summary>
    public Glaciers.GlacierYear GlacierYear { get; init; } = Glaciers.GlacierYear.Today;

    /// <summary>Fill today's glaciers with ice down to the modelled bed instead of a thin ice layer.</summary>
    public bool IceToBed { get; init; } = true;

    /// <summary>Real lake floors from swissBATHY3D where surveyed.</summary>
    public bool LakeFloors { get; init; } = true;

    /// <summary>Tree heights and crowns from the swissSURFACE3D surface model (about 20 MB per km²).</summary>
    public bool VegetationHeights { get; init; }

    /// <summary>Colour the ground from an image or map instead of the land cover classes.</summary>
    public SurfaceStyle SurfaceStyle { get; init; } = SurfaceStyle.None;

    /// <summary>Signs with peak, pass, hut, river, field and place names from swissNAMES3D.</summary>
    public bool PlaceNames { get; init; } = true;

    /// <summary>Walls, dams, cable cars, sports fields, runways, fountains, summit crosses and more from swissTLM3D.</summary>
    public bool Extras { get; init; } = true;

    /// <summary>Underground formation boundaries from the swissJURA3D model where it exists (108 MB one-time download).</summary>
    public bool Jura3d { get; init; }

    /// <summary>Cows, sheep, pigs, chickens and horses on pastures and farms, goats and rabbits in the alpine zone, foxes and wolves in forests, frogs in wetlands, salmon and squid in the water.</summary>
    public bool Wildlife { get; init; } = true;

    /// <summary>Ripe wheat, carrots, potatoes and beetroots on cropland (orthophoto fields and allotments).</summary>
    public bool Crops { get; init; } = true;

    /// <summary>Lantern posts every 30 m along streets inside settlements (needs roads and buildings).</summary>
    public bool StreetLights { get; init; } = true;

    /// <summary>Roof blocks picked from the swissIMAGE colour of each building (needs buildings).</summary>
    public bool RoofColours { get; init; } = true;

    /// <summary>Real elevation (m) above which the surface is snow.</summary>
    public double SnowLine { get; init; } = 2500;

    /// <summary>Slope in degrees at or above which the surface is bare stone.</summary>
    public double SlopeStoneDegrees { get; init; } = 32;

    /// <summary>Vanilla height (Y -64..319) or a tall world (Y -2032..2031) enabled through a data pack written into the world.</summary>
    public WorldHeight WorldHeight { get; init; } = WorldHeight.Standard;

    public int WorldMinY => WorldHeight == WorldHeight.Tall ? TallMinY : StandardMinY;
    public int WorldMaxY => WorldHeight == WorldHeight.Tall ? TallMaxY : StandardMaxY;

    public const int StandardMinY = -64;
    public const int StandardMaxY = 319;
    public const int TallMinY = -2032;
    public const int TallMaxY = 2031;
}

public enum WorldHeight
{
    /// <summary>384 blocks, Y -64..319, works without any data pack.</summary>
    Standard,
    /// <summary>4064 blocks, Y -2032..2031: room for any Swiss relief at 1 m per block; needs the data pack the generator writes into the world.</summary>
    Tall,
}

/// <summary>Per-column recipe derived from the height grid.</summary>
public sealed class ClassifiedTerrain
{
    public const short NoWater = short.MinValue;

    public int Width { get; }
    public int Height { get; }
    /// <summary>Y of the top solid block per column.</summary>
    public short[] TopY { get; }
    public Surface[] Kind { get; }
    /// <summary>Y of the water surface (top water block) per column, or <see cref="NoWater"/>.</summary>
    public short[] WaterY { get; }
    public double VerticalScale { get; }
    public float MinElevation { get; }
    public float MaxElevation { get; }
    public int MinY { get; }
    public int MaxY { get; }
    public int WaterCells { get; }
    /// <summary>Forest flag per column (null when land cover was not used).</summary>
    public bool[]? Forest { get; internal set; }
    /// <summary>Planned trees (empty when trees are off or the scale is too coarse).</summary>
    public TreeMap Trees { get; internal set; } = new();
    /// <summary>True when forest columns should show a forest-floor block instead of individual trees.</summary>
    public bool CoarseForest { get; internal set; }
    /// <summary>Scatter ground cover (grass, ferns, flowers) on grass columns.</summary>
    public bool Vegetation { get; internal set; }
    /// <summary>Place ores and other resources.</summary>
    public bool Resources { get; internal set; }
    /// <summary>Seed used for deterministic tree and plant placement.</summary>
    public long Seed { get; internal set; }
    /// <summary>Horizontal scale, needed for scale-dependent details such as windows.</summary>
    public double MetresPerBlock { get; internal set; } = 1;

    /// <summary>Road material per column (see <see cref="Water.RoadMaterial"/>), null when roads are off.</summary>
    public byte[]? Road { get; internal set; }
    public byte[]? RoadFlags { get; internal set; }
    /// <summary>Railway bits per column (see <see cref="Water.RailFlag"/>), null when rails are off.</summary>
    public byte[]? Rail { get; internal set; }
    /// <summary>Building height in blocks per column (0 = none).</summary>
    public byte[]? BuildingHeight { get; internal set; }
    /// <summary>Y of the building's floor (interior walking level) per column.</summary>
    public short[]? BuildingFloor { get; internal set; }
    /// <summary>Building bits per column (see <see cref="Water.BuildingFlag"/>) plus a wall style in bits 4-5.</summary>
    public byte[]? BuildingFlags { get; internal set; }
    public int BuildingCount { get; internal set; }
    /// <summary>Pylons, poles and wind turbines indexed by the chunks they can touch.</summary>
    public ChunkIndex<Water.Structure> Structures { get; internal set; } = new();
    /// <summary>Overhead wires indexed by the chunks they cross, with their end heights (absolute Y).</summary>
    public ChunkIndex<(Water.Wire Wire, int Y0, int Y1)> Wires { get; internal set; } = new();
    /// <summary>Villager spawn points.</summary>
    public List<VillagerSpawn> Villagers { get; internal set; } = [];
    /// <summary>Church tower height in blocks per column (0 = not a tower cell).</summary>
    public byte[]? BuildingTower { get; internal set; }
    /// <summary>Building type per column (see <see cref="Water.BuildingKind"/>).</summary>
    public byte[]? BuildingKind { get; internal set; }
    /// <summary>Doors, ladders, chimneys and cellars per column (see <see cref="BuildingDetail"/>).</summary>
    public byte[]? BuildingDetail { get; internal set; }
    /// <summary>Balcony blocks (planks with a fence on top) outside house walls.</summary>
    public ChunkIndex<(int X, int Z, int Y)> Balconies { get; internal set; } = new();
    /// <summary>Deck Y for road/rail/platform cells that are bridges or tunnels (<see cref="NoWater"/> = on the ground).</summary>
    public short[]? DeckY { get; internal set; }
    public byte[]? DeckFlags { get; internal set; }
    /// <summary>Tunnel clearance in blocks above the road surface or the rail.</summary>
    public int TunnelClearance { get; internal set; } = 5;
    /// <summary>Street name signs indexed by chunk.</summary>
    public ChunkIndex<Water.SignSpec> Signs { get; internal set; } = new();
    /// <summary>Bedrock lithology class per column (1..23, 0 = unknown), null when geology is off.</summary>
    public byte[]? Geology { get; set; }
    /// <summary>Unconsolidated deposit class per column (1..23, 0 = none) forming the top <see cref="DepositBlocks"/> blocks under the soil.</summary>
    public byte[]? Deposit { get; set; }
    public int DepositBlocks { get; set; } = 8;
    /// <summary>Y of the last rock block under the ice per column (glacier columns only); null when no ice model.</summary>
    public short[]? IceBase { get; set; }
    /// <summary>Block id that replaces the top block of a land column (0 = none), from imagery or extras.</summary>
    public byte[]? TopBlock { get; set; }
    /// <summary>Block id standing on the surface (walls, fences, jetties), 0 = none.</summary>
    public byte[]? Wall { get; set; }
    /// <summary>Formation tops from a 3D geological model: lithology class of the unit below and its top Y per column (NoWater = absent).</summary>
    public (byte Class, short[] Y)[]? Horizons { get; set; }
    /// <summary>Y of the lowest real elevation (needed to convert further elevations to Y).</summary>
    public int BaseY { get; internal set; }
    /// <summary>World height limits this terrain was built for.</summary>
    public int WorldMinY { get; internal set; } = TerrainOptions.StandardMinY;
    public int WorldMaxY { get; internal set; } = TerrainOptions.StandardMaxY;
    /// <summary>Real elevation per column (the height grid data), for biome and wildlife decisions.</summary>
    public float[]? Elevation { get; set; }
    /// <summary>Place ripe crops on cropland columns.</summary>
    public bool Crops { get; set; }
    /// <summary>Roof block override per column (0 = default roof), from the orthophoto.</summary>
    public byte[]? RoofBlock { get; set; }
    /// <summary>Snow line (m) for the snow layer gradient below it.</summary>
    public double SnowLine { get; internal set; } = 2500;

    /// <summary>Block Y for a real elevation with this terrain's scale.</summary>
    public int ToY(double elevation) => Math.Clamp(BaseY + (int)Math.Round((elevation - MinElevation) * VerticalScale), WorldMinY + 1, WorldMaxY);
    /// <summary>Block names behind the generator ids for this world.</summary>
    public Anvil.BlockPalette Palette { get; set; } = Anvil.BlockPalette.Default;

    /// <summary>True when a road, rail or building occupies the column (no trees or plants there).</summary>
    public bool IsOccupied(int i) =>
        (Road is not null && Road[i] != 0) || (Rail is not null && Rail[i] != 0) || (BuildingHeight is not null && BuildingHeight[i] != 0);

    internal ClassifiedTerrain(int width, int height, short[] topY, Surface[] kind, short[] waterY,
        double verticalScale, float minElev, float maxElev, int minY, int maxY, int waterCells)
    {
        Width = width; Height = height; TopY = topY; Kind = kind; WaterY = waterY;
        VerticalScale = verticalScale; MinElevation = minElev; MaxElevation = maxElev; MinY = minY; MaxY = maxY;
        WaterCells = waterCells;
    }

    public bool IsForest(int x, int z) => Forest is not null && Forest[z * Width + x];

    public short TopAt(int x, int z) => TopY[z * Width + x];
    public Surface KindAt(int x, int z) => Kind[z * Width + x];
    public short WaterAt(int x, int z) => WaterY[z * Width + x];

    /// <summary>Highest block (solid or water) in a column.</summary>
    public int ColumnTop(int x, int z)
    {
        int i = z * Width + x;
        return WaterY[i] == NoWater ? TopY[i] : Math.Max(TopY[i], WaterY[i]);
    }
}

public static class SurfaceClassifier
{
    /// <summary>Computes the vertical scale that would be used for a grid with the given elevation range.</summary>
    public static double ResolveVerticalScale(TerrainOptions o, float minElev, float maxElev, double metresPerBlock)
    {
        double range = Math.Max(maxElev - minElev, 1e-3);
        double available = AvailableHeight(o);
        // "1 block per metre of height" at 1 m/block; at coarser horizontal scales keep true proportions by default
        double natural = 1.0 / metresPerBlock;
        double fit = available / range;
        double scale = o.VerticalScale ?? Math.Min(natural, fit);
        return Math.Min(scale, fit); // never exceed the world height
    }

    /// <summary>Block rows available for relief above the base Y (with a little headroom).</summary>
    public static double AvailableHeight(TerrainOptions o) => o.WorldMaxY - 4 - o.BaseY;

    /// <summary>
    /// Smallest whole metres-per-block at which the relief keeps true proportions (vertical scale
    /// equal to 1/metresPerBlock) without exceeding the world height.
    /// </summary>
    public static double TrueProportionMetresPerBlock(TerrainOptions o, float minElev, float maxElev)
    {
        double range = Math.Max(maxElev - minElev, 1e-3);
        double needed = range / AvailableHeight(o);
        // sub-metre scales come in halves; above one metre whole metres
        if (needed <= 0.5) return 0.5;
        if (needed <= 1) return 1;
        return Math.Ceiling(needed - 1e-9);
    }

    /// <param name="cover">Optional land cover (water, forest) aligned with the grid.</param>
    /// <param name="seed">Seed for deterministic tree placement.</param>
    public static ClassifiedTerrain Classify(HeightGrid grid, TerrainOptions o, double metresPerBlock, Water.LandCover? cover, long seed, CancellationToken ct, ClassifyExtras? extras = null)
    {
        var (minElev, maxElev) = grid.Range();
        if (float.IsNaN(minElev)) throw new InvalidOperationException("The height grid has no valid values.");
        double vs = ResolveVerticalScale(o, minElev, maxElev, metresPerBlock);

        int w = grid.Width, h = grid.Height;
        bool[]? waterMask = o.WaterBodies ? cover?.Water : null;
        bool[]? forestMask = o.Trees ? cover?.Forest : null;
        byte[]? coverClass = cover?.Cover;
        if (waterMask is not null && waterMask.Length != w * h) throw new ArgumentException("Land cover size does not match the grid.", nameof(cover));
        float[]? lakeBed = extras?.LakeBed;
        var ice = extras?.Ice;
        short[]? iceBase = ice is null ? null : new short[w * h];
        if (ice is not null && waterMask is not null)
        {
            // Ice wins over water (lakes under the 1850 glaciers, or pro-glacial lakes surveyed as ice).
            waterMask = (bool[])waterMask.Clone();
            for (int i = 0; i < waterMask.Length; i++) if (ice.Ice[i]) waterMask[i] = false;
        }

        int? floodY = null;
        if (o.WaterLevel is { } wl && wl > minElev) floodY = ToY(wl);

        // Water surface per masked cell: local minimum of the DEM over masked neighbours, so that
        // shoreline mismatches between the water polygons and the DEM do not raise the water.
        float[]? waterSurface = waterMask is null ? null : LocalMinimum(grid, waterMask, radius: 3, ct);
        // Beaches only along water at least 5 cells wide; brooks keep grass right up to the bank.
        bool[]? shore = waterMask is null ? null : Dilate(Erode(waterMask, w, h, radius: 2, ct), w, h, radius: 4, ct, exclude: waterMask);
        int depthBlocks = Math.Max(1, (int)Math.Round(3 * vs));

        var topY = new short[w * h];
        var kind = new Surface[w * h];
        var waterY = new short[w * h];
        Array.Fill(waterY, ClassifiedTerrain.NoWater);
        int minY = int.MaxValue, maxY = int.MinValue, waterCells = 0;
        var lockObj = new object();

        Parallel.For(0, h, new ParallelOptions { CancellationToken = ct }, z =>
        {
            int localMin = int.MaxValue, localMax = int.MinValue, localWater = 0;
            for (int x = 0; x < w; x++)
            {
                int i = z * w + x;
                float elev = grid[x, z];
                int y = ToY(elev);
                Surface s;
                int wy = ClassifiedTerrain.NoWater;

                if (ice is not null && ice.Ice[i])
                {
                    // Glacier: the grid carries the ice surface; the bed is the last rock block.
                    s = Surface.Glacier;
                    iceBase![i] = (short)Math.Min(y - 1, ToY(ice.Bed[i]));
                }
                else if (waterMask is not null && waterMask[i])
                {
                    // Lake or river: water surface at the DEM level, bed a few blocks below or as surveyed.
                    wy = ToY(waterSurface![i]);
                    y = Math.Max(o.WorldMinY + 1, wy - depthBlocks);
                    if (lakeBed is not null && !float.IsNaN(lakeBed[i]))
                    {
                        int by = ToY(lakeBed[i]);
                        if (by < y) y = Math.Max(o.WorldMinY + 1, by);
                    }
                    s = Surface.Water;
                }
                else if (floodY is { } fy && y < fy)
                {
                    wy = fy;
                    s = Surface.Water;
                }
                else if ((shore is not null && shore[i]) || (floodY is { } fy2 && y <= fy2 + Math.Max(1, (int)Math.Round(2 * vs))))
                {
                    s = Surface.Sand;
                }
                else if (coverClass is not null && coverClass[i] != 0 && FromCover((Water.CoverClass)coverClass[i]) is { } cs)
                {
                    s = cs;
                }
                else if (elev >= o.SnowLine)
                {
                    s = Surface.Snow;
                }
                else if (grid.SlopeDegrees(x, z) >= o.SlopeStoneDegrees)
                {
                    s = Surface.Stone;
                }
                else
                {
                    s = Surface.Grass;
                }

                topY[i] = (short)y;
                kind[i] = s;
                waterY[i] = (short)wy;
                int colTop = wy == ClassifiedTerrain.NoWater ? y : Math.Max(y, wy);
                if (y < localMin) localMin = y;
                if (colTop > localMax) localMax = colTop;
                if (s == Surface.Water) localWater++;
            }
            lock (lockObj)
            {
                if (localMin < minY) minY = localMin;
                if (localMax > maxY) maxY = localMax;
                waterCells += localWater;
            }
        });

        var result = new ClassifiedTerrain(w, h, topY, kind, waterY, vs, minElev, maxElev, minY, maxY, waterCells)
        {
            Vegetation = o.Vegetation,
            Resources = o.Resources,
            Seed = seed,
            MetresPerBlock = metresPerBlock,
            IceBase = iceBase,
            DepositBlocks = Math.Max(2, (int)Math.Round(8 * vs)),
            BaseY = o.BaseY,
            WorldMinY = o.WorldMinY,
            WorldMaxY = o.WorldMaxY,
            SnowLine = o.SnowLine,
        };
        if (cover is not null)
        {
            if (o.Roads && cover.Road is not null) { result.Road = cover.Road; result.RoadFlags = cover.RoadFlags; }
            if (cover.Deck is { } deck && cover.DeckFlags is { } dflags)
            {
                // bridges only where the surveyed deck is clearly above the ground, tunnels only where it is clearly below
                int clearance = Math.Max(3, (int)Math.Round(5 / metresPerBlock));
                result.TunnelClearance = clearance;
                var deckY = new short[w * h];
                Array.Fill(deckY, ClassifiedTerrain.NoWater);
                int bridges = 0, tunnels = 0;
                for (int i = 0; i < deckY.Length; i++)
                {
                    if (float.IsNaN(deck[i]) || dflags[i] == 0) continue;
                    bool usable = (dflags[i] & (Water.DeckFlag.Bridge | Water.DeckFlag.Platform)) != 0 && (o.Roads && result.Road?[i] != 0 || o.Rails && cover.Rail?[i] != 0 || (dflags[i] & Water.DeckFlag.Platform) != 0)
                                  || (dflags[i] & Water.DeckFlag.Tunnel) != 0 && (o.Roads && result.Road?[i] != 0 || o.Rails && cover.Rail?[i] != 0);
                    if (!usable) continue;
                    int dy = ToY(deck[i]);
                    int ground = waterY[i] != ClassifiedTerrain.NoWater ? Math.Max(topY[i], waterY[i]) : topY[i];
                    if ((dflags[i] & (Water.DeckFlag.Bridge | Water.DeckFlag.Platform)) != 0)
                    {
                        if (dy > ground + 1) { deckY[i] = (short)Math.Min(dy, o.WorldMaxY - 6); bridges++; }
                    }
                    else if (dy + clearance + 2 < topY[i])
                    {
                        deckY[i] = (short)Math.Max(dy, o.WorldMinY + 2); tunnels++;
                    }
                }
                if (bridges + tunnels > 0) { result.DeckY = deckY; result.DeckFlags = dflags; }
            }
            if (o.Rails && cover.Rail is not null) result.Rail = cover.Rail;
            if (o.Buildings && cover.BuildingId is not null)
            {
                if (cover.RoofHeight is not null) PlanMeasuredBuildings(result, cover, seed, ToY);
                else PlanBuildings(result, cover, metresPerBlock, seed);
                result.BuildingKind = cover.BuildingKind;
                BuildingDetails.Plan(result, cover, metresPerBlock);
            }
            // Measured models already contain the real church towers.
            if (o.Buildings && cover.BuildingId is not null && cover.RoofHeight is null) PlanChurchTowers(result, cover, metresPerBlock);
            if (o.PowerLines || result.BuildingTower is not null || cover.Structures.Count > 0) PlanStructures(result, cover, metresPerBlock, o.PowerLines);
            if (cover.Surface is not null) result.TopBlock = cover.Surface;
            if (cover.Wall is not null) result.Wall = cover.Wall;
            if (o.Villagers && o.Buildings && cover.BuildingId is not null) result.Villagers = VillagerPlanner.Plan(result, cover, seed);
            if (o.StreetSigns && o.Roads)
            {
                var signs = new ChunkIndex<Water.SignSpec>();
                foreach (var sign in cover.Signs) signs.Add(sign, sign.X, sign.Z, sign.X, sign.Z);
                result.Signs = signs;
            }
        }
        if (forestMask is not null)
        {
            result.Forest = forestMask;
            result.CoarseForest = false; // trees are planted at every scale
            if (extras?.Canopy is { } canopy && metresPerBlock <= 4)
            {
                result.Trees = TreePlanner.PlanFromCanopy(grid, result, forestMask, canopy, vs, metresPerBlock, seed, coverClass);
            }
            else
            {
                result.Trees = TreePlanner.Plan(grid, result, forestMask, metresPerBlock, seed, coverClass);
                if (cover is not null) TreePlanner.AddSingleTrees(result, cover.SingleTrees, grid, seed);
            }
        }
        if (cover is not null && cover.Points.Count > 0 && result.BuildingFlags is not null && cover.BuildingId is not null) ApplyChurchPoints(result, cover);
        return result;

        int ToY(double elev)
        {
            int y = o.BaseY + (int)Math.Round((elev - minElev) * vs);
            return Math.Clamp(y, o.WorldMinY + 1, o.WorldMaxY);
        }
    }

    /// <summary>
    /// Buildings from measured roof and floor heights (swissBUILDINGS3D): every column gets the roof
    /// block at its measured roof height, so pitched roofs, dormers and towers come out as modelled.
    /// The floor is the measured floor height (or the highest terrain column when missing).
    /// </summary>
    private static void PlanMeasuredBuildings(ClassifiedTerrain t, Water.LandCover cover, long seed, Func<double, int> toY)
    {
        var ids = cover.BuildingId!;
        var roofH = cover.RoofHeight!;
        var floorH = cover.FloorHeight!;
        int n = ids.Length, w = t.Width, h = t.Height;

        // Floor per building: lowest measured floor, else highest terrain under the roof.
        var floorById = new Dictionary<int, (float Floor, int MaxTop)>();
        for (int i = 0; i < n; i++)
        {
            int id = ids[i];
            if (id == 0) continue;
            int top = t.TopY[i];
            if (t.WaterY[i] != ClassifiedTerrain.NoWater) top = Math.Max(top, t.WaterY[i]);
            floorById.TryGetValue(id, out var cur);
            float f = float.IsNaN(floorH[i]) ? (floorById.ContainsKey(id) ? cur.Floor : float.NaN) : (float.IsNaN(cur.Floor) || !floorById.ContainsKey(id) ? floorH[i] : Math.Min(cur.Floor, floorH[i]));
            floorById[id] = (f, Math.Max(floorById.ContainsKey(id) ? cur.MaxTop : int.MinValue, top));
        }

        var height = new byte[n];
        var floor = new short[n];
        var flags = new byte[n];
        for (int i = 0; i < n; i++)
        {
            int id = ids[i];
            if (id == 0) continue;
            var (f, maxTop) = floorById[id];
            int floorY = float.IsNaN(f) ? maxTop : Math.Max(toY(f), t.TopY[i] - 6);
            // never float above the terrain: at least one column of the footprint must touch the ground
            floorY = Math.Min(floorY, maxTop);
            int roofY = Math.Max(toY(roofH[i]), floorY + 2);
            if (roofY > t.WorldMaxY - 1) roofY = t.WorldMaxY - 1;
            height[i] = (byte)Math.Clamp(roofY - floorY - 1, 1, 255);
            floor[i] = (short)floorY;
            int x = i % w, z = i / w;
            bool edge = x == 0 || z == 0 || x == w - 1 || z == h - 1
                        || ids[i - 1] != id || ids[i + 1] != id || ids[i - w] != id || ids[i + w] != id;
            byte style = (byte)(TreePlanner.Hash(id, 7, seed ^ 0xB11D) % 3);
            byte coverFlags = cover.BuildingFlags is not null ? (byte)(cover.BuildingFlags[i] & (Water.BuildingFlag.Church | Water.BuildingFlag.Industrial)) : (byte)0;
            flags[i] = (byte)(coverFlags | (edge ? Water.BuildingFlag.Edge : 0) | (style << 4));
        }
        t.BuildingHeight = height;
        t.BuildingFloor = floor;
        t.BuildingFlags = flags;
        t.BuildingCount = floorById.Count;
    }

    /// <summary>
    /// Turns building footprints into per-column height, floor level and flags. The floor is the
    /// highest terrain column under the footprint, so on slopes the building gets a solid base.
    /// </summary>
    private static void PlanBuildings(ClassifiedTerrain t, Water.LandCover cover, double metresPerBlock, long seed)
    {
        var ids = cover.BuildingId!;
        var levels = cover.BuildingLevels!;
        var flags = cover.BuildingFlags!;
        int n = ids.Length;
        var floorById = new Dictionary<int, int>();
        for (int i = 0; i < n; i++)
        {
            int id = ids[i];
            if (id == 0) continue;
            int top = t.TopY[i];
            if (t.WaterY[i] != ClassifiedTerrain.NoWater) top = Math.Max(top, t.WaterY[i]);
            if (!floorById.TryGetValue(id, out int f) || top > f) floorById[id] = top;
        }

        var height = new byte[n];
        var floor = new short[n];
        var outFlags = new byte[n];
        for (int i = 0; i < n; i++)
        {
            int id = ids[i];
            if (id == 0) continue;
            int blocks = Math.Max(2, (int)Math.Round((levels[i] * 3 + 1) / metresPerBlock));
            int f = floorById[id];
            if (f + blocks + 2 > t.WorldMaxY) blocks = Math.Max(1, t.WorldMaxY - 2 - f);
            height[i] = (byte)Math.Min(blocks, 255);
            floor[i] = (short)f;
            byte style = (byte)(TreePlanner.Hash(id, 7, seed ^ 0xB11D) % 3);
            outFlags[i] = (byte)(flags[i] | (style << 4));
        }
        t.BuildingHeight = height;
        t.BuildingFloor = floor;
        t.BuildingFlags = outFlags;
        t.BuildingCount = floorById.Count;
    }

    /// <summary>Surface for a land cover class, or null when the class does not change the surface.</summary>
    private static Surface? FromCover(Water.CoverClass c) => c switch
    {
        Water.CoverClass.Rock => Surface.Stone,
        Water.CoverClass.Scree => Surface.Gravel,
        Water.CoverClass.Glacier => Surface.Glacier,
        Water.CoverClass.Wetland => Surface.Mud,
        Water.CoverClass.Vineyard => Surface.Vineyard,
        _ => null,
    };

    /// <summary>Marks the buildings under church points of interest as churches.</summary>
    private static void ApplyChurchPoints(ClassifiedTerrain t, Water.LandCover cover)
    {
        var ids = cover.BuildingId!;
        var churchIds = new HashSet<int>();
        foreach (var p in cover.Points)
        {
            if (p.Kind != "church") continue;
            // the point may sit a few cells outside the footprint: look around it
            for (int dz = -3; dz <= 3; dz++)
                for (int dx = -3; dx <= 3; dx++)
                {
                    int x = p.X + dx, z = p.Z + dz;
                    if (x < 0 || z < 0 || x >= t.Width || z >= t.Height) continue;
                    int id = ids[z * t.Width + x];
                    if (id != 0) { churchIds.Add(id); dx = 4; break; }
                }
        }
        if (churchIds.Count == 0) return;
        for (int i = 0; i < ids.Length; i++)
            if (ids[i] != 0 && churchIds.Contains(ids[i])) t.BuildingFlags![i] |= Water.BuildingFlag.Church;
    }

    private static readonly List<Water.Structure> Spires = [];

    /// <summary>
    /// Gives every church a square tower at its west end: tower cells get a height, and a spire
    /// structure is queued for the top of the tower.
    /// </summary>
    private static void PlanChurchTowers(ClassifiedTerrain t, Water.LandCover cover, double metresPerBlock)
    {
        var ids = cover.BuildingId!;
        int w = t.Width, h = t.Height;
        var westmost = new Dictionary<int, (int X, int Z)>();
        for (int z = 0; z < h; z++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = z * w + x;
                int id = ids[i];
                if (id == 0 || (cover.BuildingFlags![i] & Water.BuildingFlag.Church) == 0) continue;
                if (!westmost.TryGetValue(id, out var p) || x < p.X || (x == p.X && Math.Abs(z - p.Z) > 0 && z < p.Z)) westmost[id] = (x, z);
            }
        }
        if (westmost.Count == 0) return;

        var tower = new byte[w * h];
        int size = metresPerBlock <= 2 ? 3 : 1, half = size / 2;
        lock (Spires) Spires.Clear();
        foreach (var (id, p) in westmost)
        {
            int cx = Math.Clamp(p.X + half + 1, half, w - 1 - half), cz = Math.Clamp(p.Z, half, h - 1 - half);
            int i0 = cz * w + cx;
            int nave = t.BuildingHeight![i0] != 0 ? t.BuildingHeight[i0] : 7;
            int floor = t.BuildingHeight[i0] != 0 ? t.BuildingFloor![i0] : t.ColumnTop(cx, cz);
            int towerH = Math.Min(StructurePlanner.ChurchTowerHeight(nave, metresPerBlock), t.WorldMaxY - 8 - floor);
            if (towerH < 4) continue;
            for (int dz = -half; dz <= half; dz++)
                for (int dx = -half; dx <= half; dx++)
                {
                    int i = (cz + dz) * w + cx + dx;
                    tower[i] = (byte)Math.Min(towerH, 255);
                    if (t.BuildingHeight[i] == 0)
                    {
                        // tower cell outside the footprint: make it part of the building
                        t.BuildingHeight[i] = (byte)nave;
                        t.BuildingFloor![i] = (short)floor;
                        t.BuildingFlags![i] = (byte)(Water.BuildingFlag.Church | Water.BuildingFlag.Edge | (t.BuildingFlags[i0] & 0x30));
                    }
                }
            lock (Spires) Spires.Add(new Water.Structure(Water.StructureKind.ChurchSpire, cx, cz, 1, 0, size, floor + towerH));
        }
        t.BuildingTower = tower;
    }

    /// <summary>Indexes pylons, poles, turbines, spires and wires by chunk; wire ends sit at the top of their structure.</summary>
    private static void PlanStructures(ClassifiedTerrain t, Water.LandCover cover, double metresPerBlock, bool powerLines)
    {
        var structures = new ChunkIndex<Water.Structure>();
        // power structures need the option; everything else (lifts, crosses, fountains, ...) is always built when present
        IEnumerable<Water.Structure> all = cover.Structures.Where(s => powerLines || s.Kind is not (Water.StructureKind.Tower or Water.StructureKind.Pole or Water.StructureKind.WindTurbine));
        lock (Spires) all = all.Concat(Spires.ToList());
        foreach (var s in all)
        {
            int r = StructurePlanner.Reach(s.Kind, metresPerBlock);
            structures.Add(s, Math.Max(0, s.X - r), Math.Max(0, s.Z - r), Math.Min(t.Width - 1, s.X + r), Math.Min(t.Height - 1, s.Z + r));
        }
        t.Structures = structures;

        var wires = new ChunkIndex<(Water.Wire, int, int)>();
        foreach (var wire in cover.Wires.Where(wr => powerLines || wr.HeightMetres > 0))
        {
            int g0 = GroundAt(wire.X0, wire.Z0), g1 = GroundAt(wire.X1, wire.Z1);
            int y0 = g0 + StructurePlanner.WireHeight(wire, metresPerBlock, t.WorldMaxY - g0);
            int y1 = g1 + StructurePlanner.WireHeight(wire, metresPerBlock, t.WorldMaxY - g1);
            int minX = (int)Math.Floor(Math.Min(wire.X0, wire.X1)) - 3, maxX = (int)Math.Ceiling(Math.Max(wire.X0, wire.X1)) + 3;
            int minZ = (int)Math.Floor(Math.Min(wire.Z0, wire.Z1)) - 3, maxZ = (int)Math.Ceiling(Math.Max(wire.Z0, wire.Z1)) + 3;
            if (maxX < 0 || maxZ < 0 || minX >= t.Width || minZ >= t.Height) continue;
            wires.Add((wire, y0, y1), Math.Max(0, minX), Math.Max(0, minZ), Math.Min(t.Width - 1, maxX), Math.Min(t.Height - 1, maxZ));
        }
        t.Wires = wires;

        int GroundAt(double fx, double fz)
        {
            int x = Math.Clamp((int)Math.Floor(fx), 0, t.Width - 1), z = Math.Clamp((int)Math.Floor(fz), 0, t.Height - 1);
            return t.ColumnTop(x, z);
        }
    }

    /// <summary>Separable minimum filter over masked cells only (unmasked cells keep their own value).</summary>
    private static float[] LocalMinimum(HeightGrid grid, bool[] mask, int radius, CancellationToken ct)
    {
        int w = grid.Width, h = grid.Height;
        var tmp = new float[w * h];
        var result = new float[w * h];
        // horizontal pass
        Parallel.For(0, h, new ParallelOptions { CancellationToken = ct }, z =>
        {
            for (int x = 0; x < w; x++)
            {
                int i = z * w + x;
                float m = grid.Data[i];
                if (mask[i])
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int xx = x + dx;
                        if (xx < 0 || xx >= w) continue;
                        int j = z * w + xx;
                        if (mask[j] && grid.Data[j] < m) m = grid.Data[j];
                    }
                }
                tmp[i] = m;
            }
        });
        // vertical pass
        Parallel.For(0, h, new ParallelOptions { CancellationToken = ct }, z =>
        {
            for (int x = 0; x < w; x++)
            {
                int i = z * w + x;
                float m = tmp[i];
                if (mask[i])
                {
                    for (int dz = -radius; dz <= radius; dz++)
                    {
                        int zz = z + dz;
                        if (zz < 0 || zz >= h) continue;
                        int j = zz * w + x;
                        if (mask[j] && tmp[j] < m) m = tmp[j];
                    }
                }
                result[i] = m;
            }
        });
        return result;
    }

    /// <summary>Masked cells whose whole (2r+1)² neighbourhood is masked: the interior of wide water bodies.</summary>
    private static bool[] Erode(bool[] mask, int w, int h, int radius, CancellationToken ct)
    {
        var tmp = new bool[w * h];
        var result = new bool[w * h];
        Parallel.For(0, h, new ParallelOptions { CancellationToken = ct }, z =>
        {
            for (int x = 0; x < w; x++)
            {
                bool all = true;
                for (int dx = -radius; dx <= radius && all; dx++)
                {
                    int xx = x + dx;
                    if (xx < 0 || xx >= w || !mask[z * w + xx]) all = false;
                }
                tmp[z * w + x] = all;
            }
        });
        Parallel.For(0, h, new ParallelOptions { CancellationToken = ct }, z =>
        {
            for (int x = 0; x < w; x++)
            {
                bool all = true;
                for (int dz = -radius; dz <= radius && all; dz++)
                {
                    int zz = z + dz;
                    if (zz < 0 || zz >= h || !tmp[zz * w + x]) all = false;
                }
                result[z * w + x] = all;
            }
        });
        return result;
    }

    /// <summary>Cells within <paramref name="radius"/> (Chebyshev) of a masked cell, excluding masked cells themselves (or <paramref name="exclude"/>).</summary>
    private static bool[] Dilate(bool[] mask, int w, int h, int radius, CancellationToken ct, bool[]? exclude = null)
    {
        exclude ??= mask;
        var tmp = new bool[w * h];
        var result = new bool[w * h];
        Parallel.For(0, h, new ParallelOptions { CancellationToken = ct }, z =>
        {
            for (int x = 0; x < w; x++)
            {
                bool any = false;
                for (int dx = -radius; dx <= radius && !any; dx++)
                {
                    int xx = x + dx;
                    if (xx >= 0 && xx < w && mask[z * w + xx]) any = true;
                }
                tmp[z * w + x] = any;
            }
        });
        Parallel.For(0, h, new ParallelOptions { CancellationToken = ct }, z =>
        {
            for (int x = 0; x < w; x++)
            {
                bool any = false;
                for (int dz = -radius; dz <= radius && !any; dz++)
                {
                    int zz = z + dz;
                    if (zz >= 0 && zz < h && tmp[zz * w + x]) any = true;
                }
                result[z * w + x] = any && !exclude[z * w + x];
            }
        });
        return result;
    }
}
