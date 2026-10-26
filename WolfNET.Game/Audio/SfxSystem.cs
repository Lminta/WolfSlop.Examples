using System.Numerics;
using WolfNET.Game;

namespace WolfNET.Audio;

/// <summary>Portable C# mixing policy. Positions and distances are in map blocks.</summary>
public sealed class SfxSystem : IDisposable
{
    private sealed record Loop(ISfxVoice Voice, SfxCue Cue);
    private sealed record Shot(ISfxVoice Voice, Vector2 Position, float Volume, float Pitch);
    private readonly ISfxBackend _backend;
    private readonly SfxSettings _settings;
    private readonly float _volume;
    private readonly Vector2[] _crowds;
    private readonly Vector2 _countdownPosition;
    private readonly Dictionary<(int Id, SfxCue Cue), Loop> _loops = [];
    private readonly List<Shot> _shots = [];
    private readonly Dictionary<int, double> _impactTimes = [];
    private readonly Dictionary<int, float> _engineLoad = [];
    private readonly HashSet<(int, SfxCue)> _wanted = [];
    private RacePhase? _previousPhase;
    private int? _previousNumber;
    private double _clock;
    private bool _disposed;

    public SfxSystem(ISfxBackend backend, SoundSettings sound, IEnumerable<Vector2> crowds, Vector2 countdownPosition)
    {
        sound.Validate();
        _backend = backend;
        _settings = sound.Sfx;
        _volume = sound.Volume * _settings.Volume;
        _crowds = crowds.ToArray();
        _countdownPosition = countdownPosition;
    }

    public static (float Gain, float Pan) SpatialMix(Vector2 listener, Vector2 forward, Vector2 source,
        float referenceDistance, float maxDistance)
    {
        var offset = source - listener;
        var distance = offset.Length();
        if (distance >= maxDistance) return (0, 0);
        var direction = forward.LengthSquared() > .00001f ? Vector2.Normalize(forward) : Vector2.UnitY;
        var pan = distance > .00001f ? Vector2.Dot(offset / distance, new(direction.Y, -direction.X)) : 0;
        var tail = Math.Clamp((maxDistance - distance) / (maxDistance - referenceDistance), 0, 1);
        return (MathF.Min(1, referenceDistance / MathF.Max(referenceDistance, distance)) * tail,
            Math.Clamp(pan, -1, 1));
    }

