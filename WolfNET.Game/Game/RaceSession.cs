using System.Numerics;

namespace WolfNET.Game;

public enum RacePhase { AwaitingStart, Countdown, Racing, Finished }

public readonly record struct OpponentRaceStatus(int CompletedLaps, double TotalMilliseconds,
    IReadOnlyList<double> LapTimes, bool Finished);

public readonly record struct RaceStatus(int CompletedLaps, double TotalMilliseconds, double LapMilliseconds,
    double? LastLapMilliseconds, bool Started)
{
    public RacePhase Phase { get; init; }
    public int TotalLaps { get; init; }
    public int? CountdownNumber { get; init; }
    public bool ShowGo { get; init; }
    public IReadOnlyList<double> LapTimes { get; init; } = Array.Empty<double>();
    public OpponentRaceStatus? Opponent { get; init; }
    public double? GapMilliseconds { get; init; }
}

/// <summary>Tracks ordered forward gate crossings and active simulation time for one scene.</summary>
public sealed class RaceSession
{
    private double _countdownRemaining = 5000;
    private double _goRemaining = 1000;
    private bool _departed;
    private readonly record struct Gate(Vector2 Center, Vector2 Direction, float HalfWidth)
    {
        public static Gate From(RaceGateDefinition gate) => new(new(gate.Center[0], gate.Center[1]),
            Vector2.Normalize(new(gate.Direction[0], gate.Direction[1])), gate.HalfWidth);

        public float? Crossed(Vector2 from, Vector2 to)
        {
            var before = Vector2.Dot(from - Center, Direction);
            var after = Vector2.Dot(to - Center, Direction);
            if (before > 0 || after <= 0 || after <= before) return null;
            var fraction = -before / (after - before);
            var point = Vector2.Lerp(from, to, fraction) - Center;
            var across = new Vector2(-Direction.Y, Direction.X);
            return MathF.Abs(Vector2.Dot(point, across)) <= HalfWidth ? fraction : null;
        }
    }

    private readonly Gate _finish;
    private readonly Gate[] _checkpoints;
    private readonly List<(float Fraction, int Checkpoint)> _crossings = [];
    private int _nextCheckpoint;
    public RaceStatus Status { get; private set; } = new(0, 0, 0, null, false);

    public RaceSession(RaceDefinition definition, RaceSettings? settings = null)
    {
        settings ??= new();
        settings.Validate();
        Status = Status with { TotalLaps = settings.LapCount };
        _finish = Gate.From(definition.StartFinish);
        _checkpoints = definition.Checkpoints.Select(Gate.From).ToArray();
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
