namespace WolfNET.Game;

public interface IMap
{
    int[][] WorldMap { get; }
    IReadOnlyList<Sprite> Sprites { get; }
    ulong Revision { get; }
}
