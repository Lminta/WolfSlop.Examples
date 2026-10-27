# WolfNET

Browser raycasting engine built with Blazor WebAssembly and WebGPU. Requires the .NET 10 SDK and a browser/device with WebGPU support.

The solution contains:

- `WolfNET.Engine/`: reusable rendering, worlds, scenes/components, input, physics,
  cameras, retained UI, animation, and audio, packaged as a Razor class library.
- `WolfNET.Examples/`: exploration and racing, application rules/UI, launch profiles,
  and example assets in `wwwroot`. It references Engine.
- `Tests/EngineConsumer/`: an independent public API consumer that references Engine alone.
- `Tests/`: compiler, simulation, and browser regression checks.

Build the solution with `dotnet build WolfNET.slnx`.

Start the examples with `dotnet run --project WolfNET.Examples/WolfNET.Examples.csproj`.
Open `/` for exploration/racing or `/viewer` for a procedural scene with twelve
moving objects and an orbiting camera, using the engine without a player or race.
Open `/ui` for the interactive UI control gallery.

See [the engine guide](WolfNET.Engine/README.md) for consumption, extension points,
asset hosting, timing, and ownership. Engine shaders and JavaScript are served from
`_content/WolfNET.Engine/`; applications supply their own content. The engine
accepts arbitrary dynamic sprites and screen overlays, configurable input actions,
application-defined sound/surface identifiers and interaction commands, and custom
map metadata. Race rules, opponent AI, character art layouts, and cockpit/hand
presentation belong to Examples.

Screen UI is built with `WolfNET.Engine.UI`: a retained tree with anchored/stacked
layout, themes, text/wrapping, cropped images, vector widgets, buttons, toggles,
sliders, text input, scrolling, keyboard focus and modal scopes. Hands, cockpit,
lap timing, the race minimap, countdown and results now use this shared system.
The browser renderer uses a DPI-aware canvas plus an accessibility/input bridge,
independently of WebGPU world rendering. See the [UI guide](WolfNET.Engine/UI/README.md).

Maps now use optional `spawn` instead of `playerStart`; object interactions use
`interactionTarget` instead of `interactionMap`, and the example starting signal
uses `tag: "race-start-light"` instead of `raceStartLight: true`. Shipped maps,
objects, generation tools, and checks have been migrated together. Map metadata
such as `race` and `raceMinimap` is interpreted and validated by Examples.

The default level is **Showcase**, a gallery of materials, doors, objects, and
movement examples, with a parked car in its annex for entering Pine Loop.
**The Glassworks** is a compact abandoned botanical station
built around a central glasshouse. Explore its potting room and pump room, then
find the maintenance shortcut that links the rooms. See the
[Glassworks notes](WolfNET.Examples/wwwroot/maps/glassworks.md) for the layout.
**The Brickyard**, the original motor depot, and **Showcase** remain available
from the map selector. Open `?map=level1` to load The Brickyard directly; its
[level notes](WolfNET.Examples/wwwroot/maps/level1.md) describe the original routes.

**Pine Loop** is a 256 × 512 outdoor racing circuit with a five-tile starting grid
and road sections varying from two to four tiles, large infield loops, a long eastern straight, a 44-block tunnel,
pit boxes, and several spectator stands and landmarks. Continuous safety rails
close the cuts except for one dirt shortcut. Animated flagpoles and
cheering spectators join fake sponsor billboards and a starting signal. Separate
tire stacks protect the bends, and long buffer sprites keep a fixed orientation.
Find a parked car in the Glassworks east service room, the Brickyard motor yard,
or the Showcase annex, face it from nearby, and press **F** to enter Pine Loop.
Its
[map notes](WolfNET.Examples/wwwroot/maps/pine-loop.md) describe the route and assets.

Pine Loop uses a rotating local minimap with a 120-block viewing width. Its player
arrow stays below center and points up, leaving more room for upcoming bends.
The clean route outline includes the shortcut and start/finish marker without
scenery dots, fog, or visibility shading. Other maps retain their exploration
minimap. The race overlay has a transparent background and wider route lines,
and stays pinned to the canvas's upper-left corner, including when letterboxed.

