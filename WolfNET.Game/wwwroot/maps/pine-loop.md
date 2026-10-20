# Pine Loop

`pine-loop.json` is a **40 × 35** outdoor club racing circuit. Open
`/?map=pine-loop` or choose **Pine Loop** in the existing map selector.
The player starts at **(7.5,23.5)** facing north, immediately behind the
checkerboard line. The existing movement and camera controls apply.

Follow the starting straight north, turn east onto the northern straight,
pass through the stone tunnel, then follow the stepped east-side bends south.
The long southern straight returns west through the final bend to the grid.
Grass and exposed mud border the asphalt; low tire stacks, striped buffers,
fixed safety fences, pines, and bushes frame the course.

```text
                              NORTH (-Y)

              /========[ TUNNEL ]=======\
             /                           \
            ||                            ||
            ||       INFIELD PINES        ||
            ||                            \ \
            ||    pit apron    BOX 01       \ \
    STAND   |||       |        BOX 02        ||
            |||       |        BOX 03        ||
          START       |                     //
            |||       |                    //
             \ \      /                   //
              \==========================/

    Schematic only; the JSON grid uses worldMap[x][y].
```

The starting zone occupies **X 6–8, Y 16–24**, three full tiles across.
The third lane tapers diagonally across **(8,15)**, leaving the northern
approach at **X 6–7**, two tiles wide. The checkerboard crosses all three
starting lanes at **Y 22**; starting boxes are painted at Y 18, 20, and 24.
Other straights have two asphalt lanes, with extra space where the tiled
corners join. Mud shoulders sit beside the racing surface.

The tunnel covers **X 17–23, Y 6–7** and keeps both lanes clear. It uses
the existing stone material on its ceiling and side walls, with striped
portal piers. All other racing cells use the existing sky ceiling mode and
the shared sky panorama.

The service apron runs at **X 10–12, Y 14–26**, reached from the southern
end of the starting straight. The pit building occupies **X 13–17,
Y 15–26**. Its three open entrances are at **(13,17), (13,21), (13,25)**.
The numbered fronts, roofed interiors, and static car displays simulate
pit boxes. The grandstand sits west of the starting straight at
**(3.5,20.5)**, behind the safety fence.

Map definitions are isolated in [`../objects/raceway`](../objects/raceway/),
using IDs **500–550**. Floors are open-sky passages except for the tunnel
and pit interiors. Scenery uses existing sprite groups for scale and
elevation. Fence sections, striped buffers, and the grandstand use fixed
world planes, so they keep their orientation as the camera moves. The
outer grass-covered wall is a distant hedge backdrop behind the trees and
safety fences; it supplies the closed boundary required by the renderer.

The generated asphalt, grass, mud, and transparent scenery PNGs are in
[`../textures/raceway`](../textures/raceway/). Their generation prompts are
preserved beside the assets. The road paint, diagonal taper, pit-front
numbers, and portal stripes are editable SVG assets. Road-paint SVGs embed
the generated seamless material so they load independently and match the
unpainted floor. The map reuses the existing sky, stone, limestone, teal
wall, plaster ceiling, and eight-view car atlas.

This addition contains map data, object presets, textures, and its selector
entry. Racing mechanics, lap timing, vehicle movement, and renderer behavior
are unchanged.

`Tests/PineLoopChecks.cs` loads the map through the production compiler and
traces a complete lap using the existing collision circle. It also checks
the three-to-two lane transition, tunnel roof, pit entrances, fixed scenery
planes, and referenced assets. Run it with the existing regression suite:

```powershell
dotnet run --project Tests/WolfNET.MapTests.csproj
```
