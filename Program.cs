using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SpawnDev.BlazorJS;
using WolfNET;

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

Console.WriteLine("[C#] Starting BlazorJS runtime");

await builder.Build().BlazorJSRunAsync();