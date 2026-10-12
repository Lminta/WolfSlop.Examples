using System.Drawing;

namespace WolfNET.Graphics.Utils;

public static class Primitives
{
    public static void DrawLine(Point start, Point end, Color color, RendererSettings settings, byte[] buffer)
    {
        int x = start.X;
        int y = start.Y;

        int dx = Math.Abs(end.X - start.X);
        int dy = Math.Abs(end.Y - start.Y);

        int sx = start.X < end.X ? 1 : -1;
        int sy = start.Y < end.Y ? 1 : -1;

        int error = dx - dy;

        while (true)
        {
            if (x >= 0 && x < settings.Width && y >= 0 && y < settings.Height)
            {
                int index = (y * settings.Width + x) * 4;

                buffer[index] = color.R;
                buffer[index + 1] = color.G;
                buffer[index + 2] = color.B;
                buffer[index + 3] = color.A;
            }

            if (x == end.X && y == end.Y)
                break;

            int e2 = 2 * error;

            if (e2 > -dy)
            {
                error -= dy;
                x += sx;
            }

            if (e2 < dx)
            {
                error += dx;
                y += sy;
            }
        }
    }
}