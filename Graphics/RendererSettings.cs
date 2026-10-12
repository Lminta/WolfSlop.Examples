namespace WolfNET.Graphics;

public class RendererSettings(int width, int height)
{
    public int Width { get; } = width;
    public int Height { get; } = height;

    public float AR => Width / (float)Height;
}