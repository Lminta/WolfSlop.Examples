using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using WolfNET.Editor;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.AllowOutOfOrderMetadataProperties = true);
builder.Services.AddSingleton(new MapWorkspace(
    builder.Configuration["EngineRoot"] ?? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "WolfNET.Game", "wwwroot"))));
var app = builder.Build();

app.Use(async (context, next) =>
{
    // The editor writes local files. Serve only through local host names, without CORS.
    if (context.Request.Host.Host is not ("localhost" or "127.0.0.1" or "::1"))
    {
        context.Response.StatusCode = 403;
        return;
    }
    try { await next(); }
    catch (Exception error) when (error is InvalidDataException or JsonException or ArgumentException or IOException)
    {
        if (context.Response.HasStarted) throw;
        context.Response.StatusCode = 400;
        await context.Response.WriteAsJsonAsync(new { error = error.Message });
    }
});
app.UseDefaultFiles();
app.UseStaticFiles();
var workspace = app.Services.GetRequiredService<MapWorkspace>();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(workspace.Root),
    RequestPath = "/engine"
});
app.MapGet("/api/library", (MapWorkspace store) => store.Library());
app.MapGet("/api/maps", (MapWorkspace store) => store.Maps());
app.MapGet("/api/maps/{name}", (string name, MapWorkspace store) => store.Load(name));
app.MapPost("/api/textures", async (TextureImport request, MapWorkspace store, CancellationToken token) =>
    await store.ImportTextureAsync(request, token));
app.MapPost("/api/validate", (EditorDocument document, MapWorkspace store) => store.Validate(document));
app.MapPost("/api/export", (EditorDocument document, MapWorkspace store) =>
    Results.File(store.Export(document), "application/zip", $"{document.Name}.zip"));
app.MapPost("/api/save", async (SaveRequest request, MapWorkspace store, CancellationToken token) =>
    await store.SaveAsync(request.Document, request.Overwrite, token));
app.Run();
