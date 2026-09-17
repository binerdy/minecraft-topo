# Minecraft Topo

Turn any part of Switzerland into a Minecraft Java Edition world with the real topography.
No buildings or roads, just the landform: grass on gentle slopes, stone on steep faces, snow above
the snow line, optional water below a chosen elevation.

The app is a web UI (Angular + Leaflet) backed by a local ASP.NET Core server written in C#.
On start you see the whole of Switzerland as a blocky Minecraft-style relief map. Zoom in, drag a
rectangle, pick a scale such as 1 m = 1 block, and click **Generate world**.

Elevation data and map tiles: © swisstopo (Federal Office of Topography), free geodata.

## Requirements

- .NET SDK 10
- Node.js 20+ and npm (only to build the client)
- Minecraft Java Edition 26.3 (worlds are written with data version 5023)

## Run

```powershell
.\run.ps1
```

This builds the Angular client the first time, starts the server on <http://localhost:5100> and
opens the browser. The first start downloads swisstopo's 200 m country model (45 MB, one time).

Development with live reload:

```powershell
dotnet run --project src/MinecraftTopo.Server --launch-profile http   # API on :5100
cd client; npx ng serve                                              # UI on :4200, proxies /api
```

## Using the app

1. **Select an area.** Click *Select area*, then click and drag on the map. Drag the corner handles
   to resize, drag inside the rectangle to move it, or type LV95 coordinates and a size in the panel.
   Lakes and rivers are shown in blue on the overview (from the same swisstopo water surfaces
   used for generation). Tick *National map* to overlay the swisstopo map when you need to
   recognise places.
   The red **S** marker is the spawn point (default: centre of the area). Click *Set spawn point*
   and then click inside the area, drag the marker, or type its coordinates in the panel.
2. **Scale.** *Metres per block*: 1 gives true 1:1 detail from swissALTI3D (2 m data). Larger values
   allow bigger areas; at 25 m/block or more the 200 m country model is used and no tiles need
   downloading, so even the whole country fits.
3. **Terrain.** Vertical scale is auto-fitted so the relief fits the world height (Y -64..319);
   set it manually for exaggeration. When the relief has to be squeezed, the estimate offers a
   one-click *Use N m per block for true proportions* button. Base Y is where the lowest point
   lands. *Lakes and rivers as water* (on by default) takes lake and river outlines from swisstopo's
   VECTOR25 water surfaces and puts the water at the elevation model's level, a few blocks deep.
   *Flood below* additionally fills everything under a real elevation with water.
   *Trees in forests* (on by default) plants oak and spruce trees in swisstopo forest areas below
   the tree line, about one per 7 m at 1 m per block and one every 3 blocks at coarser scales.
   Forests also show as darker green on the overview map. *Grass, ferns and flowers* scatters
   short and tall grass plus a few flowers over open grass blocks and ferns under the trees.
   *Resources* adds coal, iron, copper, gold, redstone, lapis, diamond and emerald ore blobs at
   their vanilla depths (replacing stone and deepslate only, so the landscape is unchanged), bee
   nests with bees on about 4 % of oaks, sweet berry bushes and mushrooms in forests, the odd
   pumpkin in meadows, and clay and seagrass on lake and river beds.
4. **Output.** Either write straight into your Minecraft `saves` folder, or download a zip and
   extract it there yourself. The estimate shows blocks, chunks, tiles to download and a size guess.

Open Minecraft Java 26.3, Singleplayer, and the world is in the list. Unwritten chunks stay void.

### Regenerating

Generating into a name that already exists fails unless *Replace an existing world* is ticked, in
which case the old world files are deleted first. Quit Minecraft before replacing a world it has open.

### Format notes (verified in Minecraft 26.3)

The generated `level.dat`, `data/minecraft/world_gen_settings.dat` and region files load in 26.3.
Two 26.x-specific details: the overworld lives under `dimensions/minecraft/overworld/`, and block
palette entries are plain strings (`"minecraft:stone"`) or, when a block state has properties,
compounds `{id: "minecraft:oak_leaves", properties: {persistent: "true"}}`. The pre-26
`{Name, Properties}` form is rejected with "No key id" and replaced by air.

## Command line

`MinecraftTopo.Cli` drives the same engine without the UI:

```powershell
dotnet run --project src/MinecraftTopo.Cli -- --lv95 2657000,1257000,2659000,1259000 --name Brugg --out .\out\Brugg
dotnet run --project src/MinecraftTopo.Cli -- --source synthetic --lv95 2658000,1258000,2658512,1258512 --out .\out\test
dotnet run --project src/MinecraftTopo.Cli -- --verify .\out\test\dimensions\minecraft\overworld\region\r.0.0.mca
dotnet run --project src/MinecraftTopo.Cli -- --help
```

## How it works

- `src/MinecraftTopo.Core`: everything that matters. WGS84/LV95 maths, swissALTI3D discovery
  through the geo.admin.ch STAC API and XYZ tile parsing, the DHM25/200 country model, terrain
  classification, a dependency-free NBT writer, Anvil region file writer, and the 26.x world layout.
- `src/MinecraftTopo.Server`: minimal API. Renders the overview map tiles from the country model,
  estimates jobs, runs generation jobs one at a time and streams progress with server-sent events.
- `client`: Angular 22 standalone app with Leaflet.
- Downloads are cached under `%LOCALAPPDATA%\MinecraftTopo\cache`.

The block grid is the LV95 metre grid itself: block X grows eastwards, block Z grows southwards,
so tiles line up exactly and there is no map projection distortion.
