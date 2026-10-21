using System.Numerics;
using WolfNET.Game;

namespace WolfNET.Graphics;

public readonly record struct RenderFrame(
    Vector2 Position,
    Vector2 Direction,
    Vector2 Plane,
    RenderMap Map,
    float Pitch,
    float Elevation,
    Sprite? Character)
{
    public Sprite? Opponent { get; init; }
}
