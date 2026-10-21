using WolfNET.Game.Player.Components;
using WolfNET.Graphics;
using System.Numerics;

namespace WolfNET.Game.Player;

public interface IControlledActor
{
    Transform Transform { get; set; }
    CameraComponent Camera { get; }
    bool InAir { get; }
    bool Crouching { get; }
    int AnimationFrame { get; }
    float InteractionRadius { get; }
}

public interface ICharacterAppearance
{
    TextureCatalog AddTextures(TextureCatalog scene);
    Sprite CreateSprite(IControlledActor actor, Vector2 observer);
}
