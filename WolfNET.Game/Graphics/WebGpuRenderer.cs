
using System.Numerics;
using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;
using WolfNET.Game;

namespace WolfNET.Graphics;

public sealed class WebGpuRenderer : IDisposable
{
    private const uint WorkgroupSize = 64;
    private const int RayHitSize = 16;
    private const int CameraBufferSize = 48;
    private const int MapInfoBufferSize = 16;
    private const int SpriteSize = 32;

    private readonly RendererSettings _settings;
    private readonly BlazorJSRuntime _js;

    private GPUDevice? _device;
    private GPUCanvasContext? _context;

    private GPUComputePipeline? _computePipeline;
    private GPURenderPipeline? _renderPipeline;
    private GPURenderPipeline? _spritePipeline;
    private GPURenderPipeline? _minimapPipeline;
    private GPUBindGroup? _minimapBindGroup;

    private GPUBuffer? _cameraBuffer;
    private GPUBuffer? _mapInfoBuffer;
    private GPUBuffer? _worldMapBuffer;
    private GPUBuffer? _rayHitsBuffer;
    private GPUBuffer? _spriteBuffer;
    private GPUBindGroup? _spriteBindGroup;
    private int _spriteCapacity;
    private readonly List<(Sprite Sprite, float Distance)> _sortedSprites = [];

    private GPUBindGroup? _computeBindGroup;
    private GPUBindGroup? _renderBindGroup;
    private GPUTexture? _wallTextures;
    private GPUTextureView? _wallTextureView;
    private GPUSampler? _textureSampler;
    private GPUSampler? _surfaceSampler;
    private GPUTexture? _skyTexture;
    private GPUTextureView? _skyTextureView;
    private GPUSampler? _skySampler;
    private GPUBuffer? _skyBuffer;

    // Reusable CPU buffers.
    private readonly float[] _cameraData = new float[12];
    private GPUBuffer? _materialsBuffer;
    private TextureCatalog? _preparedTextures;
    private RenderMap? _uploadedMap;

    // Null means the map has never been uploaded.


    private bool _initialized;
    private bool _disposed;
    private int _frameCount;

    public WebGpuRenderer(
        RendererSettings settings,
        BlazorJSRuntime js)
    {
        _settings = settings;
        _js = js;
    }

