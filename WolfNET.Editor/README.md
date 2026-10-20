# WolfNET browser map editor

A separate .NET 10 ASP.NET Core project. The UI uses Canvas 2D and native browser
modules, with no npm packages or external UI services. It works without WebGPU.
The project links the engine's map definitions and compiler directly so validation
follows the engine's rules.

## Run

```powershell
dotnet run --project WolfNET.Editor/WolfNET.Editor.csproj
```

Open http://127.0.0.1:5080. In Rider, open `WolfNET.slnx` and launch
`WolfNET.Editor`. The editor is a local authoring tool and serves only local host
names. Its APIs do not enable cross-origin requests.

By default, assets come from the sibling `WolfNET.Game/wwwroot` directory. To use another asset
directory (including when running a published editor):

```powershell
dotnet run --project WolfNET.Editor/WolfNET.Editor.csproj -- --EngineRoot="C:\path\to\WolfNET\WolfNET.Game\wwwroot"
```

## Edit

- Open an existing map, or create a new enclosed 24 × 24 map. Resize up to 64 × 64.
- Choose a library object and paint by clicking or dragging. One stroke is one undo step.
- Use **Fill** for a connected area, **Pick** or right-click to sample a cell, and
  **Spawn** to place the player in a walkable cell. Set direction and speeds in the inspector.
- Canvas coordinates follow `worldMap[x][y]`: X increases rightward and Y downward.
  The engine minimap mirrors X, so the editor and the in-game minimap have opposite handedness.
- Enable **Collision** to see blocked squares, including invisible passages and walls.
  **Ray blockers** shows occlusion independently. Transparent walls have dashed outlines.
- Edit the selected object's JSON and press **Apply definition**. The texture picker
  updates a floor, ceiling, wall face, or sprite texture before applying. Use **Add object**
  for a new passage, wall, sprite or group. Group members and offsets are edited in JSON.
  Each group member can set `scale: { "x": 0.75, "y": 1.5 }` for independent
  width/height multipliers and `offset.z` for vertical movement (positive is up).
  Defaults are unit scale and zero elevation. The top-down preview shows scale;
  elevation affects the in-game shader without changing the member's map position.
- In **Object settings → Choose a texture**, use **Import texture** to upload PNG, JPEG,
  WebP or BMP images (up to 10 MB). Images are resized to 64 × 64 using nearest-neighbor
  sampling and converted to PNG, preserving transparency. The imported texture is selected
  for the current surface; press **Apply definition** to use it. Imports are stored immediately
  in the game's `wwwroot/textures` with unique filenames; existing textures are never overwritten.
- **Map settings** and **Object settings** have separate, independently scrolling panels.
  **Delete object** removes the selected definition from this map's library. When it is
  referenced, choose a replacement for its cells, passage references, or group members.
  Undo restores the definition and references. Shared library files remain unchanged.
  Deleted library IDs persist in optional `editor` metadata, which the engine ignores.
- Walls, doors, sprites and groups use `passageId`. Floor and ceiling paths belong to passages.
  Keep explicit sprite texture layers unique when using different images.
- Resizing preserves interior cells and reconstructs the outer boundary. Expanding turns
  the former boundary into floor. Shrinking may move an out-of-bounds spawn to a walkable cell.
  Undo restores the previous map.
- **Validate with engine** checks dependencies, boundaries, texture layer conflicts,
  sprite offsets and spawn validity. Invalid intermediate edits can be undone or repaired;
  they cannot be saved or exported as a bundle.

Shortcuts: **B** brush, **F** fill, **I** pick, **P** spawn, **Ctrl/Cmd+Z** undo,
**Ctrl/Cmd+Shift+Z** redo, **Ctrl/Cmd+S** save. Zoom controls and **Fit** adjust the canvas;
scroll the viewport to reach cells at larger zoom levels.

## Save, export and import

**Save map** writes `wwwroot/maps/<name>.json`. Used object definitions are copied to
`wwwroot/objects/editor/<name>/` with content hashes in their filenames. This preserves
shared definitions and old snapshots until the new map file is committed. Replacing an
existing map requires confirmation. Older unreferenced snapshot files are retained.

**Export → ZIP** downloads `maps/<name>.json` plus every required object definition.
Extract the archive into the engine's `wwwroot`. The archive includes available PNG images
referenced by the map, including imported textures. Intentionally missing textures remain missing.

**Export → Map JSON** downloads just the map and its object paths. Save first or use a ZIP
after editing definitions; a standalone map JSON cannot carry those definitions.

**Import** accepts engine map JSON. Referenced object files must already exist under the
engine's `wwwroot/objects` (extract a bundle there first). It also accepts a complete
editor document shaped as `{ "name": "level", "map": { ... }, "objects": { "0": { ... } } }`.
Imports are held in the browser until explicitly saved.

Applied changes are stored as a browser draft. On the next visit, **Restore draft** recovers
them. Unapplied object JSON text is not included in drafts. Saving or opening another map
clears the active draft. Browser storage is a convenience; use Save or Export for durable copies.

## Play

Start the engine separately with `dotnet run --project WolfNET.Game/WolfNET.Game.csproj --launch-profile http`.
Save the map, then press **Play**. The default engine address is `http://localhost:5025`;
change **Engine URL** to match another profile. The engine accepts `?map=<name>` at startup.
WebGPU requirements still apply to the engine, including secure contexts on LAN devices.

## Verification

From the repository root:

```powershell
dotnet build WolfNET.slnx
dotnet run --project Tests/WolfNET.MapTests.csproj
node Tests/editor-model.test.mjs
node Tests/editor-api-check.mjs
```

The C# checks include isolated saves, overwrite protection, ZIP dependencies, path validation,
and engine reloads. The Node checks cover grid orientation, resizing, fill and brush strokes.
The API check starts a temporary editor against a disposable copy of the assets and
exercises HTTP saves, exports, validation failures and overwrite protection.
For a Chromium browser started with remote debugging enabled, run:

```powershell
node Tests/editor-browser-check.mjs 9246 http://127.0.0.1:5080
```

The browser check edits only its browser document; disk-save behavior is tested in a
temporary asset directory. Node 22 or newer is required for its built-in WebSocket client.

Doors are available under **Doors** in the palette and **Add object**. Set `axis`
(`x` normal for east/west travel, `y` normal for north/south travel) and
`openSeconds` in object JSON. `raycastTransparent` enables the texture's alpha
view-through behavior; `collides` controls closed-door movement. Doors export with
their textures and load closed. Runtime F interaction is in the game; F remains
the fill shortcut in the editor.
