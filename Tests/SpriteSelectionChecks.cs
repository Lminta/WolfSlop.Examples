using System.Numerics;
using System.Text.Json;
using WolfNET.Game;
using WolfNET.Graphics;

internal static class SpriteSelectionChecks
{
    public static async Task<int> RunAsync(HttpClient http, string webRoot)
    {
        var assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new Exception(message);
        }
        var selector = new ReferenceSpriteSelection();
        void Select(IReadOnlyList<Sprite> sprites, bool cull = true, float range = 60, Sprite? avatar = null,
            double time = 0, Vector2? position = null, Vector2? direction = null, float fovScale = 1) =>
            selector.Select(sprites, position ?? Vector2.Zero, direction ?? Vector2.UnitX,
                new Vector2(0, -fovScale), range, cull, time, avatar);
        Sprite Make(float x, float y, uint id, float width = 1) => new(new(x, y), 10, id) { Scale = new(width, 1) };

        Select([]);
        Check(selector.Visible.Count == 0, "Empty sprite maps cannot be queried");
        Sprite[] edges = [Make(10, 0, 1), Make(-10, 0, 2), Make(10, 10.25f, 3),
            Make(10, 12, 4), Make(-.1f, 0, 5, 4), Make(60.5f, 0, 6), Make(60.51f, 0, 7),
            Make(62, 0, 8, 6), Make(100, 0, 9)];
        Select(edges);
        var ids = selector.Visible.Select(view => view.Sprite.OwnerCell).ToHashSet();
        Check(ids.SetEquals(new uint[] { 1, 3, 5, 6, 8 }), "Distance, view-edge, or near-plane culling lost a visible quad");
        Select(edges, cull: false);
        Check(selector.Visible.Any(view => view.Sprite.OwnerCell == 2), "Exploration minimap lost sprites behind the camera");
        Select(edges, direction: -Vector2.UnitX);
        Check(selector.Visible.Any(view => view.Sprite.OwnerCell == 2) && selector.Visible.All(view => view.Sprite.OwnerCell != 1),
            "Turning the camera did not update visibility");
        Select(edges, range: float.MaxValue, cull: false);
        Check(selector.Visible.Count == edges.Length, "A distance larger than the map broke the spatial query");

        var atlas = new SpriteAtlas { Directions = 2, Frames = [
            new() { X = 0, Y = 0, Width = 10, Height = 10 },
            new() { X = 10, Y = 0, Width = 60, Height = 10 }] };
        var directional = Make(62, 0, 10) with { Atlas = atlas, FacingCamera = false };
        Select([directional]);
        var resolved = selector.Visible.Single();
        Check(resolved.Scale == directional.ViewScale(Vector2.Zero) && resolved.Layer == directional.ViewLayer(Vector2.Zero) &&
              resolved.Kind == directional.ViewKind(Vector2.Zero), "Resolved directional artwork/geometry differs from the sprite contract");
        var animated = directional with { Atlas = atlas with { Directions = 1 }, Animation = new() { FramesPerSecond = 1 } };
        Sprite[] animatedSource = [animated];
        Select(animatedSource, time: 0);
        Check(selector.Visible.Count == 0, "Current narrow animation frame exceeded the distance cutoff");
        Select(animatedSource, time: 1);
        Check(selector.Visible.Single().Scale.X == 6, "Index bounds did not retain the widest animation frame");

