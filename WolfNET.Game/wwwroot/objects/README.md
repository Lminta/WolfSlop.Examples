# Map objects

Each map JSON has an `objects` index mapping IDs to paths relative to the map file:

```json
"objects": {
  "0": "../objects/passages/passage-mossy-floor-wood-ceiling.json",
  "1": "../objects/walls/wall-eagle.json"
}
```

Definitions live in `objects/passages`, `walls`, `doors`, `sprites`, and `sprite-groups`.
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
- `door`: `texture`, `passageId`, `axis` (`x` or `y`), and `openSeconds` (default 0.8). See sliding doors below.
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

Texture paths are relative to the application base, e.g. `textures/wolf/wood.png`.
Images may use different resolutions. The renderer resamples them into a shared array,
preserving source resolution up to 2048px per side and a 128 MiB scene texture budget.
All object definitions accept `jumpable` (default `false`). Solid jumpable cells
permit player movement while `InAir` is true, independently of ray transparency.
Sprite groups use the group's flag, rather than their members' flags. Passage
references supply surfaces; each wall/sprite/group retains its own jumpable flag.
All object types also accept `crouchable` (default `false`). With `collides: true`,
this allows passage only while crouched on the ground. Groups retain their own
stance flags independently of their sprite templates. Map boundaries must remain
non-jumpable and non-crouchable.

Maps can define `"sky": { "texture": "textures/skyboxes/skybox.jpg", "rotationDegrees": 0 }`.
Passages default to `"ceilingMode": "texture"`; `"ceilingMode": "sky"` opens the ceiling.
A ceiling texture's alpha reveals the map's sky, supporting mesh and translucent
roofs. Missing ceiling textures remain opaque pink. Walls, sprites and groups
inherit ceiling behavior through `passageId`. Sky panoramas load separately from
square tile textures and retain their aspect ratio, with mipmaps and horizontal wrapping.

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

### Directional sprite atlases

A sprite can add `rotationDegrees` and `atlas` alongside its existing `texture` and
`textureLayer`. `atlas.frames` is a list of pixel rectangles (`x`, `y`, `width`,
`height`) in the source image; `atlas.directions` defaults to 1. Directional views
occupy the first N frames, ordered clockwise starting with the front view. Zero
rotation faces east (+X); 90 faces south (+Y), and -90 faces north. The view is
selected from the sprite-to-player angle, so looking around in place does not
change it. Extra frames are reserved for future animation; there is no playback yet.

Every atlas frame reserves one consecutive texture layer starting at `textureLayer`;
these slots must fit within 0–255 and cannot overlap different textures or frames.
Frames are cropped before mip generation to prevent neighboring views from bleeding.
Frame width/height preserves aspect ratio, and group scale/elevation apply normally.
Use consistent frame height and baseline for steady size and ground alignment.
The editor accepts these fields in object JSON and previews the first frame.
See `sprites/sprite-directional-car.json` for an eight-view example.

### Sliding doors

`type: "door"` uses a thin plane through the center of its tile. `axis: "x"`
separates east/west and slides along Y; `axis: "y"` separates north/south and
slides along X. `openSeconds` must be positive and finite. Doors cannot be placed
on map boundaries. Their floor/ceiling use `passageId`.

Press **F** from one of the four cardinal neighboring tiles while aiming at the
door plane. Each press toggles opening/closing; a held key does not retrigger.
Pressing during animation reverses smoothly. Collision remains active until fully
open, then the whole tile becomes walkable. Closing restores collision; an occupied
door tile reopens and an overlapping player circle prevents starting a close.

`raycastTransparent: false` stops DDA at the remaining sliding panel; the open gap
lets rays through during animation. This mode always draws an opaque panel.
`raycastTransparent: true` continues rays behind the door and blends its texture's
alpha, including cutouts and partially transparent glass, over the scene. Both
modes block walking when `collides: true`. Door geometry stays aligned to the world
and participates in the sprite pass's depth/alpha handling. Runtime openness is
per placed door, resets on map reload, and is not saved into map JSON.

`autoCloseSeconds` sets the delay after a door finishes opening. Positive values
enable automatic closing; zero, negative values, or an omitted field disable it.
Existing doors use `10`. If the timer expires while the player's collision circle
overlaps the doorway, closing waits until the player clears it. A collidable door
reopens if the player enters while it is closing. Manual reopening restarts the
delay; each placed door keeps its own timer.
