using System.Numerics;

namespace WolfNET.Game;

public sealed record MapDefinition
{
    // Object ID to JSON path, relative to this map file.
    public Dictionary<int, string> Objects { get; init; } = [];
    // Each number identifies a definition from the object registry, including ID zero.
    public required int[][] WorldMap { get; init; }
    public required PlayerStartDefinition PlayerStart { get; init; }
    public SkyDefinition? Sky { get; init; }
    public RaceMinimapDefinition? RaceMinimap { get; init; }
}

public sealed record RaceMinimapDefinition
{
    public required float[][] Route { get; init; }
    public required float[] StartFinish { get; init; }
    public float[][][] Shortcuts { get; init; } = [];

    public void Validate(int width, int height)
    {
        bool ValidPoint(float[]? point) => point is { Length: 2 } &&
            float.IsFinite(point[0]) && float.IsFinite(point[1]) &&
            point[0] >= 0 && point[0] < width && point[1] >= 0 && point[1] < height;
        if (Route is not { Length: >= 4 } || !Route.All(ValidPoint) ||
            !Route[0].SequenceEqual(Route[^1]) || !ValidPoint(StartFinish) ||
            Shortcuts == null || Shortcuts.Any(path => path is not { Length: >= 2 } || !path.All(ValidPoint)))
            throw new InvalidDataException("Race minimap needs a closed route, start/finish point, and shortcut paths with finite coordinates inside the map.");
    }
}

public sealed record SkyDefinition
{
    public required string Texture { get; init; }
    public float RotationDegrees { get; init; }
}

public sealed record PlayerStartDefinition
{
    public string Controller { get; init; } = "player";
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
    public float CrouchDrop { get; init; } = 0.25f;
    public float CrouchSpeedMultiplier { get; init; } = 0.5f;

    public void Validate()
    {
        if (Controller is not ("player" or "car"))
            throw new InvalidDataException("Player controller must be player or car.");
        var direction = new Vector2(DirectionX, DirectionY);
        if (!float.IsFinite(CrouchDrop) || CrouchDrop <= 0 || CrouchDrop >= 0.5f ||
            !float.IsFinite(CrouchSpeedMultiplier) || CrouchSpeedMultiplier <= 0 ||
            !float.IsFinite(MoveSpeed * CrouchSpeedMultiplier))
            throw new InvalidDataException("crouchDrop must be greater than 0 and less than 0.5; crouchSpeedMultiplier must be positive and produce a finite speed.");
        if (!float.IsFinite(JumpRise) || JumpRise < 0 || JumpRise >= 0.5f ||
            !float.IsFinite(JumpLength) || JumpLength < 0 || JumpLength > 64 ||
            !float.IsFinite(JumpSpeedMultiplier) || JumpSpeedMultiplier <= 0 ||
            !float.IsFinite(MoveSpeed * JumpSpeedMultiplier))
            throw new InvalidDataException("jumpRise must be in [0, 0.5), jumpLength in [0, 64], and jumpSpeedMultiplier positive; all jump settings and airborne speed must be finite.");
        if (JumpLength > 0 && MoveSpeed > 0)
        {
            var flightTime = JumpLength / (MoveSpeed * JumpSpeedMultiplier);
            if (!float.IsFinite(flightTime) || flightTime <= 0)
                throw new InvalidDataException("Jump settings must produce a positive, finite flight time.");
        }
        if (!float.IsFinite(CollisionRadius) || CollisionRadius <= 0 || CollisionRadius >= 0.5f)
            throw new InvalidDataException("Player collisionRadius must be finite, greater than zero, and less than 0.5.");
        if (!float.IsFinite(MoveSpeed) || MoveSpeed < 0 ||
            !float.IsFinite(RotSpeed) || RotSpeed < 0)
            throw new InvalidDataException("Player speeds must be finite and nonnegative.");
        if (!float.IsFinite(direction.LengthSquared()) || direction.LengthSquared() <= 0)
            throw new InvalidDataException("Player start direction must be finite and nonzero.");
    }
}
