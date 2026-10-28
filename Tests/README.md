# Game regression checks

The portable engine, browser provider, and their unit/public API tests live in
[the Engine submodule](../Engine/README.md). Initialize it before building:

```powershell
git submodule update --init --recursive
dotnet build WolfNET.slnx
dotnet run --project Engine/Tests/EngineTests/WolfNET.EngineTests.csproj
dotnet run --project Engine/Tests/EngineConsumer/WolfNET.EngineConsumer.csproj
dotnet run --project Tests/WolfNET.MapTests.csproj
node --test Engine/Tests/sprite-sort.test.mjs Engine/Tests/texture-upload.test.mjs Engine/Tests/sfx-mixer.test.mjs Engine/Tests/ui.test.mjs
node --test Tests/sprite-sort.test.mjs
```

Run from this repository's root with .NET 10 and Node 22 or newer. The engine
repository can also build and run all its tests independently, with procedural
fixtures and no game assets. Run both C# suites before the Node sprite checks:
each suite generates the reference data used by its own JavaScript checks.

`WolfNET.MapTests` checks the shipped maps, application scene assembly and
cancellation, character presentation, car interaction, checkpoint/lap timing,
racing pause/restart, opponents/difficulty, race props, game sound configuration,
and SFX policy. Race Imperialis's integration and browser checks live in
[its own repository](https://github.com/Lminta/RaceImperialis).
Pine Loop's 25 authored sprite viewpoints
remain a game integration check in `Tests/sprite-sort.test.mjs`, using the engine
repository's independent native selection oracle.

Browser smoke checks use the game application as a host. Run
`node Tests/framework-browser-check.mjs 9244 http://127.0.0.1:5194` to check the
procedural viewer, provider asset hosting, navigation, and cleanup.
`node Tests/ui-browser-check.mjs 9254 http://127.0.0.1:5196` exercises the UI gallery
with real pointer/keyboard input, native text editing, modal isolation, scrolling,
DPI/resize, walking/racing HUDs, and cleanup. `Tests/ui-browser-helpers.mjs` observes
the retained engine UI display list.

Opponent simulations cover three-lap traffic and solo runs on Pine Loop,
checkpoint progress, standings, start/pause/finish behavior, speed profiles,
launches, blocking, overtaking, recovery, mistakes, and asphalt coverage.
Keep regression cases around behavior that could break gameplay or map loading.
Use manual review for artwork, shader appearance, and actual audio playback.

## Opponent speed measurements

```powershell
dotnet run --project Tests/WolfNET.MapTests.csproj -- --opponent-speeds
dotnet run --project Tests/WolfNET.MapTests.csproj -- --opponent-speeds --easy
```

Loads the shipped gameplay settings and runs three-lap traffic and solo simulations
on Pine Loop at 120 Hz. Traffic includes both rivals and a parked player. Reports
time-weighted mean, median, maximum, percentiles, low-speed and mud-contact time, plus the active speed limits and acceleration
in blocks/second to `artifacts/opponent-speeds.json`. Countdown and post-finish time
are excluded. `--easy` measures the easy profiles and writes `artifacts/opponent-speeds-easy.json`.
Both reports include the effective profiles and mistake counts. This diagnostic does not change driving behavior.

## Optional integration checks

For browser checks, start the game and a Chromium browser with a remote
debugging port, then pass that port and the app URL:

```powershell
node Tests/browser-check.mjs 9243 http://127.0.0.1:5194
node Tests/car-driving-browser-check.mjs 9243 http://127.0.0.1:5194
```

For headless Edge, include `--enable-unsafe-webgpu` and
`--autoplay-policy=no-user-gesture-required` when launching the browser.

The game smoke check covers startup, movement, input capture, UI-rendered hands
and their F gesture, map switching, texture cleanup, and failed-load recovery.
The driving check covers real input, UI cockpit/steering and chase views,
countdown control locking, start lights, difficulty switching beneath the map description, configured lap limits and results,
mid-race and results-screen restarts with the selected difficulty, pause/resume, and returning to walking. Its short-race fixture changes
invisible gates and the configured lap limit to exercise finishing on the real starting straight.

`race-performance-browser-check.mjs` and `compare-race-screenshots.mjs` remain
available for performance or rendering changes. See the [root README](../README.md)
for benchmark and screenshot comparison commands. Run timing benchmarks separately
from other checks so concurrent work does not distort the results.
