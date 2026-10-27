using System.Numerics;
using System.Text.Json;
using WolfNET.Engine.Audio;
using WolfNET.Engine.World;
using WolfNET.Engine.Physics;
using WolfNET.Engine.Actors;
using WolfNET.Engine.Graphics;

internal static class SfxChecks
{
    public static async Task<int> RunAsync(HttpClient http, string webRoot)
    {
        var count = 0;
        void Check(bool value, string message) { count++; if (!value) throw new Exception(message); }
        void Near(float value, float expected, string message) => Check(MathF.Abs(value - expected) < .001f, message);
        var settings = JsonSerializer.Deserialize<SoundSettings>(File.ReadAllText(Path.Combine(webRoot, "audio-settings.json")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        settings.Validate();
        float[] quietTone = Enumerable.Range(0, 4800).Select(i => .01f * MathF.Sin(i * MathF.Tau / 48)).ToArray();
        var quietGain = SfxClipLevels.MudGain([quietTone]);
        Check(quietGain > 10 && quietGain < 20, "Quiet mud recordings must receive a meaningful playback boost.");
        Near(quietGain, SfxClipLevels.MudGain([quietTone, quietTone]), "Gain analysis must account for actual stereo downmix.");
        Near(SfxClipLevels.MudGain([new float[100]]), 1, "Silence must not be amplified.");
        float[] splash = new float[10000];
        splash[0] = .1f;
        Check(SfxClipLevels.MudGain([splash]) * .1f <= .90001f, "Normalization must leave peak headroom for splashes.");
        Near(SfxClipLevels.MudGain([new float[] { .0001f }]), 32, "Normalization must cap excessive boosts.");
        Check(settings.Sfx.Clips.Count == Enum.GetValues<SfxCue>().Length, "SFX JSON must cover every cue.");
        Check(settings.Sfx.Clips.Values.SelectMany(paths => paths).All(path => path.StartsWith("sounds/")), "SFX paths must point to the generated sound folder.");
        Check(settings.Sfx.Clips.Values.SelectMany(paths => paths).All(path => File.Exists(Path.Combine(webRoot, path))), "Every configured sound variant must exist.");
        var variants = new SfxVariantSelector(new Random(137));
        foreach (var cue in new[] { SfxCue.EngineIdle, SfxCue.EngineLoad })
            for (var carId = 0; carId < 3; carId++)
                Check(Enumerable.Range(0, 30).All(_ => variants.Select(cue, 3, carId) == carId), "Each car must keep its own matching idle/load engine variant.");
        foreach (var cue in new[] { SfxCue.CountdownTick, SfxCue.CountdownGo })
            Check(Enumerable.Range(0, 30).All(_ => variants.Select(cue, 3) == 0), "Countdown clips must remain consistent.");
        Check(variants.Select(SfxCue.EngineIdle, 1, 2) == 0, "Engines must fall back safely when fewer variants are supplied.");
        Check(SfxSystem.MudBand(0, 9) == SfxCue.MudSlow && SfxSystem.MudBand(2.99f, 9) == SfxCue.MudSlow,
            "First speed third must select slow mud.");
        Check(SfxSystem.MudBand(3, 9) == SfxCue.MudMid && SfxSystem.MudBand(5.99f, 9) == SfxCue.MudMid,
            "Second speed third must select mid mud, including its lower boundary.");
        Check(SfxSystem.MudBand(6, 9) == SfxCue.MudHigh && SfxSystem.MudBand(30, 9) == SfxCue.MudHigh,
            "Final speed third and speeds exceeding the mud limit must select high mud.");
        foreach (var cue in new[] { SfxCue.Brakes, SfxCue.ImpactHard, SfxCue.ImpactMetal, SfxCue.ImpactWood, SfxCue.ImpactRubber, SfxCue.ImpactCar, SfxCue.Crowd })
        {
            var picks = Enumerable.Range(0, 30).Select(_ => variants.Select(cue, 3)).ToArray();
            Check(picks.All(index => index >= 0 && index < 3) && picks.Distinct().Count() == 3, "Random sound selection must use the available variants.");
            Check(picks.Zip(picks.Skip(1)).All(pair => pair.First != pair.Second), "Random selection must avoid immediate repeats.");
            Check(picks.Zip(picks.Skip(1)).Any(pair => pair.Second != (pair.First + 1) % 3), "Variants must be randomized, not round-robin.");
        }
        Check(variants.Select(SfxCue.Brakes, 1) == 0 && variants.Select(SfxCue.Brakes, 1) == 0, "A single available variant must work repeatedly.");
        var right = SfxSystem.SpatialMix(Vector2.Zero, Vector2.UnitY, new(2, 0), 2, 45);
        var left = SfxSystem.SpatialMix(Vector2.Zero, Vector2.UnitY, new(-2, 0), 2, 45);
        Near(right.Pan, 1, "Right-hand source must pan right.");
        Near(left.Pan, -1, "Left-hand source must pan left.");
        Near(right.Gain, left.Gain, "Symmetric sources must have equal loudness.");
        Near(SfxSystem.SpatialMix(Vector2.Zero, Vector2.UnitY, new(0, 2), 2, 45).Pan, 0, "Forward source must remain centered.");
        Near(SfxSystem.SpatialMix(Vector2.Zero, -Vector2.UnitY, new(2, 0), 2, 45).Pan, -1, "Turning the listener must swap stereo sides.");
        Near(SfxSystem.SpatialMix(Vector2.Zero, Vector2.Zero, Vector2.Zero, 2, 45).Gain, 1, "Coincident listener/source must remain finite at full gain.");
        Check(SfxSystem.SpatialMix(Vector2.Zero, Vector2.UnitY, new(20, 0), 2, 45).Gain < right.Gain, "Distance must reduce volume.");
        Near(SfxSystem.SpatialMix(Vector2.Zero, Vector2.UnitY, new(45, 0), 2, 45).Gain, 0, "Source at maximum distance must be silent.");
        SfxFrame Frame(CarSfxState[]? cars = null, SfxImpact[]? impacts = null, RacePhase? phase = RacePhase.Racing,
            int? number = null, bool active = true, Vector2? forward = null, float seconds = .1f) => new(
                Vector2.Zero, forward ?? Vector2.UnitY, cars ?? [], impacts ?? [], phase, number, active, seconds);
        var backend = new FakeBackend();
        using (var system = new SfxSystem(backend, settings, [], Vector2.Zero))
        {
            system.Update(Frame(phase: RacePhase.Countdown, number: 5, active: false));
            Check(backend.Voices.Count == 0 && backend.Paused, "Paused countdown must not create any voices.");
            for (var number = 5; number >= 1; number--)
            {
                system.Update(Frame(phase: RacePhase.Countdown, number: number));
                system.Update(Frame(phase: RacePhase.Countdown, number: number));
                Check(backend.Voices.Count(voice => voice.Cue == SfxCue.CountdownTick) == 6 - number, "Each countdown number must sound exactly once.");
                if (number == 3)
                {
                    system.Update(Frame(phase: RacePhase.Countdown, number: 3, active: false));
                    system.Update(Frame(phase: RacePhase.Countdown, number: 3));
                    Check(backend.Voices.Count == 3, "Resuming countdown must not replay the current beep.");
                }
            }
            system.Update(Frame()); system.Update(Frame());
            Check(backend.Voices.Count(voice => voice.Cue == SfxCue.CountdownGo) == 1, "GO must sound exactly once.");
            foreach (var voice in backend.Voices) voice.Ended = true;
            system.Update(Frame());
            Check(backend.Voices.All(voice => voice.Disposed), "Finished one shots must be released.");
        }
        Check(backend.Disposed, "SFX disposal must release the backend.");

        backend = new();
        using (var system = new SfxSystem(backend, settings, [], Vector2.Zero))
        {
            CarSfxState Car(float throttle, float acceleration, float brake = 0, float speed = 10) =>
                new(0, new(2, 0), speed, 25, throttle, acceleration, brake);
            system.Update(Frame([Car(0, 0)]));
            var idle = backend.Voices.Single(voice => voice.Cue == SfxCue.EngineIdle);
            var idlePitch = idle.Pitch;
            for (var frame = 0; frame < 8; frame++) system.Update(Frame([Car(1, 8)]));
            var loadVoice = backend.Voices.Single(voice => voice.Cue == SfxCue.EngineLoad);
            Check(loadVoice.Volume > 0 && loadVoice.Pitch > idlePitch, "Acceleration/throttle must blend the loaded engine and raise pitch.");
            var loadedVolume = loadVoice.Volume;
            for (var frame = 0; frame < 4; frame++) system.Update(Frame([Car(0, -3)]));
            Check(loadVoice.Volume < loadedVolume, "Lifting throttle must unload the engine.");
            Check(!backend.Voices.Any(voice => voice.Cue == SfxCue.Brakes), "Coasting must not play brake sounds.");
            system.Update(Frame([Car(0, -8, 1)]));
            var brakes = backend.Voices.Single(voice => voice.Cue == SfxCue.Brakes);
            Check(brakes.Volume > 0 && brakes.Loop, "Moving under braking must play a brake loop.");
            system.Update(Frame([Car(0, 0, 1, 0)]));
            Check(brakes.Disposed, "Stationary brakes must stop squealing.");
            system.Update(Frame([new(0, new(100, 0), 10, 25, 1, 8, 0)]));
            Check(backend.Voices.All(voice => voice.Disposed), "Out-of-range car loops must be released.");
        }

        backend = new();
        using (var system = new SfxSystem(backend, settings, [], Vector2.Zero))
        {
            CarSfxState[] cars = [new(0, new(-3, 0), 5, 25, 0, 0, 0), new(1, new(3, 0), 5, 25, 0, 0, 0)];
            system.Update(Frame(cars));
            Check(backend.Commits == 1, "A mix update must publish all voices in one commit.");
            Check(backend.Voices.Count == 2, "Separate cars must own separate engine voices.");
            Check(backend.Voices.Select(voice => voice.EmitterId).SequenceEqual(new[] { 0, 1 }), "Engine voices must carry stable car IDs to variant selection.");
            Check(backend.Voices[0].Pan < 0 && backend.Voices[1].Pan > 0, "Opponent engines must be spatially separated.");
            system.Update(Frame(cars, forward: -Vector2.UnitY));
            Check(backend.Voices[0].Pan > 0 && backend.Voices[1].Pan < 0, "Camera direction must update ongoing voice panning.");
            system.Update(Frame(cars, active: false));
            var pausedCommits = backend.Commits;
            system.Update(Frame(cars, active: false));
            Check(backend.Commits == pausedCommits, "Paused frames must not publish voice updates.");
            Check(backend.Paused && backend.Voices.All(voice => !voice.Disposed), "Pause must preserve loop instances.");
            system.Update(Frame(cars));
            Check(!backend.Paused && backend.Voices.Count == 2, "Resume must reuse paused loop instances.");
        }

        backend = new();
        using (var system = new SfxSystem(backend, settings, [], Vector2.Zero))
        {
            string[] materials = ["hard", "metal", "wood", "rubber", "car"];
            system.Update(Frame(impacts: materials.Select((material, id) => new SfxImpact(id, new(2, 0), material, 12)).ToArray()));
            foreach (var cue in new[] { SfxCue.ImpactHard, SfxCue.ImpactMetal, SfxCue.ImpactWood, SfxCue.ImpactRubber, SfxCue.ImpactCar })
                Check(backend.Voices.Count(voice => voice.Cue == cue) == 1, "Collision material did not select its matching cue.");
            system.Update(Frame(impacts: [new(0, new(2, 0), "hard", 12)], seconds: .05f));
            Check(backend.Voices.Count == 5, "Continuous contact must be debounced.");
            system.Update(Frame(impacts: [new(0, new(2, 0), "hard", 12)], seconds: .25f));
            Check(backend.Voices.Count == 6, "A new impact after cooldown must be audible.");
            system.Update(Frame(impacts: [new(20, new(2, 0), "car", .5f)]));
            Check(backend.Voices.Count == 6, "Very weak contacts must remain silent.");
            system.Update(Frame(impacts: [new(21, new(2, 0), "car", 2)]));
            Check(backend.Voices[^1].Volume < backend.Voices[4].Volume, "Closing speed must scale impact volume.");
            system.Update(Frame(impacts: [new(22, new(100, 0), "metal", 12)]));
            Check(backend.Voices.Count == 7, "Out-of-range collisions must not allocate voices.");
        }

        backend = new();
        using (var system = new SfxSystem(backend, settings with { Sfx = settings.Sfx with { MaxCrowdVoices = 2 } },
            [new(2, 0), new(-3, 0), new(10, 0), new(100, 0)], Vector2.Zero))
        {
            system.Update(Frame());
            Check(backend.Voices.Count == 2 && backend.Voices.All(voice => voice.Cue == SfxCue.Crowd), "Only the nearest configured crowd groups should play.");
            Check(backend.Voices[0].Pan > 0 && backend.Voices[1].Pan < 0, "Crowd groups must pan independently.");
            Check(backend.Voices[0].Phase != backend.Voices[1].Phase, "Crowd loops should use different starting offsets.");
            system.Update(Frame() with { ListenerPosition = new(10, 0) });
            Check(backend.Voices.Where(voice => !voice.Disposed).Select(voice => voice.EmitterId).ToHashSet()
                .SetEquals(new[] { -3, -1 }), "Moving must select the new nearest crowds and release the distant voice.");
        }
        Check(backend.Voices.All(voice => voice.Disposed), "Disposal must release crowd loops.");
        backend = new();
        using (var system = new SfxSystem(backend, settings with { Sfx = settings.Sfx with { MaxVoices = 2 } }, [], Vector2.Zero))
        {
            system.Update(Frame(impacts: Enumerable.Range(0, 10).Select(id => new SfxImpact(id, Vector2.Zero, "car", 12)).ToArray()));
            Check(backend.Voices.Count == 2, "SFX polyphony must remain bounded.");
        }
        backend = new() { Missing = true };
        using (var system = new SfxSystem(backend, settings, [new(2, 0)], Vector2.Zero))
        {
            system.Update(Frame([new(0, Vector2.Zero, 4, 25, 1, 4, 1)], [new(10, Vector2.Zero, "car", 12)], RacePhase.Countdown, 5));
            Check(backend.Voices.Count == 0, "Missing sound files must be safely skipped.");
        }
        backend = new();
        using (var system = new SfxSystem(backend, settings with { Volume = 0 }, [Vector2.Zero], Vector2.Zero))
        {
            system.Update(Frame([new(0, Vector2.Zero, 10, 25, 1, 5, 1)], phase: RacePhase.Countdown, number: 5));
            Check(backend.Paused && backend.Voices.Count == 0, "Master volume zero must disable SFX.");
        }

        backend = new();
        using (var system = new SfxSystem(backend, settings with { Sfx = settings.Sfx with { MudEntrySeconds = 0 } }, [], Vector2.Zero, new Random(37)))
        {
            CarSfxState MudCar(int id, float speed, bool mud) => new(id, new(2, 0), speed, 25, 0, 0, 0, mud, 9);
            static bool IsMud(Voice voice) => voice.Cue is SfxCue.MudSlow or SfxCue.MudMid or SfxCue.MudHigh;
            Voice Latest(int id = 0) => backend.Voices.Last(voice => IsMud(voice) && voice.EmitterId == id);
            system.Update(Frame([MudCar(0, 2, true), MudCar(1, 2, true)]));
            var mudVoices = backend.Voices.Where(IsMud).ToArray();
            Check(mudVoices.Length == 2 && mudVoices.All(voice => voice.Loop && voice.Cue == SfxCue.MudSlow), "Moving cars with any mud contact must play a slow mud loop.");
            var selected = mudVoices[0].VariantIndex;
            var otherSelected = mudVoices[1].VariantIndex;
            Check(selected is >= 0 and <= 2 && otherSelected is >= 0 and <= 2, "Each mud entry must select a valid numbered variant.");
            Near(mudVoices[0].Volume, mudVoices[1].Volume, "Identical speed/contact must give the same mud volume for every car.");
            Near(mudVoices[0].Volume, settings.Volume * settings.Sfx.Volume * settings.Sfx.MudVolume,
                "Slow mud must use the full configured category volume without a hidden speed reduction.");
            system.Update(Frame([MudCar(0, 4, true), MudCar(1, 2, true)]));
            var mid = Latest();
            Check(mid.Cue == SfxCue.MudMid && mid.VariantIndex == selected && mid.Loop && mudVoices[0].Disposed,
                "Entering the mid band must change sound but preserve the visit's variant number.");
            Check(Latest(1).VariantIndex == otherSelected && !mudVoices[1].Disposed, "One car's band change must not affect another car's mud loop.");
            system.Update(Frame([MudCar(0, 6, true)]));
            var high = Latest();
            Check(high.Cue == SfxCue.MudHigh && high.VariantIndex == selected && mid.Disposed,
                "Entering the high band must use the matching variant and release mid.");
            Near(high.Pitch, 1, "Recorded mud speed bands must play at their original pitch.");
            system.Update(Frame([MudCar(0, 2, true)]));
            var slow = Latest();
            Check(slow.Cue == SfxCue.MudSlow && slow.VariantIndex == selected && high.Disposed, "Slowing down must return to the same numbered slow clip.");
            system.Update(Frame([MudCar(0, 2, true)], active: false));
            Check(backend.Paused && !slow.Disposed, "Pausing must preserve the mud loop.");
            system.Update(Frame([MudCar(0, 2, true)]));
            Check(Latest() == slow && !slow.Disposed, "Resuming must not select another mud variant.");
            system.Update(Frame([MudCar(0, 0, true)]));
            Check(slow.Disposed, "Stationary cars on mud must be silent.");
            system.Update(Frame([MudCar(0, 2, true)]));
            var restarted = Latest();
            Check(!restarted.Disposed && restarted.VariantIndex == selected, "Movement after stopping on mud must preserve the visit's variant.");
            system.Update(Frame([MudCar(0, 2, true) with { Position = new(100, 0) }]));
            Check(restarted.Disposed, "Out-of-range mud sources must release their voices.");
            system.Update(Frame([MudCar(0, 2, true)]));
            Check(Latest().VariantIndex == selected, "Returning to hearing range must preserve the mud entry variant.");
            system.Update(Frame([MudCar(0, 2, false)]));
            Check(Latest().Disposed, "Leaving mud must stop its loop.");
            system.Update(Frame([MudCar(0, 2, true)]));
            Check(Latest().VariantIndex != selected, "A fresh mud entry must select a different variant without an immediate repeat.");
        }

        backend = new();
        using (var system = new SfxSystem(backend, settings with { Sfx = settings.Sfx with { MudEntrySeconds = .25f } }, [], Vector2.Zero))
        {
            CarSfxState MudCar(int id = 0, bool mud = true) => new(id, new(2, 0), 2, 25, 0, 0, 0, mud);
            int MudCount() => backend.Voices.Count(voice => voice.Cue == SfxCue.MudSlow);
            system.Update(Frame([MudCar()], seconds: .249f));
            Check(MudCount() == 0, "Mud sound must stay silent before 0.25 seconds of contact.");
            system.Update(Frame([MudCar()], active: false, seconds: .25f));
            Check(MudCount() == 0, "Paused time must not advance the mud entry threshold.");
            system.Update(Frame([MudCar()], seconds: .001f));
            Check(MudCount() == 1, "Continuous contact must start mud audio at the threshold.");
            var qualified = backend.Voices.Last(voice => voice.Cue == SfxCue.MudSlow);
            system.Update(Frame([MudCar(mud: false)]));
            Check(qualified.Disposed, "Leaving mud must immediately stop qualified mud audio.");
            system.Update(Frame([MudCar()], seconds: .1f));
            system.Update(Frame([MudCar(mud: false)]));
            system.Update(Frame([MudCar()], seconds: .2f));
            Check(MudCount() == 1, "Separate brief grazes must not accumulate toward the threshold.");
            system.Update(Frame([MudCar(), MudCar(1)], seconds: .05f));
            Check(MudCount() == 2, "A new continuous visit must qualify independently of another car.");
            system.Update(Frame([MudCar(), MudCar(1)], seconds: .2f));
            Check(MudCount() == 3, "Each car must have its own mud entry timer.");
        }

        var nav = new NavigationMap();
        var walkable = Enumerable.Range(0, 10).Select(x => Enumerable.Range(0, 10).Select(y => x > 0 && y > 0 && x < 9 && y < 9).ToArray()).ToArray();
        nav.Replace(walkable, walkable.Select(c => new bool[c.Length]).ToArray(), walkable.Select(c => new bool[c.Length]).ToArray());
        var box = new OrientedBoxCollider(nav, 1.5f, .6f);
        var motion = box.MoveAndSlide(new(7, 5), Vector2.UnitX, new(3, 0), 0, new(6, 0));
        Check(motion.Impact is { CellX: 9, Speed: > 5.9f }, "World collision must report its actual struck cell and normal impact speed.");
        var keys = new FakeKeyboard { KeyState = new() { Forward = true } };
        using var car = new CarObject(keys, new FakeMouse(), nav, new()) { Transform = new() { Position = new(4, 5), Rotation = Vector2.UnitX } };
        car.Update(100);
        Check(car.Controller.Throttle == 1 && car.Controller.Acceleration > 0 && car.Controller.Braking == 0, "Engine telemetry must expose acceleration and throttle.");
        car.Controller.ResolveCarContact(new(4, 5), new(5, 0));
        keys.KeyState = new() { Jump = true };
        car.Update(100);
        Check(car.Controller.Braking == 1 && car.Controller.Acceleration < 0 && car.Controller.Throttle == 0, "Brake telemetry must come from simulation input.");
        using var other = new CarObject(new FakeKeyboard(), new FakeMouse(), nav, new()) { Transform = new() { Position = new(5.5f, 5), Rotation = Vector2.UnitX } };
        car.Controller.ResolveCarContact(new(4, 5), new(5, 0));
        other.Controller.ResolveCarContact(new(5.5f, 5), new(-5, 0));
        Check(CarContact.Resolve(car, other, nav, 1f / 120, false, false) is { Speed: > 9.9f }, "Car collisions must report closing speed independently of wall impacts.");

        var loaded = await new GameSceneLoader(http, new RendererSettings()).LoadAsync("maps/pine-loop.json");
        Check(loaded.Map.AmbientEmitters.Any(emitter => emitter.Cue == "crowd"), "Pine Loop must expose crowd emitters from authored objects.");
        Check(loaded.Map.Objects.Values.Select(obj => obj.ImpactMaterial).Distinct().Count() == 4, "Pine Loop must include hard, metal, wood, and rubber collision materials.");
        using var scene = new GameScene(loaded.Map, loaded.Character, new FakeKeyboard(), new FakeMouse(), new RendererSettings());
        var audioFrame = scene.CaptureAudioFrame(scene.CaptureFrame(), true, .1f);
        Check(audioFrame.Cars.Count == 3, "Scene audio must include the player and both opponents.");
        Console.WriteLine($"PASS: {count} SFX assertions (stereo, distance, engine load, brakes, impact materials, countdown, crowds, physics, and lifecycle).");
        return count;
    }

    private sealed class FakeBackend : ISfxBackend<SfxCue>
    {
        public List<Voice> Voices { get; } = [];
        public bool Paused, Disposed, Missing;
        public int Commits;
        public void Commit() => Commits++;
        public ISfxVoice? Play(SfxCue cue, bool loop, float phase = 0, int emitterId = 0, int? variantIndex = null)
        {
            if (Missing) return null;
            var voice = new Voice(cue, loop, phase, emitterId, variantIndex);
            Voices.Add(voice);
            return voice;
        }
        public void SetPaused(bool paused) => Paused = paused;
        public void Dispose() => Disposed = true;
    }
    private sealed class Voice(SfxCue cue, bool loop, float phase, int emitterId, int? variantIndex) : ISfxVoice
    {
        public SfxCue Cue { get; } = cue;
        public bool Loop { get; } = loop;
        public float Phase { get; } = phase;
        public int EmitterId { get; } = emitterId;
        public int? VariantIndex { get; } = variantIndex;
        public bool Ended, Disposed;
        public float Volume, Pan, Pitch;
        public bool Finished => Ended;
        public void SetMix(float volume, float pan, float pitch) { Volume = volume; Pan = pan; Pitch = pitch; }
        public void Dispose() => Disposed = true;
    }
}
