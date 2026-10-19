# WolfNET

Browser raycasting engine built with Blazor WebAssembly and WebGPU. Requires the .NET 10 SDK and a browser/device with WebGPU support.

The solution contains sibling projects:

- `WolfNET.Game/`: the game, renderer, Blazor UI, launch profiles, and game assets in `wwwroot`.
- `WolfNET.Editor/`: the browser editor and its own web assets.
- `Tests/`: compiler, movement, editor, and browser regression checks.

Build both applications with `dotnet build WolfNET.slnx`.

Texture rendering uses mipmaps, trilinear filtering and 4× anisotropic filtering
to reduce moiré and shimmering on detailed walls, floors and ceilings. Sprite
filtering preserves alpha without adding dark borders.

Edit `WolfNET.Game/wwwroot/graphics-settings.json` to change render width, height,
target FPS, and `maxAnisotropy` (1–16, default 4). Reload the game to apply changes.

Shader and pixel regression tests can run independently of the game loop:

```powershell
node Tests/gpu-browser-check.mjs 9252 http://127.0.0.1:5195
```

Start the game at the specified address and Chromium with remote debugging on that
port first. For headless GPU testing, use `--enable-unsafe-webgpu` and
`--use-webgpu-adapter=swiftshader`. The pixel checks cover projection, sprite scaling,
alpha blending, and camera movement over distant checkerboards with and without mipmaps.

## Map editor

Open `WolfNET.slnx` in Rider to see the engine, browser map editor, and regression checks.
Start the **WolfNET.Editor** project, or run:

```powershell
dotnet run --project WolfNET.Editor/WolfNET.Editor.csproj
```

Open **http://127.0.0.1:5080**. Paint passages, walls, sprites and groups; edit object
definitions, resize maps, and set the player spawn. Collision and ray-blocking overlays
show invisible barriers. Maps are validated with the engine's own compiler before saving.

Save writes to the engine's `wwwroot/maps` with separate object snapshots, so other maps
keep their definitions. ZIP export includes the map and its object dependencies; extract
it into `wwwroot`. The editor reuses the engine's texture library.

To play a saved custom map, run the engine too:

```powershell
dotnet run --project WolfNET.Game/WolfNET.Game.csproj --launch-profile http
```

Use **Play** in the editor, or open `http://localhost:5025/?map=your-map-name`.
The editor's **Engine URL** field can point to another engine launch profile.
See [the editor guide](WolfNET.Editor/README.md) for shortcuts, imports, and deployment.

## Player movement

Click the game to capture the mouse. **W/S** move forward/back, **A/D** strafe,
and the **mouse** turns. **Esc** releases the mouse and clears held movement keys.

The player collides as a circle and slides along solid cells and rounded corners.
Each map sets `playerStart.collisionRadius` (default 0.15 map squares; greater than
zero and less than 0.5). The editor exposes it under map settings. Movement uses
short collision steps so fast motion cannot jump through walls.

Player movement is configured in each map's `playerStart` object alongside the spawn position and direction:

```json
"playerStart": {
  "x": 22,
  "y": 11.5,
  "directionX": -1,
  "directionY": 0,
  "moveSpeed": 5,
  "rotSpeed": 2,
  "collisionRadius": 0.15
}
```

`moveSpeed` is measured in map squares per second. `rotSpeed` is the maximum mouse rotation in radians per second. Mouse movement uses 0.0025 radians per pixel, capped at `rotSpeed` times the frame duration; excess movement is discarded. Both speeds are required, must be finite and nonnegative, and can be zero to disable that motion. Reload the map after editing its settings.

## Run on the local network

Both launch profiles bind to all IPv4 interfaces. In Rider, select the `https` launch profile, or run:

```powershell
dotnet run --project WolfNET.Game/WolfNET.Game.csproj --launch-profile https
```

On another device on the same network, open `https://<server-LAN-IP>:7279`. Use `ipconfig` on the server to find its IPv4 address. The address at setup time was `192.168.50.232`, so the URL was `https://192.168.50.232:7279`. This address can change with DHCP.

The HTTP profile (`dotnet run --project WolfNET.Game/WolfNET.Game.csproj --launch-profile http`) serves `http://<server-LAN-IP>:5025` for connectivity checks. WebGPU requires a secure context, so HTTP over a LAN IP will load the page but cannot render the game. See the [WebGPU specification](https://www.w3.org/TR/webgpu/#navigator-gpu).

## HTTPS certificate for LAN devices

The default ASP.NET development certificate is for localhost. For LAN rendering, use a certificate containing the server's LAN IP, trusted by every client device. Bypassing a certificate warning is not a substitute for establishing a trusted secure context.

One option is [mkcert](https://github.com/FiloSottile/mkcert). After installing it, run the following in PowerShell from the project directory (replace the IP with your current LAN IP):

```powershell
mkcert -install
New-Item -ItemType Directory -Force .certs | Out-Null
mkcert -cert-file .certs/lan.pem -key-file .certs/lan-key.pem localhost 127.0.0.1 192.168.50.232
$env:ASPNETCORE_Kestrel__Certificates__Default__Path = (Resolve-Path .certs/lan.pem).Path
$env:ASPNETCORE_Kestrel__Certificates__Default__KeyPath = (Resolve-Path .certs/lan-key.pem).Path
dotnet run --project WolfNET.Game/WolfNET.Game.csproj --launch-profile https
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
