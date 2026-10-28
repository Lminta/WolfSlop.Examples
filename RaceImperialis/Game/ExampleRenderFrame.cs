using System.Numerics;
using WolfNET.Engine.World;
using WolfNET.Engine.Graphics;

namespace RaceImperialis.Game;

// Presentation state interpreted by ExampleHud.
public enum FirstPersonViewKind { None = 0, Hands = 1, Cockpit = 2 }

/// <summary>Simulation state for screen-space sprites, composited after the world.</summary>
public readonly record struct FirstPersonView(
    FirstPersonViewKind Kind, Vector4 HandMotion = default,
    bool InAir = false, bool Crouching = false, float WheelRotation = 0);

public readonly record struct ExampleRenderFrame(
    Vector2 Position,
    Vector2 Direction,
    Vector2 Plane,
    RenderMap Map,
    float Pitch,
    float Elevation,
    Sprite? Character)
{
    public Sprite? Opponent { get; init; }
    public Sprite? SecondOpponent { get; init; }
    public FirstPersonView FirstPerson { get; init; }
    public float AspectRatio { get; init; } = 16f / 9;

    public static implicit operator RenderFrame(ExampleRenderFrame frame)
    {
        var sprites = new List<Sprite>(3);
        if (frame.Character is { } character) sprites.Add(character);
        if (frame.Opponent is { } opponent) sprites.Add(opponent);
        if (frame.SecondOpponent is { } second) sprites.Add(second);
        return new(frame.Position, frame.Direction, frame.Plane, frame.Map, frame.Pitch, frame.Elevation)
        {
            DynamicSprites = sprites,
            ShowMinimap = frame.Map.GetRaceMinimap() == null,
            CullSpritesOutsideView = frame.Map.GetRaceMinimap() != null
        };
    }
}
