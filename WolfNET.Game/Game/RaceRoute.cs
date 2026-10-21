using System.Numerics;

namespace WolfNET.Game;

/// <summary>Continuous distance along the authored circuit, independent of car speed.</summary>
public sealed class RaceRoute
{
    private readonly Vector2[] _points;
    private readonly float[] _distance;
    public float Length => _distance[^1];

    public RaceRoute(RaceMinimapDefinition definition)
    {
        _points = definition.Route.Select(p => new Vector2(p[0], p[1])).ToArray();
        _distance = new float[_points.Length];
        for (var i = 1; i < _points.Length; i++) _distance[i] = _distance[i - 1] + Vector2.Distance(_points[i - 1], _points[i]);
        if (!float.IsFinite(Length) || Length <= 0) throw new InvalidDataException("A racing line needs positive finite length.");
    }

    public Vector2 Point(float distance)
    {
        distance = (distance % Length + Length) % Length;
        var i = Array.BinarySearch(_distance, distance);
        if (i >= 0) return _points[i];
        i = Math.Clamp(~i, 1, _points.Length - 1);
        return Vector2.Lerp(_points[i - 1], _points[i], (distance - _distance[i - 1]) / (_distance[i] - _distance[i - 1]));
    }

    public float Project(Vector2 position, float? near = null)
    {
        var best = float.PositiveInfinity;
        var result = 0f;
        var normalized = near is { } n ? (n % Length + Length) % Length : 0;
        void Search(bool local)
        {
            var center = Array.BinarySearch(_distance, normalized);
            if (center < 0) center = ~center;
            var count = local ? Math.Min(81, _points.Length - 1) : _points.Length - 1;
            for (var step = 0; step < count; step++)
            {
                var i = local ? ((center - count / 2 + step + _points.Length - 2) % (_points.Length - 1)) + 1 : step + 1;
                var edge = _points[i] - _points[i - 1];
                if (edge.LengthSquared() == 0) continue;
                var amount = Math.Clamp(Vector2.Dot(position - _points[i - 1], edge) / edge.LengthSquared(), 0, 1);
                var point = _points[i - 1] + edge * amount;
                var squared = Vector2.DistanceSquared(point, position);
                if (squared >= best) continue;
                best = squared;
                result = _distance[i - 1] + edge.Length() * amount;
            }
        }
        Search(near != null);
        if (best > 36) Search(false); // Allows the authored shortcut to rejoin its later route segment.
        if (near is { } previous) result += MathF.Round((previous - result) / Length) * Length;
        return result;
    }
}

/// <summary>Arrival history indexed by unwrapped race distance; laps cannot be mixed.</summary>
public sealed class RaceProgress(RaceRoute route, Vector2 start)
{
    private readonly float _origin = route.Project(start);
    private readonly List<(float Distance, double Time)> _history = [(0, 0)];
    private float _projected = route.Project(start);
    public float Distance { get; private set; }

    public void Update(Vector2 position, double milliseconds)
    {
        _projected = route.Project(position, _projected);
        var progress = MathF.Max(0, _projected - _origin);
        // Preserve first arrival times when reversing or waiting at a location.
        Distance = progress;
        if (Distance - _history[^1].Distance >= .25f) _history.Add((Distance, milliseconds));
    }

    private double Arrival(float distance, double now)
    {
        var low = 0; var high = _history.Count;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (_history[mid].Distance < distance) low = mid + 1; else high = mid;
        }
        if (low == 0) return _history[0].Time;
        var before = _history[low - 1];
        var after = low < _history.Count ? _history[low] : (Distance, now);
        return after.Distance <= before.Distance ? before.Time : before.Time +
            (after.Item2 - before.Time) * Math.Clamp((distance - before.Distance) / (after.Distance - before.Distance), 0, 1);
    }

    public static double Gap(RaceProgress player, RaceProgress opponent, double now)
    {
        if (MathF.Abs(player.Distance - opponent.Distance) < .01f) return 0;
        return player.Distance > opponent.Distance
            ? Math.Max(0, now - player.Arrival(opponent.Distance, now))
            : -Math.Max(0, now - opponent.Arrival(player.Distance, now));
    }
}