Texture rendering uses mipmaps, trilinear filtering and 4× anisotropic filtering
to reduce moiré and shimmering on detailed walls, floors and ceilings. Sprite
filtering preserves alpha without adding dark borders.

Edit `WolfNET.Examples/wwwroot/graphics-settings.json` to change render width, height,
target FPS, `maxAnisotropy` (1–16, default 4), and `fovDegrees` (horizontal field of
view, 1–179°, default 66.85°). `renderDistance` sets the horizontal viewing radius
in map blocks (default **60**, positive and finite). `fogStartDistance` sets where
fog begins (default **40**; nonnegative and below `renderDistance`). Fog reaches
full strength at the cutoff. Walls, ground,
ceilings, and sprites use the same circular limit; distant sprites are skipped
before GPU upload. Reload the game to apply changes.

Scenery is indexed in 16-block spatial cells. Rendering visits nearby cells and,
on race maps, conservatively rejects scenery outside the horizontal view before
uploading it. Compact sort keys preserve the original alpha-blending order,
including equal-distance overlaps. The browser keeps this numeric index and
ordering work local, passing only selected sprite IDs back to C# for artwork
resolution. Wide props and atlas frames retain their full bounds;
exploration minimaps still include nearby scenery in every direction. Upload
buffers reuse capacity as the camera moves. WebGPU requests a high-performance
adapter and logs the adapter the browser actually supplies.
Frame deadlines use the browser's animation timestamps to stay aligned with
display refreshes, independently of main-thread callback delays.

Race progress uses an immutable spatial index of route segments, including when
cars take the shortcut or leave the racing line. Phase checks read the player
session directly; opponent standings are assembled when the race HUD updates.

Race loading prepares the AI corridor once for matching rival footprints and
skips collision math for known asphalt cells. Texture mip resizing/upload runs
in bounded browser batches; the loading UI throttles counter updates instead of
waiting on a paint timer for every layer. Texture quality and mip levels are retained.

To measure Pine Loop at the configured resolution and target FPS, start the
development server and Edge with hardware WebGPU and a remote-debugging port:

```powershell
node Tests/race-performance-browser-check.mjs 9252 http://127.0.0.1:5195 --output=artifacts/performance/after.json --min-fps=58
```

The check warms each authored viewpoint, samples five seconds, and records FPS,
submission and animation-callback intervals, CPU callback duration, sprite counts,
upload bytes, and adapter identification. `--views=start,tires`, `--seconds=10`,
and `--warmup=3` customize sampling. `--trace` saves a browser trace for diagnosing
CPU/GPU work; run timing acceptance without tracing or concurrent browser checks.
Add `--no-sfx` for a comparison with sound effects disabled and music unchanged;
save that run separately from the normal audio-enabled measurements.
The optional threshold requires every view to reach the requested FPS and a
95th-percentile submission interval of at most 20 ms. Headless results should also
be checked in the normal browser when assessing presentation smoothness.
Use `--views=start --drive` to benchmark acceleration along the starting straight;
add `--chase` for the third-person camera. Run each mode separately.

The numeric selection and sort also have a native reference check. Run the C#
suite first to generate its oracle fixtures, then run `node --test Tests/sprite-sort.test.mjs`.

Open the game with `?map=pine-loop&profile=1` for once-per-second `[PROFILE]`
console samples of candidate counts and CPU selection, upload, and submission
time. Submission timing does not wait for GPU completion. Normal play leaves this
instrumentation disabled. For visual comparisons, run both benchmark versions
with `--screenshots`; this freezes decorative animation at its first frame, then
compare the outputs with:

```powershell
node Tests/compare-race-screenshots.mjs artifacts/performance/before.json artifacts/performance/after.json
```

The image check excludes the FPS counter and allows a three-level RGB tolerance.

Run the regular regression checks from the repository root:

```powershell
dotnet run --project Tests/WolfNET.MapTests.csproj
dotnet run --project Tests/EngineConsumer/WolfNET.EngineConsumer.csproj
node --test Tests/sprite-sort.test.mjs Tests/texture-upload.test.mjs Tests/sfx-mixer.test.mjs Tests/ui.test.mjs
```

Run the C# suite first because it generates the sprite ordering reference files.
The retained scenarios and optional browser checks are listed in [Tests/README.md](Tests/README.md).

