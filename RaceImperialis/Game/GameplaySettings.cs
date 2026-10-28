using WolfNET.Engine.Actors;

namespace RaceImperialis.Game;

public sealed record RaceSettings
{
    public int LapCount { get; init; } = 3;
    public float OpponentCornerSpeed { get; init; } = 13;

    public void Validate()
    {
        if (LapCount <= 0) throw new InvalidDataException("Race lapCount must be positive.");
        if (!float.IsFinite(OpponentCornerSpeed) || OpponentCornerSpeed <= 0)
            throw new InvalidDataException("Opponent corner speed must be finite and positive.");
    }
}

public sealed record GameplaySettings
{
    public CarSettings Car { get; init; } = new();
    public RaceSettings Race { get; init; } = new();
    public OpponentSettings Opponents { get; init; } = new();
    public CameraShakeSettings CameraShake { get; init; } = new();
    public void Validate()
    {
        if (Car == null) throw new InvalidDataException("Gameplay settings need a car configuration.");
        Car.Validate();
        if (Race == null) throw new InvalidDataException("Gameplay settings need a race configuration.");
        Race.Validate();
        if (Opponents == null) throw new InvalidDataException("Gameplay settings need opponent profiles.");
        Opponents.Validate(Car);
        if (CameraShake == null) throw new InvalidDataException("Gameplay settings need a camera shake configuration.");
        CameraShake.Validate();
    }
}
