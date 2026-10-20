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
All floor props compensate for transparent artwork padding in their group Z offsets;
the directional frames share the same visible contact baseline. Lanterns use fixed
world-aligned planes against stationary walls, and the low service pipe also keeps
its world orientation. See the [object format](../objects/README.md) for `planeAxis`
and the visible-baseline grounding formula.

The glass roof, plaster ceiling, service pipe, and room signs are hand-authored
SVG assets. Sign typography is editable SVG text for exact room names and
wayfinding. The map reuses the existing sky panorama and glass/steel door textures.

Run the production compiler, connectivity, doorway, and asset checks with
`dotnet run --project Tests/WolfNET.MapTests.csproj`. With the game running and a
WebGPU-capable Chromium instance exposing debugging port 9252, run
`node Tests/glassworks-browser-check.mjs 9252 http://127.0.0.1:5195` to check default
startup, texture uploads, entry interaction, crossing, and switching maps. The
browser check also saves five views to `artifacts/glassworks/` for visual review.
