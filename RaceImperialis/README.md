# Race Imperialis

A standalone Blazor WebAssembly racing game copied from the Pine Loop example.
Opening `/` loads the racing grid directly, with no exploration map or car interaction
required. Click the game to capture the mouse and begin the countdown.

```powershell
dotnet run --project RaceImperialis/RaceImperialis.csproj
```

Open `https://localhost:7280`, or select **Race Imperialis: https** in Rider.

The game includes both AI opponents, Normal/Easy difficulty, three-lap races,
ordered checkpoints, standings and results, restart, cockpit/chase cameras,
the rotating minimap, music, and driving sound effects. Escape pauses driving;
click the canvas to resume. W accelerates, A/D or arrows steer, S brakes/reverses,
Space brakes, and V switches cameras.

`Game/` and `Audio/` contain independent copies of the example gameplay in the
`RaceImperialis` namespace. `wwwroot/` owns Pine Loop, its referenced objects,
textures, music, sounds, and settings. This project references only the shared
`WolfNET.Engine` and `WolfNET.Engine.Browser` projects; it does not depend on
`WolfNET.Examples` or its assets. Edit the JSON settings in this project's
`wwwroot/` to tune racing independently.

Both shared engine projects live in the `Engine/` Git submodule. After cloning,
follow the [submodule setup instructions](../README.md) before building.

The repository's C# regression runner includes standalone racing integration
checks. To run the browser driving checks against this project, start it and a
Chromium browser with remote debugging, then run:

```powershell
node Tests/car-driving-browser-check.mjs 9243 https://localhost:7280 --standalone
```
