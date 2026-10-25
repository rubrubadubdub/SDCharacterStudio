using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;

// Only this small protocol process loads installed game libraries. Nothing is copied into the editor.
string? gameDir = null;
for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--game-dir") gameDir = args[++i];
Assembly? monoGame = null;
object? engine = null, soundBank = null, activeCue = null, effect = null, instance = null;
var banks = new List<IDisposable>();
DateTime deadline = DateTime.MinValue;
bool quit = false;
if (gameDir != null) gameDir = Path.GetFullPath(gameDir);

try
{
    Task<string?> read = Console.In.ReadLineAsync();
    while (!quit)
    {
        // Engine updates, commands, stopping, and disposal all execute on this one loop.
        if (await Task.WhenAny(read, Task.Delay(16)) == read)
        {
            string? line = await read;
            if (line == null) break;
            Handle(line);
            if (!quit) read = Console.In.ReadLineAsync();
        }
        if (DateTime.UtcNow >= deadline && (activeCue != null || instance != null)) StopCue();
        engine?.GetType().GetMethod("Update")?.Invoke(engine, null);
    }
}
catch (Exception ex) { Console.Error.WriteLine(ex.GetBaseException().Message); }
finally
{
    StopCue();
    (soundBank as IDisposable)?.Dispose();
    foreach (var bank in banks) bank.Dispose();
    (engine as IDisposable)?.Dispose();
    monoGame?.GetType("Microsoft.Xna.Framework.Audio.SoundEffect")?.GetMethod("PlatformShutdown", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
}

void Handle(string line)
{
    long id = 0;
    try
    {
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        if (root.TryGetProperty("Id", out var identity)) id = identity.GetInt64();
        string? op = root.GetProperty("Op").GetString();
        switch (op)
        {
            case "ping": Reply(id, true, playing: activeCue != null || instance != null); break;
            case "stop": StopCue(); Reply(id, true); break;
            case "shutdown": StopCue(); Reply(id, true); quit = true; break;
            case "play":
                StopCue();
                string cue = root.GetProperty("Cue").GetString() ?? "";
                if (string.IsNullOrWhiteSpace(cue)) throw new InvalidDataException("Sound cue is empty.");
                int? pitch = root.TryGetProperty("Pitch", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out int value) ? value : null;
                string? catalog = root.TryGetProperty("Catalog", out var c) ? c.GetString() : null;
                double seconds = root.TryGetProperty("MaxSeconds", out var max) ? Math.Clamp(max.GetDouble(), .1, 10) : 10;
                try { Play(cue, catalog, pitch); deadline = DateTime.UtcNow.AddSeconds(seconds); Reply(id, true); }
                catch { StopCue(); throw; }
                break;
            default: throw new InvalidDataException("Unknown audio operation.");
        }
    }
    catch (Exception ex) { Console.Error.WriteLine(ex.GetBaseException()); Reply(id, false, ex.GetBaseException().Message); }
}

void LoadMonoGame()
{
    if (monoGame != null) return;
    if (gameDir == null || !File.Exists(Path.Combine(gameDir, "MonoGame.Framework.dll")))
        throw new FileNotFoundException("Installed Stardew Valley audio libraries are unavailable. Refresh the Bridge export.");
    Environment.CurrentDirectory = gameDir;
    var context = new AssemblyLoadContext("SDCS-installed-audio");
    context.Resolving += (_, name) =>
    {
        string path = Path.Combine(gameDir, name.Name + ".dll");
        return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
    };
    monoGame = context.LoadFromAssemblyPath(Path.Combine(gameDir, "MonoGame.Framework.dll"));
    monoGame.GetType("Microsoft.Xna.Framework.TitleContainer")?.GetProperty("Location", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, gameDir);
}

void Play(string cue, string? catalog, int? pitch)
{
    JsonElement? source = null;
    if (catalog != null && File.Exists(Path.Combine(catalog, "sound-previews.json")))
    {
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(catalog, "sound-previews.json")));
        if (metadata.RootElement.GetProperty("Format").GetInt32() != 1) throw new InvalidDataException("Unsupported sound preview catalog format.");
        foreach (var entry in metadata.RootElement.GetProperty("Cues").EnumerateObject())
            if (string.Equals(entry.Name, cue, StringComparison.OrdinalIgnoreCase)) { source = entry.Value.Clone(); break; }
    }
    if (source is { } data)
    {
        if (data.TryGetProperty("Error", out var error) && error.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(error.GetString()))
            throw new InvalidDataException(error.GetString());
        if (data.TryGetProperty("Variants", out var variants) && variants.GetArrayLength() > 0)
        {
            var variant = variants[Random.Shared.Next(variants.GetArrayLength())];
            string relative = variant.GetProperty("File").GetString() ?? "";
            string root = Path.GetFullPath(catalog!).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (Path.IsPathRooted(relative)) throw new InvalidDataException("Audio source path must stay within the catalog.");
            string path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Audio source path must stay within the catalog.");
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension is not (".wav" or ".ogg")) throw new InvalidDataException("Audio preview supports WAV and OGG sources.");
            if (!File.Exists(path)) throw new FileNotFoundException("Exported audio source is missing. Refresh the Bridge export.", path);
            LoadMonoGame();
            Type type = monoGame!.GetType("Microsoft.Xna.Framework.Audio.SoundEffect")!;
            using var stream = File.OpenRead(path);
            effect = type.GetMethod("FromStream", new[] { typeof(Stream), typeof(bool) })!.Invoke(null, new object[] { stream, extension == ".ogg" });
            instance = type.GetMethod("CreateInstance", Type.EmptyTypes)!.Invoke(effect, null);
            Type it = instance!.GetType();
            it.GetProperty("Pitch")!.SetValue(instance, Math.Clamp(((pitch ?? 1200) - 1200) / 1200f, -1, 1));
            bool looped = data.TryGetProperty("Looped", out var loop) && loop.GetBoolean();
            if (it.GetProperty("IsLooped") is { } isLooped) isLooped.SetValue(instance, looped);
            else it.GetProperty("LoopCount")!.SetValue(instance, looped ? 255u : 0u);
            it.GetMethod("Play", Type.EmptyTypes)!.Invoke(instance, null);
            return;
        }
    }
    LoadMonoGame();
    if (engine == null)
    {
        string xact = Path.Combine(gameDir!, "Content", "XACT");
        engine = Activator.CreateInstance(monoGame!.GetType("Microsoft.Xna.Framework.Audio.AudioEngine")!, Path.GetRelativePath(gameDir!, Path.Combine(xact, "FarmerSounds.xgs")));
        foreach (string xwb in Directory.GetFiles(xact, "*.xwb"))
            banks.Add((IDisposable)Activator.CreateInstance(monoGame.GetType("Microsoft.Xna.Framework.Audio.WaveBank")!, engine, Path.GetRelativePath(gameDir!, xwb))!);
        soundBank = Activator.CreateInstance(monoGame.GetType("Microsoft.Xna.Framework.Audio.SoundBank")!, engine, Path.GetRelativePath(gameDir!, Path.Combine(xact, "Sound Bank.xsb")));
    }
    Type bankType = soundBank!.GetType();
    if (!(bool)bankType.GetMethod("Exists")!.Invoke(soundBank, new object[] { cue })!) throw new InvalidDataException("Unknown sound cue: " + cue);
    activeCue = bankType.GetMethod("GetCue", new[] { typeof(string) })!.Invoke(soundBank, new object[] { cue });
    // Match SoundsHelper.SetPitch: always initialize the RPC variable to 1200;
    // force an explicit pitch only when the cue's RPC does not control it.
    activeCue!.GetType().GetMethod("SetVariable", new[] { typeof(string), typeof(float) })!.Invoke(activeCue, new object[] { "Pitch", (float)(pitch ?? 1200) });
    if (pitch is { } nativePitch && activeCue.GetType().GetProperty("IsPitchBeingControlledByRPC")?.GetValue(activeCue) is not true)
        activeCue.GetType().GetProperty("Pitch")?.SetValue(activeCue, Math.Clamp((nativePitch - 1200) / 1200f, -1, 1));
    activeCue!.GetType().GetMethod("Play", Type.EmptyTypes)!.Invoke(activeCue, null);
}

void StopCue()
{
    if (activeCue != null)
    {
        Type options = monoGame!.GetType("Microsoft.Xna.Framework.Audio.AudioStopOptions")!;
        activeCue.GetType().GetMethod("Stop", new[] { options })?.Invoke(activeCue, new[] { Enum.Parse(options, "Immediate") });
        (activeCue as IDisposable)?.Dispose(); activeCue = null;
    }
    if (instance != null) { instance.GetType().GetMethod("Stop", Type.EmptyTypes)!.Invoke(instance, null); (instance as IDisposable)?.Dispose(); instance = null; }
    (effect as IDisposable)?.Dispose(); effect = null;
}
void Reply(long id, bool ok, string? error = null, bool? playing = null)
{
    Console.WriteLine(JsonSerializer.Serialize(new { Id = id, Ok = ok, Error = error, Playing = playing }));
    Console.Out.Flush();
}
