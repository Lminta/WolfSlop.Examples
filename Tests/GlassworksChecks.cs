using System.Numerics;
using WolfNET.Game;
using WolfNET.Game.Physics;
using WolfNET.Graphics;

internal static class GlassworksChecks
{
    public static async Task<int> RunAsync(HttpClient http, string webRoot)
    {
        var assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new Exception(message);
        }

        var map = await GameMap.LoadAsync(http, "maps/glassworks.json", 64, 64);
        var render = map.RendererMap;
        Check(render.Width <= 27 && render.Height <= 27, "Glassworks no longer fits its compact footprint");
        Check(map.Revision == 1, "Glassworks did not compile as an initial scene");
        var collision = new CircleCollider(map.NavigationMap, map.PlayerStart.CollisionRadius);
        Check(collision.CanOccupy(map.PlayerStartPosition), "Glassworks spawn overlaps an obstacle");

        RenderCell Cell(int x, int y) => render.Cells[y * render.Width + x];
        bool Navigable(int x, int y) => map.NavigationMap.IsWalkable(x, y) ||
            map.NavigationMap.IsWalkable(x, y, inAir: true) ||
            map.NavigationMap.IsWalkable(x, y, crouching: true);

        for (var x = 0; x < render.Width; x++)
        for (var y = 0; y < render.Height; y++)
        {
            var cell = Cell(x, y);
            if (x == 0 || y == 0 || x == render.Width - 1 || y == render.Height - 1)
                Check(!Navigable(x, y) && cell.Flags.HasFlag(RenderCellFlags.StopsRay),
                    $"Glassworks boundary leaks at {x},{y}");
            Check(cell.FloorTextureLayer != TextureCatalog.MissingLayer &&
                (cell.Flags.HasFlag(RenderCellFlags.SkyCeiling) || cell.CeilingTextureLayer != TextureCatalog.MissingLayer),
                $"Glassworks has an unfinished floor or ceiling at {x},{y}");
            if (cell.Flags.HasFlag(RenderCellFlags.Wall))
            {
                var material = render.Materials[(int)cell.MaterialIndex];
                Check(new[] { material.North, material.East, material.South, material.West }
                    .All(layer => layer != TextureCatalog.MissingLayer),
                    $"Glassworks wall or door has an unfinished face at {x},{y}");
            }
        }

        var doors = map.Doors.Select(door => (door.X, door.Y)).ToHashSet();
        var spawn = ((int)map.PlayerStartPosition.X, (int)map.PlayerStartPosition.Y);
        var reached = new HashSet<(int X, int Y)> { spawn };
        var frontier = new Queue<(int X, int Y)>(reached);
        while (frontier.TryDequeue(out var point))
        foreach (var step in new[] { (X: -1, Y: 0), (X: 1, Y: 0), (X: 0, Y: -1), (X: 0, Y: 1) })
        {
            var next = (X: point.X + step.X, Y: point.Y + step.Y);
            if ((Navigable(next.X, next.Y) || doors.Contains(next)) && reached.Add(next))
                frontier.Enqueue(next);
        }
        for (var x = 0; x < render.Width; x++)
        for (var y = 0; y < render.Height; y++)
            Check(!(Navigable(x, y) || doors.Contains((x, y))) || reached.Contains((x, y)),
                $"Glassworks has disconnected floor or a stranded shortcut at {x},{y}");

