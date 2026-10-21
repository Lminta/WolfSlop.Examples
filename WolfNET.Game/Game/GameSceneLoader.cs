using System.Net.Http.Json;
using WolfNET.Game.Player;
using WolfNET.Graphics;

namespace WolfNET.Game;

public sealed record LoadedScene(GameMap Map, ICharacterAppearance Character);

public sealed class GameSceneLoader(HttpClient http, RendererSettings settings, GameplaySettings? gameplay = null)
{
    public async Task<LoadedScene> LoadAsync(string mapPath, CancellationToken cancellationToken = default)
    {
        var map = await GameMap.LoadAsync(http, mapPath, settings.MaxMapWidth, settings.MaxMapHeight, cancellationToken);
        var car = map.PlayerStart.Controller == "car";
        var character = await http.GetFromJsonAsync<SpriteDefinition>(car ? "objects/sprites/sprite-directional-car.json" : "player-character.json", cancellationToken)
            ?? throw new InvalidDataException("Player character definition is empty.");
        return new(map, car ? new CarAppearance(character, (gameplay ?? new()).Car) : new CharacterAppearance(character));
    }
}
