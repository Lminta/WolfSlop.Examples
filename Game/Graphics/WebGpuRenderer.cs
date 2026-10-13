using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;

namespace WolfNET.Game.Graphics;

public sealed class WebGpuRenderer : IDisposable
{
    private readonly RendererSettings _settings;
    private readonly BlazorJSRuntime _js;

    private GPUDevice? _device;
    private GPUCanvasContext? _context;
    private GPURenderPipeline? _pipeline;

    private GPUTexture? _texture;
    private GPUSampler? _sampler;
    private GPUBindGroup? _bindGroup;

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
        string shaderSource)
    {
        if (_initialized)
            throw new InvalidOperationException(
                "Renderer is already initialized");

        Console.WriteLine("[GPU] Initialization started");

        try
        {
            // 1. Request GPU adapter and device

            using var navigator = _js.Get<Navigator>("navigator");

            using var gpu = navigator.Gpu
                ?? throw new NotSupportedException(
                    "WebGPU is not supported");

            Console.WriteLine("[GPU] Requesting adapter");

            using var adapter = await gpu.RequestAdapter()
                ?? throw new Exception("No WebGPU adapter");

            Console.WriteLine("[GPU] Adapter acquired");

            _device = await adapter.RequestDevice()
                ?? throw new Exception("No GPU device");

            Console.WriteLine("[GPU] Device acquired");

            _device.OnUncapturedError += OnGpuError;

            // 2. Configure canvas

            _context = canvas.GetWebGPUContext()
                ?? throw new Exception(
                    "WebGPU canvas context unavailable");

            var format = gpu.GetPreferredCanvasFormat();

            _context.Configure(new GPUCanvasConfiguration
            {
                Device = _device,
                Format = format
            });

            Console.WriteLine(
                $"[GPU] Canvas configured: {format}");

            // 3. Compile shader

            Console.WriteLine("[GPU] Creating shader module");

            _device.PushErrorScope(GPUErrorFilter.Validation);

            using var shader = _device.CreateShaderModule(
                new GPUShaderModuleDescriptor
                {
                    Label = "WolfNET Framebuffer Shader",
                    Code = shaderSource
                });

            var shaderError = await _device.PopErrorScope();

            if (shaderError != null)
            {
                throw new Exception(
                    $"Shader validation failed: {shaderError.Message}");
            }

            Console.WriteLine("[GPU] Shader module created");

            // 4. Create render pipeline

            Console.WriteLine("[GPU] Creating render pipeline");

            _device.PushErrorScope(GPUErrorFilter.Validation);

            _pipeline = _device.CreateRenderPipeline(
                new GPURenderPipelineDescriptor
                {
                    Layout = GPUAutoLayoutMode.Auto,

                    Vertex = new GPUVertexState
                    {
                        Module = shader,
                        EntryPoint = "vertexMain"
                    },

                    Fragment = new GPUFragmentState
                    {
                        Module = shader,
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

            var pipelineError = await _device.PopErrorScope();

            if (pipelineError != null)
            {
                throw new Exception(
                    $"Pipeline validation failed: {pipelineError.Message}");
            }

            Console.WriteLine("[GPU] Render pipeline created");

            // 5. Allocate framebuffer texture
            // Texture contents will be updated during Render().

            CreateFramebufferTexture();

            // 6. Create texture sampler

            Console.WriteLine("[GPU] Creating sampler");

            _sampler = _device.CreateSampler(
                new GPUSamplerDescriptor
                {
                    MagFilter = GPUFilterMode.Nearest,
                    MinFilter = GPUFilterMode.Nearest
                });

            Console.WriteLine("[GPU] Sampler created");

            // 7. Create bind group

            Console.WriteLine("[GPU] Creating bind group");

            using var textureView = _texture!.CreateView();
            using var layout = _pipeline.GetBindGroupLayout(0);

            _bindGroup = _device.CreateBindGroup(
                new GPUBindGroupDescriptor
                {
                    Layout = layout,

                    Entries =
                    [
                        new GPUBindGroupEntry
                        {
                            Binding = 0,
                            Resource = textureView
                        },

                        new GPUBindGroupEntry
                        {
                            Binding = 1,
                            Resource = _sampler
                        }
                    ]
                });

            Console.WriteLine("[GPU] Bind group created");

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

    private void CreateFramebufferTexture()
    {
        if (_device == null)
        {
            throw new InvalidOperationException(
                "GPU device is not initialized");
        }

        int width = _settings.Width;
        int height = _settings.Height;

        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(_settings),
                "Framebuffer dimensions must be positive");

        Console.WriteLine(
            $"[GPU] Creating framebuffer texture: {width}x{height}");

        _texture = _device.CreateTexture(
            new GPUTextureDescriptor
            {
                Label = "WolfNET CPU Framebuffer",

                Size = [width, height, 1],

                Format = "rgba8unorm",

                Usage =
                    GPUTextureUsage.TextureBinding |
                    GPUTextureUsage.CopyDst
            });

        Console.WriteLine("[GPU] Framebuffer texture created");
    }

    private void UploadTexture(Texture frame)
    {
        if (_device == null || _texture == null)
        {
            throw new InvalidOperationException(
                "GPU framebuffer is not initialized");
        }

        int width = frame.Settings.Width;
        int height = frame.Settings.Height;

        if (width != _settings.Width ||
            height != _settings.Height)
        {
            throw new ArgumentException(
                $"Unexpected framebuffer dimensions: " +
                $"{width}x{height}. Expected " +
                $"{_settings.Width}x{_settings.Height}");
        }

        if (frame.Pixels.Length != checked(width * height * 4))
        {
            throw new ArgumentException(
                "Invalid RGBA8 framebuffer size");
        }

        // Transfer CPU byte[] into a JavaScript Uint8Array.
        using var data = new Uint8Array(frame.Pixels);

        // Copy pixels into the existing GPU texture.
        // No GPUTexture allocation happens here.
        _device.Queue.WriteTexture(
            new GPUTexelCopyTextureInfo
            {
                Texture = _texture
            },

            data,

            new GPUTexelCopyBufferLayout
            {
                BytesPerRow = (uint)(width * 4),
                RowsPerImage = (uint)height
            },

            new GPUExtent3DDict
            {
                Width = (uint)width,
                Height = (uint)height,
                DepthOrArrayLayers = 1
            }
        );
    }

    public void Render(Texture frame)
    {
        if (!_initialized ||
            _device == null ||
            _context == null ||
            _pipeline == null ||
            _bindGroup == null)
        {
            return;
        }

        _frameCount++;

        // // 1. CPU renders a new image.
        // Texture frame = _imageGenerator.Pass();

        // 2. Upload the generated image to GPU memory.
        UploadTexture(frame);

        // 3. Acquire canvas texture.
        using var canvasTexture = _context.GetCurrentTexture();
        using var view = canvasTexture.CreateView();

        // 4. Create command encoder.
        using var encoder = _device.CreateCommandEncoder();

        // 5. Start render pass.
        using var pass = encoder.BeginRenderPass(
            new GPURenderPassDescriptor
            {
                ColorAttachments =
                [
                    new GPURenderPassColorAttachment
                    {
                        View = view,

                        ClearValue = new double[]
                        {
                            0.0, 0.0, 0.0, 1.0
                        },

                        LoadOp = GPULoadOp.Clear,
                        StoreOp = GPUStoreOp.Store
                    }
                ]
            });

        // 6. Draw framebuffer onto the canvas.
        pass.SetPipeline(_pipeline);
        pass.SetBindGroup(0, _bindGroup);
        pass.Draw(3);

        pass.End();

        // 7. Submit commands to GPU.
        using var commandBuffer = encoder.Finish();

        _device.Queue.Submit([commandBuffer]);

        if (_frameCount == 1 || _frameCount % 300 == 0)
        {
            Console.WriteLine(
                $"[GPU] Rendered frame {_frameCount}");
        }
    }

    private void OnGpuError(GPUUncapturedErrorEvent e)
    {
        Console.Error.WriteLine(
            $"[GPU] Uncaptured error: {e.Error.Message}");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Console.WriteLine("[GPU] Disposing renderer");

        _disposed = true;
        _initialized = false;

        if (_device != null)
        {
            _device.OnUncapturedError -= OnGpuError;
        }

        _bindGroup?.Dispose();
        _sampler?.Dispose();

        _texture?.Destroy();
        _texture?.Dispose();

        _pipeline?.Dispose();
        _context?.Dispose();
        _device?.Dispose();

        Console.WriteLine("[GPU] Renderer disposed");
    }
}
