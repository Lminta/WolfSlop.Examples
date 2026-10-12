namespace WolfNET.Graphics;

public class RendererSettings(int width, int height, int targetFPS = 60)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public int TargetFPS { get; } = targetFPS;

    public float AR => Width / (float)Height;
}