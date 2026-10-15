
using System.Numerics;
using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;
using Array = System.Array;
using WolfNET.Game;

namespace WolfNET.Graphics;

public sealed class WebGpuRenderer : IDisposable
{
    private const uint WorkgroupSize = 64;
    private const int RayHitSize = 16;
    private const int CameraBufferSize = 32;
    private const int MapInfoBufferSize = 16;
    private const int SpriteSize = 16;

    // Map wall IDs are one-based; the first eight match Lode's texture order.
    private static readonly string[] TexturePaths =
    [
        "pics/eagle.png",
        "pics/redbrick.png",
        "pics/purplestone.png",
        "pics/greystone.png",
        "pics/bluestone.png",
        "pics/mossy.png",
        "pics/wood.png",
        "pics/colorstone.png",
        "pics/barrel.png",
        "pics/pillar.png",
        "pics/greenlight.png"
    ];

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

    // Reusable CPU buffers.
    private readonly float[] _cameraData = new float[8];
    private uint[]? _mapUploadData;

    // Null means the map has never been uploaded.
    private ulong? _uploadedMapRevision;

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
            // 1. Request GPU adapter and device.

            using var navigator =
                _js.Get<Navigator>("navigator");

            using var gpu = navigator.Gpu
                ?? throw new NotSupportedException(
                    "WebGPU is not supported.");

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
            await LoadTexturesAsync();

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
                    Targets = [new GPUColorTargetState { Format = format }]
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

            // 8. Create screen bind group.

            Console.WriteLine("[GPU] Creating render bind group");

            using (var layout =
                   _renderPipeline.GetBindGroupLayout(0))
            {
                _renderBindGroup =
                    _device.CreateBindGroup(
                        new GPUBindGroupDescriptor
                        {
                            Label = "WolfNET Render Bindings",
                            Layout = layout,

                            Entries =
                            [
                                BufferEntry(0, _rayHitsBuffer!),
                                BufferEntry(1, _cameraBuffer!),
                                new GPUBindGroupEntry
                                {
                                    Binding = 2,
                                    Resource = _wallTextureView!
                                }
                            ]
                        });
            }

            _uploadedMapRevision = null;
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

    private async Task LoadTexturesAsync()
    {
        uint width = 0;
        uint height = 0;

        using var queue = _device!.Queue;

        for (int layer = 0; layer < TexturePaths.Length; layer++)
        {
            var path = TexturePaths[layer];
            using var image = new HTMLImageElement();
            image.Src = path;

            try
            {
                await image.Decode();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load texture '{path}'.", ex);
            }

            if (layer == 0)
            {
                width = (uint)image.NaturalWidth;
                height = (uint)image.NaturalHeight;
                _wallTextures = _device.CreateTexture(new GPUTextureDescriptor
                {
                    Label = "WolfNET Wall Textures",
                    Size = [(int)width, (int)height, TexturePaths.Length],
                    Format = "rgba8unorm",
                    Usage = GPUTextureUsage.TextureBinding |
                            GPUTextureUsage.CopyDst |
                            GPUTextureUsage.RenderAttachment
                });
            }

            if (image.NaturalWidth != width || image.NaturalHeight != height)
            {
                throw new InvalidOperationException(
                    $"Texture '{path}' must be {width}x{height} to fit the texture array.");
            }

            queue.CopyExternalImageToTexture(
                new GPUCopyExternalImageSourceInfo { Source = image },
                new GPUCopyExternalImageDestInfo
                {
                    Texture = _wallTextures!,
                    Origin = new GPUOrigin3DDict { Z = (uint)layer }
                },
                new GPUExtent3DDict { Width = width, Height = height, DepthOrArrayLayers = 1 });
        }

        _wallTextureView = _wallTextures!.CreateView();
        Console.WriteLine($"[GPU] Loaded {TexturePaths.Length} textures ({width}x{height})");
    }

