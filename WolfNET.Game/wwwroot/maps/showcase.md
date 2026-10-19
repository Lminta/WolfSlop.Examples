# Showcase map

Choose **Showcase** on the page, then click the game canvas. Press Esc before
using the map buttons. Every switch resets the player to the selected map's spawn.

The GPU minimap is north-up and mirrors world X to match the camera's handedness.
Four galleries surround an open central hub and are connected by doorways. A sky
showroom annex extends the map to 34 × 25 cells beside the fallback gallery.
You start in the top-right gallery, facing north. Gallery locations below refer
to the minimap; coordinates remain the original JSON world coordinates.

| Gallery | What to try |
| --- | --- |
| Top-right: materials | Walk around the pillar at (6,5): north has an eagle, east red brick, south mossy stone, west blue stone. The checkerboard changes both floor and ceiling textures. |
| Top-left: independent flags | Enter the four lanes from the south. Left to right: a ray-transparent wall you can walk through; a ray-transparent solid wall; a visible wall you can walk through; a visible solid wall. Lights and barrels behind them make occlusion easy to compare. The open floor outside the lanes is walkable. |
| Bottom-right: sprites and groups | Most barrels and lights are walkable; pillars are solid. Props are grouped on the west side, leaving a clear approach and landing area on the east. Two rows of four columns at 0.25 scale near (9,20) are solid and jumpable. Their centers are 0.4 cells apart, and both rows share one collision cell. Approach from world position (9.5,19.5), face south, and press Space to jump two full cells to (9.5,21.5). The same jump clears the obstacle east–west and in both reverse directions. At (3,15), six barrels at 0.4 scale form a pyramid: three on the bottom, two in the middle, and one on top, using vertical offsets. The barrel-and-light group has repeated barrel IDs and position offsets. Its nearby pillar is placed on its own solid square. All sprites and groups are ray-transparent. |
| Bottom-left: pink fallback | Compare a wall with no texture fields, a wall missing its west texture, and a wall whose image does not exist. Pink sprite billboards demonstrate absent and unavailable images. Two floor patches demonstrate missing surface fields and unavailable floor/ceiling files. These failures intentionally leave the map playable. |
| Far-left annex: sky | From the fallback gallery, enter through (24,17–19). Cells (25–28,14–22) have a mesh ceiling with sky visible through its alpha; cells (29–32,14–22) have open sky. Walk and turn to compare the stationary distant sky with the nearby ceiling. |
| Crouch test: ceiling columns | At (9,16), the same two rows of four quarter-scale columns hang from the ceiling. They block standing movement. Hold Alt to pass beneath them from any side. If Alt is released underneath, the player stays low until clear. This obstacle shares one collision cell, just like the jump test at (9,20). |

Collision is per square, not per sprite silhouette. Lane walls reference `passageId: 11` to match the surrounding
color-stone floor and wood ceiling, including inside the walk-through wall.
