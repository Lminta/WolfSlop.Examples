using System.Numerics;
using WolfNET.Engine.Graphics;

namespace RaceImperialis.Game.Player;

public sealed class CarAppearance : ICharacterAppearance
{
    private readonly SpriteDefinition _definition;
    private readonly CarSettings _settings;
    public uint FirstLayer { get; private set; }

    public CarAppearance(SpriteDefinition definition, CarSettings settings)
    {
        if (string.IsNullOrWhiteSpace(definition.Texture) || definition.Atlas is not { Directions: 8, Frames.Length: 8 } ||
            definition.Atlas.Frames.Any(frame => frame.Width <= 0 || frame.Height <= 0))
            throw new InvalidDataException("Car character needs a texture and eight directional frames.");
        _definition = definition;
        _settings = settings;
    }

    public TextureCatalog AddTextures(TextureCatalog scene)
    {
        var layers = scene.Layers.ToDictionary();
        var frames = scene.Frames.ToDictionary();
        uint first = 0;
        while (first + 8 <= TextureCatalog.MaxLayers && Enumerable.Range(0, 8).Any(i => layers.ContainsKey(first + (uint)i))) first++;
        if (first + 8 > TextureCatalog.MaxLayers) throw new InvalidDataException("Scene needs eight free layers for the car.");
        FirstLayer = first;
        for (uint i = 0; i < 8; i++)
        {
            layers.Add(first + i, _definition.Texture);
            frames.Add(first + i, _definition.Atlas!.Frames[i]);
        }
        return new TextureCatalog(layers, frames);
    }

    public Sprite CreateSprite(IControlledActor actor, Vector2 observer)
    {
        var body = actor.Transform;
        var heading = MathF.Atan2(body.Rotation.Y, body.Rotation.X);
        var selector = new Sprite(body.Position, FirstLayer, 0) { Atlas = _definition.Atlas, RotationDegrees = heading * 180 / MathF.PI };
        var frameIndex = selector.ViewFrame(observer);
        // Match the selected frame's fixed plane, without continuously stretching
        // its width as the observer moves within this directional sector.
        var angle = frameIndex * MathF.Tau / 8;
        var width = _settings.Width * MathF.Abs(MathF.Cos(angle)) + _settings.Length * MathF.Abs(MathF.Sin(angle));
        var frame = _definition.Atlas!.Frames[frameIndex];
        var height = _settings.Height;
        return new Sprite(body.Position, FirstLayer, 0)
        {
            Scale = new(width * frame.Height / frame.Width, height), Elevation = (height - 1) * .5f - .005f,
            Atlas = _definition.Atlas, RotationDegrees = heading * 180 / MathF.PI, FacingCamera = false
        };
    }
}
