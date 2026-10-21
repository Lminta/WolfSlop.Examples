using System.Numerics;
using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;
using WolfNET.Game;
using WolfNET.Game.Player;

namespace WolfNET.Graphics;

public sealed partial class WebGpuRenderer
{
    public async Task PrepareSceneAsync(RenderMap map, CancellationToken cancellationToken = default,
        Func<string, Task>? reportProgress = null, CharacterAppearance? character = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_initialized) throw new InvalidOperationException("Initialize the GPU before preparing a scene.");
        ReleaseScene();
        try
        {
            _textures = await GpuSceneTextures.LoadAsync(_device!, _js, _settings, map.Sky,
                character?.AddTextures(map.Textures) ?? map.Textures, cancellationToken, reportProgress);

            if (reportProgress != null) await reportProgress("Preparing scene…");
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);

            _materialsBuffer = _device!.CreateBuffer(new GPUBufferDescriptor
            {
                Label = "WolfNET Wall Materials",
                Size = checked((ulong)map.Materials.Count * 16),
                Usage = GPUBufferUsage.Storage | GPUBufferUsage.CopyDst
            });
            var materials = new uint[map.Materials.Count * 4];
            for (int i = 0; i < map.Materials.Count; i++)
            {
                var material = map.Materials[i];
                materials[i * 4] = material.North;
                materials[i * 4 + 1] = material.East;
                materials[i * 4 + 2] = material.South;
                materials[i * 4 + 3] = material.West;
            }
            using var queue = _device.Queue;
            using (var data = new Uint32Array(materials))
                queue.WriteBuffer(_materialsBuffer, 0, data);
            using var layout = _renderPipeline!.GetBindGroupLayout(0);
            _renderBindGroup = _device.CreateBindGroup(new GPUBindGroupDescriptor
            {
                Label = "WolfNET Render Bindings",
                Layout = layout,
                Entries =
                [
                    BufferEntry(0, _rayHitsBuffer!),
                    BufferEntry(1, _cameraBuffer!),
                    new GPUBindGroupEntry { Binding = 2, Resource = _textures!.WallView },
                    BufferEntry(3, _worldMapBuffer!),
                    BufferEntry(4, _mapInfoBuffer!),
                    BufferEntry(6, _materialsBuffer),
                    new GPUBindGroupEntry { Binding = 7, Resource = _textures!.TextureSampler },
                    new GPUBindGroupEntry { Binding = 8, Resource = _textures!.SurfaceSampler },
                    new GPUBindGroupEntry { Binding = 9, Resource = _textures!.SkyView },
                    new GPUBindGroupEntry { Binding = 10, Resource = _textures!.SkySampler },
                    BufferEntry(11, _textures!.SkyBuffer)
                ]
            });
            _preparedTextures = map.Textures;
            _uploadedMap = null;
            UpdateMap(map);
            _sceneAnimationClock.Restart();
            Console.WriteLine($"[GPU] Scene prepared: {map.Width}x{map.Height}, {map.Sprites.Count} sprites");
        }
        catch
        {
            ReleaseScene();
            throw;
        }
    }

    public void ReleaseScene()
    {
        _sceneAnimationClock.Reset();
        _preparedTextures = null;
        _renderBindGroup?.Dispose();
        _spriteBindGroup?.Dispose();
        _minimapBindGroup?.Dispose();
        _renderBindGroup = _spriteBindGroup = _minimapBindGroup = null;
        DestroyBuffer(_spriteBuffer);
        _spriteBuffer = null;
        _spriteCapacity = 0;
        DestroyBuffer(_materialsBuffer);
        _materialsBuffer = null;
        _textures?.Dispose();
        _textures = null;
        _uploadedMap = null;
        _sortedSprites.Clear();
        _frameSprites.Clear();
    }

    private void UpdateSprites(IReadOnlyList<Sprite> sprites, Vector2 cameraPosition, double elapsedSeconds)
    {
        _sortedSprites.Clear();
        foreach (var sprite in sprites)
        {
            var distanceSquared = Vector2.DistanceSquared(cameraPosition, sprite.Position);
            // Retain wide quads whose near edge is still inside the visible circle.
            var maximumDistance = _settings.RenderDistance + sprite.ViewScale(cameraPosition, elapsedSeconds).X * .5f;
            if (distanceSquared <= maximumDistance * maximumDistance)
                _sortedSprites.Add((sprite, distanceSquared));
        }
        var spriteCount = _sortedSprites.Count;

        // Painter's algorithm: nearer opaque texels cover farther sprites.
        _sortedSprites.Sort(static (a, b) => b.Distance.CompareTo(a.Distance));

        if (Math.Max(1, spriteCount) > _spriteCapacity)
        {
            _spriteBindGroup?.Dispose();
            _minimapBindGroup?.Dispose();
            DestroyBuffer(_spriteBuffer);
            _spriteCapacity = Math.Max(spriteCount, 16);
            _spriteBuffer = _device!.CreateBuffer(new GPUBufferDescriptor
            {
                Label = "WolfNET Sprites",
                Size = checked((ulong)_spriteCapacity * SpriteSize),
                Usage = GPUBufferUsage.Storage | GPUBufferUsage.CopyDst
            });

            using var layout = _spritePipeline!.GetBindGroupLayout(0);
            _spriteBindGroup = _device.CreateBindGroup(new GPUBindGroupDescriptor
            {
                Label = "WolfNET Sprite Bindings",
                Layout = layout,
                Entries =
                [
                    BufferEntry(0, _rayHitsBuffer!),
                    BufferEntry(1, _cameraBuffer!),
                    new GPUBindGroupEntry { Binding = 2, Resource = _textures!.WallView },
                    BufferEntry(3, _spriteBuffer),
                    new GPUBindGroupEntry { Binding = 4, Resource = _textures!.TextureSampler },
                    BufferEntry(5, _worldMapBuffer!),
                    BufferEntry(6, _mapInfoBuffer!)
                ]
            });

            using var minimapLayout = _minimapPipeline!.GetBindGroupLayout(0);
            _minimapBindGroup = _device.CreateBindGroup(new GPUBindGroupDescriptor
            {
                Label = "WolfNET Minimap Bindings",
                Layout = minimapLayout,
                Entries =
                [
                    BufferEntry(0, _rayHitsBuffer!),
                    BufferEntry(1, _cameraBuffer!),
                    BufferEntry(3, _worldMapBuffer!),
                    BufferEntry(4, _mapInfoBuffer!),
                    BufferEntry(5, _spriteBuffer)
                ]
            });
        }

        // The third u32 of MapInfo counts active sprites, excluding spare buffer capacity.
        using var queue = _device!.Queue;
        using (var count = new Uint32Array(new uint[] { (uint)spriteCount }))
            queue.WriteBuffer(_mapInfoBuffer!, 8, count);

        if (spriteCount == 0)
            return;

        // Matches the 32-byte Sprite in both WGSL shaders, including the final geometry-kind word.
        var upload = new uint[checked(spriteCount * 8)];
        for (int i = 0; i < _sortedSprites.Count; i++)
        {
            var sprite = _sortedSprites[i].Sprite;
            upload[i * 8] = BitConverter.SingleToUInt32Bits(sprite.Position.X);
            upload[i * 8 + 1] = BitConverter.SingleToUInt32Bits(sprite.Position.Y);
            upload[i * 8 + 2] = sprite.ViewLayer(cameraPosition, elapsedSeconds);
            upload[i * 8 + 3] = sprite.OwnerCell;
            var scale = sprite.ViewScale(cameraPosition, elapsedSeconds);
            upload[i * 8 + 4] = BitConverter.SingleToUInt32Bits(scale.X);
            upload[i * 8 + 5] = BitConverter.SingleToUInt32Bits(scale.Y);
            upload[i * 8 + 6] = BitConverter.SingleToUInt32Bits(sprite.Elevation);
            upload[i * 8 + 7] = sprite.ViewKind(cameraPosition);
        }

        using var data = new Uint32Array(upload);
        queue.WriteBuffer(_spriteBuffer!, 0, data);
    }

}
