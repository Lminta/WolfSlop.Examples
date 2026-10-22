using WolfNET.Game.Player;

namespace WolfNET.Game;

public sealed record RaceSettings
{
    public int LapCount { get; init; } = 3;
    public float OpponentMaxSpeed { get; init; } = 19;
    public float OpponentCornerSpeed { get; init; } = 13;

    public void Validate()
    {
        if (LapCount <= 0) throw new InvalidDataException("Race lapCount must be positive.");
        if (!float.IsFinite(OpponentMaxSpeed) || !float.IsFinite(OpponentCornerSpeed) ||
            OpponentCornerSpeed <= 0 || OpponentMaxSpeed < OpponentCornerSpeed)
            throw new InvalidDataException("Opponent speeds must be finite, positive, with corner speed no greater than maximum speed.");
    }
}

public sealed record GameplaySettings
{
    public CarSettings Car { get; init; } = new();
    public RaceSettings Race { get; init; } = new();
    public void Validate()
    {
        if (Car == null) throw new InvalidDataException("Gameplay settings need a car configuration.");
        Car.Validate();
        if (Race == null) throw new InvalidDataException("Gameplay settings need a race configuration.");
        Race.Validate();
    }
}
