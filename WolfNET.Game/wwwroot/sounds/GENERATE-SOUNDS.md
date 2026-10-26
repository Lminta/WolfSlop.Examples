# WolfNET sound generation sheet

These are the original prompts for eleven sound categories. The generated
files now use numbered variants (`brakes-1.wav`, `brakes-2.wav`, etc.); active
filenames are listed in `../audio-settings.json`. The player uses engine idle/load
variant 1, the blue car variant 2, and the green car variant 3. Each car keeps its
matching pair throughout the race, including after pauses or leaving hearing range.
Impacts, braking, and crowds choose randomly without immediate repeats. Each
braking event keeps its chosen loop until braking ends.

`countdown-tick.wav` and `countdown-go.wav` are already synthesized in C# and
included. To regenerate them, run from the repository root:
`dotnet run --file Tools/generate-countdown-sounds.cs`. They are 700 Hz / 0.15 s
and 1200 Hz / 0.4 s, respectively, with smooth click-free envelopes.

Use **mono PCM WAV**, ideally 48 kHz / 16-bit, for positional sources. The engine
adds stereo position and distance falloff, so avoid baked-in panning, pass-bys,
distance, large reverberation, or a separate stereo ambience bed. If an export
has another extension, update its path in the settings. The current stereo
exports are automatically downmixed before spatial panning, without modifying
the originals. Music stays separate.

For loops, request seamless looping and steady intensity; listen to the end-to-start
join and trim any leading/trailing silence before exporting. Engine recordings
should sound like the **same compact, naturally aspirated four-cylinder rally car**.
Do not generate a complete acceleration sweep: C# blends the idle/load layers and
changes pitch continuously with throttle, actual acceleration, and speed.

| Filename | Duration / use | Prompt to paste |
| --- | --- | --- |
| `engine-idle.wav` | 4 s, seamless loop | Close dry recording of a compact naturally aspirated four-cylinder rally car engine idling steadily at approximately 1000 RPM. Consistent low mechanical rumble with subtle exhaust pulses, relaxed unloaded engine. Single centered mono source. Constant RPM and loudness, no revving or gear changes, no driving, no tire sound, no wind, no voices or music, no room echo. Seamless continuous loop with matching beginning and end, no fade in or fade out. |
| `engine-load.wav` | 4 s, seamless loop | The same compact naturally aspirated four-cylinder rally car engine held steadily around 3000 RPM under moderate throttle load on a dynamometer. Dry close recording, fuller throaty intake and exhaust growl, tight mechanical detail, consistent pitch and intensity. Single centered mono source. No acceleration sweep, no gear shifts, no turbo, no tires, no road or wind noise, no voices or music, no room echo. Seamless continuous loop, no fade in or fade out. |
| `brakes.wav` | 2 s, seamless loop | Sustained dry tire scrub and restrained rubber squeal from a small racing car braking firmly on asphalt. Consistent medium intensity, no collision, no engine, no deceleration or pass-by, no changing pitch, no voices or music. Single close centered mono source, minimal reverb. Seamless loop, no fade in or fade out. |
| `impact-hard.wav` | 0.7 s, one shot | One compact car bumper hitting a rigid concrete or stone barrier. Immediate short heavy dull thump, brief gritty scrape and tiny fragments, natural short decay. Moderate racing speed, no explosion, no glass shower, no multiple impacts, no engine, no speech or music. Dry close centered mono sound, no leading silence. |
| `impact-metal.wav` | 0.9 s, one shot | One compact car striking a steel guardrail at moderate speed. Immediate sharp metallic clang over a solid body thud, brief vibrating sheet metal rattle, short natural decay. One impact only, no bouncing sequence, no explosion or glass shower, no engine, no voices or music. Dry close centered mono sound, no leading silence. |
| `impact-wood.wav` | 0.7 s, one shot | One compact car bumper striking a thick wooden track barrier. Solid woody knock and low thud with a little splintering crack and brief debris, short natural decay. One moderate impact only, no falling tree or long destruction, no engine, no voices or music. Dry close centered mono sound, no leading silence. |
| `impact-rubber.wav` | 0.6 s, one shot | One compact racing car bumper hitting a stack of rubber safety tires. Deep soft rubbery thump, compressed rubber creak and a short shuffle of tires, muted and cushioned rather than metallic. Single moderate impact, no rebound sequence, no explosion, no engine, no voices or music. Dry close centered mono sound, no leading silence. |
| `impact-car.wav` | 0.8 s, one shot | One low to moderate speed contact between two compact racing cars. Dense bumper thud followed by a brief crumpling sheet metal crunch and a tiny body-panel rattle. One impact only, no explosion, no shattering windscreen, no skid, no engine, no voices or music. Dry close centered mono sound, no leading silence. |
| `countdown-tick.wav` | 0.15 s, one shot | One clean short electronic racing start-light countdown beep, approximately 700 Hz, precise immediate attack with a very short soft tail. Neutral lower-pitched cue, one beep only, centered mono, no voice, no music, no ambience, no leading silence. |
| `countdown-go.wav` | 0.4 s, one shot | One bright clear electronic racing start signal, approximately 1200 Hz, higher in pitch and slightly longer than a countdown beep. Single confident GO tone with immediate attack and short clean tail, centered mono, no spoken words, no melody, no music or ambience, no leading silence. |
| `crowd.wav` | 8 s, seamless loop | A small enthusiastic outdoor motorsport spectator group continuously cheering and applauding at a local race. Mixed indistinct human cheers and scattered hand claps, occasional brief whoop, steady moderate energy. No intelligible words, no announcer or megaphone, no chanting rhythm, no engines, no footsteps, no wind, no music. One compact centered mono group, dry and close with no stadium echo or baked-in distance. Seamless loop with no fade in or fade out. |

