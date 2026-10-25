using System.Diagnostics;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using SDCharacterStudio.Core.Compositing;

namespace SDCharacterStudio.Editor.Studio;

/// <summary>What the external editor gets next to the strip (doc 07 §11): frame size, frame count and the slot palette.</summary>
public sealed record ExternalSidecar(string Pack, string Item, string Layer, int FrameWidth, int FrameHeight, int Frames, IReadOnlyDictionary<string, string> SlotPalette, string Note);

/// <summary>One "Edit in external editor": a PNG strip in the work folder, watched until the studio closes it.</summary>
public sealed class ExternalEditSession : IDisposable
{
    private readonly FileSystemWatcher watcher;
    private readonly Action<RgbaImage> reimport;
    private readonly IUserInterface ui;
    private byte[] lastBytes;

    internal ExternalEditSession(string file, byte[] written, Action<RgbaImage> reimport, IUserInterface ui)
    {
        File = file; lastBytes = written; this.reimport = reimport; this.ui = ui;
        watcher = new FileSystemWatcher(Path.GetDirectoryName(file)!, Path.GetFileName(file)) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName, EnableRaisingEvents = true };
        watcher.Changed += (_, _) => Changed();
        watcher.Created += (_, _) => Changed();
        watcher.Renamed += (_, _) => Changed();
    }

    public string File { get; }

    // Editors save in several writes; wait until the file settles, then re-import on the UI thread.
    private void Changed() => ui.Debounce("external:" + File, TimeSpan.FromMilliseconds(400), () => CheckNow());

    /// <summary>Re-import if the file changed since the last import (also used by tests).</summary>
    public bool CheckNow()
    {
        byte[] bytes;
        try { bytes = System.IO.File.ReadAllBytes(File); }
        catch (IOException) { ui.Debounce("external:" + File, TimeSpan.FromMilliseconds(400), () => CheckNow()); return false; }
        if (bytes.AsSpan().SequenceEqual(lastBytes)) return false;
        lastBytes = bytes;
        RgbaImage image;
        try { image = StudioImages.Decode(bytes); }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException) { ui.Toast("Couldn't re-import", $"{Path.GetFileName(File)}: {ex.Message}", ToastKind.Warning); return false; }
        reimport(image);
        return true;
    }

    public void Dispose() => watcher.Dispose();
}

/// <summary>Aseprite / LibreSprite round-trip (doc 07 §11) and the reserved-slot palette files.</summary>
public static class ExternalEditor
{
    public static string WorkFolder => Path.Combine(EditorSettings.LocalFolder, "External");

    /// <summary>Write the strip and sidecar, open the strip in the chosen app, and watch it.</summary>
    public static ExternalEditSession Start(string name, RgbaImage strip, ExternalSidecar sidecar, string? app, Action<RgbaImage> reimport, IUserInterface ui, bool launch = true)
    {
        string folder = Path.Combine(WorkFolder, PackDocument.SafeFileName(sidecar.Pack));
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, PackDocument.SafeFileName(name) + ".png");
        byte[] bytes = StudioImages.Encode(strip);
        File.WriteAllBytes(file, bytes);
        File.WriteAllText(Path.ChangeExtension(file, ".json"), JsonConvert.SerializeObject(sidecar, Formatting.Indented));
        WritePalette(Path.Combine(folder, "sdcs-slots.gpl"));
        var session = new ExternalEditSession(file, bytes, reimport, ui);
        if (launch) Launch(file, app, ui);
        return session;
    }

    private static void Launch(string file, string? app, IUserInterface ui)
    {
        try
        {
            var start = string.IsNullOrWhiteSpace(app) ? new ProcessStartInfo(file) { UseShellExecute = true } : new ProcessStartInfo(app, $"\"{file}\"") { UseShellExecute = false };
            Process.Start(start);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            ui.Toast("Couldn't open the editor", $"{ex.Message}. Set the app under Character Studio › External editor. The file is {file}.", ToastKind.Warning);
        }
    }

    /// <summary>The reserved slot colours (doc 05 §4.1) as a GIMP palette, which Aseprite and LibreSprite load.</summary>
    public static string GplPalette()
    {
        var text = new StringBuilder("GIMP Palette\nName: SDCS reserved slots\nColumns: 3\n#\n");
        foreach (var (name, color) in ViewModels.PaletteViewModel.SlotColors)
            text.Append($"{(color >> 16) & 0xFF,3} {(color >> 8) & 0xFF,3} {color & 0xFF,3}\t{name}\n");
        return text.ToString();
    }

    public static void WritePalette(string path) => File.WriteAllText(path, GplPalette());

    /// <summary>Frames side by side, left to right.</summary>
    public static RgbaImage Strip(IReadOnlyList<RgbaImage?> frames, int width, int height)
    {
        var strip = StudioImages.Blank(width * Math.Max(1, frames.Count), height);
        for (int i = 0; i < frames.Count; i++)
            if (frames[i] is { } frame) PixelTools.Paste(strip, frame, i * width, 0);
        return strip;
    }

    /// <summary>Cut a strip back into frames; null when its size no longer matches.</summary>
    public static RgbaImage[]? Split(RgbaImage strip, int width, int height, int count)
    {
        if (strip.Width != width * count || strip.Height != height) return null;
        return Enumerable.Range(0, count).Select(i => StudioImages.Crop(strip, i * width, 0, width, height)).ToArray();
    }
}
