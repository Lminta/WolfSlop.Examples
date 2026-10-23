namespace WolfNET.Audio;

public sealed record SoundSettings
{
    public float Volume { get; init; } = 1;
    public float MusicVolume { get; init; } = .65f;
    public float MusicCrossfadeSeconds { get; init; } = 3;
    // Fraction of normal music volume at the start of the countdown.
    public float CountdownStartVolume { get; init; } = .15f;

    public void Validate()
    {
        static bool ValidVolume(float value) => float.IsFinite(value) && value >= 0 && value <= 1;
        if (!ValidVolume(Volume) || !ValidVolume(MusicVolume) || !ValidVolume(CountdownStartVolume) ||
            !float.IsFinite(MusicCrossfadeSeconds) || MusicCrossfadeSeconds < 0)
            throw new InvalidDataException("Sound volumes must be between zero and one; musicCrossfadeSeconds must be finite and nonnegative.");
    }

    public float GetMusicVolume(WolfNET.Game.MusicDefinition music) => Volume * (music.Volume ?? MusicVolume);

    public float CountdownVolumeScale(float progress)
    {
        progress = Math.Clamp(progress, 0, 1);
        return CountdownStartVolume + (1 - CountdownStartVolume) * progress * progress * (3 - 2 * progress);
    }
}
