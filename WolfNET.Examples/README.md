# WolfNET examples

This Blazor WebAssembly application references `WolfNET.Engine`. Run it with:

```powershell
dotnet run --project WolfNET.Examples/WolfNET.Examples.csproj
```

`/` runs the exploration and racing examples, retaining the existing maps,
movement, doors, cameras, artwork, audio, opponents, and race UI. `/viewer` runs a
procedural scene with an orbiting camera and twelve moving sprites, using only
engine scene systems and rendering. The viewer has no actor spawn or game rules.
`/ui` demonstrates text, images, vector drawing, buttons, toggles, sliders, native
text editing, scrolling, modal focus, and responsive layout without a game scene.

The engine's public API and ownership contracts are documented in
[WolfNET.Engine/README.md](../WolfNET.Engine/README.md).

## Application composition

`Program.cs` composes the browser host. `Wolf` coordinates map switches and uses
the engine's `BrowserFrameLoop`; `FrameTiming` controls deadlines without blocking
the browser. All simulation update durations are in milliseconds. Movement settings
remain in world units and radians per second.

`GameSceneLoader` loads map definitions, their referenced objects, and character
appearance. `MapCompiler` validates definitions and produces render and navigation
data. `TileMap` publishes revisions after successful compilation and applies door
state to each new snapshot.

`GameScene` owns the objects for one loaded map and connects movement, animation,
interaction, and doors. Objects own their components. Both ownership levels dispose
their contents once; objects and components have stable, read-only IDs. Objects do
not depend on the browser host or `Wolf`.

`IKeyboardInput` and `IMouseInput` are the simulation's input contracts.
`KeyboardState` handles held keys and consumable actions. `KeyTracker` and
`MouseTracker` adapt browser events; `BrowserInput` connects capture loss and Escape
and owns their subscriptions. A map switch disposes these subscriptions and cancels
pending scene preparation before starting another scene.

`CircleCollider` handles movement sweeps, contact resolution, and occupancy queries
against `INavigationMap`. `CircleGeometry` supplies the circle/cell overlap test used
by movement, door obstruction, and the third-person camera. Jump planning and stance
rules remain in `WalkingController`.

`GameScene.CaptureFrame` returns an `ExampleRenderFrame`, which converts the
example's characters and rivals into the engine's general `RenderFrame` and
exposes animation state for presentation. `ExampleHud` owns application-specific
screen content: hands, cockpit/wheel, vector minimap, timing, speed, pause,
countdown, results and restart. These all use the engine's retained UI controls
and `BrowserUiHost`; there is no separate Razor or SVG HUD implementation.
The engine frame is consumed by `ISceneRenderer`.
`WebGpuRenderer` implements that contract. Its partial files separate device and
pipeline initialization, GPU buffers, scene bindings, and frame submission.
`GpuSceneTextures` owns textures, samplers, sky settings, and mip uploads, including
cleanup when preparation fails or is canceled. Scene preparation and map switches
are serialized by `Wolf`; the renderer expects one scene preparation at a time.

`Pages/Home.razor` contains the application shell and map selection, with browser
startup in `Home.razor.cs`. `Wolf` owns the UI host and resets its document on map
switches. Shader requests run together and use the page's cancellation token.
See the [UI guide](../WolfNET.Engine/UI/README.md) for layout, input and ownership.

Run the regression suite from the repository root:

```powershell
dotnet build WolfNET.slnx
dotnet run --project Tests/WolfNET.MapTests.csproj
dotnet run --project Tests/EngineConsumer/WolfNET.EngineConsumer.csproj
node --test Tests/sprite-sort.test.mjs Tests/texture-upload.test.mjs Tests/sfx-mixer.test.mjs Tests/ui.test.mjs
```

The C# suite references the built Engine and Examples assemblies. Only device
input is substituted in movement fixtures. The independent engine consumer in
`Tests/EngineConsumer` references Engine alone.
`Tests/EngineChecks.cs` covers input transitions, timing, ownership, scene assembly,
cancellation, and door state after edits. Run the C# suite before the Node checks
so the sprite ordering reference files are available. The retained browser smoke
checks and performance tools are described in [Tests/README.md](../Tests/README.md).
