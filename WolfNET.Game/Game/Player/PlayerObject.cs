using WolfNET.Game.Core;
using WolfNET.Game.Player.Components;

namespace WolfNET.Game.Player;

public class PlayerObject : ObjectBase
{
    public CameraComponent Camera { get; }
    public PlayerController Controller { get; }
    public bool InAir => Controller.InAir;
    public bool Crouching => Controller.Crouching;
    public PlayerObject(Wolf game) : base(game)
    {
        Camera = new CameraComponent(this);
        base.AddComponent(Camera);
        Controller = new PlayerController(game.KeyTracker, game.MouseTracker, game.Map.NavigationMap, Camera, game.Map.PlayerStart, this);
        base.AddComponent(Controller);
    }
}
