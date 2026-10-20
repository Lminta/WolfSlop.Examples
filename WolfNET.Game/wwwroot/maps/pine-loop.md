# Pine Loop

`pine-loop.json` is a **256 × 512** outdoor racing circuit with a **2,452-unit
closed lap**, **18 broad bends**, and nine distinct regions. Large interlocking
loops occupy the infield instead of repeating small switchbacks. Rounded bends
have radii of 14–30 blocks, and the eastern side has a **384-block uninterrupted
straight** between its corner arcs. Distances follow the tile centerline.

Open `/?map=pine-loop` or choose **Pine Loop** in the existing map selector.
The player starts at **(25.5,445.5)** facing north, behind the checkerboard.
The existing movement and camera controls apply.

The starting straight has three full asphalt lanes at **X 24–26, Y 396–449**.
The third lane tapers diagonally across **(26,395)**, leaving two lanes at
**X 24–25** to the north. The checkerboard crosses the three starting lanes at
**Y 442**; starting boxes repeat every four tiles from Y 400 to 448. The normal
racing surface remains two tiles wide throughout the larger course, with extra
space at the tiled corner joins and mud shoulders beside the asphalt.

From the grid, the route heads north into the meadow loop, curls through the
woodland and central loops, passes the village on the inner eastern return,
and rounds the northern bowl through the quarry tunnel. The long outer eastern
straight leads to a wide southern sweep and back to the grid. There are no
crossings or shared road sections.

Continuous colliding safety rails enclose the racing surface, with a one-tile
shoulder beside its two lanes. Mesh fences line the straights; individual black
and white tire stacks replace them around the turns and corners. These close the loop
cuts, including diagonal gaps. The **Mud Express** is the one deliberate shortcut:
a two-tile dirt lane at **Y 320–321, X 102–139**, linking the Meadow loop at X 100
to the central return at X 140. Entry and rejoin signs mark both ends. Quarry,
village, plaza, and service-yard pockets keep access from their own road section.

| Region | Layout and scenery |
| --- | --- |
| Paddock and spectator straight | Seven larger grandstand modules line the west side of the start at X 17, Y 402–450. The fenced pit apron, three roofed garages, and race control building sit opposite them. |
| Meadow loop | A large lower infield loop winds between X 24–140 and Y 280–408, with rounded turns, tire stacks, approach buffers, and meadow scenery. |
| Woodland loop | Broad curves between X 64–176 and Y 184–248 turn through the central forest. |
| Stone Cut quarry | Pale stone ground, rock piles, portal stripes, and a 22-tile covered tunnel at X 82–103, Y 44–45. Both tunnel lanes stay clear. |
| Northern bowl | A broad loop between Y 44 and 116 crosses the quarry and feeds the long eastern straight. |
| Marshal Village | Two accessible timber and plaster huts, tiled approaches, a village sign, and two additional grandstand modules beside the inner return at X 208. |
| Spectator Plaza / central loop | A tiled forecourt at X 179–205, Y 261–279 opens onto the central loop at X 176. Three larger stands line its north side. |
| Eastern straight / Maintenance Yard | The outer straight follows X 232. Red and blue container blocks, service barrels, and an open aisle occupy X 192–218, Y 372–396, with access from the straight. |
| Southern sweep | Large 30-block-radius corners and a long southern straight at Y 478 reconnect with the starting grid. |

The pit apron occupies **X 29–33, Y 396–450**, with a southern connection to
the main circuit. The garage entrances are at **(34,412), (34,416), (34,420)**;
static car displays sit inside. The race control building is at X 34–40,
Y 388–394. Seven stand modules face the starting straight, two face the village
section, and three face the plaza section: **12 larger stand modules** in total.

The forest uses **3,856 individually placed tree sprites** across three species:
pines, oaks, and birches. Most trees occupy scenic corridors beside the racing
surface or two perimeter belts. Sparse groves fill the deeper infield. A dedicated
opaque forest texture closes the distant map boundary. Trees and undergrowth
keep clear setbacks around the course, pit entrances, huts, and public areas.
The complete map contains **10,324 scenery sprites**, including the continuous
safety rails. The existing 60-block render distance culls distant scenery.

**79 event props** populate the grid, spectator areas, quarry, infield loops,
and eastern straight. Checkered flags on poles and five-person cheering groups
each use a three-frame animation at four frames per second. Three phase presets
keep nearby groups from moving in unison. Crowd presets use half their original
size (1.2-unit scale), with the feet kept on the ground. A static five-lamp race signal marks
the start; it is scenery rather than a race countdown. Four fake sponsors have
reusable billboard presets in both fixed-plane orientations:

- **GRIP HAPPENS** — Tires with a positive attitude.
- **BRAKE-ish** — We'll stop eventually.
- **MUD CLUB** — Free dirt. Strict dress code.
- **APEX ACADEMY** — Cut tuition. Not corners.

Tire barriers use separate, symmetric black and white single-stack sprites.
Reusable groups place individual stacks at different world positions in rows,
corner clusters, elbows, and double rows. Each buffer uses **one long sprite**
on a fixed X- or Y-aligned plane. It keeps the same long-face texture and its
1.5-unit width from every viewpoint. `buffer-front.png` is the cropped long face
of the preserved buffer artwork; `Tools/build-buffer-atlas.ps1` can rebuild it.
Fences and grandstands also keep fixed world planes.

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
and fixed buffer definitions are preserved as source assets.
The builder also calls `Tools/build-raceway-event-props.mjs` for the shared event
definitions, billboard artwork, and starting signal. The generated flag/crowd
source sheets, prompts, and packed atlases are preserved in `textures/raceway`.
Run `pwsh -NoProfile -File Tools/build-festival-atlases.ps1` to repack their
three 512-square frames with a shared baseline at row 496.

The generator writes these review artifacts:

- `artifacts/pine-loop-overview.svg`: a labeled north-up route diagram.
- `artifacts/pine-loop-views.json`: viewpoints near the start, corners, and landmarks.
- `Tests/Fixtures/pine-loop-lap.json`: the complete lap, major corners, zone checkpoints,
  usable landmark approaches, and the same browser viewpoints.

`Tests/PineLoopChecks.cs` loads the map through the production compiler and traces
a complete lap with the existing collision circle. It checks the explicit
256 × 512 dimensions, route length, three-to-two lane transition, landmark
approaches, inline stands, tree species and sprite budget, tunnel roof, pit
entrances, separate tire groups, fixed buffer views, all referenced assets,
and expanded-map editor load/export. Navigation topology checks seal the shortcut
and split the lap into 18 isolated corner sections; opening the shortcut connects
exactly two of them. They also check that the rails cannot be jumped or crouched
through and that the animated event props retain their grounded transforms.

```powershell
dotnet run --project Tests/WolfNET.MapTests.csproj
```

With the game and a Chromium browser running with WebGPU and remote debugging,
check rendered landmarks and both buffer views using actual player positions:

```powershell
node Tests/pine-loop-browser-check.mjs 9255 http://127.0.0.1:53719 start grandstand-row buffer-front buffer-side
```

The `start` and `spectator-plaza` browser views verify all three animation layers
reach the GPU for every nearby flag and crowd without changing their geometry.
Add `mud-express` to inspect the shortcut from its entry.
