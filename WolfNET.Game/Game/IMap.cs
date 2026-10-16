namespace WolfNET.Game;

public interface IMap
{
    int[][] WorldMap { get; }
    WolfNET.Graphics.RenderMap RendererMap { get; }
    NavigationMap NavigationMap { get; }
    ulong Revision { get; }
    PlayerStartDefinition PlayerStart { get; }
}
