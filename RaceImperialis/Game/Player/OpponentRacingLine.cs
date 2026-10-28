using System.Numerics;
using WolfNET.Engine.Physics;

namespace RaceImperialis.Game.Player;

/// <summary>A smooth, car-sized corridor through the rasterized minimap route.</summary>
internal sealed class OpponentRacingLine
{
    private readonly Vector2[] _points;
    private readonly float[] _curvature;
    private readonly NavigationMap _map;
    private readonly RaceRoute _route;
    private readonly OrientedBoxCollider _box;
    private readonly CarSettings _car;
    private readonly float _spacing;
    private readonly float _length;

    public OpponentRacingLine(RaceRoute route, NavigationMap map, CarSettings car, OpponentRacingLine? source = null)
    {
        _map = map; _car = car; _route = route;
        _box = new(map, car.Length + .12f, car.Width + .12f);
        _length = route.Length;
        // Geometry depends on the shared scene and footprint, not rival speed or
        // acceleration. Keep each rival's own settings for runtime speed planning.
        if (source != null && ReferenceEquals(source._route, route) && ReferenceEquals(source._map, map) &&
            source._car.Length == car.Length && source._car.Width == car.Width)
        {
            _points = source._points;
            _curvature = source._curvature;
            _spacing = source._spacing;
            return;
        }
        var count = (int)MathF.Ceiling(route.Length);
        _spacing = route.Length / count;
        _points = new Vector2[count];
        _curvature = new float[count];
        Span<bool> open = stackalloc bool[31];
        for (var i = 0; i < count; i++)
        {
            var distance = i * _spacing;
            var point = (route.Point(distance - 1.5f) + route.Point(distance) * 2 + route.Point(distance + 1.5f)) / 4;
            var direction = Vector2.Normalize(route.Point(distance + 3) - route.Point(distance - 3));
            var side = new Vector2(-direction.Y, direction.X);
            var best = float.PositiveInfinity;
            var chosen = point;
            for (var lane = 0; lane < open.Length; lane++)
                open[lane] = OnRoad(point + side * ((lane - 15) * .15f), direction);
            // Score a corridor in one pass instead of re-walking its neighbors.
            for (var begin = 0; begin < open.Length;)
            {
                if (!open[begin]) { begin++; continue; }
                var end = begin + 1;
                while (end < open.Length && open[end]) end++;
                for (var lane = begin; lane < end; lane++)
                {
                    var offset = (lane - 15) * .15f;
                    var left = Math.Min(lane - begin, 13);
                    var right = Math.Min(end - lane - 1, 13);
                    var score = MathF.Abs(left - right) * .15f + MathF.Abs(offset) * .15f -
                        Math.Min(left, right) * .015f;
                    if (score >= best) continue;
                    best = score;
                    chosen = point + side * offset;
                }
                begin = end;
            }
            _points[i] = chosen;
        }
        // Remove the raster route's stair steps before estimating curvature, so
        // harmless cell edges do not become artificial braking points.
        // Keep the full footprint clear of verges and barriers while smoothing.
        for (var pass = 0; pass < 20; pass++)
        {
            var previous = (Vector2[])_points.Clone();
            for (var i = 0; i < count; i++)
            {
                var a = previous[(i + count - 1) % count];
                var b = previous[(i + 1) % count];
                var smoothed = (a + previous[i] * 2 + b) / 4;
                if (OnRoad(smoothed, Vector2.Normalize(b - a))) _points[i] = smoothed;
            }
        }
        for (var i = 0; i < count; i++)
        {
            var a = Point(i * _spacing - 3);
            var b = _points[i];
            var c = Point(i * _spacing + 3);
            var ab = b - a; var bc = c - b;
            _curvature[i] = 2 * (ab.X * bc.Y - ab.Y * bc.X) /
                MathF.Max(.01f, ab.Length() * bc.Length() * Vector2.Distance(a, c));
        }
    }

    public Vector2 Point(float distance)
    {
        var sample = ((distance % _length + _length) % _length) / _spacing;
        var index = (int)sample;
        return Vector2.Lerp(_points[index % _points.Length], _points[(index + 1) % _points.Length], sample - index);
    }

    public Vector2 Direction(float distance) => Vector2.Normalize(Point(distance + 2) - Point(distance - 2));

    public float Curvature(float distance)
    {
        var sample = ((distance % _length + _length) % _length) / _spacing;
        var index = (int)sample;
        return float.Lerp(_curvature[index % _points.Length], _curvature[(index + 1) % _points.Length], sample - index);
    }