    public void Update(SfxFrame frame)
    {
        if (_disposed) return;
        var active = frame.Active && _volume > 0;
        _backend.SetPaused(!active);
        if (!active) return;
        var seconds = float.IsFinite(frame.Seconds) ? Math.Clamp(frame.Seconds, 0, .25f) : 0;
        _clock += seconds;
        _wanted.Clear();
        for (var i = _shots.Count - 1; i >= 0; i--)
        {
            var shot = _shots[i];
            if (shot.Voice.Finished) { shot.Voice.Dispose(); _shots.RemoveAt(i); }
            else Mix(shot.Voice, frame, shot.Position, shot.Volume, shot.Pitch);
        }
        foreach (var car in frame.Cars)
        {
            var speed = Math.Clamp(car.Speed / MathF.Max(1, car.MaxSpeed), 0, 1);
            var targetLoad = Math.Clamp(MathF.Abs(car.Throttle) * .75f + MathF.Max(0, car.Acceleration) / 20, 0, 1);
            var oldLoad = _engineLoad.GetValueOrDefault(car.Id);
            var load = oldLoad + (targetLoad - oldLoad) * (1 - MathF.Exp(-8 * seconds));
            _engineLoad[car.Id] = load;
            var pitch = .75f + .9f * speed + .35f * load;
            SetLoop(car.Id, SfxCue.EngineIdle, car.Position, _settings.EngineVolume * (1 - .65f * load), pitch, frame);
            SetLoop(car.Id, SfxCue.EngineLoad, car.Position, _settings.EngineVolume * load, pitch, frame);
            if (car.Braking > 0 && car.Speed > 1)
                SetLoop(car.Id, SfxCue.Brakes, car.Position,
                    _settings.BrakeVolume * car.Braking * Math.Clamp(car.Speed / 10, 0, 1), .85f + .3f * speed, frame);
        }
        foreach (var impact in frame.Impacts)
        {
            if (impact.Speed < _settings.MinimumImpactSpeed ||
                _impactTimes.TryGetValue(impact.SourceId, out var previous) && _clock - previous < _settings.ImpactCooldownSeconds)
                continue;
            _impactTimes[impact.SourceId] = _clock;
            var cue = impact.Material switch
            {
                "metal" => SfxCue.ImpactMetal, "wood" => SfxCue.ImpactWood,
                "rubber" => SfxCue.ImpactRubber, "car" => SfxCue.ImpactCar, _ => SfxCue.ImpactHard
            };
            PlayShot(cue, impact.Position, _settings.ImpactVolume * Math.Clamp(impact.Speed / 12, .1f, 1),
                .95f + .1f * (impact.SourceId % 5) / 4, frame);
        }
        if (frame.Phase == RacePhase.Countdown && frame.CountdownNumber is { } number &&
            (_previousPhase != RacePhase.Countdown || _previousNumber != number))
            PlayShot(SfxCue.CountdownTick, _countdownPosition, _settings.CountdownVolume, 1, frame);
        if (frame.Phase == RacePhase.Racing && _previousPhase == RacePhase.Countdown)
            PlayShot(SfxCue.CountdownGo, _countdownPosition, _settings.CountdownVolume, 1, frame);
        _previousPhase = frame.Phase;
        _previousNumber = frame.CountdownNumber;
        // Only the closest crowd groups get voices. Their spatial positions stay
        // fixed in the map; turning the camera changes the perceived stereo side.
        foreach (var crowd in _crowds.Select((position, index) => (position, index))
            .Where(item => Vector2.DistanceSquared(item.position, frame.ListenerPosition) < _settings.CrowdMaxDistance * _settings.CrowdMaxDistance)
            .OrderBy(item => Vector2.DistanceSquared(item.position, frame.ListenerPosition)).Take(_settings.MaxCrowdVoices))
            SetLoop(-crowd.index - 1, SfxCue.Crowd, crowd.position, _settings.CrowdVolume, 1, frame);
        foreach (var key in _loops.Keys.Where(key => !_wanted.Contains(key)).ToArray())
        {
            _loops[key].Voice.Dispose();
            _loops.Remove(key);
        }
    }

    private void Mix(ISfxVoice voice, SfxFrame frame, Vector2 position, float volume, float pitch, bool crowd = false)
    {
        var spatial = SpatialMix(frame.ListenerPosition, frame.ListenerForward, position,
            _settings.ReferenceDistance, crowd ? _settings.CrowdMaxDistance : _settings.MaxDistance);
        voice.SetMix(_volume * volume * spatial.Gain, spatial.Pan, pitch);
    }

    private void SetLoop(int id, SfxCue cue, Vector2 position, float volume, float pitch, SfxFrame frame)
    {
        var limit = cue == SfxCue.Crowd ? _settings.CrowdMaxDistance : _settings.MaxDistance;
        if (volume <= .001f || Vector2.DistanceSquared(position, frame.ListenerPosition) >= limit * limit) return;
        var key = (id, cue);
        _wanted.Add(key);
        if (!_loops.TryGetValue(key, out var loop))
        {
            if (_loops.Count + _shots.Count >= _settings.MaxVoices) return;
            var phase = cue == SfxCue.Crowd ? (Math.Abs(id) * .381966f) % 1 : 0;
            var voice = _backend.Play(cue, true, phase, id);
            if (voice == null) return;
            _loops[key] = loop = new(voice, cue);
        }
        Mix(loop.Voice, frame, position, volume, pitch, cue == SfxCue.Crowd);
    }

    private void PlayShot(SfxCue cue, Vector2 position, float volume, float pitch, SfxFrame frame)
    {
        if (volume <= 0 || _loops.Count + _shots.Count >= _settings.MaxVoices ||
            SpatialMix(frame.ListenerPosition, frame.ListenerForward, position, _settings.ReferenceDistance, _settings.MaxDistance).Gain <= 0)
            return;
        var voice = _backend.Play(cue, false);
        if (voice == null) return;
        Mix(voice, frame, position, volume, pitch);
        _shots.Add(new(voice, position, volume, pitch));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var loop in _loops.Values) loop.Voice.Dispose();
        foreach (var shot in _shots) shot.Voice.Dispose();
        _loops.Clear(); _shots.Clear();
        _backend.Dispose();
    }
}
