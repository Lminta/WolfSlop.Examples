using System.Numerics;
using WolfNET.Game.Physics;
using WolfNET.Graphics;

namespace WolfNET.Game.Player;

/// <summary>Free look around the car's heading, with a collision-aware chase camera.</summary>
public sealed class CarCamera
{
    private const float CollisionRadius = .1f;
    private const float SweepStep = .025f;
    private float _idleSeconds;

    public float Yaw { get; private set; }
    public float Pitch { get; private set; }

    public void Update(float deltaSeconds, Vector2 mousePixels, CarSettings settings)
    {
        if (!float.IsFinite(deltaSeconds) || deltaSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        if (!float.IsFinite(mousePixels.X) || !float.IsFinite(mousePixels.Y))
            throw new ArgumentOutOfRangeException(nameof(mousePixels));

        if (mousePixels != Vector2.Zero)
        {
            // Wrap instead of clamping so the chase camera can orbit repeatedly.
            Yaw = MathF.IEEERemainder(Yaw - mousePixels.X * settings.MouseSensitivity, MathF.Tau);
            Pitch = Math.Clamp(Pitch - mousePixels.Y * settings.MouseSensitivity,
                -Radians(settings.CameraMaxPitchDegrees), Radians(settings.CameraMaxPitchDegrees));
            _idleSeconds = 0;
            return;
        }

        var previousIdle = _idleSeconds;
        _idleSeconds += deltaSeconds;
        // Only the part of this update after the delay contributes to centering.
        var recenterSeconds = MathF.Max(0, _idleSeconds - MathF.Max(previousIdle, settings.CameraRecenterDelay));
        var factor = MathF.Exp(-settings.CameraRecenterRate * recenterSeconds);
        Yaw *= factor;
        Pitch *= factor;
        if (MathF.Abs(Yaw) < .00001f) Yaw = 0;
        if (MathF.Abs(Pitch) < .00001f) Pitch = 0;
    }

    public Vector2 Direction(Transform body)
    {
        var heading = Vector2.Normalize(body.Rotation);
        var cosine = MathF.Cos(Yaw);
        var sine = MathF.Sin(Yaw);
        return new(heading.X * cosine - heading.Y * sine, heading.X * sine + heading.Y * cosine);
    }

    public Vector2 Plane(Transform body, float planeScale)
    {
        var direction = Direction(body);
        return new Vector2(direction.Y, -direction.X) * planeScale;
    }

    public CameraView View(Transform body, bool thirdPerson, RenderMap map, CarSettings settings)
    {
        if (!thirdPerson) return new(body.Position, 0, 0);

        var desiredDistance = settings.CameraFollowDistance * MathF.Cos(Pitch);
        var boom = -Direction(body) * desiredDistance;
        var fraction = 0f;
        if (desiredDistance > 0)
        {
            for (var next = SweepStep; next <= desiredDistance + SweepStep; next += SweepStep)
            {
                var step = MathF.Min(next / desiredDistance, 1);
                if (!Clear(body.Position + boom * step, map)) break;
                fraction = step;
                if (step == 1) break;
            }
        }
        var distance = desiredDistance * fraction;
        var elevation = Math.Clamp(settings.CameraElevation - MathF.Tan(Pitch) * distance, -.45f, .45f);
        return new(body.Position + boom * fraction, elevation, distance);
    }

    public void Reset()
    {
        Yaw = Pitch = _idleSeconds = 0;
    }

    private static float Radians(float degrees) => degrees * (MathF.PI / 180);

    private static bool Clear(Vector2 position, RenderMap map)
    {
        for (var x = (int)MathF.Floor(position.X - CollisionRadius); x <= (int)MathF.Floor(position.X + CollisionRadius); x++)
        for (var y = (int)MathF.Floor(position.Y - CollisionRadius); y <= (int)MathF.Floor(position.Y + CollisionRadius); y++)
        {
            if (x < 0 || y < 0 || x >= map.Width || y >= map.Height) return false;
            var cell = map.Cells[y * map.Width + x];
            if ((cell.Flags & (RenderCellFlags.Wall | RenderCellFlags.StopsRay)) == 0) continue;
            if (CircleGeometry.OverlapsCell(position, CollisionRadius, x, y)) return false;
        }
        return true;
    }
}
