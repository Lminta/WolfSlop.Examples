using System.Numerics;

namespace WolfNET.Game;

public readonly record struct RaceStatus(int CompletedLaps, double TotalMilliseconds, double LapMilliseconds,
    double? LastLapMilliseconds, bool Started);

/// <summary>Tracks ordered forward gate crossings and active simulation time for one scene.</summary>
public sealed class RaceSession
{
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
    public RaceStatus Status { get; private set; }

    public RaceSession(RaceDefinition definition)
    {
        _finish = Gate.From(definition.StartFinish);
        _checkpoints = definition.Checkpoints.Select(Gate.From).ToArray();
    }

    public void Update(Vector2 from, Vector2 to, float milliseconds)
    {
        if (!float.IsFinite(milliseconds) || milliseconds <= 0) return;
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
                if (!Status.Started) Status = Status with { Started = true };
                else if (_nextCheckpoint == _checkpoints.Length)
                    Status = Status with { CompletedLaps = Status.CompletedLaps + 1,
                        LastLapMilliseconds = Status.LapMilliseconds, LapMilliseconds = 0 };
                // An incomplete trip over the line cannot preserve old checkpoint progress.
                _nextCheckpoint = 0;
            }
            else if (Status.Started && crossing.Checkpoint == _nextCheckpoint) _nextCheckpoint++;
        }
        Advance((1 - previousFraction) * milliseconds);
    }

    private void Advance(double milliseconds)
    {
        if (Status.Started) Status = Status with { TotalMilliseconds = Status.TotalMilliseconds + milliseconds,
            LapMilliseconds = Status.LapMilliseconds + milliseconds };
    }
}
