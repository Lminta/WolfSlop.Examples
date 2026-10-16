using System.Net.Http.Json;
using System.Numerics;
using WolfNET.Game.Core;

namespace WolfNET.Game;

public class ConstantMap : ObjectBase, IMap
{
    public ulong Revision { get; private set; }
    // JSON uses the same column-major indexing as the game: WorldMap[x][y].
    public int[][] WorldMap { get; private set; }
    public IReadOnlyList<Sprite> Sprites { get; }
    public Vector2 PlayerStartPosition { get; }
    public Vector2 PlayerStartDirection { get; }

    private ConstantMap(Wolf game, MapDefinition definition) : base(game)
    {
        WorldMap = definition.WorldMap;
        Sprites = definition.Sprites.Select(sprite =>
            new Sprite(new Vector2(sprite.X, sprite.Y), sprite.TextureLayer)).ToArray();
        PlayerStartPosition = new Vector2(definition.PlayerStart.X, definition.PlayerStart.Y);
        PlayerStartDirection = Vector2.Normalize(new Vector2(
            definition.PlayerStart.DirectionX, definition.PlayerStart.DirectionY));
        Revision = 1;
    }

    public static async Task<ConstantMap> LoadAsync(
        Wolf game, HttpClient http, string path, CancellationToken cancellationToken = default)
    {
        var definition = await http.GetFromJsonAsync<MapDefinition>(path, cancellationToken)
            ?? throw new InvalidDataException($"Map '{path}' is empty.");

        definition.Validate(game.RendererSettings.MaxMapWidth, game.RendererSettings.MaxMapHeight);
        return new ConstantMap(game, definition);
    }

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
