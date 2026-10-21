using System.Numerics;
using WolfNET.Game.Core;
using WolfNET.Game.InputEvents;
using WolfNET.Game.Player;
using WolfNET.Graphics;

namespace WolfNET.Game;

/// <summary>Owns simulation objects for one map. Browser and GPU lifetimes belong to the host.</summary>
public sealed class GameScene : IDisposable
{
    private readonly ObjectCollection _objects = new();
    private readonly IKeyboardInput _input;
    private string? _requestedMap;
    public IMap Map { get; }
    public PlayerObject Player { get; }
    public CharacterAppearance Character { get; }

    public GameScene(IMap map, CharacterAppearance character, IKeyboardInput input, IMouseInput mouse,
        RendererSettings settings)
    {
        Map = map;
        Character = character;
        _input = input;
        var start = map.PlayerStart;
        var direction = Vector2.Normalize(new(start.DirectionX, start.DirectionY));
        Player = new PlayerObject(input, mouse, map.NavigationMap, start)
        {
            Transform = new Transform { Position = new(start.X, start.Y), Rotation = direction }
        };
        Player.Camera.Plane = new Vector2(direction.Y, -direction.X) * settings.CameraPlaneScale;
        _objects.Add(Player);
    }

    public void Update(float deltaMilliseconds)
    {
        _objects.Update(deltaMilliseconds);
        var transform = Player.Transform;
        if (_input.ConsumeAction() && !Map.Interact(transform.Position, transform.Rotation, Map.PlayerStart.CollisionRadius))
            _requestedMap ??= MapInteraction.FindDestination(Map, transform.Position, transform.Rotation);
        Map.UpdateDoors(deltaMilliseconds, transform.Position, Map.PlayerStart.CollisionRadius);
    }

    public string? ConsumeMapRequest()
    {
        var requested = _requestedMap;
        _requestedMap = null;
        return requested;
    }

    public RenderFrame CaptureFrame()
    {
        var camera = Player.Camera;
        var map = Map.RendererMap;
        var view = camera.View(map);
        Sprite? character = camera.ThirdPerson && view.Distance > .35f
            ? Character.Sprite(Player.Animation.Frame, Player.Transform.Position, Player.InAir ? camera.Elevation : 0)
            : null;
        return new(view.Position, camera.Transform.Rotation, camera.Plane, map, camera.Pitch, view.Elevation, character);
    }

    public void Dispose() => _objects.Dispose();
}
