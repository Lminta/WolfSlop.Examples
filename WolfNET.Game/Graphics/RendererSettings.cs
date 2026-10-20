namespace WolfNET.Graphics;

public class RendererSettings(int width = 1920, int height = 1080, int targetFPS = 60)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public float CameraPlaneScale => MathF.Tan(FovDegrees * MathF.PI / 360f);
    public float AR => Width / (float)Height;
    
    public int TargetFPS { get; } = targetFPS;
    public float TargetFrameInterval => 1000.0f / TargetFPS;

    public ushort MaxAnisotropy { get; init; } = 4;
    // Horizontal field of view. The default preserves the original 0.66 camera plane.
    public float FovDegrees { get; init; } = 66.85f;
    
    public int MaxMapWidth { get; init; } = 64;
    public int MaxMapHeight { get; init; } = 64;

    public void Validate()
    {
        if (MaxAnisotropy is < 1 or > 16)
            throw new ArgumentException("Graphics maxAnisotropy must be between 1 and 16.");
        if (TargetFPS <= 0)
            throw new ArgumentException("Graphics targetFPS must be positive.");
        if (!float.IsFinite(FovDegrees) || FovDegrees < 1 || FovDegrees > 179)
            throw new ArgumentException("Graphics fovDegrees must be between 1 and 179 degrees.");

        if (Width <= 0 ||
            Height <= 0)
        {
            throw new ArgumentException(
                "Renderer resolution must be positive.");
        }

        if (MaxMapWidth <= 0 ||
            MaxMapHeight <= 0)
        {
            throw new ArgumentException(
                "Maximum map dimensions must be positive.");
        }

        checked
        {
            _ = MaxMapWidth *
                MaxMapHeight;
        }
    }

}
