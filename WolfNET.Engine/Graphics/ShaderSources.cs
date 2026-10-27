namespace WolfNET.Engine.Graphics;

public sealed record ShaderSources(string Raycast, string Walls, string Sprites, string Overlays)
{
    public static async Task<ShaderSources> LoadAsync(HttpClient http, CancellationToken cancellationToken = default)
    {
        var raycast = http.GetStringAsync("_content/WolfNET.Engine/shaders/raycast.wgsl", cancellationToken);
        var walls = http.GetStringAsync("_content/WolfNET.Engine/shaders/walls.wgsl", cancellationToken);
        var sprites = http.GetStringAsync("_content/WolfNET.Engine/shaders/sprites.wgsl", cancellationToken);
        var firstPerson = http.GetStringAsync("_content/WolfNET.Engine/shaders/overlays.wgsl", cancellationToken);
        await Task.WhenAll(raycast, walls, sprites, firstPerson);
        return new(await raycast, await walls, await sprites, await firstPerson);
    }
}
