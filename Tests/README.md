# Game regression checks

The portable engine, browser provider, and their unit/public API tests live in
[the Engine submodule](../Engine/README.md). Initialize it before building:

```powershell
git submodule update --init --recursive
dotnet build WolfSlop.Examples.slnx
dotnet run --project Engine/Tests/EngineTests/WolfNET.EngineTests.csproj
dotnet run --project Engine/Tests/EngineConsumer/WolfNET.EngineConsumer.csproj
dotnet run --project Tests/WolfSlop.MapTests.csproj
node --test Engine/Tests/sprite-sort.test.mjs Engine/Tests/texture-upload.test.mjs Engine/Tests/sfx-mixer.test.mjs Engine/Tests/ui.test.mjs
node --test Tests/sprite-sort.test.mjs
```

Run from this repository's root with .NET 10 and Node 22 or newer. The engine
repository can also build and run all its tests independently, with procedural
fixtures and no game assets. Run both C# suites before the Node sprite checks:
each suite generates the reference data used by its own JavaScript checks.

`WolfSlop.MapTests` checks the shipped maps, application scene assembly and
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
dotnet run --project Tests/WolfSlop.MapTests.csproj -- --opponent-speeds
dotnet run --project Tests/WolfSlop.MapTests.csproj -- --opponent-speeds --easy
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
available for performance or rendering changes. See the performance section below for benchmark and screenshot comparison commands. Run timing benchmarks separately
from other checks so concurrent work does not distort the results.

## Performance and rendering comparisons

To measure Pine Loop at the configured resolution and target FPS, start the
development server and Edge with hardware WebGPU and a remote-debugging port:

```powershell
node Tests/race-performance-browser-check.mjs 9252 http://127.0.0.1:5195 --output=artifacts/performance/after.json --min-fps=58
```

The check warms each authored viewpoint, samples five seconds, and records FPS,
submission and animation-callback intervals, CPU callback duration, sprite counts,
upload bytes, and adapter identification. `--views=start,tires`, `--seconds=10`,
and `--warmup=3` customize sampling. `--trace` saves a browser trace for diagnosing
CPU/GPU work; run timing acceptance without tracing or concurrent browser checks.
Add `--no-sfx` for a comparison with sound effects disabled and music unchanged;
save that run separately from the normal audio-enabled measurements.
The optional threshold requires every view to reach the requested FPS and a
95th-percentile submission interval of at most 20 ms. Headless results should also
be checked in the normal browser when assessing presentation smoothness.
Use `--views=start --drive` to benchmark acceleration along the starting straight;
add `--chase` for the third-person camera. Run each mode separately.

The numeric selection and sort also have a native reference check. Run the C#
suite first to generate its oracle fixtures, then run `node --test Tests/sprite-sort.test.mjs`.

Open the game with `?map=pine-loop&profile=1` for once-per-second `[PROFILE]`
console samples of candidate counts and CPU selection, upload, and submission
time. Submission timing does not wait for GPU completion. Normal play leaves this
instrumentation disabled. For visual comparisons, run both benchmark versions
with `--screenshots`; this freezes decorative animation at its first frame, then
compare the outputs with:

```powershell
node Tests/compare-race-screenshots.mjs artifacts/performance/before.json artifacts/performance/after.json
```

The image check excludes the FPS counter and allows a three-level RGB tolerance.
