using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDCharacterStudio.Editor.Catalog;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>
/// An outfit to see a character in: a sprite sheet (null: whatever the game shows now), or one of the pack's own outfits
/// (an <see cref="StudioContext.OutfitKey"/>), named plainly.
/// </summary>
public sealed record OutfitChoice(string? Asset, string Label, string? Detail)
{
    public bool IsYours => StudioContext.OutfitId(Asset) != null;
    public override string ToString() => Detail == null ? Label : $"{Label} · {Detail}";
}

/// <summary>
/// Outfits (D-49): every appearance the export knows for the character (seasons, festivals, the island, mods' extras),
/// named by season or festival and by who adds it. Choosing one previews the whole studio in it; the pose itself stays
/// live (in game she wears whatever the day gives her) unless the pose pins an outfit.
/// </summary>
public sealed partial class CharacterStudioViewModel
{
    public ObservableCollection<OutfitChoice> Outfits { get; } = new();
    [ObservableProperty] private OutfitChoice? outfit;
    private IDisposable? outfitOverride;
    private string? outfitAsset, outfitFor;
    private bool refreshingOutfits;

    private static readonly Dictionary<string, string> Festivals = new(StringComparer.OrdinalIgnoreCase)
    {
        ["spring13"] = "Egg Festival", ["spring24"] = "Flower Dance", ["summer11"] = "Luau", ["summer28"] = "Moonlight Jellies",
        ["fall16"] = "Stardew Valley Fair", ["fall27"] = "Spirit's Eve", ["winter8"] = "Festival of Ice", ["winter25"] = "Feast of the Winter Star",
        ["spring15"] = "Desert Festival", ["summer20"] = "Trout Derby", ["winter12"] = "SquidFest",
    };

