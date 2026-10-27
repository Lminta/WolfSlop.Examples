# Rider run/debug configurations

Select any configuration in Rider and use **Run** or **Debug**. The same
configuration supports both actions and builds before launching.

| Configuration | Starts |
| --- | --- |
| `WolfNET.Examples: https` | Exploration/racing at `https://localhost:7279` |
| `WolfNET.Examples: http` | Exploration/racing at `http://localhost:5025` |
| `WolfNET.Examples: viewer` | Procedural scene viewer at `https://localhost:7279/viewer` |
| `WolfNET.MapTests` | Complete map/game regression runner |
| `WolfNET.EngineConsumer` | Independent engine public API checks |

Examples use launch profiles with Blazor WebAssembly's browser debugging proxy.
The two test runners use .NET project configurations. Engine is a class library;
debug it through an example or the engine consumer.

Web examples share ports, so stop one before starting another. Configurations use
project-relative paths and are stored here for version control.
