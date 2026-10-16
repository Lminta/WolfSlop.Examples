using System.Collections.ObjectModel;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WolfNET.Game;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(PassageDefinition), "passage")]
[JsonDerivedType(typeof(WallDefinition), "wall")]
[JsonDerivedType(typeof(SpriteDefinition), "sprite")]
[JsonDerivedType(typeof(SpriteGroupDefinition), "spriteGroup")]
public abstract record MapObjectDefinition
{
    public required int Id { get; init; }
    public required bool RaycastTransparent { get; init; }
    public required bool Collides { get; init; }
}

public sealed record PassageDefinition : MapObjectDefinition
{
    public string? FloorTexture { get; init; }
    public string? CeilingTexture { get; init; }
}

public sealed record WallDefinition : MapObjectDefinition
{
    public WallTextures? Textures { get; init; }
}

public sealed record WallTextures
{
    public string? North { get; init; }
    public string? East { get; init; }
    public string? South { get; init; }
    public string? West { get; init; }
}

public sealed record SpriteDefinition : MapObjectDefinition
{
    public string? Texture { get; init; }
    public required uint TextureLayer { get; init; }
    public required int PassageId { get; init; }
}

public sealed record SpriteGroupDefinition : MapObjectDefinition
{
    public required int PassageId { get; init; }
    public required SpriteGroupMember[] Sprites { get; init; }
}

public sealed record SpriteGroupMember
{
    public required int SpriteId { get; init; }
    public required MapOffset Offset { get; init; }
}

public sealed record MapOffset
{
    public required float X { get; init; }
    public required float Y { get; init; }
}

public static class MapObjectLoader
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        AllowOutOfOrderMetadataProperties = true
    };

    public static async Task<IReadOnlyDictionary<int, MapObjectDefinition>> LoadAsync(
        HttpClient http, string manifestPath, CancellationToken cancellationToken = default)
    {
        var files = await http.GetFromJsonAsync<string[]>(manifestPath, cancellationToken)
            ?? throw new InvalidDataException("Object manifest is empty.");
        var manifestUri = new Uri(http.BaseAddress
            ?? throw new InvalidOperationException("Map loading requires an HTTP base address."), manifestPath);
        var definitions = await Task.WhenAll(files.Select(async file =>
        {
            if (string.IsNullOrWhiteSpace(file))
                throw new InvalidDataException("Object manifest contains an empty filename.");
            var uri = new Uri(manifestUri, file);
            return await http.GetFromJsonAsync<MapObjectDefinition>(uri, Options, cancellationToken)
                ?? throw new InvalidDataException($"Object file '{file}' is empty.");
        }));

        var objects = new Dictionary<int, MapObjectDefinition>();
        foreach (var definition in definitions)
        {
            if (definition.Id < 0 || !objects.TryAdd(definition.Id, definition))
                throw new InvalidDataException($"Invalid or duplicate object ID {definition.Id}.");
        }
        if (objects.Count == 0)
            throw new InvalidDataException("Object manifest must define at least one object.");
        return new ReadOnlyDictionary<int, MapObjectDefinition>(objects);
    }
}
