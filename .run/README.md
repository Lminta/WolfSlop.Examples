# Rider run/debug configurations

Select any configuration in Rider and use **Run** or **Debug**. The same
configuration supports both actions and builds before launching.

| Configuration | Starts |
| --- | --- |
| `WolfNET.Examples: https` | Showcase at `https://localhost:7279` |
| `WolfNET.Examples: viewer` | Procedural scene viewer at `https://localhost:7279/viewer` |
| `WolfNET.MapTests` | Complete map/game regression runner |
| `WolfSlop: tests` | Portable engine regression runner from the Engine submodule |
| `WolfSlop: API checks` | Independent engine public API checks |

Examples use launch profiles with Blazor WebAssembly's browser debugging proxy.
The test runners use .NET project configurations. Engine is a class library;
debug it through an example or the engine consumer.

The Examples profiles share ports, so stop one before starting another. Examples
starts on Showcase. All web launch profiles use HTTPS only.
Race Imperialis's solution and launch configurations live in
[its own repository](https://github.com/Lminta/RaceImperialis).
Configurations use project-relative paths and are stored here for version control.

Open `WolfNET.slnx` in Rider for the games and all test runners. The engine can
also be opened independently using `Engine/WolfSlop.slnx`. Use Rider's
**Build Solution** or **Rebuild Solution** for a solution-wide build; each run/debug
configuration also builds its target project before launching. Browser selection
uses Rider's local settings, so shared configurations contain no machine-specific
browser IDs or executable paths.