    public async Task InitializeAsync(
        HTMLCanvasElement canvas,
        string computeShaderSource,
        string screenShaderSource,
        string spriteShaderSource)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_initialized)
        {
            throw new InvalidOperationException(
                "Renderer is already initialized.");
        }

        ValidateSettings();

        Console.WriteLine("[GPU] Initialization started");

        try
        {
            if (!_js.Get<bool>("window.isSecureContext"))
            {
                throw new NotSupportedException(
                    "WebGPU requires a secure connection. Open this game over HTTPS " +
                    "with a certificate trusted by this device. HTTP only works on localhost; " +
                    "an HTTP LAN IP address cannot use WebGPU. See README.md for LAN HTTPS setup.");
            }

            // 1. Request GPU adapter and device.

            using var navigator =
                _js.Get<Navigator>("navigator");

            using var gpu = navigator.Gpu
                ?? throw new NotSupportedException(
                    "WebGPU is unavailable in this browser. Use a WebGPU-capable browser " +
                    "with hardware acceleration enabled and a supported graphics driver.");

            Console.WriteLine("[GPU] Requesting adapter");

            using var adapter = await gpu.RequestAdapter()
                ?? throw new Exception(
                    "No WebGPU adapter available.");

            _device = await adapter.RequestDevice()
                ?? throw new Exception(
                    "Failed to create WebGPU device.");

            _device.OnUncapturedError += OnGpuError;

            Console.WriteLine("[GPU] Device created");

            // 2. Configure canvas.

            _context = canvas.GetWebGPUContext()
                ?? throw new Exception(
                    "WebGPU canvas context is unavailable.");

            var format = gpu.GetPreferredCanvasFormat();

            _context.Configure(
                new GPUCanvasConfiguration
                {
                    Device = _device,
                    Format = format
                });

            Console.WriteLine(
                $"[GPU] Canvas configured: {format}");

            // 3. Allocate persistent GPU buffers.

            CreateBuffers();


            // 4. Compile shaders.

            Console.WriteLine("[GPU] Creating shader modules");

            using var computeShader =
                _device.CreateShaderModule(
                    new GPUShaderModuleDescriptor
                    {
                        Label = "WolfNET DDA Shader",
                        Code = computeShaderSource
                    });

            using var screenShader =
                _device.CreateShaderModule(
                    new GPUShaderModuleDescriptor
                    {
                        Label = "WolfNET Screen Shader",
                        Code = screenShaderSource
                    });

            // 5. Create compute pipeline.

            Console.WriteLine("[GPU] Creating compute pipeline");

            _device.PushErrorScope(
                GPUErrorFilter.Validation);

            _computePipeline =
                _device.CreateComputePipeline(
                    new GPUComputePipelineDescriptor
                    {
                        Label = "WolfNET Raycast Pipeline",
                        Layout = GPUAutoLayoutMode.Auto,

                        Compute = new GPUProgrammableStage
                        {
                            Module = computeShader,
                            EntryPoint = "main"
                        }
                    });

            var computeError =
                await _device.PopErrorScope();

            if (computeError != null)
            {
                throw new Exception(
                    $"Compute pipeline validation failed: " +
                    computeError.Message);
            }

            // 6. Create render pipeline.

            Console.WriteLine("[GPU] Creating render pipeline");

            _device.PushErrorScope(
                GPUErrorFilter.Validation);

            _renderPipeline =
                _device.CreateRenderPipeline(
                    new GPURenderPipelineDescriptor
                    {
                        Label = "WolfNET Screen Pipeline",
                        Layout = GPUAutoLayoutMode.Auto,

                        Vertex = new GPUVertexState
                        {
                            Module = screenShader,
                            EntryPoint = "vertexMain"
                        },

                        Fragment = new GPUFragmentState
                        {
                            Module = screenShader,
                            EntryPoint = "fragmentMain",

                            Targets =
                            [
                                new GPUColorTargetState
                                {
                                    Format = format
                                }
                            ]
                        }
                    });

            var renderError =
                await _device.PopErrorScope();

            if (renderError != null)
            {
                throw new Exception(
                    $"Render pipeline validation failed: " +
                    renderError.Message);
            }

            // Reuse the screen shader module and all existing scene buffers.
            _device.PushErrorScope(GPUErrorFilter.Validation);
            _minimapPipeline = _device.CreateRenderPipeline(new GPURenderPipelineDescriptor
            {
                Label = "WolfNET Minimap Pipeline",
                Layout = GPUAutoLayoutMode.Auto,
                Vertex = new GPUVertexState { Module = screenShader, EntryPoint = "vertexMain" },
                Fragment = new GPUFragmentState
                {
                    Module = screenShader,
                    EntryPoint = "minimapMain",
                    Targets = [new GPUColorTargetState { Format = format }]
                }
            });

            var minimapError = await _device.PopErrorScope();
            if (minimapError != null)
                throw new Exception($"Minimap pipeline validation failed: {minimapError.Message}");

            using var spriteShader = _device.CreateShaderModule(new GPUShaderModuleDescriptor
            {
                Label = "WolfNET Sprite Shader",
                Code = spriteShaderSource
            });

            _device.PushErrorScope(GPUErrorFilter.Validation);
            _spritePipeline = _device.CreateRenderPipeline(new GPURenderPipelineDescriptor
            {
                Label = "WolfNET Sprite Pipeline",
                Layout = GPUAutoLayoutMode.Auto,
                Vertex = new GPUVertexState { Module = spriteShader, EntryPoint = "vertexMain" },
                Fragment = new GPUFragmentState
                {
                    Module = spriteShader,
                    EntryPoint = "fragmentMain",
                    Targets = [new GPUColorTargetState
                    {
                        Format = format,
                        Blend = new GPUBlendState
                        {
                            Color = new GPUBlendComponent { SrcFactor = "one", DstFactor = "one-minus-src-alpha", Operation = "add" },
                            Alpha = new GPUBlendComponent { SrcFactor = "one", DstFactor = "one-minus-src-alpha", Operation = "add" }
                        }
                    }]
                }
            });

            var spriteError = await _device.PopErrorScope();
            if (spriteError != null)
            {
                throw new Exception($"Sprite pipeline validation failed: {spriteError.Message}");
            }

            // 7. Create compute bind group.

            Console.WriteLine("[GPU] Creating compute bind group");

            using (var layout =
                   _computePipeline.GetBindGroupLayout(0))
            {
                _computeBindGroup =
                    _device.CreateBindGroup(
                        new GPUBindGroupDescriptor
                        {
                            Label = "WolfNET Compute Bindings",
                            Layout = layout,

                            Entries =
                            [
                                BufferEntry(0, _cameraBuffer!),
                                BufferEntry(1, _worldMapBuffer!),
                                BufferEntry(2, _rayHitsBuffer!),
                                BufferEntry(3, _mapInfoBuffer!)
                            ]
                        });
            }

            _uploadedMap = null;
            _frameCount = 0;
            _initialized = true;

            Console.WriteLine(
                "[GPU] Initialization completed successfully");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[GPU] Initialization failed: {ex}");

            Dispose();
            throw;
        }
    }

    public async Task PrepareSceneAsync(RenderMap map, CancellationToken cancellationToken = default,
        Func<string, Task>? reportProgress = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_initialized) throw new InvalidOperationException("Initialize the GPU before preparing a scene.");
        ReleaseScene();
        await LoadSkyAsync(map.Sky, cancellationToken, reportProgress);
        await LoadTexturesAsync(map.Textures, cancellationToken, reportProgress);

        if (reportProgress != null) await reportProgress("Preparing scene…");

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
        using (var data = new Uint32Array(materials))
            _device.Queue.WriteBuffer(_materialsBuffer, 0, data);
        using var layout = _renderPipeline!.GetBindGroupLayout(0);
        _renderBindGroup = _device.CreateBindGroup(new GPUBindGroupDescriptor
        {
            Label = "WolfNET Render Bindings",
            Layout = layout,
            Entries =
            [
                BufferEntry(0, _rayHitsBuffer!),
                BufferEntry(1, _cameraBuffer!),
                new GPUBindGroupEntry { Binding = 2, Resource = _wallTextureView! },
                BufferEntry(3, _worldMapBuffer!),
                BufferEntry(4, _mapInfoBuffer!),
                BufferEntry(6, _materialsBuffer),
                new GPUBindGroupEntry { Binding = 7, Resource = _textureSampler! },
                new GPUBindGroupEntry { Binding = 8, Resource = _surfaceSampler! },
                new GPUBindGroupEntry { Binding = 9, Resource = _skyTextureView! },
                new GPUBindGroupEntry { Binding = 10, Resource = _skySampler! },
                BufferEntry(11, _skyBuffer!)
            ]
        });
        _preparedTextures = map.Textures;
        _uploadedMap = null;
        UpdateMap(map);
        Console.WriteLine($"[GPU] Scene prepared: {map.Width}x{map.Height}, {map.Sprites.Count} sprites");
    }

    public void ReleaseScene()
    {
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
        DestroyBuffer(_skyBuffer);
        _skyBuffer = null;
        _uploadedMap = null;
        _sortedSprites.Clear();
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
    private void UpdateSprites(IReadOnlyList<Sprite> sprites, Vector2 cameraPosition)
    {
        _sortedSprites.Clear();
        foreach (var sprite in sprites)
        {
            _sortedSprites.Add((sprite, Vector2.DistanceSquared(cameraPosition, sprite.Position)));
        }

        // Painter's algorithm: nearer opaque texels cover farther sprites.
        _sortedSprites.Sort(static (a, b) => b.Distance.CompareTo(a.Distance));

        if (Math.Max(1, sprites.Count) > _spriteCapacity)
        {
            _spriteBindGroup?.Dispose();
            _minimapBindGroup?.Dispose();
            DestroyBuffer(_spriteBuffer);
            _spriteCapacity = Math.Max(sprites.Count, 16);
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
                    new GPUBindGroupEntry { Binding = 2, Resource = _wallTextureView! },
                    BufferEntry(3, _spriteBuffer),
                    new GPUBindGroupEntry { Binding = 4, Resource = _textureSampler! }
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
        using (var count = new Uint32Array(new uint[] { (uint)sprites.Count }))
            _device!.Queue.WriteBuffer(_mapInfoBuffer!, 8, count);

        if (sprites.Count == 0)
            return;

        // Matches the 32-byte Sprite in both WGSL shaders, including the final padding word.
        var upload = new uint[checked(sprites.Count * 8)];
        for (int i = 0; i < _sortedSprites.Count; i++)
        {
            var sprite = _sortedSprites[i].Sprite;
            upload[i * 8] = BitConverter.SingleToUInt32Bits(sprite.Position.X);
            upload[i * 8 + 1] = BitConverter.SingleToUInt32Bits(sprite.Position.Y);
            upload[i * 8 + 2] = sprite.ViewLayer(cameraPosition);
            upload[i * 8 + 3] = sprite.OwnerCell;
            var scale = sprite.ViewScale(cameraPosition);
            upload[i * 8 + 4] = BitConverter.SingleToUInt32Bits(scale.X);
            upload[i * 8 + 5] = BitConverter.SingleToUInt32Bits(scale.Y);
            upload[i * 8 + 6] = BitConverter.SingleToUInt32Bits(sprite.Elevation);
        }

        using var data = new Uint32Array(upload);
        _device!.Queue.WriteBuffer(_spriteBuffer!, 0, data);
    }

    private void ValidateSettings()
    {
        if (_settings.MaxAnisotropy is < 1 or > 16)
            throw new ArgumentException("Graphics maxAnisotropy must be between 1 and 16.");
        if (_settings.TargetFPS <= 0)
            throw new ArgumentException("Graphics targetFPS must be positive.");
        if (!float.IsFinite(_settings.FovDegrees) || _settings.FovDegrees < 1 || _settings.FovDegrees > 179)
            throw new ArgumentException("Graphics fovDegrees must be between 1 and 179 degrees.");

        if (_settings.Width <= 0 ||
            _settings.Height <= 0)
        {
            throw new ArgumentException(
                "Renderer resolution must be positive.");
        }

        if (_settings.MaxMapWidth <= 0 ||
            _settings.MaxMapHeight <= 0)
        {
            throw new ArgumentException(
                "Maximum map dimensions must be positive.");
        }

        checked
        {
            _ = _settings.MaxMapWidth *
                _settings.MaxMapHeight;
        }
    }

    private void CreateBuffers()
    {
        if (_device == null)
        {
            throw new InvalidOperationException(
                "GPU device is not initialized.");
        }

        int mapCapacity = checked(
            _settings.MaxMapWidth *
            _settings.MaxMapHeight);


        // Camera:
        // position   vec2f
        // direction  vec2f
        // plane      vec2f
        // screenSize vec2f
        // look       vec4f (pitch and padding)
        // Total: 48 bytes

        _cameraBuffer = _device.CreateBuffer(
            new GPUBufferDescriptor
            {
                Label = "WolfNET Camera",

                Size = (ulong)CameraBufferSize,

                Usage =
                    GPUBufferUsage.Uniform |
                    GPUBufferUsage.CopyDst
            });

        // Map dimensions:
        // width  u32
        // height u32
        // spriteCount u32 (updated with the sprite buffer)
        // Padding to 16 bytes

        _mapInfoBuffer = _device.CreateBuffer(
            new GPUBufferDescriptor
            {
                Label = "WolfNET Map Info",

                Size = (ulong)MapInfoBufferSize,

                Usage =
                    GPUBufferUsage.Uniform |
                    GPUBufferUsage.CopyDst
            });

        // Maximum map capacity.
        // Actual map can be smaller and change at runtime.

        _worldMapBuffer = _device.CreateBuffer(
            new GPUBufferDescriptor
            {
                Label = "WolfNET World Map",

                Size = checked(
                    (ulong)mapCapacity * 16),

                Usage =
                    GPUBufferUsage.Storage |
                    GPUBufferUsage.CopyDst
            });

        // One RayHit structure per screen column.
        // RayHit = 4 x 32-bit values = 16 bytes.

        _rayHitsBuffer = _device.CreateBuffer(
            new GPUBufferDescriptor
            {
                Label = "WolfNET Ray Hits",

                Size = checked(
                    (ulong)_settings.Width * RayHitSize),

                Usage = GPUBufferUsage.Storage
            });

        Console.WriteLine(
            $"[GPU] Buffers allocated. " +
            $"Max map: {_settings.MaxMapWidth}x" +
            $"{_settings.MaxMapHeight}, " +
            $"Screen: {_settings.Width}x{_settings.Height}");
    }

    private static GPUBindGroupEntry BufferEntry(
        uint binding,
        GPUBuffer buffer)
    {
        return new GPUBindGroupEntry
        {
            Binding = binding,

            Resource = new GPUBufferBinding
            {
                Buffer = buffer
            }
        };
    }

    /// <summary>Uploads compiled row-major cells only when their snapshot changes.</summary>
    private void UpdateMap(RenderMap map)
    {
        if (ReferenceEquals(_uploadedMap, map)) return;
        if (!ReferenceEquals(_preparedTextures, map.Textures))
            throw new InvalidOperationException("Prepare the scene's texture catalog before rendering it.");
        if (map.Width > _settings.MaxMapWidth || map.Height > _settings.MaxMapHeight)
            throw new InvalidDataException("Compiled map exceeds GPU buffer capacity.");
        var cells = new uint[checked(map.Cells.Count * 4)];
        for (int i = 0; i < map.Cells.Count; i++)
        {
            var cell = map.Cells[i];
            cells[i * 4] = cell.MaterialIndex;
            cells[i * 4 + 1] = cell.FloorTextureLayer;
            cells[i * 4 + 2] = cell.CeilingTextureLayer;
            cells[i * 4 + 3] = (uint)cell.Flags;
        }
        using (var data = new Uint32Array(cells))
            _device!.Queue.WriteBuffer(_worldMapBuffer!, 0, data);
        using (var info = new Uint32Array(new uint[] { (uint)map.Width, (uint)map.Height, (uint)map.Sprites.Count, 0 }))
            _device!.Queue.WriteBuffer(_mapInfoBuffer!, 0, info);
        _uploadedMap = map;
        Console.WriteLine($"[GPU] Map updated: {map.Width}x{map.Height}, revision={map.Revision}, cells={map.Cells.Count}");
    }
    /// <summary>
    /// Updates GPU camera data every frame.
    /// </summary>
    private void UpdateCamera(
        Vector2 position,
        Vector2 direction,
        Vector2 plane,
        float pitch,
        float elevation)
    {
        if (_device == null || _cameraBuffer == null)
        {
            throw new InvalidOperationException(
                "GPU camera buffer is not initialized.");
        }

        // Must match Camera in raycast.wgsl
        // and walls.wgsl / sprites.wgsl (48 bytes including look padding).

        _cameraData[0] = position.X;
        _cameraData[1] = position.Y;

        _cameraData[2] = direction.X;
        _cameraData[3] = direction.Y;

        _cameraData[4] = plane.X;
        _cameraData[5] = plane.Y;

        _cameraData[6] = _settings.Width;
        _cameraData[7] = _settings.Height;
        _cameraData[8] = pitch;
        _cameraData[9] = elevation;

        using var data = new Float32Array(_cameraData);

        _device.Queue.WriteBuffer(
            _cameraBuffer,
            0,
            data);
    }

    /// <summary>
    /// Executes compute raycasting and fragment rendering.
    /// Map buffers are synchronized only when needed.
    /// </summary>
    public void Render(
        Vector2 position,
        Vector2 direction,
        Vector2 plane,
        RenderMap map,
        float pitch = 0,
        float elevation = 0)
    {
        if (!_initialized ||
            _device == null ||
            _context == null ||
            _computePipeline == null ||
            _renderPipeline == null ||
            _spritePipeline == null ||
            _minimapPipeline == null ||
            _computeBindGroup == null ||
            _renderBindGroup == null)
        {
            return;
        }

        // 1. Synchronize map only if revision changed.
        UpdateMap(map);
        var sprites = map.Sprites;

        // 2. Update camera every frame.
        UpdateCamera(position, direction, plane, pitch, elevation);
        UpdateSprites(sprites, position);

        // 3. Create GPU command encoder.
        using var encoder =
            _device.CreateCommandEncoder();

        // 4. Compute pass: one DDA ray per screen column.
        using (var compute = encoder.BeginComputePass(
                   new GPUComputePassDescriptor
                   {
                       Label = "WolfNET DDA Compute Pass"
                   }))
        {
            compute.SetPipeline(_computePipeline);

            compute.SetBindGroup(
                0,
                _computeBindGroup);

            uint workgroups =
                ((uint)_settings.Width + WorkgroupSize - 1)
                / WorkgroupSize;

            compute.DispatchWorkgroups(workgroups);

            compute.End();
        }

        // 5. Acquire the current canvas texture.
        using var canvasTexture =
            _context.GetCurrentTexture();

        using var canvasView =
            canvasTexture.CreateView();

        // 6. Render pass: shade pixels using RayHit[].
        using (var pass = encoder.BeginRenderPass(
                   new GPURenderPassDescriptor
                   {
                       ColorAttachments =
                       [
                           new GPURenderPassColorAttachment
                           {
                               View = canvasView,

                               ClearValue = new double[]
                               {
                                   0.0,
                                   0.0,
                                   0.0,
                                   1.0
                               },

                               LoadOp = GPULoadOp.Clear,
                               StoreOp = GPUStoreOp.Store
                           }
                       ]
                   }))
        {
            pass.SetPipeline(_renderPipeline);

            pass.SetBindGroup(
                0,
                _renderBindGroup);

            // Fullscreen triangle.
            pass.Draw(3);

            if (sprites.Count > 0)
            {
                pass.SetPipeline(_spritePipeline);
                pass.SetBindGroup(0, _spriteBindGroup!);
                pass.Draw(6, (uint)sprites.Count);
            }

            // Draw last so world sprites cannot cover the minimap.
            pass.SetPipeline(_minimapPipeline);
            pass.SetBindGroup(0, _minimapBindGroup!);
            pass.Draw(3);

            pass.End();
        }

        // 7. Submit compute and render passes.
        using var commands = encoder.Finish();

        _device.Queue.Submit([commands]);

        _frameCount++;

        if (_frameCount == 1 ||
            _frameCount % 1000 == 0)
        {
            Console.WriteLine(
                $"[GPU] Frame {_frameCount}");
        }
    }

    private void OnGpuError(
        GPUUncapturedErrorEvent e)
    {
        Console.Error.WriteLine(
            $"[GPU] Uncaptured error: {e.Error.Message}");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _initialized = false;

        Console.WriteLine("[GPU] Disposing renderer");

        if (_device != null)
        {
            _device.OnUncapturedError -= OnGpuError;
        }

        _computeBindGroup?.Dispose();
        ReleaseScene();

        DestroyBuffer(_cameraBuffer);
        DestroyBuffer(_mapInfoBuffer);
        DestroyBuffer(_worldMapBuffer);
        DestroyBuffer(_rayHitsBuffer);

        _computePipeline?.Dispose();
        _renderPipeline?.Dispose();
        _spritePipeline?.Dispose();
        _minimapPipeline?.Dispose();

        _context?.Dispose();
        _device?.Dispose();

        _sortedSprites.Clear();
        _uploadedMap = null;

        Console.WriteLine("[GPU] Renderer disposed");
    }

    private static void DestroyBuffer(GPUBuffer? buffer)
    {
        if (buffer == null)
            return;

        buffer.Destroy();
        buffer.Dispose();
    }
}