    private void RefreshOutfits()
    {
        var fresh = new List<OutfitChoice>();
        if (Character is { IsFarmer: false } who && shell.Catalog?.Character(who.Key) is { } entry)
        {
            string? now = entry.Current?.Sprite;
            fresh.Add(new OutfitChoice(null, "As in the game now", now != null && now != entry.Sprite ? Describe(entry, now) : null));
            // Your own outfits first: they're what you're making.
            fresh.AddRange(OwnOutfits(who.Key).Select(id => new OutfitChoice(StudioContext.OutfitKey(id), OutfitFactory.Label(id, who.Key), "your outfit")));
            if (entry.Sprite != null) fresh.Add(new OutfitChoice(entry.Sprite, "Everyday", Origin(entry.Sprite)));
            foreach (var appearance in entry.Appearances.Where(a => a.Sprite != null && a.Sprite != entry.Sprite).GroupBy(a => a.Sprite!, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
                fresh.Add(new OutfitChoice(appearance.Sprite, OutfitLabel(appearance, who.Key), Origin(appearance.Sprite)));
        }
        string? keep = Outfit?.Asset;
        refreshingOutfits = true;
        try
        {
            if (!Outfits.SequenceEqual(fresh)) { Outfits.Clear(); foreach (var choice in fresh) Outfits.Add(choice); }
            Outfit = Outfits.FirstOrDefault(o => string.Equals(o.Asset, keep, StringComparison.OrdinalIgnoreCase)) ?? Outfits.FirstOrDefault();
        }
        finally { refreshingOutfits = false; }
        // Only a different outfit or character redraws (this runs after every edit).
        if (!string.Equals(Outfit?.Asset, outfitAsset, StringComparison.OrdinalIgnoreCase) || !string.Equals(Character?.Key, outfitFor, StringComparison.OrdinalIgnoreCase)) ApplyOutfit();
    }

    partial void OnOutfitChanged(OutfitChoice? value) { if (!refreshingOutfits) ApplyOutfit(); }

    /// <summary>The pack's outfits for a villager (sheet poses whose rig is theirs).</summary>
    public IEnumerable<string> OwnOutfits(string npc)
    {
        if (Library is not { } library) yield break;
        foreach (var item in library.Items(LibraryKind.Pose).OrderBy(i => i.Id, StringComparer.OrdinalIgnoreCase))
            if (library.Pose(item.Id) is { Kind: "sheet" } pose && (string.Equals(library.Rig(pose.For)?.Npc, npc, StringComparison.OrdinalIgnoreCase) || string.Equals(pose.For, npc, StringComparison.OrdinalIgnoreCase)))
                yield return item.Id;
    }

    /// <summary>Outfits are for villagers: the player changes clothes with a Costume beat.</summary>
    public bool CanMakeOutfit => Character is { IsFarmer: false } && shell.Document != null;

    /// <summary>
    /// A new outfit for the character, started from what the studio shows (her own look, a game outfit, or another of
    /// yours), with every frame of her sheet. It opens to paint on, and the studio shows her in it.
    /// </summary>
    [RelayCommand] private void MakeOutfit() => NewOutfit();

    public string? NewOutfit(string? name = null)
    {
        if (shell.Document == null) { shell.Ui.Toast("No project", "Open or create a project first.", ToastKind.Info); return null; }
        if (Character is not { IsFarmer: false } who || Library is not { } library)
        { shell.Ui.Toast("Outfits are for villagers", "The player changes clothes with a Costume beat in a scene.", ToastKind.Info); return null; }
        name ??= shell.Ui.Prompt("New outfit", $"What is {who.Label} wearing? (e.g. \"raincoat\") It starts from {(Outfit?.Asset == null ? "her own look" : Outfit.Label)}; you paint the clothes on.", "raincoat");
        if (string.IsNullOrWhiteSpace(name)) return null;
        string id = library.UniqueId(LibraryKind.Pose, name.StartsWith(who.Key, StringComparison.OrdinalIgnoreCase) ? name : who.Key + " " + name);
        string? basedOn = Outfit?.Asset;
        Edit("New outfit " + OutfitFactory.Label(id, who.Key), lib => lib.SavePose(OutfitFactory.Create(Context, lib, shell.Catalog, who.Key, id, basedOn)));
        if (Library?.Pose(id) == null) return null;
        RefreshOutfits();
        Outfit = Outfits.FirstOrDefault(o => string.Equals(o.Asset, StudioContext.OutfitKey(id), StringComparison.OrdinalIgnoreCase)) ?? Outfit;
        OpenItem(LibraryKind.Pose, id);
        return id;
    }

    /// <summary>Preview the chosen outfit everywhere in the studio (the canvas, the preview, frames, the character card, her colours).</summary>
    private void ApplyOutfit()
    {
        outfitOverride?.Dispose();
        outfitOverride = Outfit?.Asset is { } asset && Character is { IsFarmer: false } who ? Context.SheetOverride(who.Key, asset) : null;
        outfitAsset = Outfit?.Asset; outfitFor = Character?.Key;
        Context.InvalidatePack();
        RefreshCharacterCard();
        Palette.Refresh();
        foreach (var document in Documents.OfType<PoseDocumentViewModel>()) document.OutfitChanged();
    }

    /// <summary>What a sheet is called: an appearance's season or festival, else "Everyday".</summary>
    public string OutfitName(string? asset)
    {
        if (asset == null) return "As in the game now";
        return Outfits.FirstOrDefault(o => string.Equals(o.Asset, asset, StringComparison.OrdinalIgnoreCase))?.Label ?? asset.Split('/').Last();
    }

    private string Describe(CharacterEntry entry, string sprite) =>
        entry.Appearances.FirstOrDefault(a => string.Equals(a.Sprite, sprite, StringComparison.OrdinalIgnoreCase)) is { } a ? OutfitLabel(a, entry.Name) : sprite.Split('/').Last();

    internal static string OutfitLabel(AppearanceEntry appearance, string npc)
    {
        if (appearance.Condition is { } condition && Regex.Match(condition, @"festival_(\w+)", RegexOptions.IgnoreCase) is { Success: true } match
            && Festivals.TryGetValue(match.Groups[1].Value, out var festival)) return festival;
        if (appearance.IsIslandAttire) return "Ginger Island";
        if (appearance.Season is { Length: > 0 } season)
        {
            string name = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(season.ToLowerInvariant());
            return appearance.Indoors && !appearance.Outdoors ? name + " (indoors)" : !appearance.Indoors && appearance.Outdoors ? name + " (outdoors)" : name;
        }
        // From the id or the sheet: "...AbigailFlowerDance" or "Characters/Abigail_EggF" become "Flower Dance", "Egg F".
        string raw = appearance.Id?.Split('.').Last() ?? appearance.Sprite?.Split('/').Last() ?? "Outfit";
        if (raw.StartsWith(npc, StringComparison.OrdinalIgnoreCase)) raw = raw[npc.Length..];
        raw = Regex.Replace(raw.Replace('_', ' '), @"(?<=[a-z])(?=[A-Z])", " ").Trim();
        return raw.Length == 0 ? "Outfit" : raw;
    }

    private string? Origin(string? asset)
    {
        if (asset == null || shell.Catalog is not { } catalog || catalog.AssetOrigin(asset) is not { } origin) return null;
        return origin.IsModProvided ? catalog.ModLabel(origin.ProvidedBy[0]) : origin.IsEdited ? "vanilla, edited by " + catalog.ModLabel(origin.EditedBy[0]) : "vanilla";
    }
}
