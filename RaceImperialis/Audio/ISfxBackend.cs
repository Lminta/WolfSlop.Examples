using System.Numerics;
using System.Text.Json.Serialization;
using WolfNET.Engine.World;

namespace RaceImperialis.Audio;

[JsonConverter(typeof(JsonStringEnumConverter<SfxCue>))]
public enum SfxCue { EngineIdle, EngineLoad, Brakes, ImpactHard, ImpactMetal, ImpactWood, ImpactRubber, ImpactCar, CountdownTick, CountdownGo, Crowd, MudSlow, MudMid, MudHigh }

public readonly record struct CarSfxState(int Id, Vector2 Position, float Speed, float MaxSpeed,
    float Throttle, float Acceleration, float Braking, bool OnMud = false, float MudMaxSpeed = 8);
public readonly record struct SfxImpact(int SourceId, Vector2 Position, string Material, float Speed);
public readonly record struct SfxFrame(Vector2 ListenerPosition, Vector2 ListenerForward,
    IReadOnlyList<CarSfxState> Cars, IReadOnlyList<SfxImpact> Impacts,
    RacePhase? Phase, int? CountdownNumber, bool Active, float Seconds);
