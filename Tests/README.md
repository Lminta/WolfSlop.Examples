# Regression checks

Run from the repository root with the .NET 10 SDK and Node 22 or newer:

```powershell
dotnet build WolfNET.slnx
dotnet run --project Tests/WolfNET.MapTests.csproj
node --test Tests/editor-model.test.mjs Tests/sprite-sort.test.mjs
```

The C# runner loads every shipped map and checks map edits, validation, walking,
jumping, crouching, input, frame timing, scene ownership, doors, car interaction,
car physics and cameras, ordered checkpoint/lap timing, pause behavior,
sprite animation, and editor persistence/export.
The opponent checks simulate both rivals through three full checkpoint-validated laps on Pine Loop,
check individual top speeds and throttle/braking, blue mistake events and clean green laps, moving overtakes and defensive lane holding on both sides,
standings, grid/pause/finish behavior, car contact and solid boundaries. They measure full-footprint
asphalt coverage during solo and traffic laps, check recovery from mud and a wall-facing pose, and compare
lap-finish gaps that grow, freeze at matching finishes, and reset on the next lap.
The Node checks cover editor operations and browser sprite selection/order.
Run them after C#: `SpriteSelectionFixtures.cs` generates independent native
references in `artifacts/performance` for the browser implementation to compare.

Keep regression cases around behavior that could break gameplay or persisted data.
Exact scenery coordinates, asset counts, exhaustive authored-map cell assertions,
synthetic GPU pixel fixtures, and separate browser probes for individual features
have been removed. Visual changes to maps and shaders need manual review.

## Optional integration checks

The editor API check starts its own temporary server and tests HTTP persistence
against disposable assets:

```powershell
node Tests/editor-api-check.mjs
```

For browser checks, start the relevant app and a Chromium browser with a remote
debugging port, then pass that port and the app URL:

```powershell
node Tests/browser-check.mjs 9243 http://127.0.0.1:5194
node Tests/car-driving-browser-check.mjs 9243 http://127.0.0.1:5194
node Tests/editor-browser-check.mjs 9246 http://127.0.0.1:5080
```

The game smoke check covers startup, movement, input capture, shader-rendered hands
and their F gesture, map switching, texture cleanup, and failed-load recovery.
The driving check covers real input, shader cockpit/steering and chase views,
countdown control locking, start lights, configured lap limits and results,
restart, pause/resume, and returning to walking. Its short-race fixture changes
invisible gates and the configured lap limit to exercise finishing on the real starting straight. The editor browser
check covers its UI; disk saves are covered by the native and API checks.

`race-performance-browser-check.mjs` and `compare-race-screenshots.mjs` remain
available for performance or rendering changes. See the [root README](../README.md)
for benchmark and screenshot comparison commands. Run timing benchmarks separately
from other checks so concurrent work does not distort the results.
