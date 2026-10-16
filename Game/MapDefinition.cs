namespace WolfNET.Game;

public sealed record MapDefinition
{
    // Object ID to JSON path, relative to this map file.
    public Dictionary<int, string> Objects { get; init; } = [];
    // Each number identifies a definition from the object registry, including ID zero.
    public required int[][] WorldMap { get; init; }
    public required PlayerStartDefinition PlayerStart { get; init; }
}

public sealed record PlayerStartDefinition
{
    public required float X { get; init; }
    public required float Y { get; init; }
    public required float DirectionX { get; init; }
    public required float DirectionY { get; init; }
}
