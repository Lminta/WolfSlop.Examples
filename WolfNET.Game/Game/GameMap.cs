using System.Net.Http.Json;
using System.Numerics;
using WolfNET.Graphics;

namespace WolfNET.Game;

public sealed class GameMap : IMap
{
    private readonly MapCompiler _compiler;
    private MapDefinition _definition;
    private readonly DoorSystem _doors = new();
    public IReadOnlyCollection<DoorState> Doors => _doors.Doors;
    public int[][] WorldMap => Copy(_definition.WorldMap);
    public IReadOnlyDictionary<int, MapObjectDefinition> Objects { get; }
    public RenderMap RendererMap { get; private set; }
    public NavigationMap NavigationMap { get; } = new();
    public ulong Revision => RendererMap.Revision;
    public PlayerStartDefinition PlayerStart => _definition.PlayerStart;
    public Vector2 PlayerStartPosition => new(_definition.PlayerStart.X, _definition.PlayerStart.Y);
    public Vector2 PlayerStartDirection => Vector2.Normalize(new Vector2(
        _definition.PlayerStart.DirectionX, _definition.PlayerStart.DirectionY));

    public GameMap(MapDefinition definition, IReadOnlyDictionary<int, MapObjectDefinition> objects, int maxWidth, int maxHeight)
    {
        Objects = new System.Collections.ObjectModel.ReadOnlyDictionary<int, MapObjectDefinition>(
            new Dictionary<int, MapObjectDefinition>(objects));
        _compiler = new MapCompiler(Objects, maxWidth, maxHeight);
        _definition = definition with { WorldMap = Copy(definition.WorldMap) };
        var compiled = _compiler.Compile(_definition, 1);
        RendererMap = compiled.Renderer;
        NavigationMap.Replace(compiled.Walkable, compiled.Jumpable, compiled.Crouchable);
        _doors.Rebuild(_definition.WorldMap, Objects);
        RendererMap = _doors.Apply(RendererMap, NavigationMap);
    }

    public static async Task<GameMap> LoadAsync(HttpClient http, string path, int maxWidth, int maxHeight,
        CancellationToken cancellationToken = default)
    {
        var definition = await http.GetFromJsonAsync<MapDefinition>(path, cancellationToken)
            ?? throw new InvalidDataException($"Map '{path}' is empty.");
        var objects = await MapObjectLoader.LoadAsync(http, path, definition, cancellationToken);
        return new GameMap(definition, objects, maxWidth, maxHeight);
    }

    public void SetCell(int x, int y, int objectId)
    {
        if (_definition.WorldMap[x][y] == objectId) return;
        var cells = Copy(_definition.WorldMap);
        cells[x][y] = objectId;
        ReplaceMap(cells);
    }

    public void ReplaceMap(int[][] objectIds)
    {
        var next = _definition with { WorldMap = Copy(objectIds) };
        // Compile everything before publishing anything. Failed edits preserve both maps.
        var compiled = _compiler.Compile(next, checked(Revision + 1));
        _definition = next;
        NavigationMap.Replace(compiled.Walkable, compiled.Jumpable, compiled.Crouchable);
        _doors.Rebuild(_definition.WorldMap, Objects);
        RendererMap = _doors.Apply(RendererMap, NavigationMap);
        RendererMap = compiled.Renderer;
    }

    public bool Interact(Vector2 position, Vector2 direction, float radius) => _doors.Interact(position, direction, radius);

    public void UpdateDoors(float milliseconds, Vector2 position, float radius)
    {
        if (!_doors.Update(milliseconds, position, radius)) return;
        RendererMap = _doors.Apply(RendererMap with { Revision = checked(Revision + 1) }, NavigationMap);
    }

    private static int[][] Copy(int[][]? cells) => cells == null ? [] :
        cells.Select(column => column == null ? null! : (int[])column.Clone()).ToArray();
}
