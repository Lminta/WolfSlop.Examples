using System.Numerics;
using WolfNET.Game;
using WolfNET.Game.Physics;
using WolfNET.Graphics;

internal static class PineLoopChecks
{
    public static async Task<int> RunAsync(HttpClient http, string webRoot)
    {
        var assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new Exception(message);
        }

        var map = await GameMap.LoadAsync(http, "maps/pine-loop.json", 64, 64);
        var render = map.RendererMap;
        var collider = new CircleCollider(map.NavigationMap, map.PlayerStart.CollisionRadius);
        RenderCell Cell(int x, int y) => render.Cells[y * render.Width + x];
        string? Floor(int x, int y) => render.Textures.Layers[Cell(x, y).FloorTextureLayer];

        Check(collider.CanOccupy(map.PlayerStartPosition), "Pine Loop start overlaps scenery");
        Check(render.Sky != null && Cell(7, 20).Flags.HasFlag(RenderCellFlags.SkyCeiling),
            "Pine Loop starting straight must be outdoors");
        Check(Enumerable.Range(6, 3).All(x => map.NavigationMap.IsWalkable(x, 20) &&
            Floor(x, 20)?.EndsWith("/grid.svg") == true), "Pine Loop grid lost its three-wide starting zone");
        Check(Enumerable.Range(6, 2).All(x => map.NavigationMap.IsWalkable(x, 12) &&
            Floor(x, 12)?.EndsWith("/asphalt.png") == true) &&
            !map.NavigationMap.IsWalkable(5, 12) && !map.NavigationMap.IsWalkable(8, 12),
            "Pine Loop starting zone does not narrow to two clear lanes");

        // Trace the full circuit with the production collision radius. The final
        // segment crosses the starting line, proving the route closes without
        // driving through scenery or taking the service lane shortcut.
        Vector2[] lap = [new(7, 24), new(7, 11), new(8, 11), new(8, 10),
            new(9, 10), new(9, 9), new(10, 9), new(10, 8), new(11, 8),
            new(11, 7), new(26, 7), new(26, 8), new(27, 8), new(27, 9),
            new(28, 9), new(28, 10), new(29, 10), new(29, 15), new(30, 15),
            new(30, 16), new(31, 16), new(31, 17), new(32, 17), new(32, 18),
            new(33, 18), new(33, 24), new(32, 24), new(32, 25), new(31, 25),
            new(31, 26), new(30, 26), new(30, 27), new(29, 27), new(29, 28),
            new(12, 28), new(12, 27), new(11, 27), new(11, 26), new(10, 26),
            new(10, 25), new(7, 25), new(7, 24)];
        for (var i = 1; i < lap.Length; i++)
            Check(collider.CanOccupy(lap[i]) &&
                Vector2.Distance(collider.Trace(lap[i - 1], lap[i]), lap[i]) < .0001f,
                $"Pine Loop circuit is blocked between {lap[i - 1]} and {lap[i]}");

        foreach (var y in new[] { 6, 7 })
        foreach (var x in Enumerable.Range(17, 7))
            Check(map.NavigationMap.IsWalkable(x, y) &&
                !Cell(x, y).Flags.HasFlag(RenderCellFlags.SkyCeiling),
                $"Pine Loop tunnel has a gap or obstacle at {x},{y}");
        foreach (var y in new[] { 17, 21, 25 })
            Check(Vector2.Distance(collider.Trace(new(10.5f, y + .5f), new(13.5f, y + .5f)),
                new Vector2(13.5f, y + .5f)) < .0001f &&
                !Cell(13, y).Flags.HasFlag(RenderCellFlags.SkyCeiling),
                $"Pine Loop pit box at row {y} has no usable covered entrance");

        foreach (var sprite in render.Sprites.Where(sprite =>
            render.Textures.Layers[sprite.TextureLayer]?.EndsWith("/fence.png") == true ||
            render.Textures.Layers[sprite.TextureLayer]?.EndsWith("/buffer.png") == true ||
            render.Textures.Layers[sprite.TextureLayer]?.EndsWith("/grandstand.png") == true))
            Check(sprite.Kind.HasFlag(SpriteKind.FixedPlane), "Pine Loop track furniture rotates with the camera");

        foreach (var cell in render.Cells)
            Check(cell.FloorTextureLayer != TextureCatalog.MissingLayer &&
                (cell.Flags.HasFlag(RenderCellFlags.SkyCeiling) || cell.CeilingTextureLayer != TextureCatalog.MissingLayer),
                "Pine Loop has an unfinished floor or roof");
        foreach (var path in render.Textures.Layers.Values.Append(render.Sky?.Texture).OfType<string>().Distinct())
            Check(File.Exists(Path.Combine(webRoot, path)), $"Pine Loop texture is missing: {path}");

        Console.WriteLine($"PASS: {assertions} Pine Loop assertions (complete lap, three-to-two width, open sky, tunnel, pit entrances, fixed scenery, and textures).");
        return assertions;
    }
}
