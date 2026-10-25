using System.IO;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Palette;
using SDCharacterStudio.Runtime.Rendering;

namespace SDCharacterStudio.Editor.Studio;

/// <summary>
/// The player as the Bridge exported them (doc 06 §4.4): per-part 32×48 renders for the curated frames, in the same
/// canvas the runtime's <c>FarmerPartsRenderer</c> uses. The editor layers the requested parts in the native draw order;
/// the runtime renders the real farmer, so this is the preview of the designer's current loadout.
/// </summary>
public sealed class FarmerExport
{
    // FarmerRenderer.featureXOffsetPerFrame / featureYOffsetPerFrame, Stardew Valley 1.6.15 (decompiled; doc 08 §4).
    public static readonly int[] FeatureY =
    {
        1, 2, 2, 0, 5, 6, 1, 2, 2, 1, 0, 2, 0, 1, 1, 0, 2, 2, 3, 3, 2, 2, 1, 1, 0, 0, 2, 2, 4, 4, 0, 0, 1, 2, 1, 1, 1, 1, 0, 0,
        1, 1, 1, 0, 0, -2, -1, 1, 1, 0, -1, -2, -1, -1, 5, 4, 0, 0, 3, 2, -1, 0, 4, 2, 0, 0, 2, 1, 0, -1, 1, -2, 0, 0, 1, 1, 1, 1, 1, 1,
        0, 0, 0, 0, 1, -1, -1, -1, -1, 1, 1, 0, 0, 0, 0, 4, 1, 0, 1, 2, 1, 0, 1, 0, 1, 2, -3, -4, -1, 0, 0, 2, 1, -4, -1, 0, 0, -3, 0, 0,
        -1, 0, 0, 2, 1, 1
    };
    public static readonly int[] FeatureX =
    {
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -1, -1, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4, 0, 0, 0, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -1, 0, 0,
        0, 0, 0, 0, 0, 0
    };
    public const int Width = 32, Height = 48;
    /// <summary>Native draw order of the parts (FarmerRenderer layer depths, doc 05 §7.1).</summary>
    private static readonly (FarmerPart Part, string Name)[] Order =
    {
        (FarmerPart.Body, "Body"), (FarmerPart.Head, "Head"), (FarmerPart.Shoes, "Shoes"), (FarmerPart.Pants, "Pants"), (FarmerPart.Eyes, "Eyes"),
        (FarmerPart.Shirt, "Shirt"), (FarmerPart.Accessory, "Accessory"), (FarmerPart.Hair, "Hair"), (FarmerPart.Hat, "Hat"), (FarmerPart.Arms, "Arms")
    };

    private readonly string folder;
    private readonly Dictionary<(string Part, int Frame, string Facing), string> files = new();
    public JObject Player { get; }
    public IReadOnlyList<(string Name, string Facing, int[] Frames)> Animations { get; }

    private FarmerExport(string folder, JObject player)
    {
        this.folder = folder; Player = player;
        foreach (var entry in (player["PartFiles"] as JArray) ?? new JArray())
            if ((string?)entry["Part"] is { } part && (string?)entry["File"] is { } file)
                files[(part.ToLowerInvariant(), (int?)entry["Frame"] ?? 0, ((string?)entry["Facing"] ?? "down").ToLowerInvariant())] = file;
        Animations = ((player["AnimationFrames"] as JArray) ?? new JArray())
            .Select(a => ((string?)a["Name"] ?? "", (string?)a["Facing"] ?? "down", ((a["Frames"] as JArray) ?? new JArray()).Select(f => (int?)f["Frame"] ?? 0).ToArray())).ToArray();
    }

    public static FarmerExport? From(Catalog.ExportCatalog? catalog)
    {
        if (catalog?.Player is not { } player || (string?)player["PartsFolder"] is not { } parts) return null;
        string folder = catalog.FullPath(parts);
        return Directory.Exists(folder) ? new FarmerExport(folder, player) : null;
    }

    /// <summary>Frames that have per-facing part renders in this export.</summary>
    public IEnumerable<int> FramesFor(string facing) => files.Keys.Where(k => k.Part == "full" && k.Facing == facing).Select(k => k.Frame).Distinct().OrderBy(f => f);

    public bool HasFrame(int frame, string facing) => files.ContainsKey(("full", frame, facing));

