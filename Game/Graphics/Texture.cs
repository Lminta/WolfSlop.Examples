namespace WolfNET.Game.Graphics;

public record Texture()
{
    public required RendererSettings Settings { get; init; }
    public required byte[] Pixels { get; init; }
}