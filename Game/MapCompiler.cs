using System.Numerics;
using WolfNET.Graphics;

namespace WolfNET.Game;

public sealed class MapCompiler
{
    private sealed record SpriteTemplate(Vector2 Offset, uint Layer);
    private sealed record CompiledObject(RenderCell Cell, bool Walkable, SpriteTemplate[] Sprites);

    private readonly Dictionary<int, CompiledObject> _objects = [];
    private readonly WallMaterial[] _materials;
    private readonly TextureCatalog _textures;
    private readonly int _maxWidth;
    private readonly int _maxHeight;

    public MapCompiler(IReadOnlyDictionary<int, MapObjectDefinition> objects, int maxWidth, int maxHeight)
    {
        _maxWidth = maxWidth;
        _maxHeight = maxHeight;
        var layers = new Dictionary<uint, string?>();
        var paths = new Dictionary<string, uint>(StringComparer.Ordinal);
        static string? Path(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Replace('\\', '/');

        // Explicit sprite slots are reserved before assigning surface textures.
        foreach (var sprite in objects.Values.OfType<SpriteDefinition>().OrderBy(sprite => sprite.Id))
        {
            if (sprite.TextureLayer >= TextureCatalog.MaxLayers)
                throw new InvalidDataException($"Sprite {sprite.Id} texture layer must be between 0 and {TextureCatalog.MaxLayers - 1}.");
            var path = Path(sprite.Texture);
            if (layers.TryGetValue(sprite.TextureLayer, out var previous) && previous != path)
                throw new InvalidDataException($"Conflicting textures in layer {sprite.TextureLayer}.");
            layers[sprite.TextureLayer] = path;
            if (path != null)
                paths[path] = paths.TryGetValue(path, out var oldLayer) ? Math.Min(oldLayer, sprite.TextureLayer) : sprite.TextureLayer;
        }

        uint nextLayer = 0;
        uint Resolve(string? texture)
        {
            var path = Path(texture);
            if (path == null) return TextureCatalog.MissingLayer;
            if (paths.TryGetValue(path, out var existing)) return existing;
            while (layers.ContainsKey(nextLayer)) nextLayer = checked(nextLayer + 1);
            if (nextLayer >= TextureCatalog.MaxLayers)
                throw new InvalidDataException($"Scene textures exceed {TextureCatalog.MaxLayers} array layers.");
            var layer = nextLayer;
            layers.Add(layer, path);
            paths.Add(path, layer);
            return layer;
        }

        PassageDefinition Passage(int ownerId, int passageId) =>
            objects.TryGetValue(passageId, out var passage) && passage is PassageDefinition typed
                ? typed : throw new InvalidDataException($"Object {ownerId} references unknown/non-passage ID {passageId}.");

        var materials = new List<WallMaterial>
        {
            new(TextureCatalog.MissingLayer, TextureCatalog.MissingLayer, TextureCatalog.MissingLayer, TextureCatalog.MissingLayer)
        };
        foreach (var (id, definition) in objects.OrderBy(pair => pair.Key))
        {
            if (id < 0 || id != definition.Id)
                throw new InvalidDataException($"Invalid object ID {id}.");
            uint floor = TextureCatalog.MissingLayer, ceiling = TextureCatalog.MissingLayer, material = 0;
            var flags = definition.RaycastTransparent ? RenderCellFlags.None : RenderCellFlags.StopsRay;
            PassageDefinition? passage = null;
            SpriteTemplate[] sprites = [];
            switch (definition)
            {
                case PassageDefinition value:
                    passage = value;
                    break;
                case WallDefinition wall:
                    flags |= RenderCellFlags.Wall;
                    material = (uint)materials.Count;
                    materials.Add(new WallMaterial(Resolve(wall.Textures?.North), Resolve(wall.Textures?.East),
                        Resolve(wall.Textures?.South), Resolve(wall.Textures?.West)));
                    break;
                case SpriteDefinition sprite:
                    passage = Passage(id, sprite.PassageId);
                    sprites = [new(Vector2.Zero, sprite.TextureLayer)];
                    break;
                case SpriteGroupDefinition group:
                    passage = Passage(id, group.PassageId);
                    if (group.Sprites == null)
                        throw new InvalidDataException($"Sprite group {id} has no sprite list.");
                    sprites = group.Sprites.Select(member =>
                    {
                        if (member?.Offset == null || !float.IsFinite(member.Offset.X) || !float.IsFinite(member.Offset.Y))
                            throw new InvalidDataException($"Sprite group {id} has an invalid offset.");
                        if (!objects.TryGetValue(member.SpriteId, out var child) || child is not SpriteDefinition sprite)
                            throw new InvalidDataException($"Sprite group {id} references unknown/non-sprite ID {member.SpriteId}.");
                        return new SpriteTemplate(new Vector2(member.Offset.X, member.Offset.Y), sprite.TextureLayer);
                    }).ToArray();
                    break;
                default:
                    throw new InvalidDataException($"Unsupported object type for ID {id}.");
            }
            if (passage != null)
            {
                floor = Resolve(passage.FloorTexture);
                ceiling = Resolve(passage.CeilingTexture);
            }
            _objects.Add(id, new CompiledObject(new RenderCell(material, floor, ceiling, flags), !definition.Collides, sprites));
        }
        _materials = materials.ToArray();
        _textures = new TextureCatalog(layers);
    }

    public (RenderMap Renderer, bool[][] Walkable) Compile(MapDefinition definition, ulong revision)
    {
        var map = definition.WorldMap;
        if (map == null || map.Length == 0 || map[0] == null || map[0].Length == 0)
            throw new InvalidDataException("Map must contain a nonempty grid.");
        int width = map.Length, height = map[0].Length;
        if (width > _maxWidth || height > _maxHeight)
            throw new InvalidDataException($"Map dimensions exceed {_maxWidth}x{_maxHeight}.");
        var cells = new RenderCell[checked(width * height)];
        var walkable = new bool[width][];
        var sprites = new List<Sprite>();
        for (int x = 0; x < width; x++)
        {
            if (map[x] == null || map[x].Length != height)
                throw new InvalidDataException("Map must be rectangular (worldMap[x][y]).");
            walkable[x] = new bool[height];
            for (int y = 0; y < height; y++)
            {
                if (!_objects.TryGetValue(map[x][y], out var obj))
                    throw new InvalidDataException($"Unknown object ID {map[x][y]} at ({x}, {y}).");
                if ((x == 0 || y == 0 || x == width - 1 || y == height - 1) &&
                    (obj.Walkable || !obj.Cell.Flags.HasFlag(RenderCellFlags.StopsRay)))
                    throw new InvalidDataException("Map boundaries must block movement and rays.");
                int index = y * width + x;
                cells[index] = obj.Cell;
                walkable[x][y] = obj.Walkable;
                foreach (var sprite in obj.Sprites)
                {
                    var position = new Vector2(x + 0.5f, y + 0.5f) + sprite.Offset;
                    if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) ||
                        position.X < 0 || position.Y < 0 || position.X >= width || position.Y >= height)
                        throw new InvalidDataException($"Sprite from ({x}, {y}) is outside the map.");
                    sprites.Add(new Sprite(position, sprite.Layer, (uint)index + 1));
                }
            }
        }
        var start = definition.PlayerStart;
        if (start == null || !float.IsFinite(start.X) || !float.IsFinite(start.Y) ||
            start.X < 0 || start.Y < 0 || start.X >= width || start.Y >= height || !walkable[(int)start.X][(int)start.Y])
            throw new InvalidDataException("Player start must be in a walkable map cell.");
        var direction = new Vector2(start.DirectionX, start.DirectionY);
        if (!float.IsFinite(direction.LengthSquared()) || direction.LengthSquared() <= 0)
            throw new InvalidDataException("Player start direction must be finite and nonzero.");

        return (new RenderMap(width, height, Array.AsReadOnly(cells), Array.AsReadOnly(_materials),
            sprites.AsReadOnly(), _textures, revision), walkable);
    }
}
