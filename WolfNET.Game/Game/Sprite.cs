using System.Numerics;

namespace WolfNET.Game;

[Flags]
public enum SpriteKind : uint { Billboard = 0, DoorNormalX = 1, DoorNormalY = 2, Opaque = 4 }

// OwnerCell is the flattened cell index plus one, matching RayHit.hitCell.
public readonly record struct Sprite(Vector2 Position, uint TextureLayer, uint OwnerCell)
{
    public SpriteKind Kind { get; init; }
    public Vector2 Scale { get; init; } = Vector2.One;
    public float Elevation { get; init; }
    public SpriteAtlas? Atlas { get; init; }
    public float RotationDegrees { get; init; }

    // World X is east, world Y is south; positive angles turn clockwise.
    public int ViewFrame(Vector2 observer)
    {
        if (Atlas is not { Directions: > 1 } atlas) return 0;
        var relative = observer - Position;
        if (relative.LengthSquared() < 0.00000001f) return 0;
        var turns = (MathF.Atan2(relative.Y, relative.X) - RotationDegrees * MathF.PI / 180) / MathF.Tau;
        var frame = (int)MathF.Floor(turns * atlas.Directions + 0.5f);
        return (frame % atlas.Directions + atlas.Directions) % atlas.Directions;
    }

    public uint ViewLayer(Vector2 observer) => TextureLayer + (uint)ViewFrame(observer);
    public Vector2 ViewScale(Vector2 observer)
    {
        if (Atlas is not { } atlas) return Scale;
        var frame = atlas.Frames[ViewFrame(observer)];
        return Scale * new Vector2((float)frame.Width / frame.Height, 1);
    }
}
