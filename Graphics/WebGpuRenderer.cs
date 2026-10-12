
using System.Drawing;
using System.Numerics;
using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;
using WolfNET.Graphics.Generators;

namespace WolfNET.Graphics;

public class WebGpuRenderer : IDisposable
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
    private int _frameCount;
    private int _colorIndex;
    
    private LodeCaster _imageGenerator;

    private static readonly double[][] Backgrounds =
    [
        [0.08, 0.12, 0.20, 1.0],
        [0.20, 0.35, 0.25, 1.0],
        [0.35, 0.12, 0.20, 1.0],
        [0.15, 0.10, 0.35, 1.0]
    ];

    public WebGpuRenderer(RendererSettings settings, BlazorJSRuntime js)
    {
        _js = js;
        _settings = settings;
        _imageGenerator = new LodeCaster(_settings);
    }

    public async Task InitializeAsync(
        HTMLCanvasElement canvas,
        string shaderSource)
    {
        Console.WriteLine("[GPU] Initialization started");

        try
        {
            // -----------------------------
            // 1. Request GPU adapter/device
            // -----------------------------

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

            // -----------------------------
            // 2. Configure canvas
            // -----------------------------

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

            // -----------------------------
            // 3. Compile shader
            // -----------------------------

            Console.WriteLine("[GPU] Creating shader module");

            _device.PushErrorScope(
                GPUErrorFilter.Validation);

            using var shader = _device.CreateShaderModule(
                new GPUShaderModuleDescriptor
                {
                    Label = "WolfNET Textured Shader",
                    Code = shaderSource
                });

            var shaderError = await _device.PopErrorScope();

            if (shaderError != null)
            {
                throw new Exception(
                    $"Shader validation failed: " +
                    shaderError.Message);
            }

            Console.WriteLine("[GPU] Shader module created");

            // -----------------------------
            // 4. Create render pipeline
            // -----------------------------

            Console.WriteLine("[GPU] Creating pipeline");

            _device.PushErrorScope(
                GPUErrorFilter.Validation);

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
                    $"Pipeline validation failed: " +
                    pipelineError.Message);
            }

            Console.WriteLine("[GPU] Pipeline created");

            // -----------------------------
            // 5. Generate and upload image
            // -----------------------------

            _imageGenerator.SetPosition(new Vector2(22, 12), new Vector2(-1, 0));
            var pixels = _imageGenerator.Pass();

            UploadTexture(pixels);

            // -----------------------------
            // 6. Create texture sampler
            // -----------------------------

            Console.WriteLine("[GPU] Creating sampler");

            _sampler = _device.CreateSampler(
                new GPUSamplerDescriptor
                {
                    MagFilter = GPUFilterMode.Nearest,
                    MinFilter = GPUFilterMode.Nearest
                });

            Console.WriteLine("[GPU] Sampler created");

            // -----------------------------
            // 7. Create bind group
            // -----------------------------

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

            throw;
        }
    }

    private void UploadTexture(Texture texture)
    {
        if (_device == null)
        {
            throw new InvalidOperationException(
                "GPU device is not initialized");
        }

        if (texture.Pixels.Length != checked(texture.Settings.Width * texture.Settings.Height * 4))
        {
            throw new ArgumentException(
                "Invalid RGBA8 pixel buffer length");
        }

        Console.WriteLine(
            $"[GPU] Creating texture {texture.Settings.Width}x{texture.Settings.Height}");

        _texture = _device.CreateTexture(
            new GPUTextureDescriptor
            {
                Label = "WolfNET Checkerboard",

                Size = new[] { texture.Settings.Width, texture.Settings.Height, 1 },

                Format = "rgba8unorm",

                Usage =
                    GPUTextureUsage.TextureBinding |
                    GPUTextureUsage.CopyDst
            });

        Console.WriteLine("[GPU] Texture created");

        // Convert the C# byte array into a JS typed array.
        // SpawnDev.BlazorJS handles the interop.
        using var data = new Uint8Array(texture.Pixels);

        Console.WriteLine(
            $"[GPU] Uploading {texture.Pixels.Length} bytes");

        _device.Queue.WriteTexture(
            new GPUTexelCopyTextureInfo
            {
                Texture = _texture
            },

            data,

            new GPUTexelCopyBufferLayout
            {
                BytesPerRow = (uint)(texture.Settings.Width * 4),
                RowsPerImage = (uint)texture.Settings.Height
            },

            new GPUExtent3DDict
            {
                Width = (uint)texture.Settings.Width,
                Height = (uint)texture.Settings.Height,
                DepthOrArrayLayers = 1
            }
        );

        Console.WriteLine("[GPU] Texture upload submitted");
    }

    public void NextBackground()
    {
        _colorIndex =
            (_colorIndex + 1) % Backgrounds.Length;

        Console.WriteLine(
            $"[GPU] Background changed: {_colorIndex}");
    }

    public void Render()
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

        if (_frameCount == 1)
        {
            Console.WriteLine("[GPU] First render frame");
        }

        if (_frameCount % 300 == 0)
        {
            Console.WriteLine(
                $"[GPU] Frame {_frameCount}");
        }

        using var texture = _context.GetCurrentTexture();
        using var view = texture.CreateView();

        using var encoder = _device.CreateCommandEncoder();

        using var pass = encoder.BeginRenderPass(
            new GPURenderPassDescriptor
            {
                ColorAttachments =
                [
                    new GPURenderPassColorAttachment
                    {
                        View = view,

                        ClearValue = Backgrounds[_colorIndex],

                        LoadOp = GPULoadOp.Clear,
                        StoreOp = GPUStoreOp.Store
                    }
                ]
            });

        pass.SetPipeline(_pipeline);

        // Bind texture and sampler to shader group 0.
        pass.SetBindGroup(0, _bindGroup);

        // Three vertices form one fullscreen triangle.
        pass.Draw(3);

        pass.End();

        using var commandBuffer = encoder.Finish();

        _device.Queue.Submit([commandBuffer]);
    }

    private void OnGpuError(GPUUncapturedErrorEvent e)
    {
        Console.Error.WriteLine(
            $"[GPU] Uncaptured error: {e.Error.Message}");
    }

    public void Dispose()
    {
        Console.WriteLine("[GPU] Disposing renderer");

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
