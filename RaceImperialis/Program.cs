using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using System.Net.Http.Json;
using SpawnDev.BlazorJS;
using RaceImperialis;
using WolfNET.Engine.World;
using WolfNET.Engine.Graphics;
using WolfNET.Engine.Audio;
using WolfNET.Engine.Browser.Graphics;

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
var gameplaySettings = await http.GetFromJsonAsync<GameplaySettings>("gameplay-settings.json")
    ?? throw new InvalidDataException("Gameplay settings must contain a JSON object.");
gameplaySettings.Validate();
builder.Services.AddSingleton(gameplaySettings);
var soundSettings = await http.GetFromJsonAsync<SoundSettings>("audio-settings.json")
    ?? throw new InvalidDataException("Audio settings must contain a JSON object.");
soundSettings.Validate();
builder.Services.AddSingleton(soundSettings);
builder.Services.AddScoped<WebGpuRenderer>();
builder.Services.AddScoped<ISceneRenderer>(services => services.GetRequiredService<WebGpuRenderer>());
builder.Services.AddSingleton<GameSceneLoader>();
builder.Services.AddScoped<Wolf>();

Console.WriteLine("[C#] Starting BlazorJS runtime");

await builder.Build().BlazorJSRunAsync();
