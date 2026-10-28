using System.Numerics;

namespace RaceImperialis.Game;

public static class RaceMinimapProjection
{
    public const float ViewWidth = 120;
    public static readonly Vector2 PlayerAnchor = new(50, 70);

    // Project world coordinates into a 100-square SVG viewport. Screen-right
    // follows the game's camera plane, including its horizontal handedness.
    public static Matrix3x2 Create(Vector2 position, Vector2 direction)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) ||
            !float.IsFinite(direction.LengthSquared()) || direction.LengthSquared() <= 0)
            throw new ArgumentException("Minimap pose must have a finite position and a nonzero finite direction.");
        var forward = Vector2.Normalize(direction);
        var right = new Vector2(forward.Y, -forward.X);
        const float scale = 100 / ViewWidth;
        return new Matrix3x2(right.X * scale, -forward.X * scale,
            right.Y * scale, -forward.Y * scale,
            PlayerAnchor.X - Vector2.Dot(position, right) * scale,
            PlayerAnchor.Y + Vector2.Dot(position, forward) * scale);
    }
}
