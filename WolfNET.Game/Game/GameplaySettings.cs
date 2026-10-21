using WolfNET.Game.Player;

namespace WolfNET.Game;

public sealed record GameplaySettings
{
    public CarSettings Car { get; init; } = new();
    public void Validate()
    {
        if (Car == null) throw new InvalidDataException("Gameplay settings need a car configuration.");
        Car.Validate();
    }
}