        // Check the authored doorway orientations with the production interaction
        // system and collision circle, including passage from either side.
        foreach (var door in map.Doors)
        {
            Check(!map.NavigationMap.IsWalkable(door.X, door.Y),
                $"Glassworks door at {door.X},{door.Y} should start closed");
            var normal = door.Definition.Axis == "x" ? Vector2.UnitX : Vector2.UnitY;
            var center = new Vector2(door.X + .5f, door.Y + .5f);
            Check(collision.CanOccupy(center - normal) && collision.CanOccupy(center + normal),
                $"Glassworks door at {door.X},{door.Y} lacks clear approaches along its normal");
            foreach (var direction in new[] { normal, -normal })
            {
                var start = center - direction;
                var finish = center + direction;
                Check(map.Interact(start, direction, map.PlayerStart.CollisionRadius) && door.TargetOpen,
                    $"Glassworks door at {door.X},{door.Y} cannot be opened from {start}");
                map.UpdateDoors(door.Definition.OpenSeconds * 1000, start, map.PlayerStart.CollisionRadius);
                Check(door.Openness == 1 && Vector2.Distance(collision.Trace(start, finish), finish) < .0001f,
                    $"Glassworks door at {door.X},{door.Y} cannot be crossed from {start}");
                Check(map.Interact(finish, -direction, map.PlayerStart.CollisionRadius) && !door.TargetOpen,
                    $"Glassworks door at {door.X},{door.Y} cannot be closed from {finish}");
                map.UpdateDoors(door.Definition.OpenSeconds * 1000, finish, map.PlayerStart.CollisionRadius);
                Check(door.Openness == 0, $"Glassworks door at {door.X},{door.Y} did not close");
            }
        }

        foreach (var sprite in render.Sprites)
            Check(sprite.OwnerCell > 0 && sprite.OwnerCell <= render.Cells.Count &&
                render.Textures.Layers.TryGetValue(sprite.TextureLayer, out var path) && !string.IsNullOrWhiteSpace(path),
                "Glassworks sprite has no owner cell or texture");

        foreach (var sprite in render.Sprites.Where(sprite =>
            render.Textures.Layers[sprite.TextureLayer]?.Contains("lantern-sprite") == true))
        {
            Check(sprite.Kind.HasFlag(SpriteKind.FixedPlane), "Glassworks lantern rotates with the camera");
            // The side-profile card spans the wall normal, so the bracket projects
            // into the room instead of lying flat along the wall as a decal.
            var normalX = (sprite.Kind & (SpriteKind)3) == SpriteKind.PlaneNormalY;
            var coordinate = normalX ? sprite.Position.X : sprite.Position.Y;
            var surface = MathF.Round(coordinate);
            var mirrored = render.Textures.Layers[sprite.TextureLayer]!.Contains("mirrored");
            var inward = mirrored ? -1 : 1;
            var mountU = mirrored ? 377f / 512 : 135f / 512;
            var mount = coordinate + sprite.Scale.X * (mountU - .5f);
            Check(Math.Abs(mount - surface - inward * .003f) < .0001f,
                "Glassworks lantern mounting plate is detached from its wall");
            var bodyU = mirrored ? 202f / 512 : 310f / 512;
            var body = coordinate + sprite.Scale.X * (bodyU - .5f);
            Check((body - mount) * inward > .06f,
                "Glassworks lantern body does not project from its wall bracket");
            var wallPosition = sprite.Position + (normalX ? Vector2.UnitX : Vector2.UnitY) *
                (surface - coordinate - inward * .02f);
            var wall = Cell((int)wallPosition.X, (int)wallPosition.Y);
            Check(wall.Flags.HasFlag(RenderCellFlags.StopsRay) && !wall.Flags.HasFlag(RenderCellFlags.Door),
                "Glassworks lantern is not supported by stationary masonry");
        }
        foreach (var sprite in render.Sprites.Where(sprite =>
            render.Textures.Layers[sprite.TextureLayer]?.Contains("directional-atlas") == true))
        {
            Check(sprite.Atlas?.Directions == 8, "Glassworks furnishing has no complete turntable");
            for (var direction = 0; direction < 8; direction++)
            {
                var angle = (sprite.RotationDegrees + direction * 45) * MathF.PI / 180;
                var observer = sprite.Position + new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                Check(sprite.ViewFrame(observer) == direction, "Glassworks furnishing selected an incorrect directional view");
            }
        }

        var textures = render.Textures.Layers.Values.Append(render.Sky?.Texture)
            .Where(path => path != null).Distinct();
        foreach (var texture in textures)
            Check(File.Exists(Path.Combine(webRoot, texture!)), $"Glassworks texture file is missing: {texture}");

        Console.WriteLine($"PASS: {assertions} Glassworks assertions (compact footprint, complete surfaces, connected routes, spawn, doors, and textures).");
        return assertions;
    }
}
