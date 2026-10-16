# Showcase map

Choose **Showcase** on the page, then click the game canvas. Press Esc before
using the map buttons. Every switch resets the player to the selected map's spawn.

The GPU minimap is north-up. Four galleries surround an open central hub and
are connected by doorways. You start in the north-west gallery, facing north.

| Gallery | What to try |
| --- | --- |
| North-west: materials | Walk around the pillar at (6,5): north has an eagle, east red brick, south mossy stone, west blue stone. The checkerboard changes both floor and ceiling textures. |
| North-east: independent flags | Enter the four lanes from the south. Left to right: a visible solid wall; a visible wall you can walk through; a ray-transparent solid wall; a ray-transparent wall you can walk through. Lights and barrels behind them make occlusion easy to compare. A separate invisible passage barrier at (13,8) demonstrates collision on Passage objects. |
| South-west: sprites and groups | A normal barrel is walkable, a pillar is solid, and an opaque barrel hides the scene behind it while remaining visible itself. The pillar group is walkable despite its solid children. The mixed group is solid despite its walkable barrels and has repeated IDs, offsets, and a child extending into a neighboring tile. The opaque group hides the light behind it without hiding its own members. |
| South-east: pink fallback | Compare a wall with no texture fields, a wall missing its west texture, and a wall whose image does not exist. Pink sprite billboards demonstrate absent and unavailable images. Two floor patches demonstrate missing surface fields and unavailable floor/ceiling files. These failures intentionally leave the map playable. |

Collision is per square, not per sprite silhouette. Walk-through walls have no
defined floor/ceiling, so their interior surfaces also show the pink fallback.
