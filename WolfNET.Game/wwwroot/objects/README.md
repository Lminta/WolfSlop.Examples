# Map objects

Each map JSON has an `objects` index mapping IDs to paths relative to the map file:

```json
"objects": {
  "0": "../objects/passages/passage-mossy-floor-wood-ceiling.json",
  "1": "../objects/walls/wall-eagle.json"
}
```

Definitions live in `objects/passages`, `walls`, `sprites`, and `sprite-groups`.
Maps share these definitions by referencing the same paths in their own indexes.
Grid indexing stays `worldMap[x][y]`; every value, including zero, is an object ID.
Only grid objects and their dependencies (group members and passage references) are
loaded. Unused index entries are not fetched. An indexed ID must match the JSON's
`id`. Renaming a file requires updating its path in the owning map's index.
Only textures referenced by compiled cells and placed sprites are requested.
Switching maps disposes old game objects and input handlers, destroys scene GPU
textures and buffers, and releases references to the old map. GPU pipelines are reused.

All objects require `id`, `type`, `raycastTransparent`, and `collides`.
`collides: true` blocks walking through the entire square. Independently,
`raycastTransparent: false` stops DDA rays at that square. This is cell occlusion,
not alpha blending; transparent walls are skipped by raycasting.

Types:

- `passage`: `floorTexture` and `ceilingTexture`.
- `wall`: `textures` with `north`, `east`, `south`, and `west` paths. North is -Y;
  east is +X. Each face can use a different image. Required `passageId`
  selects the floor and ceiling, including inside walk-through and ray-transparent walls.
- `sprite`: `texture`, zero-based GPU `textureLayer`, and `passageId` for the floor
  and ceiling. A sprite is placed at the center of its tile.
- `spriteGroup`: `passageId` and a `sprites` list containing
  `{ "spriteId": 100, "offset": { "x": -0.25, "y": 0, "z": 0.2 }, "scale": { "x": 0.75, "y": 1.5 } }`
  entries. IDs may repeat. Each member's scale multiplies its billboard width (x)
  and height (y), about its center; omitted components default to 1. Multipliers
  must be finite and nonnegative; zero hides that dimension. Scale does not move
  the member's X/Y position or change collision.
  Offset X/Y add to the group tile's center and can cross into adjacent tiles, but
  final X/Y positions must remain inside the map. Offset Z defaults to zero and
  shifts the sprite vertically in map units: positive moves up, negative moves down.
  At zero Z, a unit-height sprite spans floor to ceiling. To keep a shorter sprite
  grounded, use `offset.z = (scale.y - 1) / 2`. Children must be Sprite objects.
  The group's flags control its original square, regardless of child flags or offsets.

The supplied maps use scaled group presets for barrels (`0.75` on both axes) and
lights (`0.5` on both axes), including single-member groups for direct placements.
Barrels use Z = -0.2 to compensate for transparent padding and remain grounded; lights use Z = 0.25 to hang from the ceiling.

Texture paths are relative to the application base, e.g. `textures/wood.png`.
Images may use different resolutions. The renderer resamples them into a shared array,
preserving source resolution up to 2048px per side and a 128 MiB scene texture budget.
All texture layers have a complete mip chain, with trilinear and 4× anisotropic
filtering to reduce distant texture shimmer and moiré. The memory budget includes
mipmaps. Wall and floor gradients are calculated before texture-coordinate wrapping;
sprites use premultiplied alpha filtering to avoid dark fringes around cutouts.
Missing fields and failed image loads render pink. Sprite transparency uses the image's
alpha channel, including partial alpha; opaque black remains visible.
Different images cannot claim the same explicit sprite
layer. Sprite layers must fit the GPU's default 256-layer array; other surfaces
are assigned unused layers automatically. All walls, sprites, and sprite groups require a valid passageId referencing a passage object. Missing floor/ceiling textures on that passage use the pink fallback.

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