        Sprite[] equal = [Make(4, 3, 20), Make(4, -3, 21), Make(5, 0, 22)];
        Select(equal);
        Check(selector.Visible.Select(view => view.Sprite.OwnerCell).SequenceEqual(new uint[] { 20, 21, 22 }),
            "Equal-distance painter ordering depends on spatial-grid traversal");
        var overlaps = Enumerable.Range(0, 100).Select(i => Make(10 + i % 5, i % 3 - 1, (uint)i + 200)).ToArray();
        var legacy = overlaps.Select(sprite => (Sprite: sprite, Distance: Vector2.DistanceSquared(Vector2.Zero, sprite.Position))).ToList();
        legacy.Sort(static (a, b) => b.Distance.CompareTo(a.Distance));
        Select(overlaps);
        Check(selector.Visible.Select(view => view.Sprite).SequenceEqual(legacy.Select(view => view.Sprite)),
            "Equal-distance alpha overlaps changed the legacy painter ordering");
        var orderingRandom = new Random(381);
        var orderingCases = new List<object>();
        foreach (var count in new[] { 1, 2, 3, 15, 16, 17, 31, 64, 257, 1024, 2048 })
        {
            var input = Enumerable.Range(0, count).Select(i => Make(orderingRandom.Next(-20, 21), orderingRandom.Next(-20, 21), (uint)i)).ToArray();
            var expected = input.Select(sprite => (Sprite: sprite, Distance: Vector2.DistanceSquared(Vector2.Zero, sprite.Position))).ToList();
            expected.Sort(static (a, b) => b.Distance.CompareTo(a.Distance));
            orderingCases.Add(new { distances = input.Select(sprite => Vector2.DistanceSquared(Vector2.Zero, sprite.Position)).ToArray(),
                expected = expected.Select(view => view.Sprite.OwnerCell).ToArray() });
            Select(input, cull: false);
            Check(selector.Visible.Select(view => view.Sprite).SequenceEqual(expected.Select(view => view.Sprite)),
                $"Specialized painter sort differs from legacy sorting for {count} overlapping sprites");
        }
        var artifactDirectory = Path.GetFullPath(Path.Combine(webRoot, "../../artifacts/performance"));
        Directory.CreateDirectory(artifactDirectory);
        File.WriteAllText(Path.Combine(artifactDirectory, "sprite-sort-oracle.json"), JsonSerializer.Serialize(orderingCases));
        var avatar = Make(3, 0, 30);
        Select([], avatar: avatar);
        Check(selector.Visible.Single().Sprite == avatar, "Moving player sprite was not selected independently of scenery");
        Select([], avatar: avatar with { Position = new(-3, 0) });
        Check(selector.Visible.Count == 0, "Moving player sprite retained a stale indexed position");
        Select([Make(4, 0, 40)]);
        Select([Make(-4, 0, 41)]);
        Check(selector.Visible.Count == 0, "Replacing a sprite snapshot retained its old scenery");
        selector.Clear();
        Select([Make(4, 0, 42)]);
        Check(selector.Visible.Single().Sprite.OwnerCell == 42, "Scene release left stale index state");

        Sprite[] nearby = [Make(4, 0, 50), Make(8, 0, 51)];
        Select(nearby);
        var expanded = nearby.Concat(Enumerable.Range(0, 10_000).Select(i => Make(500 + i % 100, 500 + i / 100, (uint)i + 100))).ToArray();
        Select(expanded);
        Check(selector.Visible.Count == 2, "Distant map scenery changed nearby selection");

        // Compare against direct quad clipping, independently of the grid and bounding-circle tests.
        var random = new Random(7341);
        var scatter = Enumerable.Range(0, 500).Select(i => Make(random.NextSingle() * 180 - 90, random.NextSingle() * 180 - 90,
            (uint)i + 1000, random.NextSingle() * 12) with
        {
            Kind = (i % 3) switch { 0 => SpriteKind.FixedPlane | SpriteKind.PlaneNormalX,
                1 => SpriteKind.FixedPlane | SpriteKind.PlaneNormalY, _ => SpriteKind.Billboard }
        }).ToArray();
        for (var step = 0; step < 40; step++)
        {
            var angle = step * MathF.Tau / 40;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var position = new Vector2(random.NextSingle() * 30 - 15, random.NextSingle() * 30 - 15);
            var scale = (step % 3) switch { 0 => .5f, 1 => 1f, _ => 2f };
            Select(scatter, position: position, direction: direction, fovScale: scale);
            var selected = selector.Visible.Select(view => view.Sprite.OwnerCell).ToHashSet();
            foreach (var sprite in scatter)
                if (ShouldDraw(sprite, position, direction, scale, 60))
                    Check(selected.Contains(sprite.OwnerCell), "Spatial/view culling rejected a quad intersecting the screen");
        }

