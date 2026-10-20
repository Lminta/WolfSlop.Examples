using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;
using WolfNET.Game;

namespace WolfNET.Graphics;

/// <summary>Owns a scene's tile array, sky panorama, samplers, and image upload process.</summary>
internal sealed class GpuSceneTextures : IDisposable
{
    private readonly GPUDevice _device;
    private readonly BlazorJSRuntime _js;
    private readonly RendererSettings _settings;
    private GPUTexture? _wallTextures;
    private GPUTextureView? _wallTextureView;
    private GPUSampler? _textureSampler;
    private GPUSampler? _surfaceSampler;
    private GPUTexture? _skyTexture;
    private GPUTextureView? _skyTextureView;
    private GPUSampler? _skySampler;
    private GPUBuffer? _skyBuffer;

    public GPUTextureView WallView => _wallTextureView!;
    public GPUSampler TextureSampler => _textureSampler!;
    public GPUSampler SurfaceSampler => _surfaceSampler!;
    public GPUTextureView SkyView => _skyTextureView!;
    public GPUSampler SkySampler => _skySampler!;
    public GPUBuffer SkyBuffer => _skyBuffer!;

    private GpuSceneTextures(GPUDevice device, BlazorJSRuntime js, RendererSettings settings)
    {
        _device = device;
        _js = js;
        _settings = settings;
    }

