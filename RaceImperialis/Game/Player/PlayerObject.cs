using WolfNET.Engine.Core;
using WolfNET.Engine.Input;
using WolfNET.Engine.Actors.Components;

namespace RaceImperialis.Game.Player;

public class PlayerObject : ObjectBase, IControlledActor
{
    private readonly IKeyboardInput _input;

    public CameraComponent Camera { get; }
    public WalkingController Controller { get; }
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

    public PlayerObject(IKeyboardInput input, IMouseInput mouse, INavigationMap map, ActorStartDefinition settings)
    {
        _input = input;
        InteractionRadius = settings.CollisionRadius;
        Camera = new CameraComponent(this);
        base.AddComponent(Camera);
        Controller = new WalkingController(input, mouse, map, Camera, settings, this);
        base.AddComponent(Controller);
    }
}
