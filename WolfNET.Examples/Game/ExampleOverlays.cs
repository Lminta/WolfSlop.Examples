using System.Numerics;
using WolfNET.Engine.Graphics;

namespace WolfNET.Examples.Game;

/// <summary>Hands and dashboard artwork/layout belong to this example.</summary>
public static class ExampleOverlays
{
    public static string[] Textures(bool car) => car
        ? ["textures/race/cockpit.png", "textures/race/steering-wheel.png"]
        : ["textures/characters/first-person-hands.png"];

    public static IReadOnlyList<ScreenOverlay> Compose(FirstPersonView view, float aspect)
    {
        if (view.Kind == FirstPersonViewKind.None) return [];
        if (view.Kind == FirstPersonViewKind.Cockpit)
            return [new(0, Vector2.Zero, Vector2.One),
                new(1, new(.42f, .86f), new(.28f, .28f * aspect), new(.5f), view.WheelRotation)];
        var hands = new ScreenOverlay[2];
        for (var i = 0; i < hands.Length; i++)
        {
            var side = i == 1 ? 1f : -1f;
            var swing = view.HandMotion.X * side;
            var offset = new Vector2(swing * .007f - view.HandMotion.Z * .012f,
                swing * .018f + view.HandMotion.Y * .007f + (view.InAir ? -.04f : view.Crouching ? .03f : 0));
            var degrees = swing * 3 + view.HandMotion.Z * 2 + (view.InAir ? side * -5 : 0);
            if (i == 1)
            {
                offset -= view.HandMotion.W * new Vector2(.05f, .08f);
                degrees -= view.HandMotion.W * 12;
            }
            hands[i] = new(0, new Vector2(.25f + .5f * i, 1.28f) + offset,
                new(.5f, aspect * 1024f / 1536), new(.5f, 1), degrees * MathF.PI / 180)
                { UV = new(.5f * i, 0, .5f, 1) };
        }
        return hands;
    }
}
