using WolfNET.Game.Core;
using WolfNET.Game.Player.Components;

namespace WolfNET.Game.Player;

public class PlayerObject : ObjectBase
{
    public CameraComponent Camera { get; }
    public PlayerController Controller { get; }
    public PlayerAnimation Animation { get; } = new();
    public bool InAir => Controller.InAir;
    public bool Crouching => Controller.Crouching;
    public override void Update(float deltaTime)
    {
        var previous = Transform.Position;
        base.Update(deltaTime);
        Animation.Update(deltaTime, Transform.Position - previous, Game.KeyTracker.KeyState, InAir, Crouching);
    }

    public PlayerObject(Wolf game) : base(game)
    {
        Camera = new CameraComponent(this);
        base.AddComponent(Camera);
        Controller = new PlayerController(game.KeyTracker, game.MouseTracker, game.Map.NavigationMap, Camera, game.Map.PlayerStart, this);
        base.AddComponent(Controller);
    }
}
