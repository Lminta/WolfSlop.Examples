namespace WolfNET.Graphics;

public class RendererSettings(int width, int height, int targetFPS = 60)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public float AR => Width / (float)Height;
    
    public int TargetFPS { get; } = targetFPS;
    public float TargetFrameInterval => 1000.0f / TargetFPS;
    
    public int MaxMapWidth { get; init; } = 64;
    public int MaxMapHeight { get; init; } = 64;
}