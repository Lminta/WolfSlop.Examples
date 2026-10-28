using System.Net;
using System.Numerics;
using System.Text.Json;
using WolfNET.Engine.World;
using WolfNET.Engine.Actors.Components;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
var webRoot = Path.Combine(root, "WolfSlop.Examples", "wwwroot");
using var http = new HttpClient(new FileHandler(webRoot)) { BaseAddress = new Uri("https://test.local/") };
if (args.Contains("--opponent-speeds"))
{
    await OpponentSpeedReport.RunAsync(http, root, args.Contains("--easy") ? RaceDifficulty.Easy : null);
    return;
}
int assertions = 0;
void Check(bool condition, string message)
{
    assertions++;
    if (!condition) throw new Exception(message);
}
// Load every shipped map, without pinning its authored scenery or dimensions.
foreach (var file in Directory.EnumerateFiles(Path.Combine(webRoot, "maps"), "*.json").Order())
{
    var name = Path.GetFileNameWithoutExtension(file);
    var loaded = await TileMap.LoadAsync(http, $"maps/{name}.json", 512, 512);
    Check(loaded.RendererMap.Cells.Count == loaded.RendererMap.Width * loaded.RendererMap.Height,
        $"{name}: render grid is incomplete");
    Check(loaded.RendererMap.Sprites.All(sprite => sprite.OwnerCell > 0 &&
        sprite.OwnerCell <= loaded.RendererMap.Cells.Count &&
        loaded.RendererMap.Textures.Layers.ContainsKey(sprite.TextureLayer)),
        $"{name}: sprite ownership or texture is invalid");
}

using (var malformedHttp = new HttpClient(new FileHandler(webRoot, path => path == "maps/level1.json" ? "{}" : null))
       { BaseAddress = http.BaseAddress })
{
    try
    {
        await TileMap.LoadAsync(malformedHttp, "maps/level1.json", 64, 64);
        throw new Exception("Malformed level accepted");
    }
    catch (JsonException) { assertions++; }
}

assertions += await SceneChecks.RunAsync(http);
assertions += await CarInteractionChecks.RunAsync(http);
assertions += await RaceSessionChecks.RunAsync(http, webRoot);
assertions += await OpponentChecks.RunAsync(http);
assertions += await OpponentDifficultyChecks.RunAsync(http);
assertions += await RacePropScaleChecks.RunAsync(http);
assertions += AudioChecks.Run(webRoot);
assertions += await SfxChecks.RunAsync(http, webRoot);
await SpriteSelectionFixtures.WriteAsync(http, webRoot);
Console.WriteLine($"PASS: {assertions} assertions (game maps, scenes, racing, opponents, presentation, and audio configuration).");

sealed class FileHandler(string root, Func<string, string?>? replacement = null) : HttpMessageHandler
{
    public List<string> Requests { get; } = [];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var path = request.RequestUri!.AbsolutePath.TrimStart('/');
        Requests.Add(path);
        var value = replacement?.Invoke(path);
        var file = Path.Combine(root, path);
        return Task.FromResult(value != null || File.Exists(file)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(value ?? File.ReadAllText(file), System.Text.Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
