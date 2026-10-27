using System.Numerics;
using System.Text.Json.Serialization;
using WolfNET.Game;

namespace WolfNET.Audio;

[JsonConverter(typeof(JsonStringEnumConverter<SfxCue>))]
public enum SfxCue { EngineIdle, EngineLoad, Brakes, ImpactHard, ImpactMetal, ImpactWood, ImpactRubber, ImpactCar, CountdownTick, CountdownGo, Crowd, MudSlow, MudMid, MudHigh }

/// <summary>Short decoded clips, independent of browser APIs. Each voice owns its playback instance.</summary>
public interface ISfxBackend : IDisposable
{
    ISfxVoice? Play(SfxCue cue, bool loop, float phase = 0, int emitterId = 0, int? variantIndex = null);
    void SetPaused(bool paused);
    // Publish the frame's requested mixes together. Immediate backends need no flush.
    void Commit() { }
}

public interface ISfxVoice : IDisposable
{
    bool Finished { get; }
    void SetMix(float volume, float pan, float pitch);
}

public readonly record struct CarSfxState(int Id, Vector2 Position, float Speed, float MaxSpeed,
    float Throttle, float Acceleration, float Braking, bool OnMud = false, float MudMaxSpeed = 8);
public readonly record struct SfxImpact(int SourceId, Vector2 Position, string Material, float Speed);
public readonly record struct SfxFrame(Vector2 ListenerPosition, Vector2 ListenerForward,
    IReadOnlyList<CarSfxState> Cars, IReadOnlyList<SfxImpact> Impacts,
    RacePhase? Phase, int? CountdownNumber, bool Active, float Seconds);