    public static async Task<GpuSceneTextures> LoadAsync(GPUDevice device, BlazorJSRuntime js,
        RendererSettings settings, SkyDefinition? sky, TextureCatalog catalog,
        CancellationToken cancellationToken, Func<string, Task>? reportProgress)
    {
        var textures = new GpuSceneTextures(device, js, settings);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await textures.LoadSkyAsync(sky, cancellationToken, reportProgress);
            await textures.LoadTexturesAsync(catalog, cancellationToken, reportProgress);
            cancellationToken.ThrowIfCancellationRequested();
            return textures;
        }
        catch
        {
            textures.Dispose();
            throw;
        }
    }

    private async Task LoadSkyAsync(SkyDefinition? sky, CancellationToken cancellationToken,
        Func<string, Task>? reportProgress)
    {
        using var queue = _device!.Queue;
        _skyBuffer = _device.CreateBuffer(new GPUBufferDescriptor
        {
            Label = "WolfNET Sky Settings", Size = 16,
            Usage = GPUBufferUsage.Uniform | GPUBufferUsage.CopyDst
        });
        using (var settings = new Float32Array(new[] { (sky?.RotationDegrees ?? 0) / 360f, sky == null ? 0f : 1f, 0f, 0f }))
            queue.WriteBuffer(_skyBuffer, 0, settings);
        _skySampler = _device.CreateSampler(new GPUSamplerDescriptor
        {
            AddressModeU = "repeat", AddressModeV = "clamp-to-edge",
            MagFilter = "linear", MinFilter = "linear", MipmapFilter = "linear"
        });

        if (sky == null)
        {
            _skyTexture = _device.CreateTexture(new GPUTextureDescriptor
            {
                Size = [1, 1, 1], Format = "rgba8unorm",
                Usage = GPUTextureUsage.TextureBinding | GPUTextureUsage.CopyDst
            });
            using var black = new Uint8Array(new byte[] { 0, 0, 0, 255 });
            queue.WriteTexture(new GPUTexelCopyTextureInfo { Texture = _skyTexture }, black,
                new GPUTexelCopyBufferLayout { BytesPerRow = 4, RowsPerImage = 1 },
                new GPUExtent3DDict { Width = 1, Height = 1, DepthOrArrayLayers = 1 });
        }
        else
        {
            if (reportProgress != null) await reportProgress("Loading sky panorama…");
            using var image = new HTMLImageElement { Src = sky.Texture };
            await image.Decode();
            cancellationToken.ThrowIfCancellationRequested();
            // Preserve the panorama's aspect ratio; it does not belong in the square tile array.
            var scale = Math.Min(1.0, 4096.0 / Math.Max(image.NaturalWidth, image.NaturalHeight));
            var width = Math.Max(1, (int)(image.NaturalWidth * scale));
            var height = Math.Max(1, (int)(image.NaturalHeight * scale));
            var levels = (int)Math.Floor(Math.Log2(Math.Max(width, height))) + 1;
            _skyTexture = _device.CreateTexture(new GPUTextureDescriptor
            {
                Label = "WolfNET Sky Panorama", Size = [width, height, 1], MipLevelCount = levels,
                Format = "rgba8unorm", Usage = GPUTextureUsage.TextureBinding | GPUTextureUsage.CopyDst | GPUTextureUsage.RenderAttachment
            });
            using var window = _js.Get<Window>("window");
            for (int mip = 0; mip < levels; mip++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reportProgress != null) await reportProgress($"Preparing sky ({mip + 1}/{levels})…");
                var mipWidth = Math.Max(1, width >> mip);
                var mipHeight = Math.Max(1, height >> mip);
                using var bitmap = await window.CreateImageBitmap(image, new ImageBitmapOptions
                {
                    ResizeWidth = mipWidth, ResizeHeight = mipHeight, ResizeQuality = "high", PremultiplyAlpha = "premultiply"
                });
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    queue.CopyExternalImageToTexture(new GPUCopyExternalImageSourceInfo { Source = bitmap },
                        new GPUCopyExternalImageDestInfo { Texture = _skyTexture, MipLevel = (uint)mip, PremultipliedAlpha = true },
                        new GPUExtent3DDict { Width = (uint)mipWidth, Height = (uint)mipHeight, DepthOrArrayLayers = 1 });
                }
                finally { bitmap.Close(); }
            }
        }
        _skyTextureView = _skyTexture.CreateView();
    }

    private async Task LoadTexturesAsync(TextureCatalog catalog, CancellationToken cancellationToken,
        Func<string, Task>? reportProgress)
    {
        if (catalog.LayerCount > TextureCatalog.MaxLayers)
            throw new InvalidDataException("TextureLayer must fit the GPU's 256 texture array layers.");
        var images = new Dictionary<uint, HTMLImageElement>();
        var decoded = new Dictionary<string, HTMLImageElement?>();
        try
        {
            uint size = TextureCatalog.TextureSize;
            var imageCount = catalog.Layers.Count(entry => entry.Value != null);
            var imageIndex = 0;
            foreach (var (layer, path) in catalog.Layers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (path == null) continue;
                imageIndex++;
                if (reportProgress != null) await reportProgress($"Loading textures ({imageIndex}/{imageCount})…");
                if (!decoded.TryGetValue(path, out var image))
                {
                    image = new HTMLImageElement { Src = path };
                    try { await image.Decode(); }
                    catch (Exception)
                    {
                        image.Dispose();
                        image = null;
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    decoded[path] = image;
                }
                if (image == null) continue;
                if (catalog.Frames.TryGetValue(layer, out var frame) &&
                    ((long)frame.X + frame.Width > image.NaturalWidth || (long)frame.Y + frame.Height > image.NaturalHeight))
                    throw new InvalidDataException($"Atlas frame {layer} extends outside '{path}'.");
                images.Add(layer, image);
                size = Math.Max(size, (uint)(frame == null ? Math.Max(image.NaturalWidth, image.NaturalHeight) : Math.Max(frame.Width, frame.Height)));
            }
            cancellationToken.ThrowIfCancellationRequested();
            // Include the complete mip pyramid in the 128 MiB scene texture budget.
            var memoryLimit = (uint)Math.Sqrt(128 * 1024 * 1024 / (4.0 * catalog.LayerCount * (4.0 / 3.0)));
            size = Math.Min(size, Math.Min(2048u, memoryLimit));
            var mipLevels = (uint)Math.Floor(Math.Log2(size)) + 1;
            _wallTextures = _device!.CreateTexture(new GPUTextureDescriptor
            {
                Label = "WolfNET Scene Textures",
                Size = [(int)size, (int)size, (int)catalog.LayerCount],
                MipLevelCount = (int)mipLevels,
                Format = "rgba8unorm",
                Usage = GPUTextureUsage.TextureBinding | GPUTextureUsage.CopyDst | GPUTextureUsage.RenderAttachment
            });
            _textureSampler = _device.CreateSampler(new GPUSamplerDescriptor
            {
                Label = "WolfNET Wall and Sprite Filtering",
                MagFilter = "linear", MinFilter = "linear", MipmapFilter = "linear", MaxAnisotropy = _settings.MaxAnisotropy,
                AddressModeU = "clamp-to-edge", AddressModeV = "clamp-to-edge"
            });
            _surfaceSampler = _device.CreateSampler(new GPUSamplerDescriptor
            {
                Label = "WolfNET Repeating Surface Filtering",
                MagFilter = "linear", MinFilter = "linear", MipmapFilter = "linear", MaxAnisotropy = _settings.MaxAnisotropy,
                AddressModeU = "repeat", AddressModeV = "repeat"
            });
            using var queue = _device.Queue;
            using var window = _js.Get<Window>("window");
            var pink = new byte[size * size * 4];
            for (int i = 0; i < pink.Length; i += 4) pink[i] = pink[i + 2] = pink[i + 3] = 255;
            using var pixels = new Uint8Array(pink);
            for (uint layer = 0; layer < catalog.LayerCount; layer++)
            {
                if (reportProgress != null) await reportProgress($"Preparing textures ({layer + 1}/{catalog.LayerCount})…");
                for (uint mip = 0; mip < mipLevels; mip++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var mipSize = Math.Max(1u, size >> (int)mip);
                    var extent = new GPUExtent3DDict { Width = mipSize, Height = mipSize, DepthOrArrayLayers = 1 };
                    if (!images.TryGetValue(layer, out var image))
                    {
                        // Populate every mip of holes and missing images with solid pink.
                        queue.WriteTexture(new GPUTexelCopyTextureInfo
                            { Texture = _wallTextures, MipLevel = mip, Origin = new GPUOrigin3DDict { Z = layer } },
                            pixels, new GPUTexelCopyBufferLayout { BytesPerRow = mipSize * 4, RowsPerImage = mipSize }, extent);
                        continue;
                    }
                    // High-quality area resampling creates the minification levels once per scene.
                    var crop = catalog.Frames.GetValueOrDefault(layer);
                    using var bitmap = await window.CreateImageBitmap(image,
                        crop?.X ?? 0, crop?.Y ?? 0, crop?.Width ?? image.NaturalWidth, crop?.Height ?? image.NaturalHeight, new ImageBitmapOptions
                    {
                        ResizeWidth = (int)mipSize,
                        ResizeHeight = (int)mipSize,
                        ResizeQuality = "high",
                        PremultiplyAlpha = "premultiply"
                    });
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        queue.CopyExternalImageToTexture(new GPUCopyExternalImageSourceInfo { Source = bitmap },
                            new GPUCopyExternalImageDestInfo
                            {
                                Texture = _wallTextures, MipLevel = mip,
                                Origin = new GPUOrigin3DDict { Z = layer },
                                PremultipliedAlpha = true
                            }, extent);
                    }
                    finally { bitmap.Close(); }
                }
            }
            _wallTextureView = _wallTextures.CreateView();
            Console.WriteLine($"[GPU] Loaded scene texture array ({catalog.LayerCount} layers, {size}x{size}, {mipLevels} mip levels, {_settings.MaxAnisotropy}x anisotropic filtering)");
        }
        finally
        {
            foreach (var image in decoded.Values) image?.Dispose();
        }
    }
    public void Dispose()
    {
        _wallTextureView?.Dispose();
        _wallTextureView = null;
        _textureSampler?.Dispose();
        _surfaceSampler?.Dispose();
        _textureSampler = _surfaceSampler = null;
        _wallTextures?.Destroy();
        _wallTextures?.Dispose();
        _wallTextures = null;
        _skyTextureView?.Dispose();
        _skyTextureView = null;
        _skySampler?.Dispose();
        _skySampler = null;
        _skyTexture?.Destroy();
        _skyTexture?.Dispose();
        _skyTexture = null;
        _skyBuffer?.Destroy();
        _skyBuffer?.Dispose();
        _skyBuffer = null;
    }
}
