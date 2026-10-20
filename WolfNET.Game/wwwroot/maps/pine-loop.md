# Pine Loop

`pine-loop.json` is a **256 × 512** outdoor racing circuit with a **2,296-unit
closed lap**, **54 major bends**, and nine distinct regions. Its long straights,
chamfered corners, technical esses, forest hairpins, quarry, village, spectator
areas, and service yard create a much longer route through the landscape.

Open `/?map=pine-loop` or choose **Pine Loop** in the existing map selector.
The player starts at **(25.5,445.5)** facing north, behind the checkerboard.
The existing movement and camera controls apply.

The starting straight has three full asphalt lanes at **X 24–26, Y 396–449**.
The third lane tapers diagonally across **(26,395)**, leaving two lanes at
**X 24–25** to the north. The checkerboard crosses the three starting lanes at
**Y 442**; starting boxes repeat every four tiles from Y 400 to 448. The normal
racing surface remains two tiles wide throughout the larger course, with extra
space at the tiled corner joins and mud shoulders beside the asphalt.

The route travels north through the western technical sections, crosses the
quarry and northern ridge, descends the eastern village and spectator sectors,
then returns through the southern double hairpins to the starting grid.

| Region | Layout and scenery |
| --- | --- |
| Paddock and spectator straight | Seven larger grandstand modules line the west side of the start at X 17, Y 402–450. The fenced pit apron, three roofed garages, and race control building sit opposite them. |
| Technical esses | Successive west-side direction changes between Y 266 and 358, with mud runoff, separate tire stacks, approach buffers, and dedicated signage. |
| Woodland hairpins | Forest corners wind between X 24–84 and Y 120–218, alternating short straights and long direction changes. |
| Stone Cut quarry | Pale stone ground, rock piles, portal stripes, and a 22-tile covered tunnel at X 82–103, Y 44–45. Both tunnel lanes stay clear. |
| North ridge | The northern straight at Y 26 runs east before a sequence of eastern switchbacks descends toward the village. |
| Marshal Village | Two accessible timber and plaster huts, tiled approaches, a village sign, and two additional grandstand modules beside the Y 212 section. |
| Spectator Plaza | A tiled forecourt at X 174–200, Y 261–279 connects to the track. Three larger stands line its north side. |
| Maintenance Yard | Red and blue ribbed container blocks, service barrels, and an open center aisle occupy X 192–218, Y 336–360. |
| South loop | Two large returning hairpin complexes between Y 414 and 480 reconnect with the spectator straight. |

The pit apron occupies **X 29–33, Y 396–450**, with a southern connection to
the main circuit. The garage entrances are at **(34,412), (34,416), (34,420)**;
static car displays sit inside. The race control building is at X 34–40,
Y 388–394. Seven stand modules face the starting straight, two face the village
section, and three face the plaza section: **12 larger stand modules** in total.

The forest uses **2,113 individually placed tree sprites** across three species:
pines, oaks, and birches. Most trees occupy scenic corridors beside the racing
surface or two perimeter belts. Sparse groves fill the deeper infield. A dedicated
opaque forest texture closes the distant map boundary. Trees and undergrowth
keep clear setbacks around the course, pit entrances, huts, and public areas.
The complete map contains **3,325 scenery sprites**.

Tire barriers use separate, symmetric black and white single-stack sprites.
Reusable groups place individual stacks at different world positions in rows,
corner clusters, elbows, and double rows. Buffers use one directional sprite
with **two unique views**: the long face and the narrow end. The atlas repeats
those views for opposite directions and holds the sprite orientation within
each view sector. Fences and grandstands keep fixed world planes.

Map-specific definitions live in [`../objects/raceway`](../objects/raceway/).
Generated materials, forest scenery, tire stacks, buffer atlas, and transparent
props live in [`../textures/raceway`](../textures/raceway/), with their generation
prompts. Landmark signs, container fronts, road paint, pit fronts, and portal
stripes are editable SVG assets. Painted-floor SVGs embed the seamless source
material to match the surrounding asphalt. The map reuses the existing sky,
stone, limestone, wood, teal wall, plaster ceiling, and eight-view car atlas.

Run **`node Tools/build-pine-loop-map.mjs`** from the repository root to rebuild
the current map, owned landmark definitions, fixture, browser views, and route
overview. The entry script delegates the large layout to
`Tools/build-pine-loop-grand-circuit.mjs`. The previous 80 × 70 layout remains
available through the explicit `--legacy` authoring option. Shared tree, tire,
and directional buffer definitions are preserved as source assets.

The generator writes these review artifacts:

- `artifacts/pine-loop-overview.svg`: a labeled north-up route diagram.
- `artifacts/pine-loop-views.json`: viewpoints near the start, corners, and landmarks.
- `Tests/Fixtures/pine-loop-lap.json`: the complete lap, major corners, zone checkpoints,
  usable landmark approaches, and the same browser viewpoints.

`Tests/PineLoopChecks.cs` loads the map through the production compiler and traces
a complete lap with the existing collision circle. It checks the explicit
256 × 512 dimensions, route length, three-to-two lane transition, landmark
approaches, inline stands, tree species and sprite budget, tunnel roof, pit
entrances, separate tire groups, directional buffer views, all referenced assets,
and expanded-map editor load/export.

```powershell
dotnet run --project Tests/WolfNET.MapTests.csproj
```

With the game and a Chromium browser running with WebGPU and remote debugging,
check rendered landmarks and both buffer views using actual player positions:

```powershell
node Tests/pine-loop-browser-check.mjs 9255 http://127.0.0.1:53719 start grandstand-row buffer-front buffer-side
```
