using System.Numerics;
using WolfNET.Engine.Graphics;

namespace WolfSlop.Examples.Game.Player;

public interface ICharacterAppearance
{
    TextureCatalog AddTextures(TextureCatalog scene);
    Sprite CreateSprite(IControlledActor actor, Vector2 observer);
}
