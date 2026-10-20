using WolfNET.Game.Core;
using WolfNET.Game.InputEvents;
using WolfNET.Game.Player.Components;

namespace WolfNET.Game.Player;

public class PlayerObject : ObjectBase
{
    private readonly IKeyboardInput _input;

    public CameraComponent Camera { get; }
    public PlayerController Controller { get; }
    public PlayerAnimation Animation { get; } = new();
    public bool InAir => Controller.InAir;
    public bool Crouching => Controller.Crouching;
    public override void Update(float deltaTime)
    {
        var previous = Transform.Position;
        base.Update(deltaTime);
        Animation.Update(deltaTime, Transform.Position - previous, _input.KeyState, InAir, Crouching);
    }

    public PlayerObject(IKeyboardInput input, IMouseInput mouse, INavigationMap map, PlayerStartDefinition settings)
    {
        _input = input;
        Camera = new CameraComponent(this);
        base.AddComponent(Camera);
        Controller = new PlayerController(input, mouse, map, Camera, settings, this);
        base.AddComponent(Controller);
    }
}
