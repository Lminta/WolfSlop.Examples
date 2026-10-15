using System.Numerics;
using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;
using WolfNET.Game;

namespace WolfNET.Graphics;

public sealed class MinimapRenderer(HTMLCanvasElement canvas, BlazorJSRuntime js) : IDisposable
{
    private IMap? _uploadedMap;
    private ulong? _uploadedRevision;
    private float[] _sprites = [];

    public void Render(IMap map, Vector2 position, Vector2 direction, Vector2 plane)
    {
        if (!ReferenceEquals(_uploadedMap, map) || _uploadedRevision != map.Revision)
        {
            js.CallVoid("wolfMinimap.setMap", canvas, map.WorldMap);
            _uploadedMap = map;
            _uploadedRevision = map.Revision;
        }

        if (_sprites.Length != map.Sprites.Count * 3)
            _sprites = new float[map.Sprites.Count * 3];

        for (int i = 0; i < map.Sprites.Count; i++)
        {
            var sprite = map.Sprites[i];
            _sprites[i * 3] = sprite.Position.X;
            _sprites[i * 3 + 1] = sprite.Position.Y;
            _sprites[i * 3 + 2] = sprite.TextureLayer;
        }

        js.CallVoid("wolfMinimap.draw", canvas,
            position.X, position.Y, direction.X, direction.Y, plane.X, plane.Y, _sprites);
    }

    public void Dispose()
    {
        js.CallVoid("wolfMinimap.remove", canvas);
        _uploadedMap = null;
        _uploadedRevision = null;
    }
}
