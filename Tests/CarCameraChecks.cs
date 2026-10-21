using System.Numerics;
using WolfNET.Game;
using WolfNET.Game.Player;
using WolfNET.Graphics;

internal static class CarCameraChecks
{
    public static int Run()
    {
        var count = 0;
        void Check(bool condition, string message)
        {
            count++;
            if (!condition) throw new Exception(message);
        }
        void Near(float actual, float expected, string message) =>
            Check(MathF.Abs(actual - expected) < .0001f, message);
        void PositionNear(Vector2 actual, Vector2 expected, string message) =>
            Check(Vector2.DistanceSquared(actual, expected) < .000001f, message);

        var settings = new CarSettings();
        var body = new Transform { Position = new(7.5f, 7.5f), Rotation = Vector2.UnitX };
        var camera = new CarCamera();
        var map = Map();
        var view = camera.View(body, true, map, settings);
        Near(view.Distance, settings.CameraFollowDistance, "Car chase camera lost configured follow distance.");
        Near(view.Elevation, settings.CameraElevation, "Car chase camera lost configured elevation.");
        PositionNear(view.Position, body.Position - Vector2.UnitX * settings.CameraFollowDistance,
            "Car chase camera is not behind the body heading.");
        var cockpit = camera.View(body, false, map, settings);
        PositionNear(cockpit.Position, body.Position, "Cockpit camera moved off the car.");
        Check(cockpit.Distance == 0 && cockpit.Elevation == 0, "Cockpit camera retained third-person offsets.");

        camera.Update(.01f, new(100, -50), settings);
        Near(camera.Yaw, -100 * settings.MouseSensitivity, "Car free look has the wrong horizontal sensitivity.");
        Near(camera.Pitch, 50 * settings.MouseSensitivity, "Car free look has the wrong vertical sensitivity.");
        PositionNear(body.Rotation, Vector2.UnitX, "Free look changed the car body heading.");
        var direction = camera.Direction(body);
        var plane = camera.Plane(body, .8f);
        Near(direction.Length(), 1, "Car view direction is not normalized.");
        Near(plane.Length(), .8f, "Car free look changed the field of view.");
        Near(Vector2.Dot(direction, plane), 0, "Car camera plane is not perpendicular to its view direction.");
        var orbit = camera.View(body, true, map, settings);
        PositionNear(orbit.Position, body.Position - direction * orbit.Distance,
            "Third-person free look did not orbit around the car.");

        var heldYaw = camera.Yaw;
        var heldPitch = camera.Pitch;
        camera.Update(settings.CameraRecenterDelay, Vector2.Zero, settings);
        Near(camera.Yaw, heldYaw, "Car free look recentered before its delay elapsed.");
        Near(camera.Pitch, heldPitch, "Car pitch recentered before its delay elapsed.");
        camera.Update(.25f, Vector2.Zero, settings);
        Near(camera.Yaw, heldYaw * MathF.Exp(-settings.CameraRecenterRate * .25f),
            "Car free look did not recenter exponentially after the delay.");
        camera.Update(.01f, new(1, 0), settings);
        heldYaw = camera.Yaw;
        camera.Update(settings.CameraRecenterDelay, Vector2.Zero, settings);
        Near(camera.Yaw, heldYaw, "New mouse movement did not restart the recenter delay.");

        var oneFrame = new CarCamera();
        var manyFrames = new CarCamera();
        oneFrame.Update(0, new(200, 100), settings);
        manyFrames.Update(0, new(200, 100), settings);
        oneFrame.Update(2.25f, Vector2.Zero, settings);
        for (var i = 0; i < 225; i++) manyFrames.Update(.01f, Vector2.Zero, settings);
        Near(oneFrame.Yaw, manyFrames.Yaw, "Car recentering depends on the frame rate.");
        Near(oneFrame.Pitch, manyFrames.Pitch, "Car pitch recentering depends on the frame rate.");

        camera.Update(0, new(-100000, -100000), settings);
        Near(camera.Yaw, settings.CameraMaxYawDegrees * MathF.PI / 180, "Positive car free-look yaw exceeded its limit.");
        Near(camera.Pitch, settings.CameraMaxPitchDegrees * MathF.PI / 180, "Positive car free-look pitch exceeded its limit.");
        camera.Update(0, new(100000, 100000), settings);
        Near(camera.Yaw, -settings.CameraMaxYawDegrees * MathF.PI / 180, "Negative car free-look yaw exceeded its limit.");
        Near(camera.Pitch, -settings.CameraMaxPitchDegrees * MathF.PI / 180, "Negative car free-look pitch exceeded its limit.");
        camera.Reset();
        Check(camera.Yaw == 0 && camera.Pitch == 0, "Reset left car free-look offsets behind.");

        foreach (var flags in new[] { RenderCellFlags.Wall, RenderCellFlags.StopsRay })
        {
            var blocked = Map((6, 7, flags));
            var stopped = camera.View(body, true, blocked, settings);
            Check(stopped.Distance is >= .3f and <= .41f,
                "Car chase camera passed through a wall or ray-blocking barrier.");
            Check(stopped.Position.X >= 7.099f, "Car chase camera lost its wall clearance.");
        }
        var edge = new Transform { Position = new(.5f, 7.5f), Rotation = Vector2.UnitX };
        var edgeView = camera.View(edge, true, map, settings);
        Check(edgeView.Distance == 0, "Car chase camera left the map boundary.");

        Console.WriteLine($"PASS: {count} car camera assertions (free look, recentering, views, field of view, and wall clearance).");
        return count;
    }

    private static RenderMap Map((int X, int Y, RenderCellFlags Flags)? obstacle = null)
    {
        const int size = 15;
        var cells = new RenderCell[size * size];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var flags = x == 0 || y == 0 || x == size - 1 || y == size - 1
                ? RenderCellFlags.Wall | RenderCellFlags.StopsRay
                : RenderCellFlags.SkyCeiling;
            cells[y * size + x] = new(0, 0, 0, flags);
        }
        if (obstacle is { } block) cells[block.Y * size + block.X] = new(0, 0, 0, block.Flags);
        return new(size, size, cells, [], [], new TextureCatalog([]), 1);
    }
}
