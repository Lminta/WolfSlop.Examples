using System.Numerics;
using System.Runtime.InteropServices;
using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;
using WolfNET.Game;

namespace WolfNET.Graphics;

/// <summary>Keeps the numeric scenery query in the browser; C# resolves the selected artwork.</summary>
internal sealed class BrowserSpriteSelection(JSObject module) : IDisposable
{
    private IReadOnlyList<Sprite>? _source;
    private Float32Array? _positions;
    private Uint32Array? _widthOverrides;
    private Uint32Array? _output;
    private byte[] _outputBytes = [], _widthBytes = [];
    private readonly List<int> _atlasIndices = [];
    private int[] _frames = [];
    private float[] _widths = [];

    private void Synchronize(IReadOnlyList<Sprite> sprites)
    {
        if (ReferenceEquals(_source, sprites)) return;
        ReleaseScene();
        _source = sprites;
        var bytes = new byte[Math.Max(1, sprites.Count) * 16];
        var positions = MemoryMarshal.Cast<byte, float>(bytes.AsSpan());
        _frames = new int[sprites.Count + 1];
        _widths = new float[sprites.Count + 1];
        for (var i = 0; i < sprites.Count; i++)
        {
            var sprite = sprites[i];
            positions[i * 4] = sprite.Position.X; positions[i * 4 + 1] = sprite.Position.Y;
            positions[i * 4 + 2] = _widths[i] = sprite.Scale.X;
            var maximumWidth = sprite.Scale.X;
            if (sprite.Atlas is { } atlas)
            {
                _atlasIndices.Add(i);
                maximumWidth = 0;
                foreach (var frame in atlas.Frames)
                    maximumWidth = MathF.Max(maximumWidth, sprite.Scale.X * ((float)frame.Width / frame.Height));
            }
            positions[i * 4 + 3] = maximumWidth;
        }
        _positions = new Float32Array(positions.Length);
        _positions.WriteBytes(bytes);
        module.JSRef!.CallVoid("prepare", _positions, sprites.Count);
        _widthBytes = new byte[Math.Max(1, _atlasIndices.Count) * 8];
        _widthOverrides = new Uint32Array(_widthBytes.Length / 4);
        _outputBytes = new byte[(sprites.Count + 2) * 8];
        _output = new Uint32Array(_outputBytes.Length / 4);
    }

    public void Select(SpriteSelection selection, IReadOnlyList<Sprite> sprites, Vector2 position, Vector2 direction,
        Vector2 plane, float range, bool cullView, double elapsedSeconds, Sprite? character)
    {
        Synchronize(sprites);
        var overrides = MemoryMarshal.Cast<byte, uint>(_widthBytes.AsSpan());
        for (var i = 0; i < _atlasIndices.Count; i++)
        {
            var index = _atlasIndices[i];
            var sprite = sprites[index];
            var frame = sprite.ViewFrame(position, elapsedSeconds);
            _frames[index] = frame;
            var size = sprite.Atlas!.Frames[frame];
            _widths[index] = sprite.Scale.X * ((float)size.Width / size.Height);
            overrides[i * 2] = (uint)index;
            overrides[i * 2 + 1] = BitConverter.SingleToUInt32Bits(_widths[index]);
        }
        if (character is { } avatar)
        {
            var frame = avatar.ViewFrame(position, elapsedSeconds);
            _frames[sprites.Count] = frame;
            _widths[sprites.Count] = avatar.Scale.X * (avatar.Atlas is { } atlas
                ? (float)atlas.Frames[frame].Width / atlas.Frames[frame].Height : 1);
        }
        _widthOverrides!.WriteBytes(_widthBytes);
        var actor = character.GetValueOrDefault();
        var count = module.JSRef!.CallApply<int>("select", new object[] { _output!, _widthOverrides, _atlasIndices.Count,
            position.X, position.Y, range, direction.X, direction.Y, plane.Length(), cullView,
            character.HasValue, actor.Position.X, actor.Position.Y, _widths[sprites.Count] });
        _output!.Read(0, _outputBytes, 0, (count + 1) * 8);
        selection.Resolve(sprites, MemoryMarshal.Cast<byte, uint>(_outputBytes.AsSpan()), count, _frames, _widths, character);
    }

    public void ReleaseScene()
    {
        module.JSRef!.CallVoid("release");
        _positions?.Dispose(); _widthOverrides?.Dispose(); _output?.Dispose();
        _positions = null; _widthOverrides = _output = null;
        _source = null;
        _frames = []; _widths = []; _outputBytes = _widthBytes = [];
        _atlasIndices.Clear();
    }

    public void Dispose()
    {
        ReleaseScene();
        module.Dispose();
    }
}