## Player movement

Click the game to capture the mouse. **W/S** move forward/back, **A/D** strafe,
and the **mouse** looks left/right and up/down. Vertical looking shifts the horizon
with simulated pitch limited to ±30°. **Esc** releases the mouse and clears held movement keys.

**Space** jumps while the mouse is captured. A jump follows a smooth up/down camera
arc. Map `spawn.jumpRise` sets upward camera travel (default 0.4 map squares,
from 0 to less than 0.5); `jumpLength` sets horizontal distance (default 2 map
squares, up to 64), and `jumpSpeedMultiplier` multiplies walking speed in the air
(default 1.25, must be positive). Flight time is calculated as
`jumpLength / (moveSpeed * jumpSpeedMultiplier)`. Zero length or walking speed
disables jumping. Hold does not repeat: release Space before jumping again.
The takeoff movement direction stays locked until landing; without movement input
the jump goes forward. Mouse look stays available and cannot steer the jump.

Every object type supports `jumpable` (default false). An object with both
`collides: true` and `jumpable: true` permits movement only while the player is
airborne. Groups use their own flags. Map boundaries must stay non-jumpable. Before
takeoff, the engine checks the destination using the player's full collision circle.
Any solid object overlapping the landing point stays solid for that jump, even if
jumpable. The path stops at its near edge; the player never enters it and snaps back.
Game code can read `Wolf.Player?.InAir` or `PlayerObject.InAir` to check jump status.

Hold **Alt** (left or right) to crouch. The camera lowers smoothly; map settings
`crouchDrop` (default 0.25, greater than zero and less than 0.5) and
`crouchSpeedMultiplier` (default 0.5, positive) control camera drop and walking speed.
Solid objects marked `crouchable: true` permit crouched movement beneath them;
ordinary walls remain solid. Releasing Alt beneath an obstacle keeps the player
crouched until the full collision circle clears it. Jumping is disabled while
crouched, and Alt cannot bypass obstacles in flight. Read `Wolf.Player?.Crouching`
or `PlayerObject.Crouching` for the current stance.

The player collides as a circle and slides along solid cells and rounded corners.
Each map sets `spawn.collisionRadius` (default 0.15 map squares; greater than
zero and less than 0.5). Movement uses
short collision steps so fast motion cannot jump through walls.

Player movement is configured in each map's `spawn` object alongside the spawn position and direction:

```json
"spawn": {
  "x": 6.5,
  "y": 26.5,
  "directionX": 1,
  "directionY": 0,
  "moveSpeed": 5,
  "rotSpeed": 2,
  "collisionRadius": 0.15,
  "jumpRise": 0.4,
  "jumpLength": 2,
  "jumpSpeedMultiplier": 1.25,
  "crouchDrop": 0.25,
  "crouchSpeedMultiplier": 0.5
}
```

`moveSpeed` is measured in map squares per second. `rotSpeed` is the maximum mouse rotation in radians per second. Mouse movement uses 0.0025 radians per pixel, capped at `rotSpeed` times the frame duration; excess movement is discarded. Both speeds are required, must be finite and nonnegative, and can be zero to disable that motion. Reload the map after editing its settings.

## Driving on Pine Loop

Entering Pine Loop selects `spawn.controller: "car"`; omitted controller
metadata keeps the walking character on other maps. **W** accelerates,
**A/D** or **left/right arrows** steer, **S** brakes and then reverses while held,
and **Space** brakes without reversing. Opposing W/S inputs also brake. There is
no jumping, crouching, strafing, damage, or rebound. The car coasts after throttle
release; glancing collisions preserve motion along a barrier.

