# Minecraft Topo

Turn any part of Switzerland into a Minecraft Java Edition world with the real topography.
No buildings or roads, just the landform: grass on gentle slopes, stone on steep faces, snow above
the snow line, optional water below a chosen elevation.

The app is a web UI (Angular + Leaflet) backed by a local ASP.NET Core server written in C#.
On start you see the whole of Switzerland as a blocky Minecraft-style relief map. Zoom in, drag a
rectangle, pick a scale such as 1 m = 1 block, and click **Generate world**.

All data comes from swisstopo (Federal Office of Topography), free geodata: swissALTI3D and the
DHM25 country model for elevation, swissBATHY3D for lake floors, swissSURFACE3D for tree heights,
swissTLM3D / swissTLMRegio for the landscape, swissBUILDINGS3D 3.0 for buildings, swissNAMES3D for
signs, GK500, GeoCover and swissJURA3D for rock types, the GLAMOS glacier inventories, swissIMAGE and
the Siegfried, Dufour and national maps for ground colours.
The only third-party package is ACadSharp (MIT), used to read the swissBUILDINGS3D DWG tiles.

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

1. **Select an area.** Type a place, address, peak or postcode into the search box at the top of
   the map tools (swisstopo's location index) and pick a result to zoom there. Then click
   *Select area* and click and drag on the map. Drag the corner handles
   to resize, drag inside the rectangle to move it, or type LV95 coordinates and a size in the panel.
   Or click *Select region by click*, pick municipality, district or canton, and click on the map:
   the unit's bounding box becomes the area and its border is drawn as a dashed outline (needs the
   swissTLMRegio download, whose boundaries GeoPackage holds the units).
   Lakes and rivers are shown in blue on the overview (from the same swisstopo water surfaces
   used for generation). Tick *National map* to overlay the swisstopo map when you need to
   recognise places.
   The red **S** marker is the spawn point (default: centre of the area). Click *Set spawn point*
   and then click inside the area, drag the marker, or type its coordinates in the panel.
2. **Scale.** *Metres per block*: 1 gives true 1:1 detail from swissALTI3D (2 m data); 0.5 doubles
   everything (two blocks per metre, four times the blocks per area) and is worth pairing with the
   0.5 m elevation tiles. Larger values
   allow bigger areas; at 25 m/block or more the 200 m country model is used and no tiles need
   downloading, so even the whole country fits.
   *World height*: *Automatic* (default) picks the tall world up to 2 m per block and the standard
   world above. The standard world is 384 blocks tall (Y -64 to 319), which at 1 m per block
   holds about 375 m of relief; anything steeper is squeezed vertically or the scale is raised,
   and that is what flattens road cuts and cliffs. The *tall* world is 4064 blocks (Y -2032 to
   2031): the generator writes a data pack into the world folder that overrides the overworld
   dimension type and enables it in level.dat, so any Swiss relief keeps 1 m per block with
   nothing to install (verified in Minecraft 26.3). Base Y (default -60) is where the lowest point lands; the vanilla deepslate
   boundary at Y 0 and the ore depths stay in place.
