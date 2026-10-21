using WolfNET.Game.Core;
using WolfNET.Game.InputEvents;
using WolfNET.Game.Player.Components;

namespace WolfNET.Game.Player;

public class PlayerObject : ObjectBase, IControlledActor
{
    private readonly IKeyboardInput _input;

    public CameraComponent Camera { get; }
    public PlayerController Controller { get; }
    public PlayerAnimation Animation { get; } = new();
    public bool InAir => Controller.InAir;
    public bool Crouching => Controller.Crouching;
    public int AnimationFrame => Animation.Frame;
    public float InteractionRadius { get; }
    public override void Update(float deltaTime)
    {
        var previous = Transform.Position;
        base.Update(deltaTime);
        Animation.Update(deltaTime, Transform.Position - previous, _input.KeyState, InAir, Crouching);
    }

    public PlayerObject(IKeyboardInput input, IMouseInput mouse, INavigationMap map, PlayerStartDefinition settings)
    {
        _input = input;
        InteractionRadius = settings.CollisionRadius;
        Camera = new CameraComponent(this);
        base.AddComponent(Camera);
        Controller = new PlayerController(input, mouse, map, Camera, settings, this);
        base.AddComponent(Controller);
    }
}
