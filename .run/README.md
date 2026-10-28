# Rider run/debug configurations

Select any configuration in Rider and use **Run** or **Debug**. The same
configuration supports both actions and builds before launching.

| Configuration | Starts |
| --- | --- |
| `WolfNET.Examples: https` | Exploration/racing at `https://localhost:7279` |
| `WolfNET.Examples: http` | Exploration/racing at `http://localhost:5025` |
| `Race Imperialis: http` | Standalone Pine Loop racing at `http://localhost:5026` |
| `WolfNET.Examples: viewer` | Procedural scene viewer at `https://localhost:7279/viewer` |
| `WolfNET.MapTests` | Complete map/game regression runner |
| `WolfNET.EngineTests` | Portable engine regression runner from the Engine submodule |
| `WolfNET.EngineConsumer` | Independent engine public API checks |

Examples use launch profiles with Blazor WebAssembly's browser debugging proxy.
The test runners use .NET project configurations. Engine is a class library;
debug it through an example or the engine consumer.

The Examples profiles share ports, so stop one before starting another. Race Imperialis
uses its own port and can run alongside Examples. Both apps open directly into racing.
Configurations use project-relative paths and are stored here for version control.
