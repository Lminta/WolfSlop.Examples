using WolfNET.Game.InputEvents;

namespace WolfNET.Game;

public class PlayerObject : ObjectBase
{
    public CameraComponent CameraComponent { get; }
    public PlayerObject(Wolf game) : base(game)
    {
        CameraComponent = new CameraComponent(this);
        base.AddComponent(CameraComponent);
        base.AddComponent(new PlayerController(game.KeyTracker, game.Map, CameraComponent, this));
    }
}