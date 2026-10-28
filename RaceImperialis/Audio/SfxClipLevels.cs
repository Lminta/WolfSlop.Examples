namespace RaceImperialis.Audio;

/// <summary>Playback compensation for quiet generated mud recordings; source files stay intact.</summary>
public static class SfxClipLevels
{
    public static float MudGain(IReadOnlyList<float[]> channels)
    {
        if (channels.Count == 0 || channels[0].Length == 0) return 1;
        var length = channels[0].Length;
        if (channels.Any(channel => channel.Length != length))
            throw new ArgumentException("Audio channels must have equal lengths.", nameof(channels));
        double energy = 0, peak = 0;
        for (var i = 0; i < length; i++)
        {
            double mono = 0;
            foreach (var channel in channels) mono += channel[i];
            mono /= channels.Count;
            if (!double.IsFinite(mono)) return 1;
            energy += mono * mono;
            peak = Math.Max(peak, Math.Abs(mono));
        }
        var rms = Math.Sqrt(energy / length);
        if (rms < .000001 || peak == 0) return 1;
        // Aim for -18 dBFS RMS after stereo downmix. Limit boosts to +30 dB
        // and leave 10% peak headroom instead of clipping transient splashes.
        return (float)Math.Clamp(Math.Min(.12589254 / rms, .9 / peak), 1, 32);
    }
}
