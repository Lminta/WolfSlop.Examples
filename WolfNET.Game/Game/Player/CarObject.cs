using System.Numerics;
using WolfNET.Game.Core;
using WolfNET.Game.InputEvents;
using WolfNET.Game.Player.Components;

namespace WolfNET.Game.Player;

public sealed class CarObject : ObjectBase, IControlledActor
{
    private readonly IKeyboardInput _input;
    private readonly IMouseInput _mouse;
    public CarSettings Settings { get; }
    public CameraComponent Camera { get; }
    public CarCamera Look { get; } = new();
    public CarController Controller { get; }
    public bool InAir => false;
    public bool Crouching => false;
    public int AnimationFrame => 0;
    public float InteractionRadius => MathF.Sqrt(Settings.Length * Settings.Length + Settings.Width * Settings.Width) * .5f;

    public CarObject(IKeyboardInput input, IMouseInput mouse, NavigationMap map, CarSettings settings)
    {
        _input = input;
        _mouse = mouse;
        Settings = settings;
        Camera = new CameraComponent(this);
        Controller = new CarController(this, input, map,
            position => map.GetSurface((int)MathF.Floor(position.X), (int)MathF.Floor(position.Y)), settings);
        AddComponent(Controller);
    }

    public override void Update(float deltaTime)
    {
        if (_input.ConsumeViewToggle()) Camera.ThirdPerson = !Camera.ThirdPerson;
        Look.Update(deltaTime / 1000, _mouse.ConsumeMovement(), Settings);
        base.Update(deltaTime);
    }
}
