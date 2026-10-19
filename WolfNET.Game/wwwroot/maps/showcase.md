# Showcase map

Choose **Showcase** on the page, then click the game canvas. Press Esc before
using the map buttons. Every switch resets the player to the selected map's spawn.

The GPU minimap is north-up and mirrors world X to match the camera's handedness.
Four galleries surround an open central hub and are connected by doorways. A sky
showroom annex and an obstacle corridor extend the map to 34 Ã— 25 cells beside the materials gallery.
You start at (18.5,22.5) in the bottom-left materials gallery, facing north toward the texturing examples. Gallery locations below refer
to the minimap; coordinates remain the original JSON world coordinates.

| Gallery | What to try |
| --- | --- |
| Bottom-left: materials | Walk around the pillar at (18,17): north has an eagle, east red brick, south mossy stone, west blue stone. The checkerboard around it changes both floor and ceiling textures. This gallery now occupies the former fallback area. |
| Top-left: independent flags | Enter the four lanes from the south. Left to right: a ray-transparent wall you can walk through; a ray-transparent solid wall; a visible wall you can walk through; a visible solid wall. Lights and barrels behind them make occlusion easy to compare. The open floor outside the lanes is walkable. |
| Bottom-right: sprites and groups | Most barrels and lights are walkable; pillars are solid. Props are grouped on the west side, leaving a clear approach and landing area on the east. Two rows of four columns at 0.4 scale near (9,20) are solid and jumpable. Their centers are 0.4 cells apart, and both rows share one collision cell. Approach from world position (9.5,19.5), face south, and press Space to jump two full cells to (9.5,21.5). The same jump clears the obstacle eastâ€“west and in both reverse directions. At (3,15), six barrels at 0.4 scale form a pyramid: three on the bottom, two in the middle, and one on top, using vertical offsets. The barrel-and-light group has repeated barrel IDs and position offsets. Its nearby pillar is placed on its own solid square. All sprites and groups are ray-transparent. |
| Top-right: compact pink fallback | A 7 × 7 room occupies world cells (3–9,2–8), with a south entrance at (5–7,9) and an east entrance at (10,5–7). Compare the missing-face walls at (4,4), (6,4), and (8,4), and the absent/unavailable sprites at (4,6) and (8,6). The two 2 × 2 pink surface patches at (3–4,7–8) and (8–9,7–8) demonstrate missing fields and unavailable images. Pink is intentional. |
| Far-left annex: sky | From the materials gallery, enter through (24,17â€“19). Cells (25â€“28,14â€“22) have a mesh ceiling with sky visible through its alpha; cells (29â€“32,14â€“22) have open sky. Walk and turn to compare the stationary distant sky with the nearby ceiling. The enlarged car group at (30,18) faces north; walk around it to see eight atlas views. Its vertical offset lowers it onto the floor, and its passage preserves the open sky. |
| Obstacle corridor | Enter from the independent-flags gallery through (24,11), then follow the loop north along x=26, across y=2, and south along x=30. Jump the floor columns at (26,8) and (30,4); hold Alt beneath the ceiling columns at (26,4) and (30,8). Clear sections between obstacles allow landing and standing. The loop returns to the entrance along y=11. |
| Crouch test: ceiling columns | At (9,16), two rows of four columns at 0.6 scale hang from the ceiling. They block standing movement. Hold Alt to pass beneath them from any side. If Alt is released underneath, the player stays low until clear. This obstacle shares one collision cell, just like the jump test at (9,20). |

Collision is per square, not per sprite silhouette. Lane walls reference `passageId: 11` to match the surrounding
color-stone floor and wood ceiling, including inside the walk-through wall.
