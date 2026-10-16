namespace WolfNET.Game;

public interface INavigationMap
{
    bool IsWalkable(int x, int y);
}

// Its identity stays stable while edited map revisions replace the grid.
public sealed class NavigationMap : INavigationMap
{
    private bool[][] _walkable = [];

    internal void Replace(bool[][] walkable) => _walkable = walkable;

    public bool IsWalkable(int x, int y) =>
        x >= 0 && x < _walkable.Length && y >= 0 && y < _walkable[x].Length && _walkable[x][y];
}
