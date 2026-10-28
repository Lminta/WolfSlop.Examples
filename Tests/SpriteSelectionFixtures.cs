using System.Numerics;
using System.Text.Json;
using WolfNET.Engine.World;
using WolfNET.Engine.Graphics;

internal static class SpriteSelectionFixtures
{
    public static async Task WriteAsync(HttpClient http, string webRoot)
    {
        var selector = new ReferenceSpriteSelection();
        void Select(IReadOnlyList<Sprite> sprites, Vector2 position, Vector2 direction, bool cull = true) =>
            selector.Select(sprites, position, direction, new Vector2(0, -1), 60, cull, 0);
        var artifactDirectory = Path.GetFullPath(Path.Combine(webRoot, "../../artifacts/performance"));
        Directory.CreateDirectory(artifactDirectory);
        var map = await TileMap.LoadAsync(http, "maps/pine-loop.json", 512, 512);
        var sourcePositions = map.RendererMap.Sprites.SelectMany(sprite => new[] { sprite.Position.X, sprite.Position.Y,
            sprite.Scale.X, sprite.MaximumViewWidth }).ToArray();
        var spatialCases = new List<object>();
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(webRoot, "../../Tests/Fixtures/pine-loop-lap.json")));
        foreach (var view in fixture.RootElement.GetProperty("views").EnumerateArray())
        {
            var values = view[1].EnumerateArray().Select(value => value.GetSingle()).ToArray();
            var position = new Vector2(values[0], values[1]);
            var direction = Vector2.Normalize(new(values[2], values[3]));
            Select(map.RendererMap.Sprites, position: position, direction: direction);
            var visibleOrder = selector.Visible.Select(item => item.Order).ToArray();
            var widths = map.RendererMap.Sprites.Select((sprite, index) => (sprite, index)).Where(item => item.sprite.Atlas != null)
                .Select(item => new[] { (float)item.index, item.sprite.ViewScale(position).X }).ToArray();
            Select(map.RendererMap.Sprites, cull: false, position: position, direction: direction);
            spatialCases.Add(new { x = position.X, y = position.Y, widths,
                directionX = direction.X, directionY = direction.Y, visible = visibleOrder,
                expected = selector.Visible.Select(item => item.Order).ToArray() });
        }
        File.WriteAllText(Path.Combine(artifactDirectory, "sprite-spatial-oracle.json"), JsonSerializer.Serialize(new { positions = sourcePositions, views = spatialCases }));
        Console.WriteLine($"Generated sprite-sort references: {spatialCases.Count} race views.");
    }
}
