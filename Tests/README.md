# Regression checks

Run from the repository root with the .NET 10 SDK and Node 22 or newer:

```powershell
dotnet build WolfNET.slnx
dotnet run --project Tests/WolfNET.MapTests.csproj
node --test Tests/sprite-sort.test.mjs Tests/texture-upload.test.mjs Tests/sfx-mixer.test.mjs
```

The C# runner loads every shipped map and checks map edits, validation, walking,
jumping, crouching, input, frame timing, scene ownership, doors, car interaction,
car physics and cameras, ordered checkpoint/lap timing, pause behavior,
sprite animation, parked/driven car dimensions, and music/SFX lifecycle and mixing.
Car contact checks cover rubbing during blocked overtakes, clean passes and steering
away, momentum transfer, finished cars, update-rate independence, and three cars beside a barrier.
The opponent checks simulate both rivals through three full checkpoint-validated laps on Pine Loop,
check individual top speeds and throttle/braking, blue mistake events and clean green laps, launches against an accelerating player, player-priority blocking and chase recovery, moving overtakes and defensive lane holding on both sides,
standings, grid/pause/finish behavior, car contact and solid boundaries. They measure full-footprint
asphalt coverage and a time-weighted mean pace near 19 blocks/s during traffic laps (including launch),
check recovery from mud and a wall-facing pose, and compare
lap-finish gaps that grow, freeze at matching finishes, and reset on the next lap.
Difficulty checks load the shipped JSON, validate and round-trip the profile structs,
check overrides and speed offsets, and simulate easy solo laps and a three-lap traffic race.
They verify the 16-17 blocks/s target, more blue mistakes, clean green driving, and asphalt coverage.
The Node checks cover browser sprite selection/order and
texture upload filtering, mip coverage, cancellation, and bitmap cleanup.
The SFX mixer checks cover shared timestamps, smoothing, unchanged parameters,
voice removal/reuse, and isolation between scenes.
Run them after C#: `SpriteSelectionFixtures.cs` generates independent native
references in `artifacts/performance` for the browser implementation to compare.

Keep regression cases around behavior that could break gameplay or map loading.
Avoid tests that pin decorative prop sizes, exact CSS values, or equality between
editable JSON settings and code defaults. Full-race simulations cover normal
and easy traffic; the difficulty checks also compare solo mistake profiles.
Use manual review for artwork, shader appearance, and actual browser audio playback.

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

The game smoke check covers startup, movement, input capture, shader-rendered hands
and their F gesture, map switching, texture cleanup, and failed-load recovery.
The driving check covers real input, shader cockpit/steering and chase views,
countdown control locking, start lights, difficulty switching beneath the map description, configured lap limits and results,
mid-race and results-screen restarts with the selected difficulty, pause/resume, and returning to walking. Its short-race fixture changes
invisible gates and the configured lap limit to exercise finishing on the real starting straight.

`race-performance-browser-check.mjs` and `compare-race-screenshots.mjs` remain
available for performance or rendering changes. See the [root README](../README.md)
for benchmark and screenshot comparison commands. Run timing benchmarks separately
from other checks so concurrent work does not distort the results.
