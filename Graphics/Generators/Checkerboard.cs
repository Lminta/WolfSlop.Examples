namespace WolfNET.Graphics.Generators;

public class Checkerboard(int width, int height) : IImageGenerator
{
    public int Width { get; } = width;
    public int Height { get; } = height;

    public Texture Pass()
    {
        var pixels = new byte[Width * Height * 4];

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int index = (y * Width + x) * 4;

                bool dark = ((x / 8) + (y / 8)) % 2 == 0;

                pixels[index + 0] = dark ? (byte)38 : (byte)220;
                pixels[index + 1] = dark ? (byte)62 : (byte)225;
                pixels[index + 2] = dark ? (byte)96 : (byte)235;
                pixels[index + 3] = 255;
            }
        }

        Console.WriteLine(
            $"[TEXTURE] Generated {Width}x{Height} RGBA8 checkerboard");

        return new Texture
        {
            Settings = new RendererSettings(Width, Height),
            Pixels = pixels,
        };
    }
}