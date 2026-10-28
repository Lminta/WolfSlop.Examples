<p align="center"><strong>This project is entirely AI-generated.</strong></p>

<h1 align="center">WolfSlop.Examples</h1>

<p align="center">
  Three worlds to explore. One circuit to race.<br>
  Playable examples for the <a href="https://github.com/Lminta/WolfSlop">WolfSlop</a> raycasting engine.
</p>

<p align="center">
  <strong>.NET 10</strong> &nbsp; · &nbsp; <strong>Blazor WebAssembly</strong> &nbsp; · &nbsp; <strong>WebGPU</strong>
</p>

<p align="center">
  <a href="#maps">Maps</a> &nbsp; / &nbsp;
  <a href="#run-locally">Quick start</a> &nbsp; / &nbsp;
  <a href="#controls">Controls</a> &nbsp; / &nbsp;
  <a href="#other-examples">Other examples</a> &nbsp; / &nbsp;
  <a href="#project-and-documentation">Documentation</a>
</p>

---

<h2 align="center">Maps</h2>

<p align="center"><sub>Captured in the running application, with the in-game HUD and without the page's help panels.</sub></p>

<h3 align="center">Showcase</h3>

<p align="center">
  <a href="docs/screenshots/showcase.png"><img src="docs/screenshots/showcase.png" width="960" alt="Showcase: blue stone and timber flooring, textured pillars, and the first-person exploration HUD"></a>
</p>

<p align="center">
  A hands-on gallery of materials, doors, sprites, and movement.<br>
  Test collision rules, jump and crouch obstacles, or explore the open-sky car annex.
</p>

<p align="center"><code>/?map=showcase</code> &nbsp; · &nbsp; <a href="WolfSlop.Examples/wwwroot/maps/showcase.md">Map guide</a> &nbsp; · &nbsp; <strong>Default map</strong></p>

<h3 align="center">The Glassworks</h3>

<p align="center">
  <a href="docs/screenshots/glassworks.png"><img src="docs/screenshots/glassworks.png" width="960" alt="The Glassworks: a central fern beneath the glasshouse roof, surrounded by botanical mosaic paths"></a>
</p>

<p align="center">
  An abandoned botanical station beneath a broken glass roof.<br>
  Follow mosaic paths through the potting room, herbarium, and cistern, then find the maintenance shortcuts.
</p>

<p align="center"><code>/?map=glassworks</code> &nbsp; · &nbsp; <a href="WolfSlop.Examples/wwwroot/maps/glassworks.md">Map guide</a></p>

<h3 align="center">The Brickyard</h3>

<p align="center">
  <a href="docs/screenshots/level1.png"><img src="docs/screenshots/level1.png" width="960" alt="The Brickyard: the motor depot courtyard, utility hut, and covered stone arcade"></a>
</p>

<p align="center">
  An old motor depot of brick loading bays, stone arcades, and barrel stores.<br>
  Cross the courtyard or take the service passage toward the parked car in the motor yard.
</p>

<p align="center"><code>/?map=level1</code> &nbsp; · &nbsp; <a href="WolfSlop.Examples/wwwroot/maps/level1.md">Map guide</a></p>

<h3 align="center">Pine Loop</h3>

<p align="center">
  <a href="docs/screenshots/pine-loop.png"><img src="docs/screenshots/pine-loop.png" width="960" alt="Pine Loop: the starting straight from the cockpit, with grandstands, trackside fencing, and the racing HUD"></a>
</p>

<p align="center">
  A 256 × 512 outdoor circuit through forest lanes, fast straights, and a tunnel.<br>
  Race two AI opponents with lap timing, a route minimap, cockpit and chase cameras, and two difficulty levels.
</p>

<p align="center"><code>/?map=pine-loop</code> &nbsp; · &nbsp; <a href="WolfSlop.Examples/wwwroot/maps/pine-loop.md">Circuit guide</a></p>

<p align="center"><sub>Find a parked car in any walking map, face it, and press <strong>F</strong> to enter Pine Loop.</sub></p>

---

<h2 align="center">Run locally</h2>

You need the **.NET 10 SDK** and a browser/device with **WebGPU support** and hardware acceleration enabled.

```powershell
git clone --recurse-submodules https://github.com/Lminta/WolfSlop.Examples.git
cd WolfSlop.Examples
dotnet run --project WolfSlop.Examples/WolfSlop.Examples.csproj --launch-profile https
```

Open **https://localhost:7279**. If the local HTTPS certificate is not trusted, run `dotnet dev-certs https --trust` once. For an existing checkout, initialize the engine with `git submodule update --init --recursive` before building.

Open **WolfSlop.Examples.slnx** in your IDE to work with the examples and engine together. To build from the command line:

```powershell
dotnet build WolfSlop.Examples.slnx
```

Choose a walking map using the on-screen selector, or append one of the map URLs above to the local address. WebGPU needs a secure context; see [local development](docs/local-development.md) for HTTP localhost and LAN setup.

<h2 align="center">Controls</h2>

Click the game to capture the mouse. **Esc** releases it; driving pauses until you click the game again.

| Action | Walking | Driving |
| --- | --- | --- |
| Move | W/S forward and back, A/D strafe | W accelerate, S brake/reverse |
| Turn | Mouse look | A/D or arrow keys steer |
| Space | Jump | Brake |
| Alt | Crouch | — |
| F | Open/close a nearby door or enter a car | — |
| V | First-person / third-person camera | Cockpit / chase camera |
| Mouse in third person | Look around | Orbit the car |

Use the Pine Loop side panel to change difficulty or restart the race.

<h2 align="center">Other examples</h2>

| Route | Example |
| --- | --- |
| `/viewer` | A procedural scene with twelve moving sprites and an orbiting camera, without a player or race rules. |
| `/ui` | An interactive gallery of text, images, buttons, sliders, text editing, scrolling, and modal dialogs. |

<h2 align="center">Project and documentation</h2>

| Path | Contents |
| --- | --- |
| [WolfSlop.Examples/](WolfSlop.Examples/) | Blazor app, walking and racing rules, HUD, maps, and assets. |
| [Engine/](Engine/) | The independent WolfSlop engine, included as a Git submodule. |
| [Tests/](Tests/) | Map, simulation, browser, and performance checks. |
| [Tools/](Tools/) | Map/asset generation and screenshot capture tools. |

- [Application architecture](WolfSlop.Examples/README.md)
- [Engine API and extension guide](Engine/WolfSlop.Engine/README.md)
- [Retained UI guide](Engine/WolfSlop.Engine/UI/README.md)
- [Tests and performance tools](Tests/README.md)
- [Local development and screenshot capture](docs/local-development.md)

---

<p align="center">
  Built on <a href="https://github.com/Lminta/WolfSlop">WolfSlop</a>.<br>
  Also see <a href="https://github.com/Lminta/RaceImperialis">Race Imperialis</a>, a separate game built on the same engine.
</p>