    public float Speed(float distance, float cornerGripFraction, float cornerSpeed)
    {
        var speed = float.PositiveInfinity;
        // The grip margins are calibrated to the default corner-speed setting.
        var cornerScale = cornerSpeed / 13;
        var lateralLimit = _car.Asphalt.MaxLateralAcceleration * cornerGripFraction * cornerScale * cornerScale;
        lateralLimit = MathF.Min(lateralLimit, _car.Asphalt.MaxLateralAcceleration * .98f);
        for (float ahead = 0; ahead <= 36; ahead += 2)
        {
            var index = (int)(((distance + ahead) % _length + _length) % _length / _spacing) % _points.Length;
            var curve = MathF.Max(.001f, MathF.Abs(_curvature[index]));
            var corner = MathF.Sqrt(lateralLimit / curve);
            speed = MathF.Min(speed, MathF.Sqrt(corner * corner + 2 * _car.Asphalt.BrakeDeceleration * .95f * MathF.Max(0, ahead - 1)));
        }
        return speed;
    }

    public bool CanManeuver(float distance, float preview = 16, float maximumCurvature = .035f)
    {
        for (float ahead = 0; ahead <= preview; ahead += 2)
        {
            var index = (int)(((distance + ahead) % _length + _length) % _length / _spacing) % _points.Length;
            if (MathF.Abs(_curvature[index]) > maximumCurvature) return false;
        }
        return true;
    }

    public bool LaneClear(float distance, float offset, float preview)
    {
        for (float ahead = 0; ahead <= preview; ahead += 1)
        {
            var direction = Vector2.Normalize(Point(distance + ahead + 2) - Point(distance + ahead - 2));
            var point = Point(distance + ahead) + new Vector2(-direction.Y, direction.X) * offset;
            if (!OnRoad(point, direction)) return false;
        }
        return true;
    }

    public bool RoadClear(Vector2 from, Vector2 to, Vector2 startDirection, Vector2 endDirection)
    {
        var steps = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(from, to) * 2));
        for (var i = 1; i <= steps; i++)
        {
            var amount = i / (float)steps;
            var direction = Vector2.Normalize(Vector2.Lerp(startDirection, endDirection, amount));
            if (!OnRoad(Vector2.Lerp(from, to, amount), direction)) return false;
        }
        return true;
    }

    public float ObstacleSpeed(CarObject actor)
    {
        var speed = actor.Controller.Speed;
        if (speed < 1) return float.PositiveInfinity;
        var body = actor.Transform;
        // Sweep a short arc using actual velocity and yaw, so a barrier beside a bend
        // is checked where the car is going, rather than only at its route target.
        for (float distance = .75f; distance <= MathF.Min(14, speed * .8f + 2); distance += .75f)
        {
            var time = distance / speed;
            var angle = actor.Controller.AngularVelocity * time;
            var cosine = MathF.Cos(angle); var sine = MathF.Sin(angle);
            var direction = new Vector2(body.Rotation.X * cosine - body.Rotation.Y * sine,
                body.Rotation.X * sine + body.Rotation.Y * cosine);
            var velocity = actor.Controller.Velocity;
            var point = body.Position + velocity * time + new Vector2(-velocity.Y, velocity.X) * (angle * time * .5f);
            if (!_box.CanOccupy(point, direction))
                return MathF.Sqrt(2 * _car.Asphalt.BrakeDeceleration * .65f * MathF.Max(.1f, distance - .75f));
        }
        return float.PositiveInfinity;
    }

    public bool OnRoad(Vector2 point, Vector2 direction)
    {
        // Every footprint covers its center cell; off-road centers need no SAT checks.
        if (_map.GetSurface((int)MathF.Floor(point.X), (int)MathF.Floor(point.Y)) != "asphalt") return false;
        if (!_box.CanOccupy(point, direction)) return false;
        var extent = new Vector2(_car.Length * .5f * MathF.Abs(direction.X) + _car.Width * .5f * MathF.Abs(direction.Y),
            _car.Length * .5f * MathF.Abs(direction.Y) + _car.Width * .5f * MathF.Abs(direction.X)) + new Vector2(.08f);
        for (var x = (int)MathF.Floor(point.X - extent.X); x <= (int)MathF.Floor(point.X + extent.X); x++)
        for (var y = (int)MathF.Floor(point.Y - extent.Y); y <= (int)MathF.Floor(point.Y + extent.Y); y++)
            // Asphalt cells cannot reject the footprint. Test overlap only at verges.
            if (_map.GetSurface(x, y) != "asphalt" && _box.OverlapsCell(point, direction, x, y)) return false;
        return true;
    }
}
