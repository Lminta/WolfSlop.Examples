namespace WolfNET.Graphics;

public class RendererSettings(int width, int height, int targetFPS = 60)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public float AR => Width / (float)Height;
    
    public int TargetFPS { get; } = targetFPS;
    public float TargetFrameInterval => 1000.0f / TargetFPS;
}