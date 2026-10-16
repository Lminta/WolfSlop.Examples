namespace WolfNET.Game;

public sealed record MapDefinition
{
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
