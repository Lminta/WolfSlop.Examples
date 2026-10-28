namespace RaceImperialis.Audio;

public sealed record SoundSettings : MusicSettings
{
    // Fraction of normal music volume at the start of the countdown.
    public float CountdownStartVolume { get; init; } = .15f;
    public SfxSettings Sfx { get; init; } = new();

    public override void Validate()
    {
        base.Validate();
        static bool ValidVolume(float value) => float.IsFinite(value) && value >= 0 && value <= 1;
        if (!ValidVolume(Volume) || !ValidVolume(MusicVolume) || !ValidVolume(CountdownStartVolume) ||
            !float.IsFinite(MusicCrossfadeSeconds) || MusicCrossfadeSeconds < 0)
            throw new InvalidDataException("Sound volumes must be between zero and one; musicCrossfadeSeconds must be finite and nonnegative.");
        if (Sfx == null) throw new InvalidDataException("Audio settings need an SFX configuration.");
        Sfx.Validate();
    }

    public float CountdownVolumeScale(float progress)
    {
        progress = Math.Clamp(progress, 0, 1);
        return CountdownStartVolume + (1 - CountdownStartVolume) * progress * progress * (3 - 2 * progress);
    }
}
