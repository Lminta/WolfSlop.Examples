using System.Numerics;
using WolfNET.Game.Player;

namespace WolfNET.Game.Physics;

public static class CarContact
{
    public static void Resolve(CarObject first, CarObject second, NavigationMap map, bool firstStopped, bool secondStopped)
    {
        var a = first.Transform; var b = second.Transform;
        var normal = Vector2.Zero; var depth = float.PositiveInfinity;
        Span<Vector2> axes = stackalloc Vector2[] { a.Rotation, new(-a.Rotation.Y, a.Rotation.X),
            b.Rotation, new(-b.Rotation.Y, b.Rotation.X) };
        foreach (var axis in axes)
        {
            static float Radius(CarObject actor, Vector2 axis) => .5f *
                (actor.Settings.Length * MathF.Abs(Vector2.Dot(actor.Transform.Rotation, axis)) +
                 actor.Settings.Width * MathF.Abs(Vector2.Dot(new(-actor.Transform.Rotation.Y, actor.Transform.Rotation.X), axis)));
            var signed = Vector2.Dot(a.Position - b.Position, axis);
            var overlap = Radius(first, axis) + Radius(second, axis) - MathF.Abs(signed);
            if (overlap <= 0) return;
            if (overlap < depth) { depth = overlap; normal = signed >= 0 ? axis : -axis; }
        }
        var massA = firstStopped ? 0f : 1f;
        var massB = secondStopped ? 0f : 1f;
        if (massA + massB == 0) return;
        var boxA = new OrientedBoxCollider(map, first.Settings.Length, first.Settings.Width);
        var boxB = new OrientedBoxCollider(map, second.Settings.Length, second.Settings.Width);
        var correction = normal * (depth + .0001f);
        var moveA = correction * (massA / (massA + massB));
        var moveB = -correction * (massB / (massA + massB));
        if (!boxA.CanOccupy(a.Position + moveA, a.Rotation)) { moveA = Vector2.Zero; moveB = -correction; }
        if (!boxB.CanOccupy(b.Position + moveB, b.Rotation)) { moveB = Vector2.Zero; moveA = correction; }
        if (firstStopped || !boxA.CanOccupy(a.Position + moveA, a.Rotation)) moveA = Vector2.Zero;
        if (secondStopped || !boxB.CanOccupy(b.Position + moveB, b.Rotation)) moveB = Vector2.Zero;
        var velocityA = firstStopped ? Vector2.Zero : first.Controller.Velocity;
        var velocityB = secondStopped ? Vector2.Zero : second.Controller.Velocity;
        var closing = Vector2.Dot(velocityA - velocityB, normal);
        if (closing < 0)
        {
            var impulse = -closing / (massA + massB);
            velocityA += normal * (impulse * massA);
            velocityB -= normal * (impulse * massB);
        }
        first.Controller.ResolveCarContact(a.Position + moveA, velocityA);
        second.Controller.ResolveCarContact(b.Position + moveB, velocityB);
    }
}
