using System.Numerics;
using WolfNET.Game;

namespace WolfNET.Graphics;

// Independent, deliberately unindexed reference for the browser's spatial and painter checks.
internal sealed class ReferenceSpriteSelection
{
    private readonly List<SpriteSelection.VisibleSprite> _radius = [], _visible = [];
    public IReadOnlyList<SpriteSelection.VisibleSprite> Visible => _visible;
    public void Clear() { _radius.Clear(); _visible.Clear(); }

    public void Select(IReadOnlyList<Sprite> sprites, Vector2 position, Vector2 direction, Vector2 plane,
        float range, bool cullView, double elapsedSeconds, Sprite? character = null)
    {
        Clear();
        for (var i = 0; i < sprites.Count; i++) Consider(sprites[i], i, position, range, elapsedSeconds);
        if (character is { } avatar) Consider(avatar, sprites.Count, position, range, elapsedSeconds);
        _radius.Sort(static (a, b) => b.Distance.CompareTo(a.Distance));
        var right = new Vector2(direction.Y, -direction.X);
        var scale = plane.Length();
        foreach (var view in _radius)
        {
            if (cullView)
            {
                var relative = view.Sprite.Position - position;
                var radius = view.Sprite.Scale.X * .5f;
                if (view.Sprite.Atlas is { } atlas)
                    radius = atlas.Frames.Max(frame => view.Sprite.Scale.X * ((float)frame.Width / frame.Height)) * .5f;
                var depth = Vector2.Dot(relative, direction);
                var across = Vector2.Dot(relative, right);
                var margin = radius * MathF.Sqrt(1 + scale * scale);
                if (depth + radius <= 0 || across - depth * scale > margin || -across - depth * scale > margin) continue;
            }
            _visible.Add(view);
        }
    }

    private void Consider(Sprite sprite, int order, Vector2 position, float range, double time)
    {
        var scale = sprite.ViewScale(position, time);
        var distance = Vector2.DistanceSquared(position, sprite.Position);
        if (distance > (range + scale.X * .5f) * (range + scale.X * .5f)) return;
        _radius.Add(new(sprite, scale, sprite.ViewLayer(position, time), sprite.ViewKind(position), distance, order));
    }
}
