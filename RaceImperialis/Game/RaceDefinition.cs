namespace RaceImperialis.Game;

// Invisible timing gates. These are metadata, never rendered or placed in the collision grid.
public sealed record RaceGateDefinition
{
    public required float[] Center { get; init; }
    public required float[] Direction { get; init; }
    public float HalfWidth { get; init; } = 3.5f;

    public void Validate(int width, int height)
    {
        if (Center is not { Length: 2 } || Direction is not { Length: 2 } ||
            !Center.All(float.IsFinite) || !Direction.All(float.IsFinite) ||
            Center[0] < 0 || Center[0] >= width || Center[1] < 0 || Center[1] >= height ||
            !float.IsFinite(HalfWidth) || HalfWidth <= 0 ||
            !float.IsFinite(Direction[0] * Direction[0] + Direction[1] * Direction[1]) ||
            Direction[0] * Direction[0] + Direction[1] * Direction[1] <= 0)
            throw new InvalidDataException("Race gates need a center inside the map, a finite nonzero direction, and a positive halfWidth.");
    }
}

public sealed record RaceDefinition
{
    public required RaceGateDefinition StartFinish { get; init; }
    public required RaceGateDefinition[] Checkpoints { get; init; }

    public void Validate(int width, int height)
    {
        if (StartFinish == null || Checkpoints is not { Length: >= 2 and <= 64 } || Checkpoints.Any(gate => gate == null))
            throw new InvalidDataException("A race needs a start/finish gate and between two and sixty-four ordered checkpoints.");
        StartFinish.Validate(width, height);
        foreach (var gate in Checkpoints) gate.Validate(width, height);
        var gates = Checkpoints.Prepend(StartFinish).ToArray();
        if (gates.Select(gate => (gate.Center[0], gate.Center[1])).Distinct().Count() != gates.Length)
            throw new InvalidDataException("Race gates must have distinct centers.");
    }
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