3. **Terrain.** Vertical scale is auto-fitted so the relief fits the world height (Y -64..319);
   set it manually for exaggeration. When the relief would have to be squeezed, metres per block
   is raised automatically so the proportions stay true; a notice says so and *Reset all settings*
   undoes it. Base Y is where the lowest point lands. *Lakes and rivers as water* (on by default) puts water at the elevation model's level, a
   few blocks deep. Outlines and everything else in the landscape come from one of four sources:
   **swissTLM3D** (default; the official detailed landscape model, a 4.8 GB one-time GeoPackage
   download read through its spatial index; adds rock, scree, glaciers, wetlands, orchards,
   vineyards, single trees, road widths by class, church buildings, power lines and place names),
   **swissTLMRegio** (the 1:200 000 version, 160 MB, good for coarse worlds), swisstopo's
   **VECTOR25** map layer (water and forest only, no download).
   *Flood below* additionally fills everything under a real elevation with water.
   *Trees in forests* (on by default) plants oak and spruce trees in swisstopo forest areas below
   the tree line, about one per 7 m at 1 m per block and one every 3 blocks at coarser scales.
   Forests also show as darker green on the overview map. *Grass, ferns and flowers* scatters
   short and tall grass plus a few flowers over open grass blocks and ferns under the trees.
   With swissTLM3D or swissTLMRegio as land cover, more layers can be switched on: *Roads* (asphalt-like
   gray concrete by class, 14 m motorways down to 2 m paths, white lane markings, dirt paths and
   gravel tracks, cobblestone and stone bricks where tagged; bridges and tunnels as described below), *Railways* (3 m gravel bed with correctly oriented rails, including slopes) and
   *Buildings* (by default from swissBUILDINGS3D 3.0: measured roof and floor heights per block,
   so gabled and hipped roofs, dormers and church towers come out as surveyed; alternatively the
   landscape-model footprints extruded by storeys with a synthetic church tower), *Power lines and wind turbines* (iron-bar pylons with cross arms and two
   chain wires, oak-fence poles with one wire, quartz turbine towers with a nacelle and three
   rotor blades; heights scale with metres per block) and *Villagers next to houses* (one per
   house for about 60 % of houses, standing outside; written as entities). All follow the terrain,
   so the landscape itself is unchanged. The map tools also offer *Roads on the overview* drawn
   from swisstopo's road layer.
   *Resources* adds coal, iron, copper, gold, redstone, lapis, diamond and emerald ore blobs at
   their vanilla depths (replacing stone and deepslate only, so the landscape is unchanged), bee
   nests with bees on about 4 % of oaks, sweet berry bushes and mushrooms in forests, the odd
   pumpkin in meadows, and clay and seagrass on lake and river beds.
   *Rock types underground and on bare rock* (GK500 by default) reads swisstopo's 1:500 000
   lithology map and replaces plain stone, underground and on bare rock faces, with a block per
   rock group: limestone stays stone, granite becomes granite, gneiss andesite, sandstone
   sandstone, moraines cobblestone, gravel plains gravel, and so on (23 groups). Deepslate below
   Y 0 and the ores are unchanged. *GeoCover* uses the 1:25 000 geological sheets where
   published (about 9 MB per sheet, downloaded once): the unconsolidated deposits (moraine,
   scree, alluvium, …) form the top 8 m, the mapped bedrock lies below, and GK500 fills in where
   no sheet exists. *swissJURA3D* adds the modelled formation tops of the Jura as underground
   layer boundaries; the current release (part MA1) covers the Vaud Jura between Geneva and the
   Vallée de Joux with one horizon, the top of the Ifenthal Formation (108 MB once).
   *Real lake floors from swissBATHY3D* (on by default) gives the 22 surveyed lakes their true
   depth; the lake archive is downloaded once when an area touches it (2 MB for the Rotsee,
   280 MB for Lake Thun, about 1 GB for Lake Geneva) and the estimate says so.
   **Glaciers.** *As today* keeps the surveyed surface and, with *Ice down to the modelled bed*,
   fills today's glaciers with packed ice down to the GLAMOS ice-thickness model (blue ice more
   than 40 blocks down). *As in 2010 / 1973 / 1850* raises the surface to the glacier inventory
   of that year: inside the historic extent the ice thickness is modelled from the distance to
   the ice margin (a parabolic glacier profile, up to 900 m) and the terrain is lifted to it, so
   the Little Ice Age Rhône glacier once again fills the valley down to Gletsch.
   **Ground colours.** *swissIMAGE orthophoto* classifies the aerial image into meadow, dark
   meadow, cropland (farmland), bare soil, gravel, sand and snow for every open land column.
   The *draped* modes paint the orthophoto, the Siegfried map (1870-1949), the Dufour map
   (1845-1865) or today's national map onto the ground with the nearest coloured blocks (wool,
   concrete, terracotta and natural blocks), which turns the world into a walkable historic map.
   *Real tree heights and crowns from swissSURFACE3D* (off by default, about 20 MB per km²)
   reads the surface model, takes the vegetation height above the terrain and plants one tree
   per measured crown, inside and outside mapped forests, with the trunk height from the data;
   clearings stay open and garden, park and hedge trees appear where they really stand.
   *Name signs from swissNAMES3D* (on by default, 33 MB once) puts standing signs at peaks (with
   their height), passes, hills, chapels, towers, waterfalls, springs, caves, viewpoints,
   monuments, fountains, stations, field and local names, valleys, ridges, lakes, rivers and
   lifts; signs are moved to the nearest free land cell.
   Most layers are on by default: roads, railways, buildings with roof colours, power lines,
   villagers, street signs and lights, extras, tree heights, the orthophoto ground, geology,
   glaciers, lake floors, names, wildlife, crops, and replacing an existing world of the same name;
   untick what you do not want.
   *Wildlife* (on by default) writes animals as entities where they belong: cows on pastures
   below 1600 m, sheep on meadows and alpine pastures, pigs, chickens and horses within 40 m of
   a building, goats and rabbits above 1800 m, foxes (snow foxes high up), wolves and rabbits in
   forests, frogs in wetlands, salmon in every river and lake at least two blocks deep and squid
   in lakes deeper than six. The chunk biomes are set to match (meadow above 1000 m, swamp for
   wetlands, river or frozen river for water, forest, taiga, snowy slopes, stony peaks), so the
   game keeps spawning fitting animals afterwards. *Crops* puts ripe wheat, potatoes, carrots or
   beetroots on cropland (orthophoto fields and allotments), one crop per field patch.
   **Bridges and tunnels.** Roads and railways on bridges follow the surveyed deck height from
   swissTLM3D: the deck spans the valley or river with stone piers every 20 m and iron railings,
   covered wooden bridges get plank walls and a spruce roof, and jetties and footbridges are
   included. Tunnels, galleries and underpasses are bored along the surveyed axis with the road or
   rails inside, five blocks of clearance and glowstone lights every 8 m. Outdoor stairways become
   stone brick stairs facing uphill.
   **Building types.** The landscape model tells the type of every building and the generator
   builds it accordingly: open buildings and canopies as posts with a roof, storage tanks and
   reservoirs in iron and stone, greenhouses in glass, buildings under construction as fence
   scaffolds, towers with stone walls, viewing platforms and railings, chimneys as hollow brick
   stacks, high-rises in grey concrete with storeys, stadiums with an open roof over a grass
   pitch, observatories in white, car parks with open decks, large walls, enclosed walkways.
   Antennas become iron masts with a light. Houses get a door on the side facing the street,
   a ladder between the storeys, floor slabs every 3 m, a chimney on the ridge, a cellar under
   larger houses and balconies on the street side of taller ones. Bus stops get a shelter with
   the stop name, cemeteries rows of gravestones, ski jumps a raised inrun, toboggan and bob
   runs an ice surface.
   *Street lights* puts a lantern post every 30 m along streets where the surroundings are built
   up (needs roads and buildings), so villages are lit at night and hostile mobs stay off the
   streets in survival. *Roof blocks from swissIMAGE* averages the orthophoto colour of every
   building and picks tiles, dark, grey, white or green roofs. Villagers get a trade that fits
   their building: clerics at churches, toolsmiths and masons at industrial buildings, farmers,
   shepherds and fishermen at outlying farms, librarians, cartographers, butchers and armourers in
   town centres. Jetties and boat stops get boats, railway stations a minecart. In the last 150 m
   below the snow line thin snow layers thicken towards it instead of a hard edge.
   *Extras* (swissTLM3D only, on by default) adds walls and dry-stone walls, avalanche barriers,
   river bank revetments, dams and weirs, basins with water, cable cars, gondolas, chair lifts
   and ski lifts with masts and a cable, sports fields with white lines, running tracks,
   runways, station platforms, jetties, car parks, quarries, landfills, allotments, cemeteries,
   summit crosses, fountains, monuments, wayside shrines with a lantern and boundary stones.
   **Blocks.** Every option group has a *… blocks* fold-out listing the roles it uses (grass
   surface, soil, rock, house walls, roofs, asphalt, lane markings, pylons, each rock type, …)
   with a selector of vanilla blocks; the preselected ones are the defaults described above.
   Changed roles are marked and counted, and *Reset all settings* restores them.
