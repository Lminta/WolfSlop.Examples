using System.Runtime.CompilerServices;
using System.Text.Json;
using WolfNET.Engine.Graphics;

namespace RaceImperialis.Game;

/// <summary>Application-owned interpretation of the engine's open map metadata.</summary>
public static class RaceMetadata
{
    private sealed record Data(RaceDefinition? Race, RaceMinimapDefinition? Minimap);
    private static readonly ConditionalWeakTable<RenderMap, Data> Cache = new();
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private static T? Read<T>(IReadOnlyDictionary<string, JsonElement> metadata, string key) where T : class =>
        metadata.TryGetValue(key, out var value) ? value.Deserialize<T>(Options) : null;

    public static RaceDefinition? GetRace(this MapDefinition map) => Read<RaceDefinition>(map.Metadata, "race");
    public static RaceMinimapDefinition? GetRaceMinimap(this MapDefinition map) => Read<RaceMinimapDefinition>(map.Metadata, "raceMinimap");
    public static RaceDefinition? GetRace(this RenderMap map) => Get(map).Race;
    public static RaceMinimapDefinition? GetRaceMinimap(this RenderMap map) => Get(map).Minimap;

    private static Data Get(RenderMap map) => Cache.GetValue(map, source =>
    {
        var race = Read<RaceDefinition>(source.Metadata, "race");
        var minimap = Read<RaceMinimapDefinition>(source.Metadata, "raceMinimap");
        race?.Validate(source.Width, source.Height);
        minimap?.Validate(source.Width, source.Height);
        return new(race, minimap);
    });

    public static void ValidateExample(this TileMap map)
    {
        _ = Get(map.RendererMap);
        if (map.Spawn.Controller is not ("player" or "car"))
            throw new InvalidDataException("This example supports player and car controllers.");
        foreach (var sprite in map.Objects.Values.OfType<SpriteDefinition>())
        {
            if (sprite.Tag == "race-start-light" &&
                (sprite.Atlas is not { Directions: 1, Frames.Length: 2 } atlas || sprite.Animation != null ||
                 atlas.Frames.Any(frame => frame.Width != frame.Height)))
                throw new InvalidDataException("Race start lights require two square atlas frames without animation.");
            if (sprite.InteractionTarget is { } target &&
                !System.Text.RegularExpressions.Regex.IsMatch(target, @"\Amaps/[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}\.json\z"))
                throw new InvalidDataException("Example interaction targets must name a map in maps/.");
        }
    }
}
