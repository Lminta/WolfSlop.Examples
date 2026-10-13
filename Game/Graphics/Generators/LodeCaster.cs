using System.Drawing;
using WolfNET.Game.Graphics.Utils;

namespace WolfNET.Game.Graphics.Generators;

public class LodeCaster : IImageGenerator
{
    private readonly RendererSettings _settings;
    private readonly Wolf _game;
    private readonly IMap _map;

    private float _posX;
    private float _posY;
    private float _dirX;
    private float _dirY;
    
    private float _planeX;
    private float _planeY;

    public LodeCaster(RendererSettings settings, Wolf game, IMap map)
    {
        _settings = settings;
        _game = game;
        _map = map;
    }

    private void SetPosition(ICamera camera)
    {
        _posX = camera.Transform.Position.X;
        _posY = camera.Transform.Position.Y;
        _dirX = camera.Transform.Rotation.X;
        _dirY = camera.Transform.Rotation.Y;
        _planeX = camera.Plane.X;
        _planeY = camera.Plane.Y;
    }

    public Texture Pass()
    {
        SetPosition(_camera);
        float planeX = 0, planeY = 0.66f; //the 2d raycaster version of camera plane
        
        var result = new byte[_settings.Width * _settings.Height * 4];

        for (int x = 0; x < _settings.Width; x++)
        {
            //calculate ray position and direction
            float cameraX = 2 * x / (float)_settings.Width - 1; //x-coordinate in camera space
            float rayDirX = _dirX + planeX * cameraX;
            float rayDirY = _dirY + planeY * cameraX;
            //which box of the map we're in
            int mapX = (int) _posX;
            int mapY = (int) _posY;

            //length of ray from current position to next x or y-side
            float sideDistX;
            float sideDistY;

            //length of ray from one x or y-side to next x or y-side
            float deltaDistX = (rayDirX == 0) ? 1e30f : Math.Abs(1 / rayDirX);
            float deltaDistY = (rayDirY == 0) ? 1e30f : Math.Abs(1 / rayDirY);

            float perpWallDist;

            //what direction to step in x or y-direction (either +1 or -1)
            int stepX;
            int stepY;

            int hit = 0; //was there a wall hit?
            int side = 0; //was a NS or a EW wall hit?
            //calculate step and initial sideDist
            if (rayDirX < 0)
            {
                stepX = -1;
                sideDistX = (_posX - mapX) * deltaDistX;
            }
            else
            {
                stepX = 1;
                sideDistX = (mapX + 1.0f - _posX) * deltaDistX;
            }

            if (rayDirY < 0)
            {
                stepY = -1;
                sideDistY = (_posY - mapY) * deltaDistY;
            }
            else
            {
                stepY = 1;
                sideDistY = (mapY + 1.0f - _posY) * deltaDistY;
            }

            //perform DDA
            while (hit == 0)
            {
                //jump to next map square, either in x-direction, or in y-direction
                if (sideDistX < sideDistY)
                {
                    sideDistX += deltaDistX;
                    mapX += stepX;
                    side = 0;
                }
                else
                {
                    sideDistY += deltaDistY;
                    mapY += stepY;
                    side = 1;
                }

                //Check if ray has hit a wall
                if (_map.WorldMap[mapX][mapY] > 0) hit = 1;
            }

            //Calculate distance projected on camera direction. 
            if (side == 0) perpWallDist = (sideDistX - deltaDistX);
            else perpWallDist = (sideDistY - deltaDistY);

            //Calculate height of line to draw on screen
            int lineHeight = (int)(_settings.Height / perpWallDist);

            //calculate lowest and highest pixel to fill in current stripe
            int drawStart = -lineHeight / 2 + _settings.Height / 2;
            if (drawStart < 0) drawStart = 0;
            int drawEnd = lineHeight / 2 + _settings.Height / 2;
            if (drawEnd >= _settings.Height) drawEnd = _settings.Height - 1;

            //choose wall color
            Color color;
            switch (_map.WorldMap[mapX][mapY])
            {
                case 1: color = Color.Red; break; //red
                case 2: color = Color.Green; break; //green
                case 3: color = Color.Blue; break; //blue
                case 4: color = Color.White; break; //white
                default: color = Color.Yellow; break; //yellow
            }

            //give x and y sides different brightness
            if (side == 1)
            {
                color = Color.FromArgb(
                    color.A,
                    color.R / 2,
                    color.G / 2,
                    color.B / 2
                );
            }

            //draw the pixels of the stripe as a vertical line
            Primitives.DrawLine(new Point(x, drawStart), new Point(x, drawEnd), color, _settings, result);
        }
        
        return new Texture
        {
            Settings = _settings,
            Pixels = result,
        };
    }
    
    
}