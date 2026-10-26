using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;

namespace WolfNET.Audio;

/// <summary>Decoded short clips mixed by Web Audio, controlled entirely from C#.</summary>
internal sealed class BrowserSfxBackend : ISfxBackend
{
    private readonly AudioContext _context = new();
    private readonly HTMLCanvasElement _canvas;
    private readonly GainNode _output;
    private readonly AudioParam _volume;
    private readonly Dictionary<SfxCue, List<AudioBuffer>> _clips = [];
    private readonly Dictionary<AudioBuffer, float> _mudGains = [];
    private readonly SfxVariantSelector _variants = new();
    private readonly List<Voice> _voices = [];
    private bool _paused = true, _disposed;

    private BrowserSfxBackend(HTMLCanvasElement canvas)
    {
        _canvas = canvas;
        _output = _context.CreateGain();
        _volume = _output.Gain;
        _volume.Value = 0;
        using var destination = _context.Destination;
        _output.Connect(destination).Dispose();
        _canvas.OnClick += OnClick;
    }

    public static async Task<BrowserSfxBackend> CreateAsync(HttpClient http, HTMLCanvasElement canvas,
        SfxSettings settings, CancellationToken cancellationToken)
    {
        var backend = new BrowserSfxBackend(canvas);
        try
        {
            // Decode each unique file once, even if several cues share it.
            var decoded = new Dictionary<string, AudioBuffer?>();
            foreach (var (cue, files) in settings.Clips)
            {
                var buffers = new List<AudioBuffer>();
                backend._clips[cue] = buffers;
                foreach (var path in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!decoded.TryGetValue(path, out var buffer))
                    {
                        try
                        {
                            var bytes = await http.GetByteArrayAsync(path, cancellationToken);
                            using var data = (ArrayBuffer)bytes;
                            buffer = await backend._context.DecodeAudioData(data);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[SFX] Clip unavailable: {path}. {ex.Message}");
                            buffer = null;
                        }
                        decoded[path] = buffer;
                    }
                    if (buffer != null)
                    {
                        buffers.Add(buffer);
                        if (cue is SfxCue.MudSlow or SfxCue.MudMid or SfxCue.MudHigh && !backend._mudGains.ContainsKey(buffer))
                        {
                            var channels = new float[buffer.NumberOfChannels][];
                            for (var channelIndex = 0; channelIndex < channels.Length; channelIndex++)
                            {
                                using var channel = buffer.GetChannelData(channelIndex);
                                channels[channelIndex] = channel.ToArray();
                            }
                            backend._mudGains[buffer] = SfxClipLevels.MudGain(channels);
                        }
                    }
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            return backend;
        }
        catch { backend.Dispose(); throw; }
    }

    public ISfxVoice? Play(SfxCue cue, bool loop, float phase = 0, int emitterId = 0, int? variantIndex = null)
    {
        if (_disposed || !_clips.TryGetValue(cue, out var clips) || clips.Count == 0) return null;
        var index = variantIndex is { } selected
            ? (int)(Math.Abs((long)selected) % clips.Count) : _variants.Select(cue, clips.Count, emitterId);
        var gain = cue is SfxCue.MudSlow or SfxCue.MudMid or SfxCue.MudHigh
            ? _mudGains.GetValueOrDefault(clips[index], 1) : 1;
        var voice = new Voice(this, clips[index], loop, phase, gain);
        _voices.Add(voice);
        return voice;
    }

    private void OnClick(MouseEvent e) => _ = ChangeStateAsync(false);

    public void SetPaused(bool paused)
    {
        if (_disposed || _paused == paused) return;
        _paused = paused;
        _volume.Value = paused ? 0 : 1;
        _ = ChangeStateAsync(paused);
    }

    private async Task ChangeStateAsync(bool paused)
    {
        using var context = _context.JSRefCopy<AudioContext>();
        try
        {
            if (paused) await context.Suspend();
            else await context.Resume();
        }
        catch (Exception ex) { if (!_disposed) Console.WriteLine($"[SFX] Audio activation: {ex.Message}"); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _canvas.OnClick -= OnClick;
        foreach (var voice in _voices.ToArray()) voice.Dispose();
        foreach (var buffer in _clips.Values.SelectMany(clips => clips).Distinct()) buffer.Dispose();
        _clips.Clear();
        _mudGains.Clear();
        _volume.Dispose(); _output.Disconnect(); _output.Dispose();
        _ = CloseAsync();
        _context.Dispose();
    }

    private async Task CloseAsync()
    {
        using var context = _context.JSRefCopy<AudioContext>();
        try { await context.Close(); }
        catch (Exception ex) { Console.WriteLine($"[SFX] Audio close: {ex.Message}"); }
    }

    private sealed class Voice : ISfxVoice
    {
        private readonly BrowserSfxBackend _owner;
        private readonly AudioBufferSourceNode _source;
        private readonly GainNode _gain;
        private readonly StereoPannerNode _panner;
        private readonly AudioParam _volume, _pan, _pitch;
        private readonly float _clipGain;
        private bool _disposed;
        private float _lastVolume = float.NaN, _lastPan = float.NaN, _lastPitch = float.NaN;
        public bool Finished { get; private set; }

        public Voice(BrowserSfxBackend owner, AudioBuffer clip, bool loop, float phase, float clipGain)
        {
            _owner = owner;
            _clipGain = clipGain;
            _source = owner._context.CreateBufferSource();
            _source.Buffer = clip;
            _source.Loop = loop;
            _gain = owner._context.CreateGain();
            // Generated assets can be stereo. Downmix each positional source
            // before panning, preserving the original files on disk.
            _gain.ChannelCount = 1;
            _gain.ChannelCountMode = "explicit";
            _panner = owner._context.CreateStereoPanner();
            _volume = _gain.Gain; _pan = _panner.Pan;
            _pitch = _source.PlaybackRate ?? throw new InvalidOperationException("Audio source has no playback rate.");
            _volume.Value = 0;
            _source.Connect(_gain).Dispose();
            _gain.Connect(_panner).Dispose();
            _panner.Connect(owner._output).Dispose();
            _source.OnEnded += OnEnded;
            _source.Start(0, loop ? (float)(clip.Duration * Math.Clamp(phase, 0, .999f)) : 0);
        }

        private void OnEnded(Event e) => Finished = true;

        public void SetMix(float volume, float pan, float pitch)
        {
            if (_disposed) return;
            void Smooth(AudioParam parameter, float value, ref float previous)
            {
                if (MathF.Abs(previous - value) < .0001f) return;
                previous = value;
                parameter.SetTargetAtTime(value, _owner._context.CurrentTime, .025f).Dispose();
            }
            Smooth(_volume, Math.Clamp(volume, 0, 1) * _clipGain, ref _lastVolume);
            Smooth(_pan, Math.Clamp(pan, -1, 1), ref _lastPan);
            Smooth(_pitch, Math.Clamp(pitch, .5f, 2.5f), ref _lastPitch);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _source.OnEnded -= OnEnded;
            _source.Stop(0d);
            _source.Disconnect(); _gain.Disconnect(); _panner.Disconnect();
            _volume.Dispose(); _pan.Dispose(); _pitch.Dispose();
            _source.Dispose(); _gain.Dispose(); _panner.Dispose();
            _owner._voices.Remove(this);
        }
    }
}
