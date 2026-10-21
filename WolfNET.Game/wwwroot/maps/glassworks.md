# The Glassworks

`glassworks.json` is the default playable map: a compact, complete **25 × 23**
botanical station built around a central glasshouse. Warm limestone, worn teal
tiles, brass pumping equipment, terracotta pots, and botanical mosaic paths tie
the rooms together. Workbenches sit in the potting wing, herbs occupy the east
wing, and the north pump room feeds the station beside its cistern.

The player starts in the southern entrance hall at **(12.5,20.5)**, facing north.
Walk toward the glass door at **(12,17)** and press **F** from the neighboring
tile. Inside, the mosaic cross leads around a large central specimen beneath a
broken roof. The surrounding glass roof keeps its iron frame and translucent
panes, with the sky visible through gaps. Ferns and wall-mounted lanterns frame clear
walking routes rather than filling the floor.

```text
                            NORTH (-Y)
       +--------------+     +--------------+     +--------------+
       |  SEED STORE  |--D--|  PUMP ROOM   |--D--|   CISTERN    |
       +------D-------+     +------D-------+     +------D-------+
              |                    |                    |
       +--------------+     +--------------+     +--------------+
       |   POTTING    |--D--|  GLASSHOUSE  |--D--|  HERBARIUM   |
       |  workbenches |     | specimen /   |     |  herb pots   |
       |              |     | broken roof  |     |              |
       +--------------+     +------D-------+     +--------------+
              |                    |                    |
          low pipe                 |              spilled planters
          hold Alt                 |                Space jump
              |                    |                    |
       +--------------+     +--------------+     +--------------+
       | WEST SERVICE |--D--| ENTRANCE HALL|--D--| EAST SERVICE |
       +--------------+     |    START ^   |     +--------------+
                            +--------------+

       D = door; lines show connections, not exact distance or tile scale.
```

The west loop runs from the glasshouse through the potting room and seed store,
then back through the pump room. The east loop links the herbarium, cistern, and
pump room. Each wing has its own floor treatment, signs, furnishings, and doors,
so the plan reads as one working station with distinct rooms.

Two maintenance routes reconnect the entrance hall with the side wings. In the
southwest, hold **Alt** beneath the low pipe at **(3,17)** to pass between the
west service room and potting wing. In the southeast, use **Space** to jump the
spilled planters at **(21,17)** between the east service room and herbarium.
Both obstacles have clear approaches and landing space on either side. Line up
with the gap before jumping; the jump direction stays fixed after takeoff.

A parked car occupies **(20,20)** in the east service room. Face it from nearby
and press **F** to enter the Pine Loop racing circuit. The car shares the same
interaction key as doors.

Coordinates use **`worldMap[x][y]`**, not conventional row-first indexing. The
outer JSON arrays are X columns, and north is decreasing Y. The diagram above
uses the conventional north-up view; do not read the JSON arrays as its rows.
Map-specific object definitions live in [`../objects/glassworks`](../objects/glassworks/).

Open the game at **`/?map=glassworks`**, for example
`http://localhost:5025/?map=glassworks`, or choose **The Glassworks** in the map
selector. Click the game to capture the mouse. **W/S** move forward/back,
**A/D** strafe, and the **mouse** looks around. **F** opens or closes the door
you face from a cardinal neighboring tile; wait for it to open before crossing.
**Space** jumps, **Alt** crouches, **V** changes camera mode, and **Esc** releases
the mouse.

The material and sprite atlases were generated for this map with the built-in
image generation tool. The material atlas supplies limestone walls and paving,
teal wainscot, a pressure panel, checker flooring, and botanical mosaic. The
transparent sprite atlas supplies the fern, potting bench, pump, and lantern.
Their exact prompts and extraction details are preserved in
[`materials.prompt.txt`](../textures/glassworks/materials.prompt.txt) and
[`sprites.prompt.txt`](../textures/glassworks/sprites.prompt.txt), beside the
source atlases and extracted textures in `textures/glassworks/`.

Benches and pumps use eight directional views so their asymmetric sides and backs
remain consistent as the player circles them. Their new reference-based turntable
atlases and exact prompts are documented in
[`directional.prompt.txt`](../textures/glassworks/directional.prompt.txt).
Both set `facingCamera: false`: their quads hold one world orientation within a
45-degree view sector and switch orientation and texture together at its boundary.
Set this flag to `true` in the sprite definition to compare the original billboard
behavior; groups inherit it from the bench or pump sprite.
The runtime v2 atlases isolate each connected silhouette from the generated
sheet, avoiding neighboring furniture fragments. The engine views are ordered by
wheel and gauge orientation. Rebuild them from the preserved sheets with
`node Tools/rebuild-glassworks-atlases.mjs`; measured extraction bounds and view
assignments live in `textures/glassworks/directional-atlas-layout.json`.
All floor props compensate for transparent artwork padding in their group Z offsets;
the directional frames share the same visible contact baseline. The fern uses
`fern-grounded-v2.png`: detached pixels below the actual pot base were removed,
and its visible baseline is row 469 of 512. Lanterns use fixed side-profile planes
perpendicular to their supporting walls. The mounting plate touches the masonry,
while the bracket and lantern project into the room. A mirrored copy of the
original image handles the opposite wall directions. The low service pipe also
keeps its world orientation.
See the [object format](../objects/README.md) for `planeAxis` and the
visible-baseline grounding formula.

The glass roof, plaster ceiling, service pipe, and room signs are hand-authored
SVG assets. Sign typography is editable SVG text for exact room names and
wayfinding. The map reuses the existing sky panorama and glass/steel door textures.

Run the production compiler, connectivity, doorway, and asset checks with
`dotnet run --project Tests/WolfNET.MapTests.csproj`. With the game running and a
WebGPU-capable Chromium instance exposing debugging port 9252, run
`node Tests/glassworks-browser-check.mjs 9252 http://127.0.0.1:5195` to check default
startup, texture uploads, entry interaction, crossing, and switching maps. The
browser check also saves eleven views to `artifacts/glassworks/` for visual review.
