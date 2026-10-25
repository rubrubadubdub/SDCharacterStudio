using System.Reflection;
using System.Runtime.Loader;
using System.Collections;
using System.Globalization;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using xTile;
using xTile.Tiles;

string gamePath = Path.GetFullPath(Environment.GetEnvironmentVariable("GamePath")
    ?? @"C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley");
// Game assemblies remain in the installation. Register before JIT-compiling Dumper.Run.
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    string path = Path.Combine(gamePath, name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};
try
{
    return Dumper.Run(args, gamePath);
}
catch (Exception ex)
{
    while (ex is TargetInvocationException && ex.InnerException is not null)
        ex = ex.InnerException;
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

internal static class Dumper
{
    private const string Usage = "Usage: xnbdump strings <asset> <keyPrefix> | map <asset> | maplayout <asset> <out.tsv> | texture <asset> <out.raw> | backtypes <asset> | vanilla-index <output.json>";

    public static int Run(string[] args, string gamePath)
    {
        int expectedArguments = args.FirstOrDefault() switch
        {
            "strings" or "maplayout" or "texture" => 3,
            "map" or "backtypes" or "vanilla-index" => 2,
            _ => 0
        };
        if (expectedArguments == 0 || args.Length != expectedArguments)
            throw new ArgumentException(Usage);
        string contentPath = Path.Combine(gamePath, "Content");
        if (!Directory.Exists(contentPath))
            throw new DirectoryNotFoundException($"Content directory not found: {contentPath}. Set GamePath to the installed game directory.");
        if (args[0] == "vanilla-index")
        {
            GenerateVanillaIndex(contentPath, args[1]);
            return 0;
        }
        string asset = args[1].Replace('\\', '/');
        if (asset.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase))
            asset = asset[..^4];
        string assetPath = Path.GetFullPath(Path.Combine(contentPath, asset + ".xnb"));
        if (!assetPath.StartsWith(Path.GetFullPath(contentPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(assetPath))
            throw new FileNotFoundException($"Vanilla XNB asset not found under Content: {args[1]}");

        var services = new GameServiceContainer();
        services.AddService<IGraphicsDeviceService>(new HeadlessGraphicsService());
        using var content = new ContentManager(services, contentPath);
        switch (args[0])
        {
            case "strings":
                foreach (var entry in content.Load<Dictionary<string, string>>(asset)
                             .Where(pair => pair.Key.StartsWith(args[2], StringComparison.Ordinal))
                             .OrderBy(pair => pair.Key, StringComparer.Ordinal))
                    Console.WriteLine($"{Escape(entry.Key)}\t{Escape(entry.Value)}");
                break;
            case "map":
                var buildings = content.Load<Map>(asset).GetLayer("Buildings")
                    ?? throw new InvalidDataException("Map has no Buildings layer.");
                for (int y = 0; y < buildings.LayerHeight; y++)
                for (int x = 0; x < buildings.LayerWidth; x++)
                {
                    var tile = buildings.Tiles[x, y];
                    if (tile is not null && tile.Properties.TryGetValue("Action", out var action))
                        Console.WriteLine($"{x}\t{y}\t{Escape(action.ToString())}");
                }
                break;
            case "maplayout":
                var map = content.Load<Map>(asset);
                using (var writer = OpenTextOutput(args[2]))
                {
                    writer.WriteLine("layer\tx\ty\tsheet\tindex");
                    foreach (var layer in map.Layers)
                    for (int y = 0; y < layer.LayerHeight; y++)
                    for (int x = 0; x < layer.LayerWidth; x++)
                    {
                        Tile? tile = layer.Tiles[x, y];
                        writer.WriteLine($"{Escape(layer.Id)}\t{x}\t{y}\t{Escape(tile?.TileSheet.Id ?? "")}\t{tile?.TileIndex.ToString() ?? ""}");
                    }
                }
                Console.WriteLine($"Wrote tile layout to {Path.GetFullPath(args[2])}");
                break;
            case "texture":
                DumpTexture(content, asset, assetPath, args[2]);
                break;
            case "backtypes":
                var back = content.Load<Map>(asset).GetLayer("Back")
                    ?? throw new InvalidDataException("Map has no Back layer.");
                var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
                for (int y = 0; y < back.LayerHeight; y++)
                for (int x = 0; x < back.LayerWidth; x++)
                {
                    var tile = back.Tiles[x, y];
                    if (tile is null)
                        continue;
                    // Per-tile properties override the tile sheet's shared properties, as in game.
                    string type = tile.Properties.TryGetValue("Type", out var value)
                        || tile.TileIndexProperties.TryGetValue("Type", out value)
                        ? value.ToString() : "(none)";
                    counts.TryGetValue(type, out int count);
                    counts[type] = count + 1;
                }
                foreach (var count in counts)
                    Console.WriteLine($"{Escape(count.Key)}\t{count.Value}");
                break;
        }
        return 0;
    }

    private static void DumpTexture(ContentManager content, string asset, string assetPath, string output)
    {
        using var stream = File.OpenRead(assetPath);
        using var header = new BinaryReader(stream);
        using var reader = OpenReader(content, asset, stream, header);
        string primaryType = ReadPrimaryType(reader, out int sharedResources);
        if (sharedResources != 0 || !IsTexture(primaryType))
            throw new InvalidDataException("Asset is not a standalone Texture2D XNB.");
        int format = reader.ReadInt32();
        int width = reader.ReadInt32();
        int height = reader.ReadInt32();
        int levels = reader.ReadInt32();
        int size = reader.ReadInt32();
        if (width <= 0 || height <= 0 || levels <= 0 || size < 0)
            throw new InvalidDataException("Invalid texture dimensions or data size.");
        byte[] pixels = reader.ReadBytes(size);
        if (pixels.Length != size || (format == 0 && size != checked((long)width * height * 4)))
            throw new InvalidDataException("Texture payload does not match its dimensions/data size.");
        PrepareOutput(output);
        using (var writer = new BinaryWriter(File.Create(output)))
        {
            writer.Write(width);
            writer.Write(height);
            writer.Write(format);
            writer.Write(pixels);
        }
        Console.WriteLine($"Wrote {width}x{height}, format {format}, {pixels.Length} bytes (first mip level) to {Path.GetFullPath(output)}");
    }

    private static ContentReader OpenReader(ContentManager content, string asset, Stream stream, BinaryReader header)
    {
        // The installed MonoGame ContentManager owns XNB validation and LZX/LZ4 decompression.
        // Its reader factory is non-public; fail clearly if a future game changes that API.
        var createReader = typeof(ContentManager).GetMethod("GetContentReaderFromXnb", BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(string), typeof(Stream), typeof(BinaryReader), typeof(Action<IDisposable>) }, null)
            ?? throw new NotSupportedException("Installed MonoGame lacks the expected XNB content reader factory.");
        return (ContentReader)createReader.Invoke(content, new object?[] { asset, stream, header, null })!;
    }

    private static string ReadPrimaryType(ContentReader reader, out int sharedResources)
    {
        int readerCount = reader.Read7BitEncodedInt();
        if (readerCount < 1 || readerCount > 1024)
            throw new InvalidDataException("Invalid XNB type reader count.");
        string[] readers = new string[readerCount];
        for (int i = 0; i < readerCount; i++)
        {
            readers[i] = reader.ReadString();
            _ = reader.ReadInt32(); // reader version
        }
        sharedResources = reader.Read7BitEncodedInt();
        int primaryReader = reader.Read7BitEncodedInt();
        if (primaryReader < 1 || primaryReader > readers.Length)
            throw new InvalidDataException("Invalid XNB primary reader.");
        return readers[primaryReader - 1];
    }

    private static bool IsTexture(string reader) => reader.StartsWith("Microsoft.Xna.Framework.Content.Texture2DReader,", StringComparison.Ordinal);

    private static void GenerateVanillaIndex(string contentPath, string output)
    {
        var services = new GameServiceContainer();
        services.AddService<IGraphicsDeviceService>(new HeadlessGraphicsService());
        using var content = new ContentManager(services, contentPath);
        var textures = new SortedSet<string>(StringComparer.Ordinal);
        var mapAssets = new SortedSet<string>(StringComparer.Ordinal);
        int inspected = 0;
        foreach (string file in Directory.EnumerateFiles(contentPath, "*.xnb", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            string asset = Path.GetRelativePath(contentPath, file)[..^4].Replace('\\', '/');
            using var stream = File.OpenRead(file);
            using var header = new BinaryReader(stream);
            using var reader = OpenReader(content, asset, stream, header);
            string readerType = ReadPrimaryType(reader, out _);
            if (IsTexture(readerType)) textures.Add(asset);
            if (readerType.StartsWith("xTile.", StringComparison.Ordinal)) mapAssets.Add(asset);
            inspected++;
        }
        var dataKeys = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        // Load<object> dispatches through the installed XNB's typed primary reader. No game
        // globals, SMAPI, ItemRegistry initialization, mod content or graphics device is used.
        foreach (string asset in new[] { "Data/Characters", "Data/Hats", "Data/Shirts", "Data/Pants", "Data/HairData", "Data/Boots", "Data/Objects", "Data/Locations", "Data/Furniture", "Data/BigCraftables", "Data/Weapons", "Data/Tools", "Data/Trinkets", "Data/Mannequins" })
        {
            if (content.Load<object>(asset) is not IDictionary data)
                throw new InvalidDataException($"Expected a vanilla dictionary for {asset}.");
            dataKeys[asset] = data.Keys.Cast<object>().Select(key => Convert.ToString(key, CultureInfo.InvariantCulture)!)
                .OrderBy(key => key, StringComparer.Ordinal).ToArray();
        }
        FieldInfo? versionField = typeof(StardewValley.Game1).GetField("version", BindingFlags.Public | BindingFlags.Static);
        string version = versionField?.IsLiteral == true ? (string)versionField.GetRawConstantValue()!
            : System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(StardewValley.Game1).Assembly.Location).ProductVersion
                ?? throw new InvalidDataException("Unable to determine the installed game version.");
        var semanticVersion = System.Text.RegularExpressions.Regex.Match(version, @"^\d+\.\d+\.\d+(?:\.\d+)?");
        if (!semanticVersion.Success) throw new InvalidDataException($"Unrecognized installed game version: {version}");
        version = semanticVersion.Value;
        using (var writer = OpenTextOutput(output))
            writer.Write(JsonSerializer.Serialize(new { GameVersion = version, Assets = textures.Concat(mapAssets).OrderBy(x => x, StringComparer.Ordinal), DataKeys = dataKeys }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Inspected {inspected} vanilla XNBs; wrote {textures.Count} textures, {mapAssets.Count} maps and {dataKeys.Sum(p => p.Value.Length)} keys in {dataKeys.Count} data assets for game {version} to {Path.GetFullPath(output)}");
    }

    private static StreamWriter OpenTextOutput(string path)
    {
        PrepareOutput(path);
        return new StreamWriter(path, false, new System.Text.UTF8Encoding(false));
    }

    private static void PrepareOutput(string path) => Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");

    private sealed class HeadlessGraphicsService : IGraphicsDeviceService
    {
        public GraphicsDevice GraphicsDevice => null!;
        public event EventHandler<EventArgs> DeviceCreated { add { } remove { } }
        public event EventHandler<EventArgs> DeviceDisposing { add { } remove { } }
        public event EventHandler<EventArgs> DeviceReset { add { } remove { } }
        public event EventHandler<EventArgs> DeviceResetting { add { } remove { } }
    }
}
