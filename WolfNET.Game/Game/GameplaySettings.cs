using WolfNET.Game.Player;

namespace WolfNET.Game;

public sealed record RaceSettings
{
    public int LapCount { get; init; } = 3;

    public void Validate()
    {
        if (LapCount <= 0) throw new InvalidDataException("Race lapCount must be positive.");
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
