# Local development

Run all commands from the repository root. The engine is a Git submodule; initialize it with `git submodule update --init --recursive`.

## Localhost

The `https` launch profile serves the examples at **https://localhost:7279**:

```powershell
dotnet dev-certs https --trust
dotnet run --project WolfSlop.Examples/WolfSlop.Examples.csproj --launch-profile https
```

For browser automation, HTTP loopback also provides a secure context for WebGPU:

```powershell
dotnet run --project WolfSlop.Examples/WolfSlop.Examples.csproj --no-launch-profile --urls http://127.0.0.1:5195
```

## LAN access

The HTTPS profile binds to all IPv4 interfaces. Open `https://<server-LAN-IP>:7279` from another device on the same network. Use `ipconfig` on the server to find its current IPv4 address.

The localhost development certificate does not cover the LAN IP. Use a certificate containing that address and trust its issuing CA on each client. For example, with [mkcert](https://github.com/FiloSottile/mkcert) installed, substitute your server's current address below:

```powershell
mkcert -install
New-Item -ItemType Directory -Force .certs | Out-Null
mkcert -cert-file .certs/lan.pem -key-file .certs/lan-key.pem localhost 127.0.0.1 <server-LAN-IP>
$env:ASPNETCORE_Kestrel__Certificates__Default__Path = (Resolve-Path .certs/lan.pem).Path
$env:ASPNETCORE_Kestrel__Certificates__Default__KeyPath = (Resolve-Path .certs/lan-key.pem).Path
dotnet run --project WolfSlop.Examples/WolfSlop.Examples.csproj --launch-profile https
```

Install the public `rootCA.pem` from `mkcert -CAROOT` into each client's trusted certificate authorities. Keep the CA private key and server private key private. Regenerate the server certificate if its LAN IP changes.

If the page loads but graphics do not start, check `window.isSecureContext` and `navigator.gpu` in the client's browser console. HTTP LAN addresses do not provide a secure context. A trusted HTTPS connection, WebGPU support, and hardware acceleration are required. If the server is unreachable, check Windows Firewall and Wi-Fi client isolation.

Use `dotnet run` for LAN testing. When using `dotnet watch`, set `$env:DOTNET_WATCH_SUPPRESS_BROWSER_REFRESH = "true"` if browser refresh tries to connect to the client device's own localhost.

## Graphics settings

Edit [graphics-settings.json](../WolfSlop.Examples/wwwroot/graphics-settings.json) and reload the app to change rendering resolution, target frame rate, field of view, anisotropic filtering, render distance, or fog distance.

## Refresh the README screenshots

The gallery contains actual 1280 × 720 browser screenshots with the game HUD. Capture hides the page's navigation and help panels and selects interior viewpoints for Glassworks and Brickyard without editing the map files.

Start the app on HTTP loopback as shown above. In a second PowerShell terminal, launch a dedicated headless Edge instance with WebGPU and remote debugging enabled:

```powershell
$screenshotProfile = Join-Path (Get-Location) 'artifacts/readme-capture/edge-profile'
Start-Process -FilePath 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe' -WindowStyle Hidden -ArgumentList @(
    '--headless=new',
    '--enable-unsafe-webgpu',
    '--autoplay-policy=no-user-gesture-required',
    '--no-first-run',
    '--no-default-browser-check',
    '--remote-debugging-port=9260',
    ('--user-data-dir="' + $screenshotProfile + '"'),
    'about:blank'
)
```

With Node.js 22 or newer, run:

```powershell
node Tools/capture-map-screenshots.mjs 9260 http://127.0.0.1:5195
```

Images are written to [screenshots/](screenshots/). Append `showcase`, `glassworks`, `level1`, or `pine-loop` to capture a single map. The tool waits for rendered frames, checks for runtime errors, and waits for the race countdown to finish before capturing Pine Loop. Review the resulting images before committing them.

See [Tests/README.md](../Tests/README.md) for regression checks and performance measurements.
