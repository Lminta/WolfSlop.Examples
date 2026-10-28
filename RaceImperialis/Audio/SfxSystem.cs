using System.Numerics;
using WolfNET.Engine.World;

namespace RaceImperialis.Audio;

/// <summary>Portable C# mixing policy. Positions and distances are in map blocks.</summary>
public sealed class SfxSystem : IDisposable
{
    private sealed record Loop(ISfxVoice Voice, SfxCue Cue);
    private sealed record Shot(ISfxVoice Voice, Vector2 Position, float Volume, float Pitch);
    private readonly ISfxBackend<SfxCue> _backend;
    private readonly SfxSettings _settings;
    private readonly float _volume;
    private readonly Vector2[] _crowds;
    private readonly int[] _nearestCrowds;
    private readonly float[] _crowdDistances;
    private readonly List<int> _staleMud = [];
    private readonly List<(int Id, SfxCue Cue)> _staleLoops = [];
    private readonly Vector2 _countdownPosition;
    private readonly Dictionary<(int Id, SfxCue Cue), Loop> _loops = [];
    private readonly List<Shot> _shots = [];
    private readonly Dictionary<int, double> _impactTimes = [];
    private readonly Dictionary<int, float> _engineLoad = [];
    private readonly HashSet<(int, SfxCue)> _wanted = [];
    private readonly Dictionary<int, int> _mudVisits = [];
    private readonly Dictionary<int, float> _mudContactSeconds = [];
    private readonly Dictionary<int, int> _previousMudVariants = [];
    private readonly HashSet<int> _mudContacts = [];
    private readonly Random _random;
    private readonly int _mudVariantCount;
    private RacePhase? _previousPhase;
    private int? _previousNumber;
    private double _clock;
    private bool _disposed;

    public SfxSystem(ISfxBackend<SfxCue> backend, SoundSettings sound, IEnumerable<Vector2> crowds, Vector2 countdownPosition,
        Random? random = null)
    {
        sound.Validate();
        _backend = backend;
        _settings = sound.Sfx;
        _volume = sound.Volume * _settings.Volume;
        _crowds = crowds.ToArray();
        _nearestCrowds = new int[Math.Min(_crowds.Length, _settings.MaxCrowdVoices)];
        _crowdDistances = new float[_nearestCrowds.Length];
        _countdownPosition = countdownPosition;
        _random = random ?? Random.Shared;
        _mudVariantCount = new[] { SfxCue.MudSlow, SfxCue.MudMid, SfxCue.MudHigh }
            .Select(cue => _settings.Clips.TryGetValue(cue, out var files) ? files.Length : 0)
            .Where(count => count > 0).DefaultIfEmpty(1).Min();
    }

    public static (float Gain, float Pan) SpatialMix(Vector2 listener, Vector2 forward, Vector2 source,
        float referenceDistance, float maxDistance)
        => SpatialAudio.Mix(listener, forward, source, referenceDistance, maxDistance);

    public void Update(SfxFrame frame)
    {
        if (_disposed) return;
        var active = frame.Active && _volume > 0;
        _backend.SetPaused(!active);
        if (!active) return;
        var seconds = float.IsFinite(frame.Seconds) ? Math.Clamp(frame.Seconds, 0, .25f) : 0;
        _clock += seconds;
        _wanted.Clear();
        _mudContacts.Clear();
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
            UpdateMud(car, frame, seconds);
        }
        _staleMud.Clear();
        foreach (var id in _mudContactSeconds.Keys)
            if (!_mudContacts.Contains(id)) _staleMud.Add(id);
        foreach (var id in _staleMud)
        {
            _mudVisits.Remove(id);
            _mudContactSeconds.Remove(id);
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
        var crowdCount = FindNearestCrowds(frame.ListenerPosition);
        for (var i = 0; i < crowdCount; i++)
        {
            var index = _nearestCrowds[i];
            SetLoop(-index - 1, SfxCue.Crowd, _crowds[index], _settings.CrowdVolume, 1, frame);
        }
        _staleLoops.Clear();
        foreach (var key in _loops.Keys)
            if (!_wanted.Contains(key)) _staleLoops.Add(key);
        foreach (var key in _staleLoops)
        {
            _loops[key].Voice.Dispose();
            _loops.Remove(key);
        }
        _backend.Commit();
    }

