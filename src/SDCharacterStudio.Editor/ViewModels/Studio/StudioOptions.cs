using SDCharacterStudio.Core;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>Choice lists shared by the studio editors (the scene inspector has its own copies).</summary>
public static class StudioOptions
{
    public static IEnumerable<ChoiceItem> Facings() => new[] { new ChoiceItem("down", "Down ▼"), new ChoiceItem("left", "Left ◀"), new ChoiceItem("up", "Up ▲"), new ChoiceItem("right", "Right ▶") };
    public static IEnumerable<ChoiceItem> Play() => new[] { new ChoiceItem("once", "Once (stop on the last frame)"), new ChoiceItem("loop", "Loop"), new ChoiceItem("once-then-loop", "Intro, then loop"), new ChoiceItem("hold-first", "Hold the first frame") };
    public static IEnumerable<ChoiceItem> Ease() => new[] { new ChoiceItem("linear", "Steady"), new ChoiceItem("inOut", "Smooth start and stop"), new ChoiceItem("in", "Speed up"), new ChoiceItem("out", "Slow down"), new ChoiceItem("step", "Jump at the end") };
    public static IEnumerable<ChoiceItem> StepFps() => new[] { new ChoiceItem("0", "Smooth"), new ChoiceItem("12", "12 fps (on twos)"), new ChoiceItem("8", "8 fps"), new ChoiceItem("6", "6 fps") };
    public static IEnumerable<ChoiceItem> Sounds(ExportCatalog? catalog) => (catalog?.Sounds ?? new[] { "dwop", "coin", "shwip", "thudStep", "money", "getNewSpecialItem" }).Select(s => new ChoiceItem(s, s));
    public static IEnumerable<ChoiceItem> Emotes(ExportCatalog? catalog) => Ids.Emotes.OrderBy(e => e.Value).Select(e => new ChoiceItem(e.Key, catalog?.Emotes.FirstOrDefault(x => x.Id == e.Value)?.DisplayName ?? e.Key));
    public static IEnumerable<ChoiceItem> Effects(PackLibrary? library) => new[] { "dust", "sweat-drop", "steam", "sparkle", "heart-burst", "tears" }.Select(e => new ChoiceItem(e, e))
        .Concat((library?.Items(LibraryKind.Pose) ?? Enumerable.Empty<LibraryItem>()).Where(p => library!.Pose(p.Id)?.Kind == "effect").Select(p => new ChoiceItem(p.Id, p.Id, "custom effect")));
    public static IEnumerable<ChoiceItem> RoleKinds() => new[] { new ChoiceItem("Npc", "A character"), new ChoiceItem("Farmer", "The player"), new ChoiceItem("Any", "Anyone") };

    /// <summary>The player's render parts a Parts key can hide (doc 03 §7.3).</summary>
    public static readonly string[] RenderParts = { "Base", "Head", "Body", "Eyes", "Pants", "Shirt", "Accessory", "Hair", "Hat", "Arms", "Shoes" };
    /// <summary>The look keys a Parts key or costume can set (doc 03 §13).</summary>
    public static readonly string[] LookParts = { "Hat", "Shirt", "Pants", "Shoes", "Hair", "HairColor", "Accessory", "Skin", "EyeColor" };

    public static JToken DefaultPartValue(string part) => part switch { "Hat" or "Shoes" => "none", "Hair" => 0, "Accessory" => -1, "Skin" => 0, "HairColor" or "EyeColor" => "#7F4A2A", _ => "original" };
    public static string PartLabel(string part) => part switch { "HairColor" => "Hair colour", "EyeColor" => "Eye colour", "Shoes" => "Shoes/boots", _ => part };

    public static IEnumerable<ChoiceItem> PartValues(ExportCatalog? catalog, string part)
    {
        var list = new List<ChoiceItem> { new("original", "Their own (original)") };
        if (part is "Hat" or "Shoes") list.Add(new ChoiceItem("none", "None (take it off)"));
        string? type = part switch { "Hat" => "(H)", "Shirt" => "(S)", "Pants" => "(P)", "Shoes" => "(B)", _ => null };
        if (type != null && catalog?.ItemsByType.TryGetValue(type, out var items) == true)
            list.AddRange(items.Select(i => new ChoiceItem(i.Qualified, $"{i.Label}  {i.Qualified}", null, Images.Load(catalog.FullPath(i.Icon)))));
        if (part == "Hair" && catalog != null)
            list.AddRange(catalog.Hairstyles.Select(h => new ChoiceItem(h.Index.ToString(CultureInfo.InvariantCulture), $"Hairstyle {h.Index}{(h.Origin.IsModProvided ? " · " + catalog.ModLabel(h.Origin.ProvidedBy[0]) : "")}")));
        return list;
    }

    /// <summary>The "keep the hat, lose the hat, change the shirt" editor (doc 07 §7.2): one row per changed part.</summary>
    public static ListField LookList(string label, string key, ExportCatalog? catalog, Func<Dictionary<string, JToken>?> get, Action<Dictionary<string, JToken>?> set, FieldCommit commit)
    {
        var field = new ListField(label, key, "Change a part", () =>
        {
            var current = get() ?? new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);
            string? free = LookParts.FirstOrDefault(p => !current.ContainsKey(p));
            if (free == null) return;
            commit("Change a part", () => { current[free] = DefaultPartValue(free); set(current); }, true);
        }) { EmptyText = "Nothing changes. Add a part: Hat none takes the hat off; Shirt picks another shirt." };
        foreach (var (part, value) in (get() ?? new()).ToArray())
        {
            string name = part;
            var row = new ListRowViewModel(() => commit("Remove part change", () => { var d = get()!; d.Remove(name); set(d.Count == 0 ? null : d); }, true));
            row.Cells.Add(new ChoiceField("", "", LookParts.Select(p => new ChoiceItem(p, PartLabel(p))), () => name, v =>
            {
                if (v == null || v == name) return;
                var d = get()!; d.Remove(name); d[v] = DefaultPartValue(v); set(d);
            }, commit) { Rebuild = true });
            row.Cells.Add(new ChoiceField("", $"{key}.{name}", PartValues(catalog, name), () => value.ToString(), v =>
            {
                var d = get()!;
                d[name] = name is "Hair" or "Accessory" or "Skin" && int.TryParse(v, out int n) ? n : v ?? "original";
                set(d);
            }, commit) { IsEditable = true });
            field.Rows.Add(row);
        }
        return field;
    }
}
