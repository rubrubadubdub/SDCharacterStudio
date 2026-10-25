using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework.Audio;
using StardewValley;
using StardewModdingAPI;
using SDCharacterStudio.Core.Audio;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Loading;

namespace SDCharacterStudio.Runtime.Loading;

internal static class NativePackMusic
{
    // Each embedded engine owns its registrations; bank identity also separates a rebuilt audio engine.
    private static readonly ConditionalWeakTable<ISoundBank, Dictionary<string, HashSet<string>>> Owned = new();

    public static bool Register(string packId, StudioIndex? index, string folder, IMonitor monitor)
    {
        var staged = new List<(string Cue, SoundEffect Effect, bool Loop)>();
        var transferred = new HashSet<SoundEffect>();
        string currentId = "";
        try
        {
            // Decode the complete batch before touching a live definition. A corrupt later track
            // must not leave earlier tracks replaced while the old cutscenes are still registered.
            foreach (var (id, track) in index?.Music ?? new Dictionary<string, MusicTrack>())
            {
                currentId = id;
                if (track == null) throw new InvalidDataException("Music entry is null.");
                string file = track.File ?? throw new InvalidDataException("Music file is missing.");
                if (!PackPath.IsSafe(file) || !file.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) && !file.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Music file must be a safe WAV or OGG path.");
                string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string path = Path.GetFullPath(Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar)));
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) throw new FileNotFoundException("Music file is missing.", path);
                bool ogg = path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase);
                using var stream = File.OpenRead(path);
                var effect = SoundEffect.FromStream(stream, ogg);
                staged.Add((PackMusic.CueName(packId, id), effect, track.Loop));
            }
            if (staged.Count == 0) { Remove(packId, monitor); return true; }

            var bank = Game1.soundBank;
            int category = Game1.audioEngine.GetCategoryIndex("Music");
            var packs = Owned.GetValue(bank, _ => new(StringComparer.OrdinalIgnoreCase));
            if (!packs.TryGetValue(packId, out var owned)) packs[packId] = owned = new(StringComparer.Ordinal);
            foreach (var track in staged)
            {
                var definition = bank.Exists(track.Cue) ? bank.GetCueDefinition(track.Cue) : new CueDefinition { name = track.Cue };
                definition.SetSound(new[] { track.Effect }, category, track.Loop, false);
                transferred.Add(track.Effect);
                bank.AddCue(definition);
                owned.Add(track.Cue);
                // Match AudioCueModificationManager: playing/paused native cues must swap their
                // instances before AudioEngine.Update disposes the previous recording.
                definition.OnModified?.Invoke();
            }
            var retained = staged.Select(x => x.Cue).ToHashSet(StringComparer.Ordinal);
            foreach (var removed in owned.Where(x => !retained.Contains(x)).ToArray())
            {
                Silence(bank, removed, category);
                owned.Remove(removed);
            }
            return true;
        }
        catch (Exception ex)
        {
            monitor.Log($"Could not register imported music '{currentId}' for '{packId}': {ex.Message}", LogLevel.Error);
            return false;
        }
        finally
        {
            // Published effects belong to native cue definitions. SetSound releases their
            // dependencies, and OnModified refreshes active instances before native disposal.
            foreach (var track in staged)
                if (!transferred.Contains(track.Effect)) track.Effect.Dispose();
        }
    }

    public static void Remove(string packId, IMonitor monitor)
    {
        var bank = Game1.soundBank;
        if (bank == null || !Owned.TryGetValue(bank, out var packs) || !packs.TryGetValue(packId, out var owned)) return;
        foreach (string cue in owned.ToArray())
        {
            try
            {
                Silence(bank, cue, Game1.audioEngine.GetCategoryIndex("Music"));
                owned.Remove(cue);
            }
            catch (Exception ex) { monitor.Log($"Could not release imported music '{cue}' for '{packId}': {ex.Message}", LogLevel.Warn); }
        }
        if (owned.Count == 0) packs.Remove(packId);
    }

    private static void Silence(ISoundBank bank, string cue, int category)
    {
        if (!bank.Exists(cue)) return;
        // ISoundBank has no RemoveCue. Leave a harmless, non-looping silent cue instead of a
        // dangling name, and let SetSound release the potentially large previous recording.
        var silence = new SoundEffect(new byte[2], 8000, AudioChannels.Mono);
        var definition = bank.GetCueDefinition(cue);
        try { definition.SetSound(silence, category); }
        catch { silence.Dispose(); throw; }
        definition.OnModified?.Invoke();
    }
}