    private int FindNearestCrowds(Vector2 listener)
    {
        if (_nearestCrowds.Length == 0) return 0;
        var count = 0;
        var limit = _settings.CrowdMaxDistance * _settings.CrowdMaxDistance;
        for (var index = 0; index < _crowds.Length; index++)
        {
            var distance = Vector2.DistanceSquared(_crowds[index], listener);
            if (!(distance < limit)) continue;
            var insert = count;
            // Strict comparison keeps authored order for equally distant groups.
            while (insert > 0 && distance < _crowdDistances[insert - 1]) insert--;
            if (insert == _nearestCrowds.Length) continue;
            for (var i = Math.Min(count, _nearestCrowds.Length - 1); i > insert; i--)
            {
                _nearestCrowds[i] = _nearestCrowds[i - 1];
                _crowdDistances[i] = _crowdDistances[i - 1];
            }
            _nearestCrowds[insert] = index;
            _crowdDistances[insert] = distance;
            count = Math.Min(count + 1, _nearestCrowds.Length);
        }
        return count;
    }

    public static SfxCue MudBand(float speed, float mudMaxSpeed)
    {
        var limit = float.IsFinite(mudMaxSpeed) && mudMaxSpeed > 0 ? mudMaxSpeed : 8;
        // Use the full-mud forward limit for both partial and full contact;
        // asphalt top speed would make the high band unreachable on mud.
        return speed < limit / 3 ? SfxCue.MudSlow : speed < limit * (2f / 3) ? SfxCue.MudMid : SfxCue.MudHigh;
    }

    private void UpdateMud(CarSfxState car, SfxFrame frame, float seconds)
    {
        if (!car.OnMud) return;
        _mudContacts.Add(car.Id);
        var contactTime = MathF.Min(_settings.MudEntrySeconds, _mudContactSeconds.GetValueOrDefault(car.Id) + seconds);
        _mudContactSeconds[car.Id] = contactTime;
        // A brief graze must neither play a sound nor consume a new variant.
        // Leaving mud resets this per-car timer; paused time never reaches here.
        if (contactTime + .000001f < _settings.MudEntrySeconds) return;
        if (!_mudVisits.TryGetValue(car.Id, out var variant))
        {
            var hasPrevious = _previousMudVariants.TryGetValue(car.Id, out var previous) && previous < _mudVariantCount;
            variant = _mudVariantCount == 1 ? 0 : _random.Next(_mudVariantCount - (hasPrevious ? 1 : 0));
            if (_mudVariantCount > 1 && hasPrevious && variant >= previous) variant++;
            _mudVisits[car.Id] = variant;
            _previousMudVariants[car.Id] = variant;
        }
        // Stops and loss of hearing range release the voice, but preserve the
        // visit's selected variant until the car actually leaves the mud.
        if (car.Speed <= .2f) return;
        SetLoop(car.Id, MudBand(car.Speed, car.MudMaxSpeed), car.Position,
            _settings.MudVolume, 1, frame, variant);
    }

    private void Mix(ISfxVoice voice, SfxFrame frame, Vector2 position, float volume, float pitch, bool crowd = false)
    {
        var spatial = SpatialMix(frame.ListenerPosition, frame.ListenerForward, position,
            _settings.ReferenceDistance, crowd ? _settings.CrowdMaxDistance : _settings.MaxDistance);
        voice.SetMix(_volume * volume * spatial.Gain, spatial.Pan, pitch);
    }

    private void SetLoop(int id, SfxCue cue, Vector2 position, float volume, float pitch, SfxFrame frame, int? variantIndex = null)
    {
        var limit = cue == SfxCue.Crowd ? _settings.CrowdMaxDistance : _settings.MaxDistance;
        if (volume <= .001f || Vector2.DistanceSquared(position, frame.ListenerPosition) >= limit * limit) return;
        var key = (id, cue);
        _wanted.Add(key);
        if (!_loops.TryGetValue(key, out var loop))
        {
            if (_loops.Count + _shots.Count >= _settings.MaxVoices) return;
            var phase = cue == SfxCue.Crowd ? (Math.Abs(id) * .381966f) % 1 : 0;
            var voice = _backend.Play(cue, true, phase, id, variantIndex);
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
        _mudVisits.Clear(); _previousMudVariants.Clear();
        _mudContactSeconds.Clear();
        _backend.Dispose();
    }
}
