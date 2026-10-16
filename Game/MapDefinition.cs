using System.Numerics;

namespace WolfNET.Game;

public sealed record MapDefinition
{
    public required int[][] WorldMap { get; init; }
    public required SpriteDefinition[] Sprites { get; init; }
    public required PlayerStartDefinition PlayerStart { get; init; }

    public void Validate(int maxWidth, int maxHeight)
    {
        if (WorldMap == null || WorldMap.Length == 0 ||
            WorldMap[0] == null || WorldMap[0].Length == 0)
            throw new InvalidDataException("Map must contain a nonempty grid.");

        int width = WorldMap.Length;
        int height = WorldMap[0].Length;
        if (width > maxWidth || height > maxHeight)
            throw new InvalidDataException($"Map dimensions exceed {maxWidth}x{maxHeight}.");

        for (int x = 0; x < width; x++)
        {
            if (WorldMap[x] == null || WorldMap[x].Length != height)
                throw new InvalidDataException("Map must be rectangular (worldMap[x][y]).");

            for (int y = 0; y < height; y++)
            {
                if (WorldMap[x][y] < 0)
                    throw new InvalidDataException("Wall IDs cannot be negative.");
                if ((x == 0 || y == 0 || x == width - 1 || y == height - 1) && WorldMap[x][y] == 0)
                    throw new InvalidDataException("Map boundaries must be enclosed by walls.");
            }
        }

        bool IsWalkable(float x, float y) =>
            float.IsFinite(x) && float.IsFinite(y) &&
            x >= 0 && y >= 0 && x < width && y < height && WorldMap[(int)x][(int)y] == 0;

        if (PlayerStart == null || !IsWalkable(PlayerStart.X, PlayerStart.Y))
            throw new InvalidDataException("Player start must be in an empty map cell.");

        var direction = new Vector2(PlayerStart.DirectionX, PlayerStart.DirectionY);
        if (!float.IsFinite(direction.LengthSquared()) || direction.LengthSquared() <= 0)
            throw new InvalidDataException("Player start direction must be finite and nonzero.");

        if (Sprites == null || Sprites.Any(sprite => sprite == null || !IsWalkable(sprite.X, sprite.Y)))
            throw new InvalidDataException("Sprites must be placed in empty map cells.");
    }
}

public sealed record SpriteDefinition
{
    public required float X { get; init; }
    public required float Y { get; init; }
    public required uint TextureLayer { get; init; }
}

public sealed record PlayerStartDefinition
{
    public required float X { get; init; }
    public required float Y { get; init; }
    public required float DirectionX { get; init; }
    public required float DirectionY { get; init; }
}
