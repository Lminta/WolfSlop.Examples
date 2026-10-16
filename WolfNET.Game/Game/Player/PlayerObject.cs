using WolfNET.Game.Core;
using WolfNET.Game.Player.Components;

namespace WolfNET.Game.Player;

public class PlayerObject : ObjectBase
{
    public CameraComponent Camera { get; }
    public PlayerObject(Wolf game) : base(game)
    {
        Camera = new CameraComponent(this);
        base.AddComponent(Camera);
        base.AddComponent(new PlayerController(game.KeyTracker, game.MouseTracker, game.Map.NavigationMap, Camera, game.Map.PlayerStart, this));
    }
}
