using System.Numerics;
using System.Text.Json;
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

        var map = await GameMap.LoadAsync(http, "maps/pine-loop.json", 512, 512);
        var render = map.RendererMap;
        var collider = new CircleCollider(map.NavigationMap, map.PlayerStart.CollisionRadius);
        RenderCell Cell(int x, int y) => render.Cells[y * render.Width + x];
        string? Floor(int x, int y) => render.Textures.Layers[Cell(x, y).FloorTextureLayer];

        Check(render.Width == 256 && render.Height == 512,
            "Pine Loop no longer has the requested 256 by 512 dimensions");
        Check(collider.CanOccupy(map.PlayerStartPosition), "Pine Loop start overlaps scenery");
        Check(render.Sky != null && Cell(25, 440).Flags.HasFlag(RenderCellFlags.SkyCeiling),
            "Pine Loop starting straight must be outdoors");
        Check(Enumerable.Range(24, 3).All(x => map.NavigationMap.IsWalkable(x, 440) &&
            Floor(x, 440)?.EndsWith("/grid.svg") == true), "Pine Loop grid lost its three-wide starting zone");
        Check(Enumerable.Range(24, 2).All(x => map.NavigationMap.IsWalkable(x, 390) &&
            Floor(x, 390)?.EndsWith("/asphalt.png") == true) &&
            Floor(23, 390)?.EndsWith("/asphalt.png") != true && Floor(26, 390)?.EndsWith("/asphalt.png") != true,
            "Pine Loop starting zone does not narrow to two clear lanes");

        // Trace the full circuit with the production collision radius. The final
        // segment crosses the starting line, proving the route closes without
        // driving through scenery or taking the service lane shortcut.
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.GetFullPath(
            Path.Combine(webRoot, "../../Tests/Fixtures/pine-loop-lap.json"))));
        var lap = fixture.RootElement.GetProperty("lap").EnumerateArray()
            .Select(point => new Vector2(point[0].GetSingle(), point[1].GetSingle())).ToArray();
        Check(lap.Length > 2 && lap[0] == lap[^1], "Pine Loop lap fixture does not form a closed circuit");
        float lapLength = 0;
        for (var i = 1; i < lap.Length; i++)
        {
            lapLength += Vector2.Distance(lap[i - 1], lap[i]);
            Check(collider.CanOccupy(lap[i]) &&
                Vector2.Distance(collider.Trace(lap[i - 1], lap[i]), lap[i]) < .0001f,
                $"Pine Loop circuit is blocked between {lap[i - 1]} and {lap[i]}");
        }
        Check(lapLength >= 1500, "Expanded Pine Loop lost its much longer route");
        Check(fixture.RootElement.GetProperty("majorCorners").GetArrayLength() >= 25,
            "Pine Loop lost its distinct corner complexes");
        var zones = fixture.RootElement.GetProperty("zones").EnumerateArray().ToArray();
        Check(zones.Length >= 8, "Pine Loop is missing its distinct places of interest");
        foreach (var zone in zones)
        {
            var point = zone.GetProperty("checkpoint");
            Check(collider.CanOccupy(new(point[0].GetSingle(), point[1].GetSingle())),
                $"Pine Loop zone checkpoint is blocked: {zone.GetProperty("name").GetString()}");
        }
        foreach (var approach in fixture.RootElement.GetProperty("poiApproaches").EnumerateArray())
        {
            var from = approach.GetProperty("from"); var to = approach.GetProperty("to");
            var start = new Vector2(from[0].GetSingle(), from[1].GetSingle());
            var finish = new Vector2(to[0].GetSingle(), to[1].GetSingle());
            Check(collider.CanOccupy(start) && collider.CanOccupy(finish) &&
                Vector2.Distance(collider.Trace(start, finish), finish) < .0001f,
                $"Pine Loop landmark is unreachable: {approach.GetProperty("name").GetString()}");
        }

        foreach (var y in new[] { 44, 45 })
        foreach (var x in Enumerable.Range(82, 22))
            Check(map.NavigationMap.IsWalkable(x, y) &&
                !Cell(x, y).Flags.HasFlag(RenderCellFlags.SkyCeiling),
                $"Pine Loop tunnel has a gap or obstacle at {x},{y}");
        foreach (var y in new[] { 412, 416, 420 })
            Check(Vector2.Distance(collider.Trace(new(31.5f, y + .5f), new(35.5f, y + .5f)),
                new Vector2(35.5f, y + .5f)) < .0001f &&
                !Cell(35, y).Flags.HasFlag(RenderCellFlags.SkyCeiling),
                $"Pine Loop pit box at row {y} has no usable covered entrance");

        foreach (var sprite in render.Sprites.Where(sprite =>
            render.Textures.Layers[sprite.TextureLayer]?.EndsWith("/fence.png") == true ||
            render.Textures.Layers[sprite.TextureLayer]?.EndsWith("/grandstand.png") == true))
            Check(sprite.Kind.HasFlag(SpriteKind.FixedPlane), "Pine Loop track furniture rotates with the camera");
        var startStands = render.Sprites.Where(sprite =>
            render.Textures.Layers[sprite.TextureLayer]?.EndsWith("/grandstand.png") == true &&
            sprite.Position.X == 17.5f && sprite.Position.Y >= 400 && sprite.Position.Y <= 452).ToArray();
        Check(startStands.Length >= 7 && startStands.All(sprite => sprite.Scale.X >= 6),
            "Pine Loop needs its row of larger inline grandstand modules");

        var treeTextures = new[] { "/tree.png", "/tree-oak.png", "/tree-birch.png" };
        int treeCount = 0;
        foreach (var texture in treeTextures)
        {
            var count = render.Sprites.Count(sprite =>
                render.Textures.Layers[sprite.TextureLayer]?.EndsWith(texture) == true);
            Check(count > 0, $"Pine Loop is missing its tree species {texture}");
            treeCount += count;
        }
        Check(treeCount is >= 2000 and <= 5000, "Pine Loop forest density is outside its authored budget");
        Check(render.Sprites.Count <= 4500, "Pine Loop scenery exceeds its authored sprite budget");
        var boundaryMaterial = render.Materials[(int)Cell(0, 0).MaterialIndex];
        Check(render.Textures.Layers[boundaryMaterial.East]?.EndsWith("/forest-boundary.png") == true,
            "Pine Loop boundary does not use the forest backdrop");
        Check(render.Sprites.All(sprite => render.Textures.Layers[sprite.TextureLayer]?.EndsWith("/tires.png") != true),
            "Pine Loop still uses a flat row of tire stacks");
        Check(map.Objects.Values.OfType<SpriteGroupDefinition>().Any(group =>
            group.Sprites.Count(member => member.SpriteId is 521 or 531) >= 3),
            "Pine Loop has no reusable groups of separate tire stacks");
        foreach (var id in new[] { 543, 580, 581, 582, 585 })
        {
            var group = (SpriteGroupDefinition)map.Objects[id];
            Check(group.Sprites.Length >= 3 && group.Sprites.All(member => member.SpriteId is 521 or 531) &&
                group.Sprites.Select(member => (member.Offset.X, member.Offset.Y)).Distinct().Count() == group.Sprites.Length,
                $"Pine Loop tire group {id} does not place individual stacks at separate world positions");
        }
        foreach (var id in new[] { 544, 545 })
        {
            var group = (SpriteGroupDefinition)map.Objects[id];
            Check(group.Sprites.Length == 1, $"Pine Loop buffer {id} is assembled from multiple planes");
            var sprite = (SpriteDefinition)map.Objects[group.Sprites[0].SpriteId];
            Check(!sprite.FacingCamera && sprite.PlaneAxis == null && sprite.Atlas is { Directions: 4 } &&
                sprite.Atlas.Frames.Length == 4 && sprite.Atlas.Frames.Distinct().Count() == 2 &&
                sprite.Atlas.Frames[0] == sprite.Atlas.Frames[2] && sprite.Atlas.Frames[1] == sprite.Atlas.Frames[3],
                $"Pine Loop buffer {id} needs two repeated directional views");
        }
        var bufferPoint = fixture.RootElement.GetProperty("bufferCoordinate");
        var bufferCenter = new Vector2(bufferPoint[0].GetSingle() + .5f, bufferPoint[1].GetSingle() + .5f);
        var sampleBuffer = render.Sprites.Single(sprite => sprite.Position == bufferCenter);
        Check(sampleBuffer.ViewLayer(bufferCenter + Vector2.UnitX * 2) == sampleBuffer.TextureLayer &&
            sampleBuffer.ViewLayer(bufferCenter + Vector2.UnitY * 2) == sampleBuffer.TextureLayer + 1 &&
            (sampleBuffer.ViewKind(bufferCenter + Vector2.UnitX * 2) & (uint)SpriteKind.FixedPlane) != 0,
            "Pine Loop buffer front/side observer positions select incorrect directional views");
        Check(Math.Abs(sampleBuffer.Elevation - sampleBuffer.Scale.Y * .5f + .5f) < .0001f,
            "Pine Loop directional buffer is not grounded");
        foreach (var view in fixture.RootElement.GetProperty("views").EnumerateArray().Where(view =>
            view[0].GetString()?.StartsWith("buffer-") == true))
            Check(collider.CanOccupy(new(view[1][0].GetSingle(), view[1][1].GetSingle())),
                "Pine Loop buffer review observer overlaps scenery");

        foreach (var cell in render.Cells)
            Check(cell.FloorTextureLayer != TextureCatalog.MissingLayer &&
                (cell.Flags.HasFlag(RenderCellFlags.SkyCeiling) || cell.CeilingTextureLayer != TextureCatalog.MissingLayer),
                "Pine Loop has an unfinished floor or roof");
        foreach (var path in render.Textures.Layers.Values.Append(render.Sky?.Texture).OfType<string>().Distinct())
            Check(File.Exists(Path.Combine(webRoot, path)), $"Pine Loop texture is missing: {path}");

        var workspace = new WolfNET.Editor.MapWorkspace(webRoot);
        var document = workspace.Load("pine-loop");
        Check(document.Map.WorldMap.Length == 256 && document.Map.WorldMap[0].Length == 512,
            "Editor cannot load the expanded Pine Loop map");
        using var exported = new System.IO.Compression.ZipArchive(new MemoryStream(workspace.Export(document)));
        using var exportedMap = exported.GetEntry("maps/pine-loop.json")!.Open();
        var roundTrip = JsonSerializer.Deserialize<MapDefinition>(exportedMap, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Check(roundTrip.WorldMap.Length == 256 && roundTrip.WorldMap[0].Length == 512,
            "Editor export loses the expanded Pine Loop dimensions");

        Console.WriteLine($"PASS: {assertions} Pine Loop assertions (grand circuit lap, landmarks, inline stands, forest budget, directional buffers, tire groups, tunnel, pits, textures, and editor export).");
        return assertions;
    }
}