    /// <summary>The requested parts for one base frame and facing, layered like the runtime renderer.</summary>
    public RgbaImage Render(int frame, int facing, FarmerPart parts, ICollection<string>? problems = null)
    {
        string facingName = FacingName(facing);
        var canvas = StudioImages.Blank(Width, Height);
        if (parts == FarmerPart.Full && Load("full", frame, facingName, false) is { } full) return full;
        bool any = false;
        foreach (var (part, name) in Order)
        {
            if (!parts.HasFlag(part)) continue;
            var image = Load(name.ToLowerInvariant(), frame, facingName, false);
            // Base/Head/Body exist for every frame un-flipped; left facings mirror them like the native sprite flip.
            if (image == null && part is FarmerPart.Body or FarmerPart.Head)
                image = Load(name.ToLowerInvariant(), frame, "down", facing == 3);
            if (image == null) continue;
            Compositor.Blit(canvas, image, 0, 0);
            any = true;
        }
        if (!any) problems?.Add($"The game export has no player render for frame {frame} facing {facingName}. Export again, or pick a frame from the player list.");
        return canvas;
    }

    private RgbaImage? Load(string part, int frame, string facing, bool flip)
    {
        if (!files.TryGetValue((part, frame, facing), out var file)) return null;
        var image = StudioImages.LoadFile(Path.Combine(folder, file));
        return image == null ? null : flip ? StudioImages.FlipHorizontal(image) : image;
    }

    /// <summary>The neck point in the 32×48 canvas (FarmerPartsRenderer.GetNeckAnchor, without live animation offsets).</summary>
    public static PixelPoint Neck(int frame, int facing) =>
        (uint)frame < FeatureY.Length ? new(16 + (facing == 3 ? -1 : 1) * FeatureX[frame], 31 + FeatureY[frame]) : new(16, 31);

    /// <summary>The player's colours for Farmer.* slot sources (doc 05 §4.2), from the exported loadout.</summary>
    public ColorRamp Palette(string sample)
    {
        switch (sample.ToLowerInvariant())
        {
            case "shirt": return Fixed((string?)Player["ShirtColor"]);
            case "pants": return Fixed((string?)Player["PantsColor"]);
            case "outline": return new(0xFF000000, 0xFF000000, 0xFF000000);
            case "eyes":
                var eyes = Parse((string?)Player["EyeColor"]) ?? 0xFF7A4434;
                return new(ColorSampler.FromColor(eyes).Dark, eyes, eyes);
            case "hair": return Common(Load("hair", 0, "down", false), Parse((string?)Player["HairColor"]) ?? 0xFFC15A32);
            case "shoes": return Common(Load("shoes", 0, "down", false), 0xFF6B4A2B);
            case "skin":
                var head = Load("head", 0, "down", false);
                if (head != null)
                {
                    var rig = new CharacterRig { Id = "farmer", ColorSamples = { ["Skin"] = new ColorSample { Mode = "auto-skin", Frame = 0, Box = new[] { 8, 16, 16, 16 } } } };
                    return ColorSampler.Resolve(rig, "Skin", _ => head);
                }
                return ColorSampler.FromColor(0xFFF9C8A7);
            default: throw new ArgumentException($"Unknown farmer colour source '{sample}'.");
        }
    }

    /// <summary>Same as the runtime's hair rule: the three most common opaque colours, darkest first.</summary>
    private static ColorRamp Common(RgbaImage? image, uint fallback)
    {
        var colors = image?.Pixels.Where(x => x >> 24 == 255).GroupBy(x => x).OrderByDescending(x => x.Count()).Take(3).Select(x => x.Key)
            .OrderBy(c => ((c >> 16) & 255) * 299 + ((c >> 8) & 255) * 587 + (c & 255) * 114).ToArray() ?? Array.Empty<uint>();
        return colors.Length switch { 0 => ColorSampler.FromColor(fallback), 1 => new(colors[0], colors[0], colors[0]), 2 => new(colors[0], colors[1], colors[1]), _ => new(colors[0], colors[1], colors[2]) };
    }

    private static ColorRamp Fixed(string? hex) => ColorSampler.FromColor(Parse(hex) ?? 0xFFFFFFFF);
    private static uint? Parse(string? hex) => hex is { Length: 7 } && hex[0] == '#' && uint.TryParse(hex[1..], System.Globalization.NumberStyles.HexNumber, null, out var rgb) ? 0xFF000000 | rgb : null;
    public static string FacingName(int facing) => facing switch { 0 => "up", 1 => "right", 3 => "left", _ => "down" };
    public static int FacingIndex(string? facing) => facing?.ToLowerInvariant() switch { "up" => 0, "right" => 1, "left" => 3, _ => 2 };
}