4. **Spawn.** The spawn point is moved to walkable ground automatically (off water, ice, roads and
   buildings). As soon as the terrain is classified, the job card shows a live map of the world
   being written; click on it to move the spawn point, before or after the chunks are done. The
   change is written into level.dat until the world is opened in Minecraft for the first time;
   after that use /setworldspawn in the game. The map is built up stage by stage while the job
   runs: relief, water, land cover, forest, roads, railways, buildings, measured buildings,
   glaciers, rock types, ground colours and finally the full world. The view opens full screen
   on its own when the first stage arrives (Esc or *Close* returns), every stage is shown for
   about a second and a half with a crossfade even when the generator produces several at once,
   the chips at the top let you flip back to any stage, and unwritten chunks stay veiled until
   their region file is written. In full screen the log runs down the left side, the mouse wheel
   zooms the map around the cursor, dragging pans it (arrow keys pan too, + and - zoom, 0 resets),
   and a click without a drag still moves the spawn point. Every world also gets an icon.png rendered from the same map
   for the world list.
5. **Output.** *Game mode* (creative with commands, survival, adventure or hardcore) and
   *difficulty* (peaceful to hard) go into level.dat; hardcore locks the difficulty to hard.
   Either write straight into your Minecraft `saves` folder, or download a zip and
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
dotnet run --project src/MinecraftTopo.Cli -- --list-blocks
dotnet run --project src/MinecraftTopo.Cli -- --lv95 2621000,1126200,2622200,1127200 --out .\out\Ergisch --blocks asphalt=black_concrete,geo19=calcite
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
