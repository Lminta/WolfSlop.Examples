# WolfSlop.Examples

Playable examples for [WolfSlop](https://github.com/Lminta/WolfSlop), a .NET raycasting engine with a Blazor WebAssembly host and WebGPU rendering. Explore three walking maps or take a car onto an outdoor racing circuit.

## Maps

All screenshots below were captured from the running application. The page's navigation and help panels are hidden; the in-game HUD is shown.

### Showcase

![Showcase: blue stone and timber flooring, textured pillars, and the first-person exploration HUD](docs/screenshots/showcase.png)

The default map is a hands-on gallery of engine features: wall and floor materials, glass and steel doors, sprite groups, collision rules, jump and crouch obstacles, and an open-sky annex with a directional car sprite.

**Open:** `/?map=showcase` · [Map guide](WolfSlop.Examples/wwwroot/maps/showcase.md)

### The Glassworks

![The Glassworks: a central fern beneath the glasshouse roof, surrounded by botanical mosaic paths](docs/screenshots/glassworks.png)

An abandoned botanical station built around a glasshouse. Limestone walls, teal rooms, potted ferns, workbenches, and pumping equipment give its connected wings distinct character. Explore the potting room, herbarium, and cistern, then find the maintenance shortcuts.

**Open:** `/?map=glassworks` · [Map guide](WolfSlop.Examples/wwwroot/maps/glassworks.md)

### The Brickyard

![The Brickyard: the motor depot courtyard, utility hut, and covered stone arcade](docs/screenshots/level1.png)

An old motor depot with brick loading bays, covered arcades, barrel stores, and a courtyard utility hut. Follow the main route through the warehouse or take the service passage toward the parked car in the motor yard.

**Open:** `/?map=level1` · [Map guide](WolfSlop.Examples/wwwroot/maps/level1.md)

### Pine Loop

![Pine Loop: the starting straight from the cockpit, with grandstands, trackside fencing, and the racing HUD](docs/screenshots/pine-loop.png)

A 256 × 512 outdoor circuit through forest lanes, broad passing sections, a long straight, and a tunnel. Race against two AI opponents with lap timing, a local route minimap, cockpit and chase cameras, and Normal or Easy difficulty. Grandstands, pit boxes, animated spectators, and trackside props line the course.

**Open:** `/?map=pine-loop` · [Circuit guide](WolfSlop.Examples/wwwroot/maps/pine-loop.md)

You can also enter Pine Loop by facing a parked car in any walking map and pressing **F**.

## Run locally

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

## Controls

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

## Other examples

| Route | Example |
| --- | --- |
| `/viewer` | A procedural scene with twelve moving sprites and an orbiting camera, without a player or race rules. |
| `/ui` | An interactive gallery of text, images, buttons, sliders, text editing, scrolling, and modal dialogs. |

## Project and documentation

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

[Race Imperialis](https://github.com/Lminta/RaceImperialis) is a separate game built on the same engine, with its own repository and solution.
