using WolfNET.Game.Player;

namespace WolfNET.Game;

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

public sealed record CameraShakeSettings
{
    // Amplitudes are radians, speeds blocks/second, durations seconds, frequencies Hz.
    public float MudAmplitude { get; init; } = .004f;
    public float MudEntryDelay { get; init; } = .1f;
    public float MudFullStrengthSpeed { get; init; } = 4;
    public float MudFadeInRate { get; init; } = 10;
    public float MudFadeOutRate { get; init; } = 18;
    public float MudYawFrequency { get; init; } = 5;
    public float MudYawDetailFrequency { get; init; } = 9;
    public float MudPitchFrequency { get; init; } = 7;
    public float MudPitchDetailFrequency { get; init; } = 11;
    public float ImpactMinSpeed { get; init; } = .75f;
    public float ImpactFullStrengthSpeed { get; init; } = 20;
    public float ImpactAmplitude { get; init; } = .05f;
    public float ImpactMinDuration { get; init; } = .15f;
    public float ImpactMaxDuration { get; init; } = .55f;
    public float ImpactLowSpeedFrequency { get; init; } = 10;
    public float ImpactHighSpeedFrequency { get; init; } = 6;
    public float ImpactYawScale { get; init; } = .45f;
    public float ImpactYawFrequencyScale { get; init; } = .8f;

    public void Validate()
    {
        foreach (var value in new[] { MudAmplitude, MudEntryDelay, ImpactMinSpeed, ImpactAmplitude, ImpactYawScale })
            if (!float.IsFinite(value) || value < 0)
                throw new InvalidDataException("Camera shake amplitudes, entry delay and minimum speed must be finite and nonnegative.");
        foreach (var value in new[] { MudFullStrengthSpeed, MudFadeInRate, MudFadeOutRate, MudYawFrequency,
            MudYawDetailFrequency, MudPitchFrequency, MudPitchDetailFrequency, ImpactFullStrengthSpeed,
            ImpactMinDuration, ImpactMaxDuration, ImpactLowSpeedFrequency, ImpactHighSpeedFrequency, ImpactYawFrequencyScale })
            if (!float.IsFinite(value) || value <= 0)
                throw new InvalidDataException("Camera shake speeds, rates, durations and frequencies must be finite and positive.");
        if (ImpactFullStrengthSpeed <= ImpactMinSpeed || ImpactMaxDuration < ImpactMinDuration)
            throw new InvalidDataException("Camera shake impact speed and duration ranges must be ordered.");
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