        var map = await GameMap.LoadAsync(http, "maps/pine-loop.json", 512, 512);
        var sourcePositions = map.RendererMap.Sprites.SelectMany(sprite => new[] { sprite.Position.X, sprite.Position.Y,
            sprite.Scale.X, sprite.Atlas?.Frames.Max(frame => sprite.Scale.X * ((float)frame.Width / frame.Height)) ?? sprite.Scale.X }).ToArray();
        var spatialCases = new List<object>();
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(webRoot, "../../Tests/Fixtures/pine-loop-lap.json")));
        foreach (var view in fixture.RootElement.GetProperty("views").EnumerateArray())
        {
            var values = view[1].EnumerateArray().Select(value => value.GetSingle()).ToArray();
            var position = new Vector2(values[0], values[1]);
            var direction = Vector2.Normalize(new(values[2], values[3]));
            Select(map.RendererMap.Sprites, position: position, direction: direction);
            var selected = selector.Visible.Select(item => item.Order).ToHashSet();
            var visibleOrder = selector.Visible.Select(item => item.Order).ToArray();
            for (var i = 0; i < map.RendererMap.Sprites.Count; i++)
                if (ShouldDraw(map.RendererMap.Sprites[i], position, direction, 1, 60))
                    Check(selected.Contains(i), $"Pine Loop {view[0]} lost visible scenery {i}");
            var widths = map.RendererMap.Sprites.Select((sprite, index) => (sprite, index)).Where(item => item.sprite.Atlas != null)
                .Select(item => new[] { (float)item.index, item.sprite.ViewScale(position).X }).ToArray();
            Select(map.RendererMap.Sprites, cull: false, position: position, direction: direction);
            spatialCases.Add(new { x = position.X, y = position.Y, widths,
                directionX = direction.X, directionY = direction.Y, visible = visibleOrder,
                expected = selector.Visible.Select(item => item.Order).ToArray() });
        }
        File.WriteAllText(Path.Combine(artifactDirectory, "sprite-spatial-oracle.json"), JsonSerializer.Serialize(new { positions = sourcePositions, views = spatialCases }));
        Console.WriteLine($"PASS: {assertions} sprite-selection assertions (spatial queries, clipping, atlas bounds, ordering, snapshots, and Pine Loop views).");
        return assertions;
    }

    private static bool ShouldDraw(Sprite sprite, Vector2 position, Vector2 direction, float planeScale, float range)
    {
        var width = sprite.ViewScale(position).X;
        if (Vector2.Distance(position, sprite.Position) > range + width * .5f) return false;
        var right = new Vector2(direction.Y, -direction.X);
        var axis = sprite.Kind.HasFlag(SpriteKind.PlaneNormalX) ? Vector2.UnitY :
            sprite.Kind.HasFlag(SpriteKind.PlaneNormalY) ? Vector2.UnitX : right;
        var a = sprite.Position - axis * (width * .5f) - position;
        var b = sprite.Position + axis * (width * .5f) - position;
        var begin = 0f; var end = 1f;
        foreach (var normal in new[] { direction, direction * planeScale + right, direction * planeScale - right })
        {
            var start = Vector2.Dot(a, normal);
            var change = Vector2.Dot(b - a, normal);
            if (MathF.Abs(change) < .000001f) { if (start < 0) return false; continue; }
            var crossing = -start / change;
            if (change > 0) begin = MathF.Max(begin, crossing); else end = MathF.Min(end, crossing);
            if (begin > end) return false;
        }
        return true;
    }
}
