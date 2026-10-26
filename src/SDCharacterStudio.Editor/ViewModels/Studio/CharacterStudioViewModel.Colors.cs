using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Palette;
using SDCharacterStudio.Editor.Project;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>A named colour ramp, read: dark, mid and light as colours.</summary>
public sealed record NamedRamp(string Name, uint Dark, uint Mid, uint Light);

/// <summary>
/// Named colours (after Pixelorama's indexed colours, made simple): you paint with a named ramp's plain colours, and
/// changing the ramp changes those colours everywhere they're painted in the pack (every frame of every outfit and
/// pose, portraits and expressions too), and the recolours that use them, as one undo step.
/// </summary>
public sealed partial class CharacterStudioViewModel
{
    public IReadOnlyList<NamedRamp> NamedColors() =>
        shell.Document?.Project.Colors.Select(c => PaletteViewModel.Parse(c.Dark) is { } d && PaletteViewModel.Parse(c.Mid) is { } m && PaletteViewModel.Parse(c.Light) is { } l
            ? new NamedRamp(c.Name, d, m, l) : null).OfType<NamedRamp>().ToList() ?? new List<NamedRamp>();

    private static string Hex(uint color) => $"#{color & 0xFFFFFF:X6}";

    /// <summary>A new named colour around <paramref name="mid"/> (its dark and light worked out like the shading brush).</summary>
    public NamedRamp? AddNamedColor(string name, uint mid)
    {
        if (shell.Document is not { } doc || string.IsNullOrWhiteSpace(name)) return null;
        name = name.Trim();
        if (doc.Project.Colors.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
        { shell.Ui.Toast("Name taken", $"There's already a colour called {name}.", ToastKind.Info); return null; }
        var ramp = Shading.RampFor(mid | 0xFF000000);
        shell.Edit("New colour " + name, () => doc.Project.Colors.Add(new NamedColor { Name = name, Dark = Hex(ramp.Dark), Mid = Hex(ramp.Mid), Light = Hex(ramp.Light) }));
        Palette.Refresh();
        return new NamedRamp(name, ramp.Dark, ramp.Mid, ramp.Light);
    }

    /// <summary>Forget a colour's name; what was painted with it stays as it is.</summary>
    public void ForgetNamedColor(string name)
    {
        if (shell.Document is not { } doc) return;
        shell.Edit("Forget colour " + name, () => doc.Project.Colors.RemoveAll(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)));
        Palette.Refresh();
    }

    /// <summary>
    /// Change a named colour: its whole ramp around a new mid (<paramref name="shade"/> null), or one shade. Every pixel
    /// painted with an old shade becomes the new one, in all the pack's art. Returns how many pictures changed.
    /// </summary>
    public int ChangeNamedColor(string name, uint color, int? shade = null)
    {
        if (shell.Document is not { } doc || doc.Project.Colors.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) is not { } entry
            || NamedColors().FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) is not { } old) return 0;
        color |= 0xFF000000;
        var whole = Shading.RampFor(color);
        var fresh = shade switch
        {
            0 => old with { Dark = color }, 1 => old with { Mid = color }, 2 => old with { Light = color },
            _ => old with { Dark = whole.Dark, Mid = whole.Mid, Light = whole.Light },
        };
        var swaps = new Dictionary<uint, uint>();
        foreach (var (from, to) in new[] { (old.Dark, fresh.Dark), (old.Mid, fresh.Mid), (old.Light, fresh.Light) })
            if (from != to) swaps[from | 0xFF000000] = to | 0xFF000000;
        int pictures = 0;
        Edit("Change colour " + name, lib =>
        {
            pictures = Repaint(lib, swaps);
            entry.Dark = Hex(fresh.Dark); entry.Mid = Hex(fresh.Mid); entry.Light = Hex(fresh.Light);
        });
        Context.InvalidatePack();
        Palette.Refresh();
        shell.Ui.Toast("Colour changed", pictures == 0 ? $"{name} isn't painted anywhere yet." : $"{name} changed in {pictures} picture{(pictures == 1 ? "" : "s")}.", ToastKind.Info);
        return pictures;
    }

    /// <summary>
    /// Every opaque pixel of a swapped colour in the pack's own art (paint and image layers of poses, outfits and
    /// portraits, expression frames), and recolours whose new colour was one of them. Inside an edit.
    /// </summary>
    private static int Repaint(PackLibrary lib, IReadOnlyDictionary<uint, uint> swaps)
    {
        if (swaps.Count == 0) return 0;
        var images = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Layers(IEnumerable<Layer> layers)
        {
            foreach (var layer in layers)
                switch (layer)
                {
                    case PaintLayer { Image: { } paint }: images.Add(paint); break;
                    case ImageLayer { Image: { } image }: images.Add(image); break;
                }
        }
        foreach (var item in lib.Items(LibraryKind.Pose).ToList())
        {
            if (lib.Pose(item.Id) is not { } pose) continue;
            Layers(pose.Frames.SelectMany(f => f.Layers));
            // A recolour to the old colour follows the change.
            bool changed = false;
            foreach (var rule in pose.Frames.SelectMany(f => f.Layers).OfType<LiveLayer>().SelectMany(l => l.Recolor ?? new()))
                if (PaletteViewModel.Parse(rule.To) is { } to && swaps.TryGetValue(to, out var next)) { rule.To = Hex(next); changed = true; }
            if (changed) lib.SavePose(pose);
        }
        foreach (var item in lib.Items(LibraryKind.Portrait).ToList())
            if (lib.Portrait(item.Id) is { } portrait) Layers(portrait.Emotions.Values.SelectMany(e => e.Layers));
        foreach (var expression in lib.Expressions().Expressions)
            foreach (var frame in expression.Frames) if (frame.Image != null) images.Add(frame.Image);
        int count = 0;
        foreach (var path in images)
        {
            if (lib.ReadImage(path) is not { } image) continue;
            RgbaImage? result = null;
            for (int i = 0; i < image.Pixels.Length; i++)
                if (image.Pixels[i] >> 24 == 255 && swaps.TryGetValue(image.Pixels[i], out var to)) (result ??= StudioImages.Clone(image)).Pixels[i] = to;
            if (result == null) continue;
            lib.WriteImage(path, result);
            count++;
        }
        return count;
    }
}
