using System.Numerics;
using WolfNET.Game;

namespace WolfNET.Graphics;

/// <summary>Resolves the browser's ordered sprite IDs without repeating atlas-frame selection.</summary>
internal sealed class SpriteSelection
{
    internal readonly record struct VisibleSprite(Sprite Sprite, Vector2 Scale, uint Layer, uint Kind, float Distance, int Order);
    private readonly List<VisibleSprite> _visible = [];
    public IReadOnlyList<VisibleSprite> Visible => _visible;
    public int CandidatesExamined { get; private set; }

    public void Clear()
    {
        _visible.Clear();
        CandidatesExamined = 0;
    }

    public void Resolve(IReadOnlyList<Sprite> sprites, ReadOnlySpan<uint> keys, int count,
        ReadOnlySpan<int> frames, ReadOnlySpan<float> widths, Sprite? character, Sprite? opponent = null)
    {
        _visible.Clear();
        CandidatesExamined = (int)keys[1];
        for (var i = 0; i < count; i++)
        {
            var index = (int)keys[2 + i * 2];
            var sprite = index < sprites.Count ? sprites[index] : index == sprites.Count ? character!.Value : opponent!.Value;
            var frame = frames[index];
            _visible.Add(new(sprite, new(widths[index], sprite.Scale.Y), sprite.TextureLayer + (uint)frame,
                sprite.ViewKind(frame), BitConverter.UInt32BitsToSingle(keys[3 + i * 2]), index));
        }
    }
}
