using System.Numerics;
using WolfNET.Game;
using WolfNET.Game.Player;

internal static class RacePropScaleChecks
{
    public static async Task<int> RunAsync(HttpClient http)
    {
        var loaded = await new GameSceneLoader(http, new()).LoadAsync("maps/pine-loop.json");
        using var scene = new GameScene(loaded.Map, loaded.Character, new FakeKeyboard(), new FakeMouse(), new());
        var player = (CarObject)scene.Player;
        var count = 0;
        void Check(bool value, string message) { count++; if (!value) throw new Exception(message); }
        var parkedCars = loaded.Map.RendererMap.Sprites.Where(sprite => sprite.Footprint != null).ToArray();
        Check(parkedCars.Length > 0, "Race map has no parked cars with physical dimensions.");
        foreach (var parked in parkedCars)
        {
            var angle = parked.RotationDegrees * MathF.PI / 180;
            player.Transform = new() { Position = parked.Position, Rotation = new(MathF.Cos(angle), MathF.Sin(angle)) };
            for (var frame = 0; frame < 8; frame++)
            {
                var bearing = angle + frame * MathF.Tau / 8;
                var observer = parked.Position + new Vector2(MathF.Cos(bearing), MathF.Sin(bearing)) * 5;
                var driven = loaded.Character.CreateSprite(player, observer);
                Check(Vector2.Distance(parked.ViewScale(observer), driven.ViewScale(observer)) < .0001f &&
                    MathF.Abs(parked.Elevation - driven.Elevation) < .0001f,
                    "Parked car size or grounding differs from the driven sports car.");
                Check(parked.MaximumViewWidth >= parked.ViewScale(observer).X - .0001f,
                    "Parked car could be culled while its directional plane remains visible.");
            }
        }
        Console.WriteLine($"PASS: {count} race prop scale assertions (parked cars, directional dimensions, grounding, and culling).");
        return count;
    }
}
