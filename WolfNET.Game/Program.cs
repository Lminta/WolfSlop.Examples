using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using System.Net.Http.Json;
using SpawnDev.BlazorJS;
using WolfNET;
using WolfNET.Game;
using WolfNET.Graphics;

Console.WriteLine("[C#] Creating Blazor host");

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddBlazorJSRuntime();

var http = new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
};
builder.Services.AddSingleton(http);

var graphicsSettings = await http.GetFromJsonAsync<RendererSettings>("graphics-settings.json")
    ?? throw new InvalidDataException("Graphics settings must contain a JSON object.");


builder.Services.AddSingleton(graphicsSettings);
graphicsSettings.Validate();
builder.Services.AddSingleton<WebGpuRenderer>();
builder.Services.AddSingleton<ISceneRenderer>(services => services.GetRequiredService<WebGpuRenderer>());
builder.Services.AddSingleton<GameSceneLoader>();
builder.Services.AddSingleton<Wolf>();

Console.WriteLine("[C#] Starting BlazorJS runtime");

await builder.Build().BlazorJSRunAsync();
