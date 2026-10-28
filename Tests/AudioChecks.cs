using System.Text.Json;
using WolfNET.Engine.Audio;
using WolfNET.Engine.World;

internal static class AudioChecks
{
    public static int Run(string webRoot)
    {
        var count = 0;
        void Check(bool value, string message) { count++; if (!value) throw new Exception(message); }
        void Near(double value, double expected, string message) => Check(Math.Abs(value - expected) < .001, message);
        var sound = JsonSerializer.Deserialize<SoundSettings>(File.ReadAllText(Path.Combine(webRoot, "audio-settings.json")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        sound.Validate();
        Near(sound.CountdownVolumeScale(0), sound.CountdownStartVolume, "Countdown must use its configured starting volume.");
        Near(sound.CountdownVolumeScale(1), 1, "Countdown must reach normal volume at GO.");
        var customSound = new SoundSettings { Volume = .5f, MusicVolume = .4f,
            MusicCrossfadeSeconds = 0, CountdownStartVolume = .2f };
        Near(customSound.CountdownVolumeScale(.5f), .6, "Countdown ramp must honor its configured starting volume.");
        foreach (var invalid in new SoundSettings[] {
            new() { Volume = float.NaN }, new() { MusicVolume = 1.1f },
            new() { MusicCrossfadeSeconds = -1 }, new() { CountdownStartVolume = -.1f } })
        {
            var rejected = false;
            try { invalid.Validate(); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Invalid sound settings must be rejected.");
        }
        var map = JsonSerializer.Deserialize<MapDefinition>(File.ReadAllText(Path.Combine(webRoot, "maps/pine-loop.json")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var tracks = Directory.GetFiles(Path.Combine(webRoot, "music"), "*.mp3")
            .Select(path => "music/" + Path.GetFileName(path)).Order(StringComparer.Ordinal).ToArray();
        Check(map.Music != null && map.Music.Tracks.SequenceEqual(tracks), "Pine Loop must declare all supplied MP3s in order.");
        Console.WriteLine($"PASS: {count} game audio configuration assertions.");
        return count;
    }
}