The car uses a rotating **0.9 × 0.6 block** footprint and a 120 Hz simulation.
Its directional sprite follows the configured footprint width and length.
The 10° road-wheel steering limit and 0.65-block wheelbase give a low-speed
turning radius of approximately **3.7 blocks**. Steering builds progressively;
angular speed is limited by both travel speed and available tire grip, including
while braking, so the car cannot keep pivoting as it slows to a stop.
Engine acceleration tapers strongly at higher speeds, stretching out the approach to the speed limit. Asphalt provides stronger grip
and rolling friction than off-road terrain. Grass and mud share handling; the
`surfacePenaltyCoefficient` progressively removes excess speed rather than
snapping the car to a lower limit. Any mixed asphalt/off-road footprint receives
a fixed half penalty (`partialMudPenalty: 0.5`), independent of overlap area.
Its handling and limits lie halfway between the profiles: **13.5 blocks/s** forward
and **3 blocks/s** reverse by default. Unequal left/right mud contact pulls the
car toward that side, with strength controlled by `mudPullStrength` (radians/s
at the asphalt speed limit); stationary cars do not rotate. Fully paved and
fully off-road footprints use their full respective profiles.
Defaults are **19 blocks/s** forward and
**4 blocks/s** reverse on asphalt, **8** and **2** off-road.

Edit [gameplay-settings.json](WolfNET.Examples/wwwroot/gameplay-settings.json) and
reload to tune dimensions, engine acceleration, speed limits, braking, lateral
grip, rolling resistance, terrain penalty, steering, and cameras. Distances use
blocks, speeds blocks/second, accelerations blocks/second², response coefficients
1/second, and settings named `Degrees` use degrees. Passage `surface` metadata
(`asphalt`, `mud`, `grass`; default `mud`) supplies physics independently of floor
artwork, so painted grid tiles and the covered tunnel retain asphalt handling.

The `cameraShake` section tunes mud amplitude, entry delay, speed scaling,
fade rates and frequencies, plus impact amplitude, speed thresholds, duration,
frequencies and horizontal kick. Amplitudes use radians, durations and entry delay
use seconds, and frequencies use Hz. Set `mudAmplitude` or `impactAmplitude` to
zero to disable that effect. Partial mud contact retains half strength.

**V** toggles the transparent cockpit and chase view without changing motion.
The chase camera can orbit through full revolutions, including looking backward,
independently of steering, and recenters after one second without mouse movement.
The directional car sprite uses fixed planes within its eight view sectors.
First-person mouse camera control is disabled; the cockpit wheel still follows
the simulated steering angle. Switching to the cockpit resets orbit and pitch.
**Esc**, capture loss, or window blur pauses the car and retains momentum;
clicking the canvas resumes. Asset prompts are in
[cockpit-assets.md](WolfNET.Examples/wwwroot/textures/race/cockpit-assets.md).

