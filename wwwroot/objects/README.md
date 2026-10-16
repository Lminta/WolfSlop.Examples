# Map objects

`index.json` lists every object file, relative to this directory. The game loads all
definitions before compiling `maps/level1.json`. Grid indexing stays `worldMap[x][y]`;
every value, including zero, is an object ID.

All objects require `id`, `type`, `raycastTransparent`, and `collides`.
`collides: true` blocks walking through the entire square. Independently,
`raycastTransparent: false` stops DDA rays at that square. This is cell occlusion,
not alpha blending; transparent walls are skipped by raycasting.

Types:

- `passage`: `floorTexture` and `ceilingTexture`.
- `wall`: `textures` with `north`, `east`, `south`, and `west` paths. North is -Y;
  east is +X. Each face can use a different image.
- `sprite`: `texture`, zero-based GPU `textureLayer`, and `passageId` for the floor
  and ceiling. A sprite is placed at the center of its tile.
- `spriteGroup`: `passageId` and a `sprites` list containing
  `{ "spriteId": 100, "offset": { "x": -0.25, "y": 0 } }` entries. IDs may repeat.
  Offsets add to the group tile's center and can cross into adjacent tiles, but
  final positions must remain inside the map. Children must be Sprite objects.
  The group's flags control its original square, regardless of child flags or offsets.

Texture paths are relative to the application base, e.g. `pics/wood.png`.
Images use the existing 64x64 format. Missing fields, failed image loads, and images
of other sizes render pink. Different images cannot claim the same explicit sprite
layer. Sprite layers must fit the GPU's default 256-layer array; other surfaces
are assigned unused layers automatically. Walls have no floor/ceiling definition,
so any sampled surface inside a wall tile is pink.

The compiled renderer map, navigation map, sprite list, and minimap all update
together when a cell changes. Invalid edits leave the previous revision intact.

Run the compiler and loader regression checks from the repository root:

```powershell
dotnet run --project Tests/WolfNET.MapTests.csproj
```

For browser and shader checks, run the app and a Chromium browser with WebGPU
and remote debugging enabled, then pass the debugging port and app URL to:

```powershell
node Tests/browser-check.mjs 9243 http://127.0.0.1:5194
node Tests/browser-check.mjs 9243 http://127.0.0.1:5194 --missing-textures
```

These checks use Node's built-in WebSocket support (Node 22 or later) and no
additional packages. The second run simulates failed loads for every texture.
