# Showcase map

Choose **Showcase** on the page, then click the game canvas. Press Esc before
using the map buttons. Every switch resets the player to the selected map's spawn.

The GPU minimap is north-up and mirrors world X to match the camera's handedness.
Four galleries surround an open central hub and are connected by doorways.
You start in the top-right gallery, facing north. Gallery locations below refer
to the minimap; coordinates remain the original JSON world coordinates.

| Gallery | What to try |
| --- | --- |
| Top-right: materials | Walk around the pillar at (6,5): north has an eagle, east red brick, south mossy stone, west blue stone. The checkerboard changes both floor and ceiling textures. |
| Top-left: independent flags | Enter the four lanes from the south. Left to right: a ray-transparent wall you can walk through; a ray-transparent solid wall; a visible wall you can walk through; a visible solid wall. Lights and barrels behind them make occlusion easy to compare. A separate invisible passage barrier at (13,8) demonstrates collision on Passage objects. |
| Bottom-right: sprites and groups | A normal barrel is walkable and a pillar is solid. The pillar group is walkable despite its solid children. The mixed group is solid despite its walkable barrels and has repeated IDs, offsets, and a child extending into a neighboring tile. All sprites and groups are ray-transparent. |
| Bottom-left: pink fallback | Compare a wall with no texture fields, a wall missing its west texture, and a wall whose image does not exist. Pink sprite billboards demonstrate absent and unavailable images. Two floor patches demonstrate missing surface fields and unavailable floor/ceiling files. These failures intentionally leave the map playable. |

Collision is per square, not per sprite silhouette. Lane walls define `floorTexture` and `ceilingTexture` to match the surrounding
color-stone floor and wood ceiling, including inside the walk-through wall.
