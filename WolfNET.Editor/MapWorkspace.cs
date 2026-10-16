using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WolfNET.Game;

namespace WolfNET.Editor;

public sealed record EditorDocument(string Name, MapDefinition Map, Dictionary<int, MapObjectDefinition> Objects, int[]? DeletedObjectIds = null);
public sealed record SaveRequest(EditorDocument Document, bool Overwrite = false);

public sealed partial class MapWorkspace
{
    public string Root { get; }
    private readonly SemaphoreSlim _saveLock = new(1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        AllowOutOfOrderMetadataProperties = true
    };

    public MapWorkspace(string root)
    {
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!Directory.Exists(Path.Combine(Root, "maps")))
            throw new InvalidDataException("EngineRoot must point to the engine's wwwroot directory.");
    }

    [GeneratedRegex("^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$")]
    private static partial Regex SafeName();

    private static string Name(string name)
    {
        if (string.IsNullOrEmpty(name) || !SafeName().IsMatch(name) ||
            Regex.IsMatch(name, "^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])$", RegexOptions.IgnoreCase))
            throw new InvalidDataException("Use 1–64 letters, digits, underscores or hyphens for the map name.");
        return name;
    }

    private string AssetPath(string relative)
    {
        var path = Path.GetFullPath(Path.Combine(Root, relative));
        if (!path.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Object paths must stay inside the engine assets directory.");
        // Do not follow junctions or symbolic links outside the workspace.
        for (var entry = new FileInfo(path) as FileSystemInfo; entry != null && entry.FullName != Root;
             entry = entry is FileInfo file ? file.Directory : ((DirectoryInfo)entry).Parent)
            if (entry.Exists && (entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked asset paths are not supported.");
        return path;
    }

    private T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json)
        ?? throw new InvalidDataException($"Empty JSON file: {Path.GetFileName(path)}.");

    public string[] Maps() => Directory.GetFiles(Path.Combine(Root, "maps"), "*.json")
        .Select(Path.GetFileNameWithoutExtension).OfType<string>().Order().ToArray();

    public object Library()
    {
        var objects = new List<object>();
        foreach (var kind in new[] { "passages", "walls", "sprites", "sprite-groups" })
        foreach (var file in Directory.GetFiles(Path.Combine(Root, "objects", kind), "*.json").Order())
            objects.Add(new { path = "../" + Path.GetRelativePath(Root, file).Replace('\\', '/'), definition = Read<MapObjectDefinition>(file) });
        var textures = Directory.GetFiles(Path.Combine(Root, "pics"), "*.png")
            .Select(file => "pics/" + Path.GetFileName(file)).Order().ToArray();
        return new { objects, textures };
    }

    public EditorDocument Load(string name)
    {
        var mapPath = AssetPath($"maps/{Name(name)}.json");
        var map = Read<MapDefinition>(mapPath);
        if (map.WorldMap is null || map.WorldMap.Any(column => column is null) || map.Objects is null)
            throw new InvalidDataException("Map grid and object index are required.");
        var objects = new Dictionary<int, MapObjectDefinition>();
        var pending = new Queue<int>(map.WorldMap.SelectMany(column => column).Distinct());
        while (pending.TryDequeue(out var id))
        {
            if (objects.ContainsKey(id)) continue;
            if (!map.Objects.TryGetValue(id, out var path) || string.IsNullOrWhiteSpace(path))
                throw new InvalidDataException($"Missing object path for ID {id}.");
            var definition = Read<MapObjectDefinition>(AssetPath(Path.Combine("maps", path)));
            if (definition.Id != id) throw new InvalidDataException($"Object ID {id} does not match its definition.");
            objects.Add(id, definition);
            foreach (var dependency in Dependencies(definition)) pending.Enqueue(dependency);
        }
        using var json = JsonDocument.Parse(File.ReadAllText(mapPath));
        var deleted = json.RootElement.TryGetProperty("editor", out var metadata) &&
            metadata.TryGetProperty("deletedObjectIds", out var ids) ? ids.Deserialize<int[]>() : null;
        var document = new EditorDocument(name, map, objects, deleted);
        Validate(document);
        return document;
    }

    private static IEnumerable<int> Dependencies(MapObjectDefinition definition) => definition switch
    {
        WallDefinition wall => [wall.PassageId],
        SpriteDefinition sprite => [sprite.PassageId],
        SpriteGroupDefinition group => new[] { group.PassageId }.Concat(
            (group.Sprites ?? throw new InvalidDataException("Sprite group needs a sprites list."))
            .Select(member => member?.SpriteId ?? throw new InvalidDataException("Null sprite group member."))),
        _ => []
    };

    private static Dictionary<int, MapObjectDefinition> Used(EditorDocument document)
    {
        if (document.Map?.WorldMap is null || document.Map.WorldMap.Any(column => column is null) || document.Objects is null)
            throw new InvalidDataException("Map grid and object definitions are required.");
        var result = new Dictionary<int, MapObjectDefinition>();
        var pending = new Queue<int>(document.Map.WorldMap.SelectMany(column => column).Distinct());
        while (pending.TryDequeue(out var id))
        {
            if (result.ContainsKey(id)) continue;
            if (!document.Objects.TryGetValue(id, out var definition) || definition is null || definition.Id != id)
                throw new InvalidDataException($"Missing or mismatched definition for object {id}.");
            result.Add(id, definition);
            foreach (var dependency in Dependencies(definition)) pending.Enqueue(dependency);
        }
        return result;
    }

    public object Validate(EditorDocument document)
    {
        Name(document.Name);
        var objects = Used(document);
        var compiled = new MapCompiler(objects, 64, 64).Compile(document.Map, 1);
        return new { valid = true, width = compiled.Renderer.Width, height = compiled.Renderer.Height,
            sprites = compiled.Renderer.Sprites.Count, objects = objects.Count };
    }

    private static (MapDefinition Map, Dictionary<string, string> Files) Package(EditorDocument document)
    {
        // Content-addressed files keep existing map snapshots intact until the map is committed.
        var files = new Dictionary<string, string>();
        var index = new Dictionary<int, string>();
        foreach (var (id, definition) in Used(document).OrderBy(pair => pair.Key))
        {
            var json = JsonSerializer.Serialize<MapObjectDefinition>(definition, Json);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..12].ToLowerInvariant();
            var path = $"objects/editor/{Name(document.Name)}/{id}-{hash}.json";
            index.Add(id, "../" + path);
            files.Add(path, json);
        }
        var map = document.Map with { Objects = index };
        var mapJson = JsonSerializer.SerializeToNode(map, Json)!.AsObject();
        if (document.DeletedObjectIds is { Length: > 0 })
            mapJson["editor"] = JsonSerializer.SerializeToNode(new { deletedObjectIds = document.DeletedObjectIds }, Json);
        files.Add($"maps/{Name(document.Name)}.json", mapJson.ToJsonString(Json));
        return (map, files);
    }

    public byte[] Export(EditorDocument document)
    {
        Validate(document);
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        foreach (var (path, json) in Package(document).Files)
        {
            using var writer = new StreamWriter(archive.CreateEntry(path).Open());
            writer.Write(json);
        }
        return stream.ToArray();
    }

    public async Task<object> SaveAsync(EditorDocument document, bool overwrite, CancellationToken token)
    {
        Validate(document);
        var package = Package(document);
        await _saveLock.WaitAsync(token);
        try
        {
            var mapPath = AssetPath($"maps/{document.Name}.json");
            if (File.Exists(mapPath) && !overwrite)
                throw new InvalidDataException("A map with this name already exists. Confirm replacement or choose another name.");
            foreach (var (relative, json) in package.Files.Where(pair => !pair.Key.StartsWith("maps/")))
            {
                var path = AssetPath(relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (!File.Exists(path)) await WriteAtomically(path, json, false, token);
            }
            // Publish only after every referenced object has been written successfully.
            await WriteAtomically(mapPath, package.Files[$"maps/{document.Name}.json"], overwrite, token);
            return new { path = $"maps/{document.Name}.json", map = package.Map };
        }
        finally { _saveLock.Release(); }
    }

    private static async Task WriteAtomically(string path, string content, bool overwrite, CancellationToken token)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, content, token);
            File.Move(temporary, path, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
