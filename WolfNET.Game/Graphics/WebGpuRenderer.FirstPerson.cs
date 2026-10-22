using SpawnDev.BlazorJS.JSObjects;

namespace WolfNET.Graphics;

public sealed partial class WebGpuRenderer
{
    private GPURenderPipeline? _firstPersonPipeline;
    private GPUBuffer? _firstPersonBuffer;
    private GPUBindGroup? _firstPersonBindGroup;
    private GPUTexture? _firstPersonTexture, _firstPersonDetailTexture;
    private GPUTextureView? _firstPersonView, _firstPersonDetailView;
    private GPUSampler? _firstPersonSampler;
    private Float32Array? _firstPersonUpload;
    private readonly float[] _firstPersonData = new float[12];
    private readonly byte[] _firstPersonBytes = new byte[48];

    private async Task CreateFirstPersonPipelineAsync(string source, string format, CancellationToken token)
    {
        _firstPersonBuffer = _device!.CreateBuffer(new GPUBufferDescriptor
        {
            Label = "WolfNET First Person", Size = 48,
            Usage = GPUBufferUsage.Uniform | GPUBufferUsage.CopyDst
        });
        _firstPersonUpload = new Float32Array(12);
        using var shader = _device.CreateShaderModule(new GPUShaderModuleDescriptor
        {
            Label = "WolfNET First Person Shader", Code = source
        });
        _device.PushErrorScope(GPUErrorFilter.Validation);
        _firstPersonPipeline = _device.CreateRenderPipeline(new GPURenderPipelineDescriptor
        {
            Label = "WolfNET First Person Pipeline",
            Layout = GPUAutoLayoutMode.Auto,
            Vertex = new GPUVertexState { Module = shader, EntryPoint = "vertexMain" },
            Fragment = new GPUFragmentState
            {
                Module = shader, EntryPoint = "fragmentMain",
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
        using var error = await _device.PopErrorScope();
        token.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (error != null) throw new Exception($"First-person pipeline validation failed: {error.Message}");
    }

    private async Task PrepareFirstPersonAsync(bool car, CancellationToken token, Func<string, Task>? reportProgress)
    {
        if (reportProgress != null) await reportProgress("Preparing first-person sprites…");
        _firstPersonTexture = await LoadFirstPersonTextureAsync(car ? "textures/race/cockpit.png"
            : "textures/characters/first-person-hands.png", token);
        _firstPersonView = _firstPersonTexture.CreateView();
        if (car)
        {
            _firstPersonDetailTexture = await LoadFirstPersonTextureAsync("textures/race/steering-wheel.png", token);
            _firstPersonDetailView = _firstPersonDetailTexture.CreateView();
        }
        token.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        _firstPersonSampler = _device!.CreateSampler(new GPUSamplerDescriptor
        {
            Label = "WolfNET First Person Filtering",
            MinFilter = "linear", MagFilter = "linear",
            AddressModeU = "clamp-to-edge", AddressModeV = "clamp-to-edge"
        });
        using var layout = _firstPersonPipeline!.GetBindGroupLayout(0);
        _firstPersonBindGroup = _device.CreateBindGroup(new GPUBindGroupDescriptor
        {
            Label = "WolfNET First Person Bindings", Layout = layout,
            Entries =
            [
                BufferEntry(0, _firstPersonBuffer!),
                new GPUBindGroupEntry { Binding = 1, Resource = _firstPersonView },
                new GPUBindGroupEntry { Binding = 2, Resource = _firstPersonDetailView ?? _firstPersonView },
                new GPUBindGroupEntry { Binding = 3, Resource = _firstPersonSampler }
            ]
        });
    }

    private async Task<GPUTexture> LoadFirstPersonTextureAsync(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var image = new HTMLImageElement { Src = path };
        try { await image.Decode(); }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            // Match missing world-image behavior without preventing scene startup.
            var fallback = _device!.CreateTexture(new GPUTextureDescriptor
            {
                Label = $"WolfNET First Person Missing: {path}", Size = [1, 1, 1], Format = "rgba8unorm",
                Usage = GPUTextureUsage.TextureBinding | GPUTextureUsage.CopyDst
            });
            using var pixels = new Uint8Array(new byte[] { 255, 0, 255, 255 });
            using var fallbackQueue = _device.Queue;
            fallbackQueue.WriteTexture(new GPUTexelCopyTextureInfo { Texture = fallback }, pixels,
                new GPUTexelCopyBufferLayout { BytesPerRow = 4, RowsPerImage = 1 },
                new GPUExtent3DDict { Width = 1, Height = 1, DepthOrArrayLayers = 1 });
            return fallback;
        }
        token.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        var texture = _device!.CreateTexture(new GPUTextureDescriptor
        {
            Label = $"WolfNET First Person Texture: {path}",
            Size = [image.NaturalWidth, image.NaturalHeight, 1], Format = "rgba8unorm",
            Usage = GPUTextureUsage.TextureBinding | GPUTextureUsage.CopyDst | GPUTextureUsage.RenderAttachment
        });
        try
        {
            using var window = _js.Get<Window>("window");
            using var bitmap = await window.CreateImageBitmap(image, new ImageBitmapOptions { PremultiplyAlpha = "premultiply" });
            try
            {
                token.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(_disposed, this);
                using var queue = _device.Queue;
                queue.CopyExternalImageToTexture(new GPUCopyExternalImageSourceInfo { Source = bitmap },
                    new GPUCopyExternalImageDestInfo { Texture = texture, PremultipliedAlpha = true },
                    new GPUExtent3DDict { Width = (uint)image.NaturalWidth, Height = (uint)image.NaturalHeight, DepthOrArrayLayers = 1 });
            }
            finally { bitmap.Close(); }
            return texture;
        }
        catch
        {
            texture.Destroy();
            texture.Dispose();
            throw;
        }
    }

    private void UpdateFirstPerson(FirstPersonView view)
    {
        // Three vec4f values, matching FirstPerson in first-person.wgsl.
        _firstPersonData[0] = _settings.Width;
        _firstPersonData[1] = _settings.Height;
        _firstPersonData[2] = (float)view.Kind;
        _firstPersonData[3] = view.WheelRotation;
        _firstPersonData[4] = view.HandMotion.X;
        _firstPersonData[5] = view.HandMotion.Y;
        _firstPersonData[6] = view.HandMotion.Z;
        _firstPersonData[7] = view.HandMotion.W;
        _firstPersonData[8] = view.InAir ? 1 : 0;
        _firstPersonData[9] = view.Crouching ? 1 : 0;
        Buffer.BlockCopy(_firstPersonData, 0, _firstPersonBytes, 0, _firstPersonBytes.Length);
        _firstPersonUpload!.WriteBytes(_firstPersonBytes);
        using var queue = _device!.Queue;
        queue.WriteBuffer(_firstPersonBuffer!, 0, _firstPersonUpload);
    }

    private void ReleaseFirstPerson()
    {
        _firstPersonBindGroup?.Dispose();
        _firstPersonBindGroup = null;
        _firstPersonView?.Dispose();
        _firstPersonDetailView?.Dispose();
        _firstPersonView = _firstPersonDetailView = null;
        _firstPersonSampler?.Dispose();
        _firstPersonSampler = null;
        _firstPersonTexture?.Destroy();
        _firstPersonTexture?.Dispose();
        _firstPersonDetailTexture?.Destroy();
        _firstPersonDetailTexture?.Dispose();
        _firstPersonTexture = _firstPersonDetailTexture = null;
    }
}
