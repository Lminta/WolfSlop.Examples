using System.Numerics;
using WolfNET.Engine.Graphics;

namespace RaceImperialis.Game.Player;

public sealed class CharacterAppearance : ICharacterAppearance
{
    private readonly SpriteDefinition _definition;
    public uint FirstLayer { get; private set; }

    public CharacterAppearance(SpriteDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Texture) || definition.Atlas is not { Frames.Length: 15 } atlas ||
            atlas.Frames.Any(f => f == null || f.X < 0 || f.Y < 0 || f.Width <= 0 || f.Height <= 0))
            throw new InvalidDataException("Player character needs a texture and fifteen valid atlas frames.");
        _definition = definition;
    }

    public TextureCatalog AddTextures(TextureCatalog scene)
    {
        var layers = scene.Layers.ToDictionary();
        var frames = scene.Frames.ToDictionary();
        uint first = 0;
        while (first + 15 <= TextureCatalog.MaxLayers &&
               Enumerable.Range(0, 15).Any(i => layers.ContainsKey(first + (uint)i))) first++;
        if (first + 15 > TextureCatalog.MaxLayers)
            throw new InvalidDataException("Scene needs fifteen free consecutive layers for the player character.");
        FirstLayer = first;
        for (uint i = 0; i < 15; i++)
        {
            layers.Add(first + i, _definition.Texture);
            frames.Add(first + i, _definition.Atlas!.Frames[i]);
        }
        return new TextureCatalog(layers, frames);
    }

    public Sprite Sprite(int frame, Vector2 position, float jumpElevation)
    {
        var rect = _definition.Atlas!.Frames[frame];
        // All atlas poses use the same pixel-to-world scale: crouching stays shorter.
        var multiplier = frame is 10 or 11 ? .7f : .8f;
        var unitsPerPixel = .9f / 310 * multiplier;
        var scale = new Vector2(rect.Width, rect.Height) * unitsPerPixel;
        return new Sprite(position, FirstLayer + (uint)frame, 0)
        {
            Scale = scale,
            Elevation = (scale.Y - 1) * .5f - .005f + jumpElevation
        };
    }

    public Sprite CreateSprite(IControlledActor actor, Vector2 observer) =>
        Sprite(actor.AnimationFrame, actor.Transform.Position, actor.InAir ? actor.Camera.Elevation : 0);
}
