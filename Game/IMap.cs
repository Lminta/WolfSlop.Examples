namespace WolfNET.Game;

public interface IMap
{
    int[][] WorldMap { get; }
    ulong Revision { get; }
}