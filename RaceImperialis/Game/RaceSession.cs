using System.Numerics;

namespace RaceImperialis.Game;

public enum RacePhase { AwaitingStart, Countdown, Racing, Finished }

public readonly record struct OpponentRaceStatus(int CompletedLaps, double TotalMilliseconds,
    IReadOnlyList<double> LapTimes, bool Finished);

public readonly record struct RaceStanding(string Name, int Place, bool IsPlayer, OpponentRaceStatus Status);

public readonly record struct RaceStatus(int CompletedLaps, double TotalMilliseconds, double LapMilliseconds,
    double? LastLapMilliseconds, bool Started)
{
    public RacePhase Phase { get; init; }
    public int TotalLaps { get; init; }
    public int? CountdownNumber { get; init; }
    public float CountdownProgress { get; init; }
    public bool ShowGo { get; init; }
    public IReadOnlyList<double> LapTimes { get; init; } = Array.Empty<double>();
    public OpponentRaceStatus? Opponent { get; init; }
    public IReadOnlyList<OpponentRaceStatus> Opponents { get; init; } = Array.Empty<OpponentRaceStatus>();
    public IReadOnlyList<RaceStanding> Standings { get; init; } = Array.Empty<RaceStanding>();
    public double? GapMilliseconds { get; init; }
}

/// <summary>Elapsed separation between matching lap finishes; holds until the next lap finish.</summary>
public static class RaceLapGap
{
    public static double? Calculate(RaceStatus player, RaceStatus opponent, double now)
    {
        var lap = Math.Max(player.CompletedLaps, opponent.CompletedLaps);
        if (lap == 0) return null;
        static double FinishTime(RaceStatus racer, int lap)
        {
            double time = 0;
            for (var i = 0; i < lap; i++) time += racer.LapTimes[i];
            return time;
        }
        var playerArrived = player.CompletedLaps >= lap;
        var opponentArrived = opponent.CompletedLaps >= lap;
        if (playerArrived && opponentArrived) return FinishTime(opponent, lap) - FinishTime(player, lap);
        return playerArrived ? Math.Max(0, now - FinishTime(player, lap)) : -Math.Max(0, now - FinishTime(opponent, lap));
    }
}

/// <summary>Tracks ordered forward gate crossings and active simulation time for one scene.</summary>
public sealed class RaceSession
{
    private const double CountdownMilliseconds = 5000;
    private double _countdownRemaining = CountdownMilliseconds;
    private double _goRemaining = 1000;
    private bool _departed;
    private RaceRoute? _progressRoute;
    private float[] _gateDistances = [];
    private float? _projectedDistance;
    private static DirectedGate Gate(RaceGateDefinition gate) => new(new(gate.Center[0], gate.Center[1]),
        new(gate.Direction[0], gate.Direction[1]), gate.HalfWidth);

    private readonly DirectedGate _finish;
    private readonly DirectedGate[] _checkpoints;
    private readonly List<(float Fraction, int Checkpoint)> _crossings = [];
    private int _nextCheckpoint;
    public RaceStatus Status { get; private set; } = new(0, 0, 0, null, false);

    public RaceSession(RaceDefinition definition, RaceSettings? settings = null)
    {
        settings ??= new();
        settings.Validate();
        Status = Status with { TotalLaps = settings.LapCount };
        _finish = Gate(definition.StartFinish);
        _checkpoints = definition.Checkpoints.Select(Gate).ToArray();
    }

