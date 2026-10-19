namespace WolfNET.Game;

public sealed record MapDefinition
{
    // Object ID to JSON path, relative to this map file.
    public Dictionary<int, string> Objects { get; init; } = [];
    // Each number identifies a definition from the object registry, including ID zero.
    public required int[][] WorldMap { get; init; }
    public required PlayerStartDefinition PlayerStart { get; init; }
    public SkyDefinition? Sky { get; init; }
}

public sealed record SkyDefinition
{
    public required string Texture { get; init; }
    public float RotationDegrees { get; init; }
}

public sealed record PlayerStartDefinition
{
    public required float X { get; init; }
    public required float Y { get; init; }
    public required float DirectionX { get; init; }
    public required float DirectionY { get; init; }
    // Squares per second and maximum radians per second, respectively.
    public required float MoveSpeed { get; init; }
    public required float RotSpeed { get; init; }
    public float CollisionRadius { get; init; } = 0.15f;
    public float JumpRise { get; init; } = 0.4f;
    // Horizontal travel in map units; zero disables jumping.
    public float JumpLength { get; init; } = 2f;
    public float JumpSpeedMultiplier { get; init; } = 1.25f;
}
