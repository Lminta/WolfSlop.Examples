namespace WolfNET.Graphics;

public class RendererSettings(int width = 1920, int height = 1080, int targetFPS = 60)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public float AR => Width / (float)Height;
    
    public int TargetFPS { get; } = targetFPS;
    public float TargetFrameInterval => 1000.0f / TargetFPS;

    public ushort MaxAnisotropy { get; init; } = 4;
    
    public int MaxMapWidth { get; init; } = 64;
    public int MaxMapHeight { get; init; } = 64;
}
