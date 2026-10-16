using System.Numerics;

namespace WolfNET.Game;

// OwnerCell is the flattened cell index plus one, matching RayHit.hitCell.
public readonly record struct Sprite(Vector2 Position, uint TextureLayer, uint OwnerCell);
