namespace WolfNET.Graphics;

public sealed record ShaderSources(string Raycast, string Walls, string Sprites)
{
    public static async Task<ShaderSources> LoadAsync(HttpClient http, CancellationToken cancellationToken = default)
    {
        var raycast = http.GetStringAsync("shaders/raycast.wgsl", cancellationToken);
        var walls = http.GetStringAsync("shaders/walls.wgsl", cancellationToken);
        var sprites = http.GetStringAsync("shaders/sprites.wgsl", cancellationToken);
        await Task.WhenAll(raycast, walls, sprites);
        return new(await raycast, await walls, await sprites);
    }
}
