using System.Numerics;

namespace WolfNET.Game;

/// <summary>Continuous distance along the authored circuit, independent of car speed.</summary>
public sealed class RaceRoute
{
    private readonly Vector2[] _points;
    private readonly float[] _distance;
    private readonly int[] _segments;
    private readonly Node[] _nodes;
    private int _nodeCount;
    private readonly record struct Node(Vector2 Min, Vector2 Max, int Start, int Count,
        int FirstSegment, int LastSegment, int Left = -1, int Right = -1);
    private struct Nearest
    {
        public float Squared, Distance;
        public int Rank;
    }
    public float Length => _distance[^1];

    public RaceRoute(RaceMinimapDefinition definition)
    {
        _points = definition.Route.Select(p => new Vector2(p[0], p[1])).ToArray();
        _distance = new float[_points.Length];
        for (var i = 1; i < _points.Length; i++) _distance[i] = _distance[i - 1] + Vector2.Distance(_points[i - 1], _points[i]);
        if (!float.IsFinite(Length) || Length <= 0) throw new InvalidDataException("A racing line needs positive finite length.");
        _segments = Enumerable.Range(0, _points.Length - 1).ToArray();
        _nodes = new Node[_segments.Length * 2];
        Build(0, _segments.Length);
    }

    // Immutable segment bounds let shortcut/off-road queries find the exact nearest
    // segment without repeatedly scanning the whole circuit in WebAssembly.
    private int Build(int start, int count)
    {
        var min = new Vector2(float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity);
        var first = int.MaxValue;
        var last = 0;
        for (var i = start; i < start + count; i++)
        {
            var segment = _segments[i];
            min = Vector2.Min(min, Vector2.Min(_points[segment], _points[segment + 1]));
            max = Vector2.Max(max, Vector2.Max(_points[segment], _points[segment + 1]));
            first = Math.Min(first, segment);
            last = Math.Max(last, segment);
        }
        var index = _nodeCount++;
        _nodes[index] = new(min, max, start, count, first, last);
        if (count <= 8) return index;
        var horizontal = max.X - min.X >= max.Y - min.Y;
        Array.Sort(_segments, start, count, Comparer<int>.Create((a, b) =>
        {
            var centerA = _points[a] + _points[a + 1];
            var centerB = _points[b] + _points[b + 1];
            return (horizontal ? centerA.X : centerA.Y).CompareTo(horizontal ? centerB.X : centerB.Y);
        }));
        var left = Build(start, count / 2);
        var right = Build(start + count / 2, count - count / 2);
        _nodes[index] = _nodes[index] with { Left = left, Right = right };
        return index;
    }

    private float BoundsDistance(int index, Vector2 position)
    {
        ref readonly var node = ref _nodes[index];
        return Vector2.DistanceSquared(position, Vector2.Clamp(position, node.Min, node.Max));
    }

    private void Search(int index, Vector2 position, int start, int count, ref Nearest best)
    {
        ref readonly var node = ref _nodes[index];
        var end = start + count;
        var total = _segments.Length;
        if (end <= total
            ? node.LastSegment < start || node.FirstSegment >= end
            : node.LastSegment < start && node.FirstSegment >= end - total) return;
        if (node.Left >= 0)
        {
            var left = node.Left;
            var right = node.Right;
            var leftDistance = BoundsDistance(left, position);
            var rightDistance = BoundsDistance(right, position);
            if (rightDistance < leftDistance)
            {
                (left, right) = (right, left);
                (leftDistance, rightDistance) = (rightDistance, leftDistance);
            }
            if (leftDistance <= best.Squared) Search(left, position, start, count, ref best);
            if (rightDistance <= best.Squared) Search(right, position, start, count, ref best);
            return;
        }
        for (var step = node.Start; step < node.Start + node.Count; step++)
        {
            var segment = _segments[step];
            var rank = (segment - start + total) % total;
            if (rank >= count) continue;
            var edge = _points[segment + 1] - _points[segment];
            var squaredLength = edge.LengthSquared();
            if (squaredLength == 0) continue;
            var amount = Math.Clamp(Vector2.Dot(position - _points[segment], edge) / squaredLength, 0, 1);
            var squared = Vector2.DistanceSquared(_points[segment] + edge * amount, position);
            if (squared > best.Squared || squared == best.Squared && rank >= best.Rank) continue;
            best.Squared = squared;
            best.Rank = rank;
            best.Distance = _distance[segment] + edge.Length() * amount;
        }
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
        var best = new Nearest { Squared = float.PositiveInfinity, Rank = int.MaxValue };
        if (near is { } n)
        {
            var normalized = (n % Length + Length) % Length;
            var center = Array.BinarySearch(_distance, normalized);
            if (center < 0) center = ~center;
            var count = Math.Min(81, _segments.Length);
            var start = (center - count / 2 - 1 + _segments.Length) % _segments.Length;
            Search(0, position, start, count, ref best);
            if (best.Squared > 36)
            {
                // Keep the local winner on an exact tie, as the original scan did.
                best.Rank = -1;
                Search(0, position, 0, _segments.Length, ref best);
            }
        }
        else Search(0, position, 0, _segments.Length, ref best);
        var result = best.Distance;
        if (near is { } previous) result += MathF.Round((previous - result) / Length) * Length;
        return result;
    }
}
