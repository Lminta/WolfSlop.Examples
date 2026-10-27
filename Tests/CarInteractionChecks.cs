using System.Numerics;
using WolfNET.Engine.World;
using WolfNET.Engine.Graphics;

internal static class CarInteractionChecks
{
    public static async Task<int> RunAsync(HttpClient http)
    {
        var count = 0;
        void Check(bool condition, string message)
        {
            count++;
            if (!condition) throw new Exception(message);
        }
        const string destination = "maps/pine-loop.json";
        foreach (var name in new[] { "glassworks", "level1", "showcase" })
        {
            var map = await TileMap.LoadAsync(http, $"maps/{name}.json", 512, 512);
            var cars = map.RendererMap.Sprites.Where(sprite => sprite.InteractionTarget == destination).ToArray();
            Check(cars.Length > 0, $"{name} has no car entrance to Pine Loop");
            foreach (var car in cars)
            {
                var approaches = new[] { Vector2.UnitX, -Vector2.UnitX, Vector2.UnitY, -Vector2.UnitY }
                    .Select(offset => car.Position + offset)
                    .Where(position => map.NavigationMap.IsWalkable((int)position.X, (int)position.Y)).ToArray();
                Check(approaches.Length > 0 && approaches.Any(position =>
                    MapInteraction.FindTarget(map, position, car.Position - position) == destination),
                    $"{name} car cannot be interacted with from its surroundings");
            }
        }

        var loaded = await new GameSceneLoader(http, new RendererSettings()).LoadAsync("maps/showcase.json");
        var test = MakeMap();
        var input = new FakeKeyboard();
        using var scene = new GameScene(test, loaded.Character, input, new FakeMouse(), new RendererSettings());
        scene.Player.Transform = scene.Player.Transform with { Position = new(3.5f, 3.5f) };
        scene.Update(16);
        Check(scene.ConsumeMapRequest() == null, "Walking near a car requested a map without F");
        scene.Player.Transform = scene.Player.Transform with { Rotation = -Vector2.UnitX };
        input.ActionRequested = true; scene.Update(16);
        Check(scene.ConsumeMapRequest() == null, "F interacted with a car behind the player");
        scene.Player.Transform = scene.Player.Transform with { Rotation = Vector2.UnitX };
        input.ActionRequested = true; scene.Update(16);
        Check(scene.ConsumeMapRequest() == destination && scene.ConsumeMapRequest() == null,
            "Car interaction was not delivered exactly once");
        scene.Update(16);
        Check(scene.ConsumeMapRequest() == null, "Car interaction repeated without another press");
        scene.Player.Camera.ThirdPerson = true;
        input.ActionRequested = true; scene.Update(16);
        Check(scene.ConsumeMapRequest() == destination, "Third-person interaction ignored the player's position/facing");
        Check(MapInteraction.FindTarget(test, new(1.5f, 3.5f), Vector2.UnitX) == null,
            "Car interaction has unlimited reach");
        Check(MapInteraction.FindTarget(test, new(3.5f), Vector2.UnitY) == null,
            "F interacted while looking beside the car");
        Check(MapInteraction.FindTarget(test, new(2.5f, 3.5f), Vector2.UnitX) == destination,
            "Nearby car is out of interaction range");
        test.SetCell(3, 3, 1);
        Check(MapInteraction.FindTarget(test, new(2.5f, 3.5f), Vector2.UnitX) == null,
            "Car interaction passed through a wall");
        test.SetCell(3, 3, 4);
        Check(MapInteraction.FindTarget(test, new(2.5f, 3.5f), Vector2.UnitX) == null,
            "Car interaction passed through a closed door");
        scene.Player.Transform = scene.Player.Transform with { Position = new(2.5f, 3.5f), Rotation = Vector2.UnitX };
        input.ActionRequested = true; scene.Update(16);
        Check(test.Doors.Single().TargetOpen && scene.ConsumeMapRequest() == null,
            "Using a door also entered a car behind it");
        test.UpdateDoors(1000, scene.Player.Transform.Position, .15f);
        Check(MapInteraction.FindTarget(test, scene.Player.Transform.Position, Vector2.UnitX) == destination,
            "Open doors incorrectly block car interaction");
        test.SetCell(3, 3, 0);
        test.SetCell(3, 2, 1);
        Check(MapInteraction.FindTarget(test, new(3.5f, 2.5f), new(1, 1)) == null,
            "Interaction starts inside a blocked cell");

        foreach (var invalid in new[] { "../pine-loop.json", "https://example.com/map.json", "maps/../../secret.json", "maps/pine-loop.json\n" })
        {
            try { MakeMap(invalid).ValidateExample(); }
            catch (InvalidDataException) { count++; continue; }
            throw new Exception("Invalid interaction map path accepted");
        }
        Console.WriteLine($"PASS: {count} car interaction assertions (all maps, F, range, facing, occlusion, doors, and single-use requests).");
        return count;
    }

    private static TileMap MakeMap(string target = "maps/pine-loop.json")
    {
        var cells = Enumerable.Range(0, 8).Select(x => Enumerable.Range(0, 8)
            .Select(y => x == 0 || y == 0 || x == 7 || y == 7 ? 1 : 0).ToArray()).ToArray();
        cells[4][3] = 3;
        var objects = new Dictionary<int, MapObjectDefinition>
        {
            [0] = new PassageDefinition { Id = 0, RaycastTransparent = true, Collides = false },
            [1] = new WallDefinition { Id = 1, PassageId = 0, RaycastTransparent = false, Collides = true },
            [2] = new SpriteDefinition { Id = 2, PassageId = 0, RaycastTransparent = true, Collides = true,
                Texture = "textures/car.png", TextureLayer = 10, InteractionTarget = target },
            [3] = new SpriteGroupDefinition { Id = 3, PassageId = 0, RaycastTransparent = true, Collides = true,
                Sprites = [new() { SpriteId = 2, Offset = new() { X = 0, Y = 0 } }] },
            [4] = new DoorDefinition { Id = 4, PassageId = 0, RaycastTransparent = false, Collides = true, Axis = "x" }
        };
        return new TileMap(new MapDefinition { WorldMap = cells, Spawn = new() {
            X = 2.5f, Y = 2.5f, DirectionX = 1, DirectionY = 0, MoveSpeed = 3, RotSpeed = 3 } }, objects, 8, 8);
    }
}
