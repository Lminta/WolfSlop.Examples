using System.Numerics;

namespace WolfNET.Game;

[Flags]
public enum SpriteKind : uint
{
    Billboard = 0,
    PlaneNormalX = 1,
    PlaneNormalY = 2,
    DoorNormalX = PlaneNormalX,
    DoorNormalY = PlaneNormalY,
    Opaque = 4,
    FixedPlane = 8,
    DirectionalPlane = 16
}

// OwnerCell is the flattened cell index plus one, matching RayHit.hitCell.
public readonly record struct Sprite(Vector2 Position, uint TextureLayer, uint OwnerCell)
{
    public string? InteractionMap { get; init; }
    public bool RaceStartLight { get; init; }
    public bool SwapRedBlue { get; init; }
    public SpriteKind Kind { get; init; }
    public Vector2 Scale { get; init; } = Vector2.One;
    public float Elevation { get; init; }
    public SpriteAtlas? Atlas { get; init; }
    public SpriteAnimation? Animation { get; init; }
    public float RotationDegrees { get; init; }
    public bool FacingCamera { get; init; } = true;

    // Animated atlases use absolute scene seconds; ordinary atlases use the view direction.
    // World X is east, world Y is south; positive angles turn clockwise.
    public int ViewFrame(Vector2 observer, double elapsedSeconds = 0)
    {
        if (Animation is { } animation && Atlas is { } animatedAtlas)
        {
            if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            var cycleSeconds = animatedAtlas.Frames.Length / (double)animation.FramesPerSecond;
            var cyclePosition = (elapsedSeconds % cycleSeconds + animation.PhaseSeconds % cycleSeconds) % cycleSeconds;
            return Math.Min(animatedAtlas.Frames.Length - 1, (int)(cyclePosition * animation.FramesPerSecond));
        }
        if (Atlas is not { Directions: > 1 } atlas) return 0;
        var relative = observer - Position;
        if (relative.LengthSquared() < 0.00000001f) return 0;
        var turns = (MathF.Atan2(relative.Y, relative.X) - RotationDegrees * MathF.PI / 180) / MathF.Tau;
        var frame = (int)MathF.Floor(turns * atlas.Directions + 0.5f);
        return (frame % atlas.Directions + atlas.Directions) % atlas.Directions;
    }

    public uint ViewLayer(Vector2 observer, double elapsedSeconds = 0) => TextureLayer + (uint)ViewFrame(observer, elapsedSeconds);

    // A non-billboard atlas holds its quad normal at the selected view's center.
    // The geometry word keeps flags in its low 16 bits and a world-space normal
    // angle (one turn = 65536) in its high 16 bits, preserving the GPU record size.
    public uint ViewKind(Vector2 observer)
        => ViewKind(ViewFrame(observer));

    internal uint ViewKind(int frame)
    {
        if (FacingCamera || Atlas is not { Directions: > 1 } atlas || Kind != SpriteKind.Billboard)
            return (uint)Kind | (SwapRedBlue ? 32u : 0u);
        var degrees = (RotationDegrees % 360 + frame * (360f / atlas.Directions)) % 360;
        if (degrees < 0) degrees += 360;
        var angle = (uint)MathF.Round(degrees / 360 * 65536) & 0xffffu;
        return (uint)(SpriteKind.FixedPlane | SpriteKind.DirectionalPlane) | (angle << 16) | (SwapRedBlue ? 32u : 0u);
    }

    public Vector2 ViewScale(Vector2 observer, double elapsedSeconds = 0)
    {
        if (Atlas is not { } atlas) return Scale;
        var frame = atlas.Frames[ViewFrame(observer, elapsedSeconds)];
        return Scale * new Vector2((float)frame.Width / frame.Height, 1);
    }
}
