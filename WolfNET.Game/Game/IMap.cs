namespace WolfNET.Game;

public interface IMap
{
    string GetImpactMaterial(int x, int y) => "hard";
    IReadOnlyList<System.Numerics.Vector2> CrowdPositions => [];
    bool Interact(System.Numerics.Vector2 position, System.Numerics.Vector2 direction, float radius);
    void UpdateDoors(float milliseconds, System.Numerics.Vector2 position, float radius);
    int[][] WorldMap { get; }
    WolfNET.Graphics.RenderMap RendererMap { get; }
    NavigationMap NavigationMap { get; }
    ulong Revision { get; }
    PlayerStartDefinition PlayerStart { get; }
}