The panel beneath the race minimap shows completed/total laps (for example `1/3`), total elapsed time,
current lap time, and the last completed lap. The first click starts a five-second
countdown with fading center-screen numbers and red starting lights. GO turns the
lights green, releases the controls, and starts timing. Countdown and timing pause
with driving. After the initial departure over the line, later finish crossings
count only after passing all invisible checkpoints in order. Reversing over the
line or repeatedly crossing it cannot add laps. An incomplete finish clears
checkpoint progress while time continues. The configured final lap freezes driving
and timing, releases the mouse, and shows total and individual lap times with a
restart button. Set `race.lapCount` in `WolfNET.Examples/wwwroot/gameplay-settings.json`
to change the race length (default: 3). Restarting loads a fresh grid and countdown;
leaving the map also resets the session.
Music starts with the countdown at 15% of its configured volume and smoothly
reaches normal volume at GO. Pausing driving also pauses music and the fade.
Pine Loop plays every MP3 currently
in `WolfNET.Examples/wwwroot/music` in order, repeating the queue with a three-second
crossfade (including the last track back to the first). Restarting resets music
to the first track; leaving the map releases both streams.
Edit `WolfNET.Examples/wwwroot/audio-settings.json` and reload to change
`volume` (0–1, default 1), `musicVolume` (0–1, default 0.65),
`musicCrossfadeSeconds` (nonnegative, default 3; zero disables blending), and
`countdownStartVolume` (0–1, default 0.15; a fraction of normal music volume).
`volume` scales all music, including any per-map volume override. Set it to `0`
to turn music off entirely: no audio context is created and tracks are not loaded.
Maps can define a `music` object with `tracks` (paths relative to `wwwroot`),
and optional `crossfadeSeconds` and `volume` overrides. Omitted overrides use
the sound settings; Pine Loop stores only its playlist. Omit `music` for silence.
Playback uses streamed media elements
and [Web Audio mixing](https://developer.mozilla.org/en-US/docs/Web/API/AudioContext/createMediaElementSource),
with only the current and upcoming tracks loaded. `Audio/MusicPlayer.cs` owns
the queue, fades, pause state, and failed-file handling in portable C#. Its
`IAudioBackend`/`IAudioStream` interfaces isolate playback primitives;
`BrowserAudioBackend` implements them using the existing C# browser bindings.
A future native host can supply another backend and call `MusicPlayer.Update`
from its game loop. Other audio channels can be added separately.

Sound effects run in C# through `Audio/SfxSystem.cs` and `ISfxBackend`.
Player and opponent engine layers react to throttle, acceleration, and speed;
brake loops require actual braking while moving.

Mixing policy remains in C#. `BrowserSfxBackend` stages changed voice values and
commits one reusable numeric buffer per frame to `sfx-mixer.js`. The browser reads
the audio clock once for that batch and preserves 25 ms gain/pan/pitch smoothing.
Unchanged music gains are not rescheduled, and nearest-crowd selection reuses
storage instead of sorting and allocating on each frame.

Mud sounds divide each car's configured full-mud forward speed limit into equal
thirds: slow below 1/3, mid from 1/3 to below 2/3, and high from 2/3 upward.
Partial and full off-road contact (including grass) use the same thresholds;
speeds above the mud limit stay in the high band. At the default 8 blocks/s
limit, the boundaries are approximately 2.67 and 5.33 blocks/s.
Each fresh mud entry picks a random variant number (1–3), avoiding the previous
entry's number. That number stays matched across `mud-slow-N.wav`,
`mud-mid-N.wav`, and `mud-high-N.wav` as speed changes. Sounds loop at their
recorded pitch. Pauses, stops on mud, and leaving hearing range preserve the
selection; returning fully to asphalt ends the visit. Stopped cars are silent.
Adjust `sfx.mudVolume` and the three clip arrays in `audio-settings.json`.
`sfx.mudEntrySeconds` requires continuous contact before the sound starts
(default 0.25 seconds); brief edge grazes stay silent. Returning to asphalt
resets the timer, and pauses do not advance it. Set this to 0 for immediate sound.
Quiet generated mud clips receive playback gain calculated from their mono RMS,
targeting -18 dBFS where peak headroom allows (maximum boost: 32×). Original
files remain unchanged. Moving mud uses the configured category volume in all
speed bands; there is no extra low-speed attenuation. Distance falloff still applies.

World impacts use the struck object's `impactMaterial` (`hard`, `metal`, `wood`, `rubber`), and car contacts
have their own cue. Closing speed controls impact volume, with cooldowns to avoid
repeated hits from continuous contact. Countdown beeps play at 5–1 and GO from
the start/finish gantry. Objects with `ambientSound: "crowd"` emit crowd loops;
Pine Loop's cheering groups already carry this metadata.

The rendered camera position and heading determine distance falloff and stereo
pan for every source, including ongoing impacts. Distances use map blocks.
`audio-settings.json` → `sfx` controls category volumes, ranges, voice limits,
and arrays of clip variants. Master `volume: 0` skips both music and SFX loading.
Impacts, braking, and crowds choose random variants without immediate repeats.
Each new braking event chooses once and keeps that loop until braking ends.
Each car keeps a distinct matching idle/load pair: the player uses variant 1,
the blue opponent variant 2, and the green opponent variant 3. Selections remain
stable after pauses and when cars leave and re-enter hearing range.
Stereo source assets are downmixed before positioning.
`countdown-tick.wav` (700 Hz, 0.15 s) and `countdown-go.wav` (1200 Hz, 0.4 s)
are synthesized mono WAVs; regenerate them from the repository root with
`dotnet run --file Tools/generate-countdown-sounds.cs`.

`sfx.volume: 0` skips only effects. Effects pause with mouse capture and are
released on map changes. Short effects are decoded once per scene; music streams.
Missing clips are reported once per load and skipped. Generate the eleven files
using the [ElevenLabs sound prompts](WolfNET.Examples/wwwroot/sounds/GENERATE-SOUNDS.md),
then put them in `WolfNET.Examples/wwwroot/sounds/` and reload.

Pine Loop's checkpoint sequence permits its existing Mud Express shortcut.
The map's optional `race` metadata stores a `startFinish` gate and ordered
`checkpoints`; each gate has `center`, forward `direction`, and `halfWidth`.
These gates create no sprites, collision geometry, or visible checkpoint markers.

Blue and green opponents share the player's car physics, collision footprint, and surface penalties.
Their asphalt top speeds follow the player's configured speed automatically: blue is **3 blocks/s faster**,
and green is **3 blocks/s slower** (28 and 22 blocks/s with the current 25 blocks/s player limit).
In normal difficulty, asphalt acceleration is tuned separately for corner exits: blue uses 1.2 times the player's
acceleration and green uses 3 times, compensating for its steeper taper near the lower speed limit.
Blue occasionally brakes too early for a corner and loses time; green drives a consistent, clean line.
Both follow a road-centered path, brake ahead of bends, avoid barriers, and check road clearance before passing or defending.
They launch in their own lanes, prioritize the player over rival duels, and abandon prolonged rival battles to resume racing.
When the player is ahead, they focus on catching up at their configured speeds.
The racing line smooths the raster route before estimating curvature. Steering anticipates bends
and compensates yaw response, targeting an average loop pace around 19 blocks/s on Pine Loop.
Passing and defending reserve grip and time to merge before bends; road and barrier checks remain active.
`race.opponentCornerSpeed` adjusts their cornering pace; `race.lapCount` applies to all three racers.
`opponents` in `gameplay-settings.json` contains `defaultDifficulty` (`normal` by default)
and separate `normal` and `easy` groups, each with `blue` and `green` profiles.
Each profile configures `maxSpeedOffset` relative to the player, `accelerationMultiplier`,
`cornerGripFraction`, `steeringCurvatureGain`, and `mistakes`. Mistake timing uses seconds:
`firstAfterSeconds`, `intervalSeconds`, `variationSeconds`, and `durationSeconds`;
`speedMultiplier` scales the planned speed during the mistake, and `enabled` can disable them.
Easy blue brakes early more often and for longer. Easy green remains consistent, with a top speed
5 blocks/s below the player (20 blocks/s at the default player limit) and gentler acceleration.
Easy targets an average loop pace around 16-17 blocks/s.
The Normal/Easy buttons beneath Pine Loop's description start a fresh race with the selected profiles.
The selection carries through race restarts and map switches; reloading the app uses the JSON default.
The Restart race button below the difficulty controls reloads the entire race, resetting all cars,
laps, timers, and the countdown while keeping the selected difficulty.
The Gap row measures the player's delay to the leader, with zero when leading. It starts measuring
separation at lap finishes and freezes between matching finishes. Pausing freezes the cars and timers.
Results show both racers' total and individual lap times. If the player finishes
first, the opponent continues while results are open and its final time fills in
when it finishes. Restart resets both cars, timers, and lap comparisons.

Run the normal checks with `dotnet run --project Tests/WolfNET.MapTests.csproj`.
The normal checks include portable C# music queue and lifecycle tests.
They also cover SFX mixing, collision telemetry/materials, engine load, braking,
countdown events, crowd limits, and missing clips.
Review actual music and sound playback manually in the browser, including
countdown audio, crossfades, stereo placement, and pause/resume.
With the game and a WebGPU browser running, use
`node Tests/car-driving-browser-check.mjs <debug-port> <game-url>` for actual
driving input, both views, pause/resume, cockpit alignment, and walking-map restoration.

## Run on the local network

Both launch profiles bind to all IPv4 interfaces. In Rider, select the `https` launch profile, or run:

```powershell
dotnet run --project WolfNET.Examples/WolfNET.Examples.csproj --launch-profile https
```

On another device on the same network, open `https://<server-LAN-IP>:7279`. Use `ipconfig` on the server to find its IPv4 address. The address at setup time was `192.168.50.232`, so the URL was `https://192.168.50.232:7279`. This address can change with DHCP.

The HTTP profile (`dotnet run --project WolfNET.Examples/WolfNET.Examples.csproj --launch-profile http`) serves `http://<server-LAN-IP>:5025` for connectivity checks. WebGPU requires a secure context, so HTTP over a LAN IP will load the page but cannot render the game. See the [WebGPU specification](https://www.w3.org/TR/webgpu/#navigator-gpu).

## HTTPS certificate for LAN devices

The default ASP.NET development certificate is for localhost. For LAN rendering, use a certificate containing the server's LAN IP, trusted by every client device. Bypassing a certificate warning is not a substitute for establishing a trusted secure context.

One option is [mkcert](https://github.com/FiloSottile/mkcert). After installing it, run the following in PowerShell from the project directory (replace the IP with your current LAN IP):

```powershell
mkcert -install
New-Item -ItemType Directory -Force .certs | Out-Null
mkcert -cert-file .certs/lan.pem -key-file .certs/lan-key.pem localhost 127.0.0.1 192.168.50.232
$env:ASPNETCORE_Kestrel__Certificates__Default__Path = (Resolve-Path .certs/lan.pem).Path
$env:ASPNETCORE_Kestrel__Certificates__Default__KeyPath = (Resolve-Path .certs/lan-key.pem).Path
dotnet run --project WolfNET.Examples/WolfNET.Examples.csproj --launch-profile https
```

Install the `rootCA.pem` from the directory printed by `mkcert -CAROOT` into each client's trusted certificate authorities, following that device's certificate trust instructions. On iOS, also enable full trust in Certificate Trust Settings. Share only the public `rootCA.pem`, never `rootCA-key.pem` or `lan-key.pem`. Regenerate the server certificate if the LAN IP changes. The environment variables above apply to the current terminal; configure them in Rider's run configuration if launching there.

## Connection troubleshooting

If the page loads but reports that WebGPU requires a secure connection, check
`window.isSecureContext` and `navigator.gpu` in the browser console on the client PC.
An HTTP LAN URL is not a secure context, even though HTTP localhost works on the
server PC. Use the HTTPS profile and trusted LAN certificate described above.
If the context is secure but `navigator.gpu` is absent, check that the client browser
supports WebGPU and has hardware acceleration enabled.

A refused WebSocket connection to `ws://localhost:<port>/` from
`aspnetcore-browser-refresh.js` is the development browser-refresh service trying
to connect to the client PC's own localhost. It is separate from WebGPU rendering.
For LAN testing, start with `dotnet run` as shown above rather than `dotnet watch`
or the IDE's browser-refresh integration. When using `dotnet watch`, set
`$env:DOTNET_WATCH_SUPPRESS_BROWSER_REFRESH = "true"` before starting it to suppress
automatic browser refresh.

If another device cannot connect, check Windows Firewall and ensure the devices are on the same LAN without guest Wi-Fi/client isolation. If needed, run this in an administrator PowerShell to allow only local-subnet connections to these ports:

```powershell
New-NetFirewallRule -DisplayName 'WolfNET LAN development' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 5025,7279 -RemoteAddress LocalSubnet -Profile Any
```

To remove that rule later:

```powershell
Remove-NetFirewallRule -DisplayName 'WolfNET LAN development'
```

Third-person view: press **V** while the mouse is captured to toggle modes. Mouse
look orbits behind the player, who turns with camera yaw. The camera sits 1.5
cells back with no shoulder offset, framing the player horizontally centered in
the lower part of the screen. Third-person movement and jump takeoff use the updated character
heading before the camera follows it. Standing/jumping sprites use 80% size; crouching uses 70% size. Walls shorten the camera
boom; it returns to its normal distance when clear. Very close walls hide the
avatar rather than showing it through the camera. First-person movement, jump
planning and collision stay at the player's position. Camera elevation uses the
engine's existing simulated pitch and stays within floor/ceiling height limits.

The character uses `WolfNET.Examples/wwwroot/player-character.json` and the v3 atlas.
Two-frame pairs are: rear walk 0–1, rear-left 2–3, rear-right 4–5, left strafe 6–7,
right strafe 8–9, rear crouch 10–11, rear jump 12–13; frame 14 is idle. Actual
movement advances walking/crouching frames every 160 ms; jumping uses the same
interval. Standing against a wall does not animate walking. Character frames load
with each scene, and the player sprite participates in normal wall/sprite occlusion.

Press **F** while facing a door from a cardinal neighboring tile to open/close it.
The showcase includes an opaque steel door at (18,12) and an alpha-glass door at
(24,18). Wait until fully open to walk through; closing doors avoid the player.