    public float Progress(RaceRoute route, Vector2 position)
    {
        if (!ReferenceEquals(_progressRoute, route))
        {
            _progressRoute = route;
            _gateDistances = new[] { _finish }.Concat(_checkpoints).Append(_finish)
                .Select(g => route.Project(g.Center)).ToArray();
            _projectedDistance = null;
        }
        if (!_departed) return -Vector2.Distance(position, _finish.Center);
        var previous = _gateDistances[_nextCheckpoint];
        var next = _gateDistances[_nextCheckpoint + 1];
        _projectedDistance = route.Project(position, _projectedDistance);
        var length = (next - previous + route.Length) % route.Length;
        var distance = ((_projectedDistance.Value - previous) % route.Length + route.Length) % route.Length;
        return Status.CompletedLaps * (_checkpoints.Length + 1) + _nextCheckpoint +
            Math.Clamp(distance / MathF.Max(length, .01f), 0, .9999f);
    }

    public void BeginCountdown()
    {
        if (Status.Phase != RacePhase.AwaitingStart) return;
        Status = Status with { Phase = RacePhase.Countdown, CountdownNumber = 5 };
    }

    // Returns only the part of this frame after GO, so a countdown boundary
    // never gives the car an extra frame of acceleration or race time.
    public float AdvanceStart(float milliseconds)
    {
        if (!float.IsFinite(milliseconds) || milliseconds <= 0) return 0;
        if (Status.Phase == RacePhase.Countdown)
        {
            var countdownTime = Math.Min(milliseconds, _countdownRemaining);
            _countdownRemaining -= countdownTime;
            milliseconds -= (float)countdownTime;
            Status = Status with { CountdownProgress = (float)(1 - _countdownRemaining / CountdownMilliseconds) };
            Status = _countdownRemaining > 0
                ? Status with { CountdownNumber = (int)Math.Ceiling(_countdownRemaining / 1000) }
                : Status with { Phase = RacePhase.Racing, CountdownNumber = null, Started = true, ShowGo = true };
        }
        return Status.Phase == RacePhase.Racing ? milliseconds : 0;
    }

    public void Update(Vector2 from, Vector2 to, float milliseconds)
    {
        if (Status.Phase != RacePhase.Racing || !float.IsFinite(milliseconds) || milliseconds <= 0) return;
        _goRemaining = Math.Max(0, _goRemaining - milliseconds);
        Status = Status with { ShowGo = _goRemaining > 0 };
        _crossings.Clear();
        if (_finish.Crossed(from, to) is { } finish) _crossings.Add((finish, -1));
        for (var i = 0; i < _checkpoints.Length; i++)
            if (_checkpoints[i].Crossed(from, to) is { } fraction) _crossings.Add((fraction, i));
        _crossings.Sort(static (a, b) => a.Fraction.CompareTo(b.Fraction));
        var previousFraction = 0f;
        foreach (var crossing in _crossings)
        {
            Advance((crossing.Fraction - previousFraction) * milliseconds);
            previousFraction = crossing.Fraction;
            if (crossing.Checkpoint == -1)
            {
                if (!_departed) _departed = true;
                else if (_nextCheckpoint == _checkpoints.Length)
                {
                    Status = Status with { CompletedLaps = Status.CompletedLaps + 1,
                        LastLapMilliseconds = Status.LapMilliseconds,
                        LapTimes = Array.AsReadOnly(Status.LapTimes.Append(Status.LapMilliseconds).ToArray()),
                        LapMilliseconds = Status.CompletedLaps + 1 == Status.TotalLaps ? Status.LapMilliseconds : 0,
                        Phase = Status.CompletedLaps + 1 == Status.TotalLaps ? RacePhase.Finished : RacePhase.Racing };
                    if (Status.Phase == RacePhase.Finished) return;
                }
                // An incomplete trip over the line cannot preserve old checkpoint progress.
                _nextCheckpoint = 0;
            }
            else if (_departed && crossing.Checkpoint == _nextCheckpoint) _nextCheckpoint++;
        }
        Advance((1 - previousFraction) * milliseconds);
    }

    private void Advance(double milliseconds)
    {
        if (Status.Started) Status = Status with { TotalMilliseconds = Status.TotalMilliseconds + milliseconds,
            LapMilliseconds = Status.LapMilliseconds + milliseconds };
    }
}
