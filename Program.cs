using System.Numerics;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SpawnDev.BlazorJS;
using WolfNET;
using WolfNET.Game;
using WolfNET.Game.Graphics;
using WolfNET.Game.Graphics.Generators;
using WolfNET.Game.InputEvents;

Console.WriteLine("[C#] Creating Blazor host");

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddBlazorJSRuntime();

// Register HttpClient for dependency injection
builder.Services.AddScoped(
    _ => new HttpClient
    {
        BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
    });


builder.Services.AddSingleton<InputEventSystem>();
builder.Services.AddSingleton(new RendererSettings(800, 600));
builder.Services.AddSingleton<WebGpuRenderer>();
builder.Services.AddSingleton<Wolf>();
builder.Services.AddSingleton<IImageGenerator, LodeCaster>();
builder.Services.AddSingleton<KeyTracker>();
builder.Services.AddSingleton<IMap, ConstantMap>();

Console.WriteLine("[C#] Starting BlazorJS runtime");

await builder.Build().BlazorJSRunAsync();