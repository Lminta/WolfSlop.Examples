# Engine structure

`Program.cs` composes the browser host. `Wolf` coordinates map switches and browser
animation callbacks; `FrameTiming` controls simulation deadlines without blocking
the browser. All simulation update durations are in milliseconds. Movement settings
remain in world units and radians per second.

`GameSceneLoader` loads map definitions, their referenced objects, and character
appearance. `MapCompiler` validates definitions and produces render and navigation
data. `GameMap` publishes revisions after successful compilation and applies door
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
rules remain in `PlayerController`.

`GameScene.CaptureFrame` returns a `RenderFrame` consumed by `ISceneRenderer`.
`WebGpuRenderer` implements that contract. Its partial files separate device and
pipeline initialization, GPU buffers, scene bindings, and frame submission.
`GpuSceneTextures` owns textures, samplers, sky settings, and mip uploads, including
cleanup when preparation fails or is canceled. Scene preparation and map switches
are serialized by `Wolf`; the renderer expects one scene preparation at a time.

`Pages/Home.razor` contains the UI, with browser startup and map selection in
`Home.razor.cs`. Shader requests run together and use the page's cancellation token.

Run the regression suite from the repository root:

```powershell
dotnet build WolfNET.slnx
dotnet run --project Tests/WolfNET.MapTests.csproj
node --test Tests/editor-model.test.mjs
```

The C# suite compiles the production simulation, object, component, camera, input
state, and scene code. Only device input is substituted in movement fixtures.
`Tests/EngineChecks.cs` covers input transitions, timing, ownership, scene assembly,
cancellation, and door state after edits. Browser and GPU checks are described in
the repository's root README.

With the game and a WebGPU-capable Chromium instance running, the focused lifecycle
check also verifies recovery from failed GPU preparation and cancellation during
texture upload:

```powershell
node Tests/scene-lifecycle-browser-check.mjs 9254 http://127.0.0.1:5195
```
