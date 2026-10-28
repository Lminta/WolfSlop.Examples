using System.Numerics;
using WolfNET.Engine.Navigation;

namespace RaceImperialis.Game;

/// <summary>Adapts the example map's route artwork to the engine's path queries.</summary>
public sealed class RaceRoute(RaceMinimapDefinition definition)
{
    private readonly PolylinePath _path = new(definition.Route.Select(point => new Vector2(point[0], point[1])), loop: true);
    public float Length => _path.Length;
    public Vector2 Point(float distance) => _path.Point(distance);
    public float Project(Vector2 position, float? near = null) => _path.Project(position, near);
}
