using System.Numerics;
using WolfNET.Game;

namespace WolfNET.Graphics;

// Values are part of the first-person.wgsl uniform contract.
public enum FirstPersonViewKind { None = 0, Hands = 1, Cockpit = 2 }

/// <summary>Simulation state for screen-space sprites, composited after the world.</summary>
public readonly record struct FirstPersonView(
    FirstPersonViewKind Kind, Vector4 HandMotion = default,
    bool InAir = false, bool Crouching = false, float WheelRotation = 0);

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
    public FirstPersonView FirstPerson { get; init; }
}