    private void UpdateSprites(IReadOnlyList<Sprite> sprites, Vector2 cameraPosition)
    {
        _sortedSprites.Clear();
        foreach (var sprite in sprites)
        {
            if (sprite.TextureLayer >= TexturePaths.Length)
                throw new ArgumentException("Sprite texture layer is outside the loaded texture array.", nameof(sprites));

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
                    BufferEntry(3, _spriteBuffer)
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

        // Matches Sprite in sprites.wgsl: position, texture layer, padding.
        var upload = new float[checked(sprites.Count * 4)];
        for (int i = 0; i < _sortedSprites.Count; i++)
        {
            var sprite = _sortedSprites[i].Sprite;
            upload[i * 4] = sprite.Position.X;
            upload[i * 4 + 1] = sprite.Position.Y;
            upload[i * 4 + 2] = sprite.TextureLayer;
        }

        using var data = new Float32Array(upload);
        _device!.Queue.WriteBuffer(_spriteBuffer!, 0, data);
    }

    private void ValidateSettings()
    {
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

        _mapUploadData = new uint[mapCapacity];

        // Camera:
        // position   vec2f
        // direction  vec2f
        // plane      vec2f
        // screenSize vec2f
        // Total: 32 bytes

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
                    (ulong)mapCapacity * sizeof(uint)),

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

    /// <summary>
    /// Uploads the map only when its revision changes.
    /// The first call always uploads the map.
    ///
    /// Expected C# indexing: worldMap[x][y].
    /// GPU layout: worldMap[y * width + x].
    /// </summary>
    private void UpdateMap(
        int[][] worldMap,
        ulong revision)
    {
        if (_device == null ||
            _worldMapBuffer == null ||
            _mapInfoBuffer == null ||
            _mapUploadData == null)
        {
            throw new InvalidOperationException(
                "GPU map buffers are not initialized.");
        }

        // Already synchronized.
        if (_uploadedMapRevision == revision)
            return;

        if (worldMap == null || worldMap.Length == 0)
            throw new ArgumentException(
                "World map cannot be empty.",
                nameof(worldMap));

        int width = worldMap.Length;

        if (worldMap[0] == null ||
            worldMap[0].Length == 0)
        {
            throw new ArgumentException(
                "World map cannot contain empty columns.",
                nameof(worldMap));
        }

        int height = worldMap[0].Length;

        if (width > _settings.MaxMapWidth ||
            height > _settings.MaxMapHeight)
        {
            throw new ArgumentException(
                $"Map dimensions {width}x{height} exceed " +
                $"the configured maximum " +
                $"{_settings.MaxMapWidth}x" +
                $"{_settings.MaxMapHeight}.");
        }

        // Validate and flatten the map.
        //
        // C#:  worldMap[x][y]
        // GPU: flattened[y * width + x]

        for (int x = 0; x < width; x++)
        {
            int[] column = worldMap[x];

            if (column == null ||
                column.Length != height)
            {
                throw new ArgumentException(
                    "World map must be rectangular.",
                    nameof(worldMap));
            }

            for (int y = 0; y < height; y++)
            {
                int cell = column[y];

                if (cell < 0)
                {
                    throw new ArgumentException(
                        "Wall IDs cannot be negative.",
                        nameof(worldMap));
                }

                _mapUploadData[y * width + x] =
                    (uint)cell;
            }
        }

        int cellCount = checked(width * height);

        // Upload only the active map cells.
        // Reuse the managed buffer for flattening.

        var activeCells = new uint[cellCount];

        Array.Copy(
            _mapUploadData,
            activeCells,
            cellCount);

        using (var data = new Uint32Array(activeCells))
        {
            _device.Queue.WriteBuffer(
                _worldMapBuffer,
                0,
                data);
        }

        // Update the actual map dimensions.
        //
        // The compute shader uses these dimensions
        // for indexing and bounds checking.

        using (var info = new Uint32Array(
            new uint[]
            {
                (uint)width,
                (uint)height,
                0,
                0
            }))
        {
            _device.Queue.WriteBuffer(
                _mapInfoBuffer,
                0,
                info);
        }

        // Only mark synchronized after both writes
        // have been successfully submitted.

        _uploadedMapRevision = revision;

        Console.WriteLine(
            $"[GPU] Map updated: {width}x{height}, " +
            $"revision={revision}, " +
            $"cells={cellCount}");
    }

    /// <summary>
    /// Updates GPU camera data every frame.
    /// </summary>
    private void UpdateCamera(
        Vector2 position,
        Vector2 direction,
        Vector2 plane)
    {
        if (_device == null || _cameraBuffer == null)
        {
            throw new InvalidOperationException(
                "GPU camera buffer is not initialized.");
        }

        // Must match Camera in raycast.wgsl
        // and screen.wgsl.

        _cameraData[0] = position.X;
        _cameraData[1] = position.Y;

        _cameraData[2] = direction.X;
        _cameraData[3] = direction.Y;

        _cameraData[4] = plane.X;
        _cameraData[5] = plane.Y;

        _cameraData[6] = _settings.Width;
        _cameraData[7] = _settings.Height;

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
        int[][] worldMap,
        ulong mapRevision,
        IReadOnlyList<Sprite> sprites)
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
        UpdateMap(worldMap, mapRevision);

        // 2. Update camera every frame.
        UpdateCamera(position, direction, plane);
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
            _frameCount % 300 == 0)
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
        _renderBindGroup?.Dispose();
        _spriteBindGroup?.Dispose();
        _minimapBindGroup?.Dispose();
        _wallTextureView?.Dispose();
        _wallTextures?.Destroy();
        _wallTextures?.Dispose();

        DestroyBuffer(_cameraBuffer);
        DestroyBuffer(_mapInfoBuffer);
        DestroyBuffer(_worldMapBuffer);
        DestroyBuffer(_rayHitsBuffer);
        DestroyBuffer(_spriteBuffer);

        _computePipeline?.Dispose();
        _renderPipeline?.Dispose();
        _spritePipeline?.Dispose();
        _minimapPipeline?.Dispose();

        _context?.Dispose();
        _device?.Dispose();

        _mapUploadData = null;
        _sortedSprites.Clear();
        _uploadedMapRevision = null;

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
