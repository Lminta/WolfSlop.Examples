using System.Numerics;
using WolfNET.Game.Core;

namespace WolfNET.Game;

public class ConstantMap : ObjectBase, IMap
{
    public ulong Revision { get; private set; }
    
    public ConstantMap(Wolf game) : base(game)
    {
    }

    // Lode's sprite tutorial map, indexed as WorldMap[x][y].
    public int[][] WorldMap { get; private set; } =
    [
        [8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 4, 4, 6, 4, 4, 6, 4, 6, 4, 4, 4, 6, 4],
        [8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 8, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4],
        [8, 0, 3, 3, 0, 0, 0, 0, 0, 8, 8, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 6],
        [8, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 6],
        [8, 0, 3, 3, 0, 0, 0, 0, 0, 8, 8, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4],
        [8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 8, 4, 0, 0, 0, 0, 0, 6, 6, 6, 0, 6, 4, 6],
        [8, 8, 8, 8, 0, 8, 8, 8, 8, 8, 8, 4, 4, 4, 4, 4, 4, 6, 0, 0, 0, 0, 0, 6],
        [7, 7, 7, 7, 0, 7, 7, 7, 7, 0, 8, 0, 8, 0, 8, 0, 8, 4, 0, 4, 0, 6, 0, 6],
        [7, 7, 0, 0, 0, 0, 0, 0, 7, 8, 0, 8, 0, 8, 0, 8, 8, 6, 0, 0, 0, 0, 0, 6],
        [7, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 8, 6, 0, 0, 0, 0, 0, 4],
        [7, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 8, 6, 0, 6, 0, 6, 0, 6],
        [7, 7, 0, 0, 0, 0, 0, 0, 7, 8, 0, 8, 0, 8, 0, 8, 8, 6, 4, 6, 0, 6, 6, 6],
        [7, 7, 7, 7, 0, 7, 7, 7, 7, 8, 8, 4, 0, 6, 8, 4, 8, 3, 3, 3, 0, 3, 3, 3],
        [2, 2, 2, 2, 0, 2, 2, 2, 2, 4, 6, 4, 0, 0, 6, 0, 6, 3, 0, 0, 0, 0, 0, 3],
        [2, 2, 0, 0, 0, 0, 0, 2, 2, 4, 0, 0, 0, 0, 0, 0, 4, 3, 0, 0, 0, 0, 0, 3],
        [2, 0, 0, 0, 0, 0, 0, 0, 2, 4, 0, 0, 0, 0, 0, 0, 4, 3, 0, 0, 0, 0, 0, 3],
        [1, 0, 0, 0, 0, 0, 0, 0, 1, 4, 4, 4, 4, 4, 6, 0, 6, 3, 3, 0, 0, 0, 3, 3],
        [2, 0, 0, 0, 0, 0, 0, 0, 2, 2, 2, 1, 2, 2, 2, 6, 6, 0, 0, 5, 0, 5, 0, 5],
        [2, 2, 0, 0, 0, 0, 0, 2, 2, 2, 0, 0, 0, 2, 2, 0, 5, 0, 5, 0, 0, 0, 5, 5],
        [2, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0, 2, 5, 0, 5, 0, 5, 0, 5, 0, 5],
        [1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 5],
        [2, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0, 2, 5, 0, 5, 0, 5, 0, 5, 0, 5],
        [2, 2, 0, 0, 0, 0, 0, 2, 2, 2, 0, 0, 0, 2, 2, 0, 5, 0, 5, 0, 0, 0, 5, 5],
        [2, 2, 2, 2, 1, 2, 2, 2, 2, 2, 2, 1, 2, 2, 2, 5, 5, 5, 5, 5, 5, 5, 5, 5]
    ];

    // Lights, a row of pillars, and groups of barrels from Lode's sprite demo.
    public IReadOnlyList<Sprite> Sprites { get; } = new Sprite[]
    {
        new(new Vector2(20.5f, 11.5f), 10),
        new(new Vector2(18.5f, 4.5f), 10),
        new(new Vector2(10.0f, 4.5f), 10),
        new(new Vector2(10.0f, 12.5f), 10),
        new(new Vector2(3.5f, 6.5f), 10),
        new(new Vector2(3.5f, 20.5f), 10),
        new(new Vector2(3.5f, 14.5f), 10),
        new(new Vector2(14.5f, 20.5f), 10),
        new(new Vector2(18.5f, 10.5f), 9),
        new(new Vector2(18.5f, 11.5f), 9),
        new(new Vector2(18.5f, 12.5f), 9),
        new(new Vector2(21.5f, 1.5f), 8),
        new(new Vector2(15.5f, 1.5f), 8),
        new(new Vector2(16.0f, 1.8f), 8),
        new(new Vector2(16.2f, 1.2f), 8),
        new(new Vector2(3.5f, 2.5f), 8),
        new(new Vector2(9.5f, 15.5f), 8),
        new(new Vector2(10.0f, 15.1f), 8),
        new(new Vector2(10.5f, 15.8f), 8)
    };

    public void SetCell(int x, int y, int value)
    {
        if (WorldMap[x][y] == value)
            return;

        WorldMap[x][y] = value;
        Revision++;
    }

    public void ReplaceMap(int[][] worldMap)
    {
        WorldMap = worldMap;
        Revision++;
    }
}