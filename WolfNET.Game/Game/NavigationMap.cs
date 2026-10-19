namespace WolfNET.Game;

public interface INavigationMap
{
    bool IsWalkable(int x, int y, bool inAir = false, bool crouching = false);
}

// Its identity stays stable while edited map revisions replace the grid.
public sealed class NavigationMap : INavigationMap
{
    private bool[][] _walkable = [];
    private bool[][] _jumpable = [];
    private bool[][] _crouchable = [];

    internal void Replace(bool[][] walkable, bool[][] jumpable, bool[][] crouchable)
    {
        _walkable = walkable;
        _jumpable = jumpable;
        _crouchable = crouchable;
    }

    public bool IsWalkable(int x, int y, bool inAir = false, bool crouching = false) =>
        x >= 0 && x < _walkable.Length && y >= 0 && y < _walkable[x].Length &&
        (_walkable[x][y] || (inAir && _jumpable[x][y]) || (!inAir && crouching && _crouchable[x][y]));
}
