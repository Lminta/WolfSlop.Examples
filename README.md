# WolfNET

Browser raycasting engine built with Blazor WebAssembly and WebGPU. Requires the .NET 10 SDK and a browser/device with WebGPU support.

## Player movement

Player movement is configured in each map's `playerStart` object alongside the spawn position and direction:

```json
"playerStart": {
  "x": 22,
  "y": 11.5,
  "directionX": -1,
  "directionY": 0,
  "moveSpeed": 5,
  "rotSpeed": 2
}
```

`moveSpeed` is measured in map squares per second. `rotSpeed` is the maximum rotation in radians per second for mouse and keyboard combined. Mouse movement uses 0.0025 radians per pixel, capped at `rotSpeed` times the frame duration; excess movement is discarded. Both speeds are required, must be finite and nonnegative, and can be zero to disable that motion. Reload the map after editing its settings.

## Run on the local network

Both launch profiles bind to all IPv4 interfaces. In Rider, select the `https` launch profile, or run:

```powershell
dotnet run --launch-profile https
```

On another device on the same network, open `https://<server-LAN-IP>:7279`. Use `ipconfig` on the server to find its IPv4 address. The address at setup time was `192.168.50.232`, so the URL was `https://192.168.50.232:7279`. This address can change with DHCP.

The HTTP profile (`dotnet run --launch-profile http`) serves `http://<server-LAN-IP>:5025` for connectivity checks. WebGPU requires a secure context, so HTTP over a LAN IP will load the page but cannot render the game. See the [WebGPU specification](https://www.w3.org/TR/webgpu/#navigator-gpu).

## HTTPS certificate for LAN devices

The default ASP.NET development certificate is for localhost. For LAN rendering, use a certificate containing the server's LAN IP, trusted by every client device. Bypassing a certificate warning is not a substitute for establishing a trusted secure context.

One option is [mkcert](https://github.com/FiloSottile/mkcert). After installing it, run the following in PowerShell from the project directory (replace the IP with your current LAN IP):

```powershell
mkcert -install
New-Item -ItemType Directory -Force .certs | Out-Null
mkcert -cert-file .certs/lan.pem -key-file .certs/lan-key.pem localhost 127.0.0.1 192.168.50.232
$env:ASPNETCORE_Kestrel__Certificates__Default__Path = (Resolve-Path .certs/lan.pem).Path
$env:ASPNETCORE_Kestrel__Certificates__Default__KeyPath = (Resolve-Path .certs/lan-key.pem).Path
dotnet run --launch-profile https
```

Install the `rootCA.pem` from the directory printed by `mkcert -CAROOT` into each client's trusted certificate authorities, following that device's certificate trust instructions. On iOS, also enable full trust in Certificate Trust Settings. Share only the public `rootCA.pem`, never `rootCA-key.pem` or `lan-key.pem`. Regenerate the server certificate if the LAN IP changes. The environment variables above apply to the current terminal; configure them in Rider's run configuration if launching there.

## Connection troubleshooting

If another device cannot connect, check Windows Firewall and ensure the devices are on the same LAN without guest Wi-Fi/client isolation. If needed, run this in an administrator PowerShell to allow only local-subnet connections to these ports:

```powershell
New-NetFirewallRule -DisplayName 'WolfNET LAN development' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 5025,7279 -RemoteAddress LocalSubnet -Profile Any
```

To remove that rule later:

```powershell
Remove-NetFirewallRule -DisplayName 'WolfNET LAN development'
```
