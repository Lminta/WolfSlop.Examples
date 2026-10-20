using System.Net.Http.Json;
using WolfNET.Game.Player;
using WolfNET.Graphics;

namespace WolfNET.Game;

public sealed record LoadedScene(GameMap Map, CharacterAppearance Character);

public sealed class GameSceneLoader(HttpClient http, RendererSettings settings)
{
    public async Task<LoadedScene> LoadAsync(string mapPath, CancellationToken cancellationToken = default)
    {
        var map = await GameMap.LoadAsync(http, mapPath, settings.MaxMapWidth, settings.MaxMapHeight, cancellationToken);
        var character = await http.GetFromJsonAsync<SpriteDefinition>("player-character.json", cancellationToken)
            ?? throw new InvalidDataException("Player character definition is empty.");
        return new(map, new CharacterAppearance(character));
    }
}
