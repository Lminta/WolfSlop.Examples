using System.Text.Json.Serialization;
using WolfNET.Engine.Actors;

namespace RaceImperialis.Game;

[JsonConverter(typeof(JsonStringEnumConverter<RaceDifficulty>))]
public enum RaceDifficulty { Normal, Easy }

public readonly record struct OpponentMistakeSettings
{
    public bool Enabled { get; init; } = true;
    public float FirstAfterSeconds { get; init; } = 20;
    public float IntervalSeconds { get; init; } = 24;
    public float VariationSeconds { get; init; } = 7;
    public float DurationSeconds { get; init; } = 1.1f;
    public float SpeedMultiplier { get; init; } = .65f;

    public OpponentMistakeSettings() { }

    public void Validate()
    {
        if (!float.IsFinite(FirstAfterSeconds) || FirstAfterSeconds < 0 ||
            !float.IsFinite(VariationSeconds) || VariationSeconds < 0 ||
            !float.IsFinite(IntervalSeconds) || IntervalSeconds <= 0 ||
            !float.IsFinite(DurationSeconds) || DurationSeconds <= 0 ||
            !float.IsFinite(SpeedMultiplier) || SpeedMultiplier <= 0 || SpeedMultiplier >= 1)
            throw new InvalidDataException("Opponent mistake timing must be finite and valid, with a speed multiplier between 0 and 1.");
    }
}

public readonly record struct OpponentProfileSettings
{
    public float MaxSpeedOffset { get; init; } = 3;
    public float AccelerationMultiplier { get; init; } = 1.2f;
    public float CornerGripFraction { get; init; } = .92f;
    public float SteeringCurvatureGain { get; init; } = 1.3f;
    public OpponentMistakeSettings Mistakes { get; init; } = new();

    public OpponentProfileSettings() { }

    public static OpponentProfileSettings Normal(bool green) => green ? new()
    {
        MaxSpeedOffset = -3, AccelerationMultiplier = 3, CornerGripFraction = .96f,
        SteeringCurvatureGain = 1.4f, Mistakes = new() { Enabled = false }
    } : new();

    public void Validate(CarSettings player)
    {
        if (!float.IsFinite(MaxSpeedOffset) || !float.IsFinite(player.Asphalt.MaxForwardSpeed + MaxSpeedOffset) ||
            player.Asphalt.MaxForwardSpeed + MaxSpeedOffset <= player.Asphalt.MaxReverseSpeed ||
            !float.IsFinite(AccelerationMultiplier) || AccelerationMultiplier <= 0 ||
            !float.IsFinite(player.Asphalt.Acceleration * AccelerationMultiplier) ||
            !float.IsFinite(CornerGripFraction) || CornerGripFraction <= 0 || CornerGripFraction > .98f ||
            !float.IsFinite(SteeringCurvatureGain) || SteeringCurvatureGain <= 0)
            throw new InvalidDataException("Opponent profiles need valid forward speeds, acceleration, corner grip, and steering gains.");
        Mistakes.Validate();
    }
}

public sealed record OpponentDifficultySettings
{
    public OpponentProfileSettings Blue { get; init; } = OpponentProfileSettings.Normal(false);
    public OpponentProfileSettings Green { get; init; } = OpponentProfileSettings.Normal(true);

    public OpponentProfileSettings Get(bool green) => green ? Green : Blue;
    public void Validate(CarSettings player) { Blue.Validate(player); Green.Validate(player); }
}

public sealed record OpponentSettings
{
    public RaceDifficulty DefaultDifficulty { get; init; } = RaceDifficulty.Normal;
    public OpponentDifficultySettings Normal { get; init; } = new();
    public OpponentDifficultySettings Easy { get; init; } = new()
    {
        Blue = OpponentProfileSettings.Normal(false) with
        {
            Mistakes = new()
            {
                FirstAfterSeconds = 6, IntervalSeconds = 6, VariationSeconds = 2,
                DurationSeconds = 3.2f, SpeedMultiplier = .5f
            }
        },
        Green = OpponentProfileSettings.Normal(true) with { MaxSpeedOffset = -5, AccelerationMultiplier = .8f }
    };

    public OpponentDifficultySettings Get(RaceDifficulty difficulty) => difficulty switch
    {
        RaceDifficulty.Normal => Normal,
        RaceDifficulty.Easy => Easy,
        _ => throw new InvalidDataException("Unknown race difficulty.")
    };

    public void Validate(CarSettings player)
    {
        if (!Enum.IsDefined(DefaultDifficulty) || Normal == null || Easy == null)
            throw new InvalidDataException("Opponent settings need normal and easy profiles and a valid default difficulty.");
        Normal.Validate(player);
        Easy.Validate(player);
    }
}
