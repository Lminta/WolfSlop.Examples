using System.Text.Json;
using WolfNET.Audio;
using WolfNET.Game;

internal static class AudioChecks
{
    public static int Run(string webRoot)
    {
        var count = 0;
        void Check(bool value, string message) { count++; if (!value) throw new Exception(message); }
        void Near(double value, double expected, string message) => Check(Math.Abs(value - expected) < .001, message);
        MusicDefinition Definition(float fade = 3, params string[] tracks) => new()
            { Tracks = tracks.Length == 0 ? ["music/a.mp3", "music/b.mp3"] : tracks, CrossfadeSeconds = fade };

        var sound = JsonSerializer.Deserialize<SoundSettings>(File.ReadAllText(Path.Combine(webRoot, "sound-settings.json")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        sound.Validate();
        Near(sound.CountdownVolumeScale(0), sound.CountdownStartVolume, "Countdown must use its configured starting volume.");
        Near(sound.CountdownVolumeScale(1), 1, "Countdown must reach normal volume at GO.");
        var customSound = new SoundSettings { MasterVolume = .5f, MusicVolume = .4f,
            MusicCrossfadeSeconds = 0, CountdownStartVolume = .2f };
        Near(customSound.CountdownVolumeScale(.5f), .6, "Countdown ramp must honor its configured starting volume.");
        foreach (var invalid in new SoundSettings[] {
            new() { MasterVolume = float.NaN }, new() { MusicVolume = 1.1f },
            new() { MusicCrossfadeSeconds = -1 }, new() { CountdownStartVolume = -.1f } })
        {
            var rejected = false;
            try { invalid.Validate(); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Invalid sound settings must be rejected.");
        }
        var configuredBackend = new FakeBackend();
        var playlist = new MusicDefinition { Tracks = ["music/a.mp3", "music/b.mp3"] };
        using (var music = new MusicPlayer(configuredBackend, playlist, customSound))
        {
            music.SetPlaying(true);
            music.Update();
            Near(configuredBackend.Volume, .2, "Global master and music volumes must both apply.");
            configuredBackend.Streams[0].Position = 9;
            music.Update();
            Check(configuredBackend.Streams[1].Paused, "Global zero crossfade must disable overlap.");
        }
        configuredBackend = new();
        using (var music = new MusicPlayer(configuredBackend,
            playlist with { Volume = .8f, CrossfadeSeconds = 3 }, customSound))
        {
            music.SetPlaying(true);
            music.Update();
            Near(configuredBackend.Volume, .4, "Master volume must also scale a per-map volume override.");
            configuredBackend.Streams[0].Position = 9;
            music.Update();
            Check(!configuredBackend.Streams[1].Paused, "Per-map crossfade must override the global default.");
        }

        var backend = new FakeBackend();
        using (var music = new MusicPlayer(backend, Definition()))
        {
            var first = backend.Streams[0];
            var second = backend.Streams[1];
            music.Update();
            Check(first.Paused && second.Paused, "Music played before GO.");
            Check(backend.Volume == 0, "Music output was not muted before GO.");
            Check(backend.Streams.Count == 2, "Music must keep only two streams open.");
            music.SetPlaying(true);
            music.Update();
            Check(!first.Paused && second.Paused, "GO must start only the current track.");
            Near(backend.Volume, .65, "Music volume was not applied.");
            first.Position = 9;
            music.Update();
            Check(!second.Paused, "Next track did not start in the overlap window.");
            music.Update();
            first.Position = 10.5;
            second.Position = 1.5;
            music.Update();
            Near(first.Volume, .5, "Outgoing track did not fade out.");
            Near(second.Volume, .5, "Incoming track did not fade in.");
            music.SetPlaying(false);
            music.Update();
            Check(first.Paused && second.Paused && backend.Volume == 0, "Pause must stop both overlap streams.");
            Near(second.Position, 1.5, "Pause reset the track position.");
            music.SetPlaying(true);
            music.Update();
            Check(!first.Paused && !second.Paused, "Resume must continue both overlap streams.");
            Near(second.Volume, .5, "Resume lost crossfade progress.");
            second.Position = 3;
            music.Update();
            Check(first.Disposed, "Completed outgoing stream was not released.");
            Check(backend.Streams[2].Path == first.Path, "Queue did not wrap back to its first entry.");
            Near(second.Volume, 1, "Promoted stream must reach full volume.");
            Check(backend.Streams.Count(stream => !stream.Disposed) == 2, "Crossfade leaked a stream.");
        }
        Check(backend.Disposed && backend.Streams.All(stream => stream.Disposed), "Disposal must release backend and streams.");

        backend = new();
        using (var music = new MusicPlayer(backend, Definition()))
        {
            var first = backend.Streams[0];
            music.SetPlaying(true, .15f);
            music.Update();
            Check(!first.Paused && first.Position == 0, "Quiet playback must start at the beginning of the first track.");
            Near(backend.Volume, .65 * .15, "Countdown music must start quietly.");
            first.Position = 2;
            music.SetPlaying(true, .575f);
            Near(backend.Volume, .65 * .575, "Volume must increase while the same track keeps playing.");
            Check(first.PlayCalls == 1 && first.Position == 2, "Changing volume must not restart playback.");
            music.SetPlaying(false, .575f);
            music.Update();
            Check(first.Paused && backend.Volume == 0, "Countdown pause must mute and pause music.");
            music.SetPlaying(true, .575f);
            music.Update();
            Near(backend.Volume, .65 * .575, "Countdown resume must restore its intermediate volume.");
            Check(first.Position == 2, "Countdown resume must preserve the track position.");
            music.SetPlaying(true);
            Near(backend.Volume, .65, "GO must reach the configured music volume.");
            Check(first.PlayCalls == 2 && first.Position == 2, "GO must continue the countdown track without restarting it.");
        }

        backend = new();
        using (var music = new MusicPlayer(backend, Definition(0, "music/solo.mp3")))
        {
            music.SetPlaying(true);
            var first = backend.Streams[0];
            var second = backend.Streams[1];
            first.Position = 11.99;
            music.Update();
            Check(second.PlayCalls == 0, "Zero crossfade must not overlap tracks.");
            first.Ended = true;
            music.Update();
            Check(first.Disposed && !second.Paused, "Ended track was not replaced.");
            Check(backend.Streams[2].Path == second.Path, "One-track queue did not repeat.");
        }

        backend = new();
        using (var music = new MusicPlayer(backend, Definition(3, "bad", "good", "also-bad")))
        {
            backend.Streams[0].Failed = true;
            music.SetPlaying(true);
            music.Update();
            var good = backend.Streams[1];
            Check(backend.Streams[0].Disposed && !good.Paused, "Failed file must be skipped.");
            backend.Streams[2].Failed = true;
            music.Update();
            Check(backend.Streams[3].Path == "good", "Queue must skip previously failed entries.");
            good.Failed = true;
            music.Update();
            var streamCount = backend.Streams.Count;
            music.Update();
            Check(backend.Streams.Count == streamCount && backend.Streams.All(stream => stream.Disposed),
                "Entirely failed playlist must stop retrying and release all streams.");
        }

        backend = new();
        using (var music = new MusicPlayer(backend, Definition()))
        {
            music.SetPlaying(true);
            music.Update();
            var first = backend.Streams[0];
            var second = backend.Streams[1];
            second.DeferStart = true;
            first.Position = 9;
            music.Update();
            music.Update();
            Check(second.PlayCalls == 1, "Pending playback must not be requested repeatedly.");
            Near(first.Volume, 1, "Current track faded before the incoming stream was ready.");
            music.SetPlaying(false);
            second.CompleteStart();
            music.Update();
            Check(first.Paused && second.Paused, "Pause must win over pending playback.");
            music.Dispose();
            music.SetPlaying(true);
            music.Update();
            Check(backend.Streams.All(stream => stream.Disposed), "Disposed music must stay stopped.");
        }

        backend = new();
        using (var music = new MusicPlayer(backend, Definition(20)))
        {
            music.SetPlaying(true);
            music.Update();
            var first = backend.Streams[0];
            var second = backend.Streams[1];
            first.Position = 5;
            music.Update();
            Check(second.Paused, "Long fades must be limited to half the track duration.");
            first.Position = 6;
            music.Update();
            music.Update();
            second.Position = 3;
            music.Update();
            Near(second.Volume, .5, "Short-track fade was not clamped to six seconds.");
        }

        backend = new();
        using (var music = new MusicPlayer(backend, new MusicDefinition()))
        {
            music.SetPlaying(true);
            music.Update();
            Check(backend.Streams.Count == 0, "Empty playlists must not open streams.");
        }
        var map = JsonSerializer.Deserialize<MapDefinition>(File.ReadAllText(Path.Combine(webRoot, "maps/pine-loop.json")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var tracks = Directory.GetFiles(Path.Combine(webRoot, "music"), "*.mp3")
            .Select(path => "music/" + Path.GetFileName(path)).Order(StringComparer.Ordinal).ToArray();
        Check(map.Music != null && map.Music.Tracks.SequenceEqual(tracks), "Pine Loop must declare all supplied MP3s in order.");
        Console.WriteLine($"PASS: {count} portable audio assertions (queue, fades, pause, pending starts, failures, and cleanup).");
        return count;
    }

    private sealed class FakeBackend : IAudioBackend
    {
        public List<FakeStream> Streams { get; } = [];
        public float Volume;
        public bool Disposed;
        public IAudioStream OpenStream(string path)
        {
            var stream = new FakeStream(path);
            Streams.Add(stream);
            return stream;
        }
        public void SetVolume(float volume) => Volume = volume;
        public void Resume() { }
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeStream(string path) : IAudioStream
    {
        public string Path { get; } = path;
        public double Position;
        public bool Paused = true, Ended, Failed, Disposed, DeferStart;
        public float Volume;
        public int PlayCalls;
        private bool _starting, _desiredPlaying;
        public AudioStreamState State => new(Position, 12, Paused, Ended, _starting, Failed);
        public void Play()
        {
            PlayCalls++;
            _desiredPlaying = true;
            if (DeferStart) _starting = true;
            else Paused = false;
        }
        public void CompleteStart() { _starting = false; Paused = !_desiredPlaying; }
        public void Pause() { _desiredPlaying = false; Paused = true; }
        public void SetVolume(float volume, double rampSeconds = 0) => Volume = volume;
        public void Dispose() { Disposed = true; Pause(); }
    }
}
