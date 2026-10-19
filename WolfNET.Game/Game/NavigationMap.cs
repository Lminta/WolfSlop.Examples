namespace WolfNET.Game;

public interface INavigationMap
{
    bool IsWalkable(int x, int y, bool inAir = false);
}

// Its identity stays stable while edited map revisions replace the grid.
public sealed class NavigationMap : INavigationMap
{
    private bool[][] _walkable = [];
    private bool[][] _jumpable = [];

    internal void Replace(bool[][] walkable, bool[][] jumpable)
    {
        _walkable = walkable;
        _jumpable = jumpable;
    }

    public bool IsWalkable(int x, int y, bool inAir = false) =>
        x >= 0 && x < _walkable.Length && y >= 0 && y < _walkable[x].Length &&
        (_walkable[x][y] || (inAir && _jumpable[x][y]));
}
