// Run from the repository root: dotnet run --file Tools/generate-countdown-sounds.cs
// Produces dry mono PCM tones with short attack/release envelopes to avoid clicks.
using System;
using System.IO;
using System.Text;

var directory = Path.Combine(Directory.GetCurrentDirectory(), "WolfSlop.Examples", "wwwroot", "sounds");
if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("Run from the WolfSlop repository root.");
WriteTone("countdown-tick.wav", 700, .15);
WriteTone("countdown-go.wav", 1200, .4);

void WriteTone(string name, double frequency, double duration)
{
    const int rate = 48000;
    var samples = (int)Math.Round(rate * duration);
    using var writer = new BinaryWriter(File.Create(Path.Combine(directory, name)), Encoding.ASCII);
    writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples * 2);
    writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
    writer.Write((short)1); writer.Write((short)1); // PCM, mono
    writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
    writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2);
    for (var i = 0; i < samples; i++)
    {
        var attack = Math.Clamp(i / (rate * .005), 0, 1);
        var release = Math.Clamp((samples - 1 - i) / (rate * .025), 0, 1);
        var envelope = Math.Pow(Math.Sin(Math.PI * .5 * attack) * Math.Sin(Math.PI * .5 * release), 2);
        var phase = 2 * Math.PI * frequency * i / rate;
        var tone = (Math.Sin(phase) + .08 * Math.Sin(phase * 2)) / 1.08;
        writer.Write((short)Math.Round(short.MaxValue * .45 * envelope * tone));
    }
    Console.WriteLine($"Generated {name}: {frequency} Hz, {duration:0.00} s, mono 48 kHz / 16-bit PCM.");
}