The countdown plays a tick on 5, 4, 3, 2, 1 and a higher GO tone from the
start/finish gantry. Crowd loops come from placed cheering-group objects.
Player and opponent cars each have their own engine and brake sources.
Impacts use collision closing speed to set loudness and have a per-source cooldown.

Optional: generate 2–3 variants of each impact and the crowd loop. Name them
`impact-metal-2.wav`, `impact-metal-3.wav`, `crowd-2.wav`, etc., and add the paths
to the corresponding arrays in `audio-settings.json` → `sfx.clips`. The backend
randomly chooses among the available variants without immediate repeats; crowd
loops start at different offsets. Engines use stable matching variants per car.

Settings: `volume: 0` disables all audio loading. `sfx.volume: 0` disables only
effects. Tune category volumes and distance limits in map blocks under `sfx`.
Restart/reload after adding clips or changing settings. Crowd and engine loops,
one shots, and their clocks pause with the game and are released on map changes.

## Mud driving

The active mud sounds are now your nine generated files:

- `mud-slow-1.wav`, `mud-slow-2.wav`, `mud-slow-3.wav`
- `mud-mid-1.wav`, `mud-mid-2.wav`, `mud-mid-3.wav`
- `mud-high-1.wav`, `mud-high-2.wav`, `mud-high-3.wav`

Each car's full-mud forward speed limit is divided into equal thirds. The first
third uses slow, the second mid, and the last high; exceeding the limit stays
high. Partial and full contact use identical thresholds. Each fresh mud entry
randomly chooses a variant number, avoiding an immediate repeat, and retains
that number across all three bands. The current clip loops until the speed band
changes or movement stops. Pausing, stopping on mud, or going out of hearing
range does not choose another number; leaving mud does. Clips play at their
recorded pitch. Tune `sfx.mudVolume` in the audio settings.

The older synthesized `mud.wav` and its generator are no longer used by the
active settings. For future replacement clips, adapt this original prompt to
slow, moderate, or high speed, keeping each four-second recording loopable:

> Close dry mono recording of car tires continuously rolling through soft wet
> mud at a steady moderate speed. Low gritty tire rumble, thick damp squelching,
> small irregular soft wet spatters, consistent texture and intensity. No engine,
> no brakes or squealing, no voices, music, wind or reverb. No pass-by or speed
> change. Seamless four-second loop with matching beginning and end, no leading
> or trailing silence and no fade in or fade out.
