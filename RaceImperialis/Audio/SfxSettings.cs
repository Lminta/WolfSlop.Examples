namespace RaceImperialis.Audio;

public sealed record SfxSettings
{
    public float Volume { get; init; } = .8f;
    public float EngineVolume { get; init; } = .6f;
    public float BrakeVolume { get; init; } = .5f;
    public float MudVolume { get; init; } = .65f;
    public float MudEntrySeconds { get; init; } = .25f;
    public float ImpactVolume { get; init; } = .85f;
    public float CountdownVolume { get; init; } = .8f;
    public float CrowdVolume { get; init; } = .3f;
    public float ReferenceDistance { get; init; } = 2;
    public float MaxDistance { get; init; } = 45;
    public float CrowdMaxDistance { get; init; } = 65;
    public float ImpactCooldownSeconds { get; init; } = .2f;
    public float MinimumImpactSpeed { get; init; } = 1;
    public int MaxCrowdVoices { get; init; } = 4;
    public int MaxVoices { get; init; } = 32;
    public Dictionary<SfxCue, string[]> Clips { get; init; } = new()
    {
        [SfxCue.EngineIdle] = ["sounds/engine-idle-1.wav", "sounds/engine-idle-2.wav", "sounds/engine-idle-3.wav"],
        [SfxCue.EngineLoad] = ["sounds/engine-load-1.wav", "sounds/engine-load-2.wav", "sounds/engine-load-3.wav"],
        [SfxCue.Brakes] = ["sounds/brakes-1.wav", "sounds/brakes-2.wav", "sounds/brakes-3.wav"],
        [SfxCue.ImpactHard] = ["sounds/impact-hard-1.wav", "sounds/impact-hard-2.wav", "sounds/impact-hard-3.wav"],
        [SfxCue.ImpactMetal] = ["sounds/impact-metal-1.wav", "sounds/impact-metal-2.wav", "sounds/impact-metal-3.wav"],
        [SfxCue.ImpactWood] = ["sounds/impact-wood-1.wav", "sounds/impact-wood-2.wav", "sounds/impact-wood-3.wav"],
        [SfxCue.ImpactRubber] = ["sounds/impact-rubber-1.wav", "sounds/impact-rubber-2.wav", "sounds/impact-rubber-3.wav"],
        [SfxCue.ImpactCar] = ["sounds/impact-car-1.wav", "sounds/impact-car-2.wav", "sounds/impact-car-3.wav"],
        [SfxCue.CountdownTick] = ["sounds/countdown-tick.wav"],
        [SfxCue.CountdownGo] = ["sounds/countdown-go.wav"],
        [SfxCue.Crowd] = ["sounds/crowd-1.wav", "sounds/crowd-2.wav", "sounds/crowd-3.wav"],
        [SfxCue.MudSlow] = ["sounds/mud-slow-1.wav", "sounds/mud-slow-2.wav", "sounds/mud-slow-3.wav"],
        [SfxCue.MudMid] = ["sounds/mud-mid-1.wav", "sounds/mud-mid-2.wav", "sounds/mud-mid-3.wav"],
        [SfxCue.MudHigh] = ["sounds/mud-high-1.wav", "sounds/mud-high-2.wav", "sounds/mud-high-3.wav"]
    };

    public void Validate()
    {
        var volumes = new[] { Volume, EngineVolume, BrakeVolume, MudVolume, ImpactVolume, CountdownVolume, CrowdVolume };
        if (volumes.Any(value => !float.IsFinite(value) || value < 0 || value > 1) ||
            !float.IsFinite(MudEntrySeconds) || MudEntrySeconds < 0 ||
            !float.IsFinite(ReferenceDistance) || ReferenceDistance <= 0 ||
            !float.IsFinite(MaxDistance) || MaxDistance <= ReferenceDistance ||
            !float.IsFinite(CrowdMaxDistance) || CrowdMaxDistance <= ReferenceDistance ||
            !float.IsFinite(ImpactCooldownSeconds) || ImpactCooldownSeconds < 0 ||
            !float.IsFinite(MinimumImpactSpeed) || MinimumImpactSpeed <= 0 ||
            MaxCrowdVoices < 0 || MaxCrowdVoices > 16 || MaxVoices < 1 || MaxVoices > 128 ||
            Clips == null || Clips.Any(pair => !Enum.IsDefined(pair.Key) || pair.Value == null ||
                pair.Value.Length > 16 || pair.Value.Any(string.IsNullOrWhiteSpace)))
            throw new InvalidDataException("Invalid SFX volumes, distances, voice limits, cooldown, or clip paths.");
    }
}
