using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;

namespace WolfNET.Graphics;

public sealed class WebGpuRenderer : IDisposable
{
    private readonly BlazorJSRuntime _js;

    private GPUDevice? _device;
    private GPUCanvasContext? _context;
    private GPURenderPipeline? _pipeline;

    private int _frameCount;

    private static readonly float[][] Backgrounds =
    [
        [0.08f, 0.12f, 0.20f],
        [0.20f, 0.35f, 0.25f],
        [0.35f, 0.12f, 0.20f],
        [0.15f, 0.10f, 0.35f]
    ];

    private int _colorIndex;

    public WebGpuRenderer(BlazorJSRuntime js)
    {
        _js = js;
    }

    public async Task InitializeAsync(
        HTMLCanvasElement canvas,
        string shaderSource)
    {
        Console.WriteLine("[GPU] Initializing");

        using var navigator = _js.Get<Navigator>("navigator");
        using var gpu = navigator.Gpu
            ?? throw new NotSupportedException(
                "WebGPU is not supported");

        Console.WriteLine("[GPU] Requesting adapter");

        using var adapter = await gpu.RequestAdapter()
            ?? throw new Exception("No WebGPU adapter");

        Console.WriteLine("[GPU] Adapter acquired");

        _device = await adapter.RequestDevice()
            ?? throw new Exception("GPU device unavailable");

        Console.WriteLine("[GPU] Device acquired");

        _device.OnUncapturedError += OnGpuError;

        _context = canvas.GetWebGPUContext()
            ?? throw new Exception("No WebGPU canvas context");

        var format = gpu.GetPreferredCanvasFormat();

        Console.WriteLine($"[GPU] Canvas format: {format}");

        _context.Configure(new GPUCanvasConfiguration
        {
            Device = _device,
            Format = format
        });

        Console.WriteLine("[GPU] Canvas configured");

        _device.PushErrorScope(GPUErrorFilter.Validation);

        using var shader = _device.CreateShaderModule(
            new GPUShaderModuleDescriptor
            {
                Label = "Triangle shader",
                Code = shaderSource
            });

        Console.WriteLine("[GPU] Shader module created");

        var shaderError = await _device.PopErrorScope();

        if (shaderError != null)
            throw new Exception(
                $"Shader validation error: {shaderError.Message}");

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
            throw new Exception(
                $"Pipeline validation error: {pipelineError.Message}");

        Console.WriteLine("[GPU] Pipeline created");
        Console.WriteLine("[GPU] Initialization complete");
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
        if (_device == null ||
            _context == null ||
            _pipeline == null)
            return;

        _frameCount++;

        if (_frameCount == 1)
            Console.WriteLine("[GPU] First frame");

        if (_frameCount % 300 == 0)
            Console.WriteLine(
                $"[GPU] Frame {_frameCount}");

        var color = Backgrounds[_colorIndex];

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
                        ClearValue = new double[]
                        {
                            color[0],
                            color[1],
                            color[2],
                            1.0
                        },
                        LoadOp = GPULoadOp.Clear,
                        StoreOp = GPUStoreOp.Store
                    }
                ]
            });

        pass.SetPipeline(_pipeline);
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
        if (_device != null)
            _device.OnUncapturedError -= OnGpuError;

        _pipeline?.Dispose();
        _context?.Dispose();
        _device?.Dispose();

        Console.WriteLine("[GPU] Renderer disposed");
    }
}