using System.IO;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Editor.Catalog;
using SDCharacterStudio.Editor.Studio;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>
/// The ready-made poses on real villagers, with the average head line (row 15) and with each one's own: for looking at.
/// Off by default: set SDCS_HEAD_SHOTS to an output folder, and SDCS_UI_EXPORT to a Bridge export profile (read only).
/// </summary>
public class HeadSnapshotTests
{
    private static readonly string[] Villagers = { "Abigail", "Haley", "Leah", "Elliott", "Sebastian", "Penny", "Emily", "Alex", "Jas", "Gunther", "Mister Qi", "Linus" };
    private static readonly string[] Facings = { "down", "right", "up", "left" };
    private static readonly (string Preset, int Frame)[] Shown = { ("bow", 0), ("tilt", 0), ("nod", 2), ("slump", 0), ("shrug", 0) };

    [Fact]
    public void RenderReadyMadeHeads()
    {
        string? output = Environment.GetEnvironmentVariable("SDCS_HEAD_SHOTS");
        if (string.IsNullOrEmpty(output)) return;
        Directory.CreateDirectory(output);
        var catalog = ExportCatalog.Load(UiSnapshotTests.ExportProfile());
        const int scale = 5, cellW = 16 * scale + 6, cellH = 32 * scale + 6;
        foreach (var name in Villagers)
        {
            if (catalog.Character(name) is not { } who || StudioImages.LoadFile(catalog.TextureFile(who.Sprite)) is not { } sheet) continue;
            RgbaImage? Standing(string facing) => StudioImages.Frame(sheet, 16, 32, Array.IndexOf(Facings, facing) * 4);
            var own = HeadFinder.Rows(who.Name, Standing);
            // Rows: average head then her own, per facing; columns: standing, then each ready-made pose.
            var picture = new RgbaImage(cellW * (Shown.Length + 1), cellH * Facings.Length * 2, new uint[cellW * (Shown.Length + 1) * cellH * Facings.Length * 2]);
            Array.Fill(picture.Pixels, 0xFF2A2D34u);
            for (int f = 0; f < Facings.Length; f++)
            {
                if (Standing(Facings[f]) is not { } standing) continue;
                for (int kind = 0; kind < 2; kind++)
                {
                    int row = kind == 0 || own == null ? 15 : own[Facings[f]];
                    var body = BodyMaps.Auto(standing, (_, y) => y <= row, Facings[f]);
                    int top = (f * 2 + kind) * cellH;
                    if (kind == 1) for (int x = 0; x < picture.Width; x++) picture.Pixels[top * picture.Width + x] = 0xFF60C080;
                    Blit(picture, standing, 0, top);
                    for (int s = 0; s < Shown.Length; s++)
                    {
                        var preset = PosePresets.Find(Shown[s].Preset)!;
                        var map = PosePresets.Map(standing, body, preset.Frames[Math.Min(Shown[s].Frame, preset.Frames.Count - 1)], Facings[f]);
                        Blit(picture, Compositor.Remap(standing, map), (s + 1) * cellW, top);
                    }
                }
            }
            File.WriteAllBytes(Path.Combine(output, $"heads-{name.Replace(' ', '-')}.png"), StudioImages.Encode(picture));
        }

        void Blit(RgbaImage into, RgbaImage frame, int left, int top)
        {
            for (int y = 0; y < frame.Height * scale; y++)
                for (int x = 0; x < frame.Width * scale; x++)
                {
                    uint pixel = frame.Pixels[(y / scale) * frame.Width + x / scale];
                    if (pixel >> 24 == 0) continue;
                    into.Pixels[(top + 3 + y) * into.Width + left + 3 + x] = pixel | 0xFF000000;
                }
        }
    }
}
