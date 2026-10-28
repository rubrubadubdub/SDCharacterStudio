using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Catalog;
using SDCharacterStudio.Editor.Infrastructure;
using SDCharacterStudio.Editor.Schema;
using Wpf.Ui.Controls;

namespace SDCharacterStudio.Editor.ViewModels;

public enum AssetKind { Character, Sprite, Portrait, Location, Item, Hairstyle, Sound, Emote, Video, Pose, Animation }
public enum BadgeKind { None, Provided, Edited, Unknown }

public sealed record AssetCategory(string Id, string Name, SymbolRegular Icon, bool IsPack = false)
{
    public override string ToString() => Name;
}

public sealed record SourceOption(string? ModId, string Label)
{
    public override string ToString() => Label;
}

/// <summary>The asset browser (doc 07 §2): the game's and every mod's characters, maps, items and more, from the Bridge export.</summary>
public sealed partial class AssetBrowserViewModel : ObservableObject
{
    private const int PageSize = 400;
    private readonly MainViewModel shell;
    private readonly HashSet<string> toasted = new(StringComparer.OrdinalIgnoreCase);
    private List<AssetItemViewModel> matches = new();

    public AssetBrowserViewModel(MainViewModel shell)
    {
        this.shell = shell;
        Categories = new ObservableCollection<AssetCategory>(new[]
        {
            new AssetCategory("characters", "Characters", SymbolRegular.People24),
            new AssetCategory("sprites", "Outfits & sprite sheets", SymbolRegular.PersonEdit24),
            new AssetCategory("portraits", "Portraits", SymbolRegular.PersonCircle24),
            new AssetCategory("locations", "Locations", SymbolRegular.Map24),
            new AssetCategory("objects", "Items", SymbolRegular.Box24),
            new AssetCategory("hats", "Hats", SymbolRegular.HatGraduation24),
            new AssetCategory("shirts", "Shirts", SymbolRegular.PersonEdit24),
            new AssetCategory("pants", "Pants", SymbolRegular.PersonEdit24),
            new AssetCategory("boots", "Boots", SymbolRegular.PersonEdit24),
            new AssetCategory("hair", "Hairstyles", SymbolRegular.Person24),
            new AssetCategory("furniture", "Furniture & craftables", SymbolRegular.Home24),
            new AssetCategory("sounds", "Sounds & music", SymbolRegular.Speaker224),
            new AssetCategory("emotes", "Emotes", SymbolRegular.EmojiSmileSlight24),
            new AssetCategory("videos", "My videos", SymbolRegular.MoviesAndTv24, true),
            new AssetCategory("poses", "My poses", SymbolRegular.Accessibility24, true),
            new AssetCategory("animations", "My animations", SymbolRegular.VideoClipMultiple24, true),
        });
        selectedCategory = Categories[0];
    }

    public ObservableCollection<AssetCategory> Categories { get; }
    public ObservableCollection<AssetItemViewModel> Items { get; } = new();
    public ObservableCollection<SourceOption> Sources { get; } = new() { new SourceOption(null, "All sources") };
    [ObservableProperty] private AssetCategory selectedCategory;
    [ObservableProperty] private string search = "";
    [ObservableProperty] private SourceOption? selectedSource;
    [ObservableProperty] private AssetItemViewModel? selected;
    [ObservableProperty] private string? emptyText;
    [ObservableProperty] private int totalCount;
    public bool HasMore => TotalCount > Items.Count;
    public string CountText => TotalCount == 0 ? "" : HasMore ? $"{Items.Count} of {TotalCount}" : $"{TotalCount} item{(TotalCount == 1 ? "" : "s")}";
    public string ShowMoreText => $"Show {Math.Min(PageSize, TotalCount - Items.Count)} more";
    public bool ShowBanner => shell.Catalog == null;
    public string Banner => shell.CatalogError ?? "No game export yet.";

    partial void OnSelectedCategoryChanged(AssetCategory value) => Refill();
    partial void OnSearchChanged(string value) => shell.Ui.Debounce("asset-search", TimeSpan.FromMilliseconds(150), Refill);
    partial void OnSelectedSourceChanged(SourceOption? value) => Refill();
    partial void OnSelectedChanged(AssetItemViewModel? value)
    {
        if (value?.Badge == BadgeKind.Provided && toasted.Add(value.Key))
            shell.Ui.Toast("From a mod", $"Using {value.Name} adds a dependency on {value.BadgeTooltip}.", ToastKind.Info);
    }

    public void Rebuild()
    {
        Sources.Clear();
        Sources.Add(new SourceOption(null, "All sources"));
        Sources.Add(new SourceOption("vanilla", "Vanilla game"));
        foreach (var mod in shell.Catalog?.Mods.OrderBy(m => m.Label, StringComparer.OrdinalIgnoreCase) ?? Enumerable.Empty<ModEntry>())
            Sources.Add(new SourceOption(mod.UniqueID, mod.Label));
        SelectedSource = Sources[0];
        OnPropertyChanged(nameof(ShowBanner)); OnPropertyChanged(nameof(Banner));
        Refill();
    }

    public void RefreshPackCategories() { if (SelectedCategory.IsPack) Refill(); }

    [RelayCommand] private void ShowMore() { foreach (var item in matches.Skip(Items.Count).Take(PageSize)) Items.Add(item); OnPropertyChanged(nameof(HasMore)); OnPropertyChanged(nameof(CountText)); OnPropertyChanged(nameof(ShowMoreText)); }

    private void Refill()
    {
        Items.Clear();
        var catalog = shell.Catalog;
        IEnumerable<AssetItemViewModel> all = SelectedCategory.Id switch
        {
            "videos" => (shell.Document?.Videos ?? new()).Select(v => new AssetItemViewModel(this, AssetKind.Video, v.Id, v.Id, $"{v.FrameCount} frames · {v.Width}×{v.Height}", () => Images.Load(shell.Document?.AssetFile(v.FirstFrame)), null)),
            "poses" => (shell.Document?.Library.Poses ?? new()).Select(p => new AssetItemViewModel(this, AssetKind.Pose, p.Value.Id ?? p.File, p.Value.Id ?? p.File, $"{p.Value.Kind} · {p.Value.Frames.Count} frames", () => shell.CharacterStudio.PoseThumbnail(p.Value.Id), null)),
            "animations" => (shell.Document?.Library.Clips ?? new()).Where(c => c.Value.Id != null).Select(c => new AssetItemViewModel(this, AssetKind.Animation, c.Value.Id!, string.IsNullOrWhiteSpace(c.Value.Title) ? c.Value.Id! : c.Value.Title!,
                $"{c.Value.Duration / 1000.0:0.#} s · {string.Join(", ", c.Value.Roles.Select(r => r.Id))}", () => shell.CharacterStudio.ClipThumbnail(c.Value.Id), null)),
            _ when catalog == null => Enumerable.Empty<AssetItemViewModel>(),
            "characters" => catalog.Characters.Select(c => new AssetItemViewModel(this, AssetKind.Character, c.Name, c.DisplayName ?? c.Name, c.DisplayName != null && c.DisplayName != c.Name ? c.Name : null,
                () => Images.Frame(catalog.TextureFile(c.Current?.Sprite ?? c.Sprite), c.FrameWidth, c.FrameHeight, 0), c.Origin)),
            "sprites" => SpriteAssets(catalog, "Characters/").Select(t => new AssetItemViewModel(this, AssetKind.Sprite, t.Asset, t.Asset.Replace("Characters/", ""), t.Character,
                () => Images.Frame(catalog.TextureFile(t.Asset), t.Width, t.Height, 0), catalog.AssetOrigin(t.Asset))),
            "portraits" => SpriteAssets(catalog, "Portraits/").Select(t => new AssetItemViewModel(this, AssetKind.Portrait, t.Asset, t.Asset.Replace("Portraits/", ""), t.Character,
                () => Images.Frame(catalog.TextureFile(t.Asset), 64, 64, 0), catalog.AssetOrigin(t.Asset))),
            "locations" => catalog.Locations.Select(l => new AssetItemViewModel(this, AssetKind.Location, l.Name, l.Name, $"{l.Width}×{l.Height} tiles",
                () => Images.Load(catalog.FullPath(l.Preview)), l.Origin)),
            "objects" => ItemsOf(catalog, "(O)"),
            "hats" => ItemsOf(catalog, "(H)"),
            "shirts" => ItemsOf(catalog, "(S)"),
            "pants" => ItemsOf(catalog, "(P)"),
            "boots" => ItemsOf(catalog, "(B)"),
            "furniture" => ItemsOf(catalog, "(F)").Concat(ItemsOf(catalog, "(BC)")),
            "hair" => catalog.Hairstyles.Select(h => new AssetItemViewModel(this, AssetKind.Hairstyle, h.Index.ToString(), $"Hairstyle {h.Index}", h.IsBaldStyle ? "bald" : null,
                () => Images.Crop(catalog.TextureFile(h.Texture), h.TileX * 16, h.TileY * 16, 16, 32), h.Origin)),
            "sounds" => catalog.Sounds.Select(s => new AssetItemViewModel(this, AssetKind.Sound, s, s, null, () => null, null)),
            "emotes" => catalog.Emotes.Where(e => !e.Hidden).Select(e => new AssetItemViewModel(this, AssetKind.Emote, e.Name ?? e.Id.ToString(), e.DisplayName ?? e.Name ?? "", $"#{e.Id}",
                () => Images.Crop(catalog.FullPath("emotes.png"), 0, e.Id * 4, 16, 16), null)),
            _ => Enumerable.Empty<AssetItemViewModel>()
        };
        string search = Search.Trim();
        if (search.Length > 0) all = all.Where(a => a.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || a.Key.Contains(search, StringComparison.OrdinalIgnoreCase) || (a.Detail?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        if (SelectedSource?.ModId is { } mod)
            all = mod == "vanilla" ? all.Where(a => a.Origin == null || a.Origin.Vanilla) : all.Where(a => a.Origin != null && (a.Origin.ProvidedBy.Contains(mod, StringComparer.OrdinalIgnoreCase) || a.Origin.EditedBy.Contains(mod, StringComparer.OrdinalIgnoreCase)));
        matches = all.ToList();
        TotalCount = matches.Count;
        foreach (var item in matches.Take(PageSize)) Items.Add(item);
        EmptyText = catalog == null && !SelectedCategory.IsPack ? null
            : TotalCount > 0 ? null : SelectedCategory.Id switch
            {
                "videos" => "No videos yet. Pack › Import video.",
                "poses" => "No poses in this pack yet. Make them in the Character Studio.",
                "animations" => "No animations yet. Make one in the Character Studio, or use one from My library.",
                "locations" when catalog?.HasWorld == false => "Load a save in the game so the Bridge exports maps.",
                _ => search.Length > 0 ? $"Nothing matches \"{search}\"." : "Nothing here."
            };
        OnPropertyChanged(nameof(HasMore)); OnPropertyChanged(nameof(CountText)); OnPropertyChanged(nameof(ShowMoreText));

        IEnumerable<AssetItemViewModel> ItemsOf(ExportCatalog c, string type) => c.ItemsByType.TryGetValue(type, out var list)
            ? list.Select(i => new AssetItemViewModel(this, AssetKind.Item, i.Qualified, i.Label, i.Qualified, () => Images.Load(c.FullPath(i.Icon)), i.Origin))
            : Enumerable.Empty<AssetItemViewModel>();
    }

    private static IEnumerable<(string Asset, string? Character, int Width, int Height)> SpriteAssets(ExportCatalog catalog, string prefix)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in catalog.Characters)
        {
            var assets = prefix == "Characters/" ? new[] { c.Sprite }.Concat(c.Appearances.Select(a => a.Sprite)) : new[] { c.Portrait }.Concat(c.Appearances.Select(a => a.Portrait));
            foreach (var asset in assets)
                if (asset != null && seen.Add(asset) && catalog.TextureFile(asset) != null) yield return (asset, c.Label, c.FrameWidth, c.FrameHeight);
        }
    }

    // ---------- using an asset ----------

    /// <summary>Double-click: do the obvious thing with the asset for the current selection.</summary>
    public void Use(AssetItemViewModel item)
    {
        var scene = shell.Scene;
        if (scene == null) return;
        var board = shell.Storyboard;
        string lane = shell.Selection is BeatSelection b ? b.Lane : shell.Selection is BranchSelection br ? br.Lane : LaneNames.Main;
        int at = shell.Selection is BeatSelection s ? s.Index + 1 : LaneNames.Get(scene, lane)?.Count ?? 0;
        switch (item.Kind)
        {
            case AssetKind.Character:
                var (lastX, lastY) = scene.Actors.LastOrDefault()?.Start is { } last ? shell.Stage.Staged.Tile(last) : (10, 10);
                shell.Stage.DropCharacter(item.Key, lastX + 1, lastY);
                break;
            case AssetKind.Location: shell.Stage.SetLocation(item.Key); break;
            case AssetKind.Item when SelectedBeat() is ItemBeat itemBeat: shell.Edit("Change item", () => itemBeat.Item = item.Key); break;
            case AssetKind.Item when SelectedBeat() is PropBeat propBeat: shell.Edit("Change item", () => propBeat.Item = item.Key); break;
            case AssetKind.Item: board.Insert(lane, at, BeatCatalog.Kind("Item"), beat => ((ItemBeat)beat).Item = item.Key); break;
            case AssetKind.Sound when SelectedBeat() is SoundBeat soundBeat: shell.Edit("Change sound", () => soundBeat.Cue = item.Key); break;
            case AssetKind.Sound: board.Insert(lane, at, BeatCatalog.Kind("Sound"), beat => ((SoundBeat)beat).Cue = item.Key); break;
            case AssetKind.Emote: board.Insert(lane, at, BeatCatalog.Kind("Emote"), beat => ((EmoteBeat)beat).Emote = item.Key); break;
            case AssetKind.Video: board.Insert(lane, at, BeatCatalog.Kind("Video"), beat => ((VideoBeat)beat).Video = item.Key); break;
            case AssetKind.Pose: board.Insert(lane, at, BeatCatalog.Kind("Pose"), beat => ((PoseBeat)beat).Pose = item.Key); break;
            // Cast onto the selected actor (dropping on an actor selects it first), doc 07 §15.
            case AssetKind.Animation: shell.CharacterStudio.UseClipInScene(item.Key, lane: lane, index: at); break;
            case AssetKind.Sprite when shell.Selection is ActorSelection a && a.Index < scene.Actors.Count && scene.Actors[a.Index].Kind == "Npc":
                var actor = scene.Actors[a.Index];
                shell.Edit($"Change {actor.Id}'s outfit", () => actor.Sprite = new SpriteReference { Asset = item.Key });
                break;
            case AssetKind.Sprite:
                board.Insert(lane, at, BeatCatalog.Kind("Costume"), beat => { var c = (CostumeBeat)beat; c.Actor = CharacterFor(item); c.Set = null; c.Sprite = new SpriteReference { Asset = item.Key }; });
                break;
            default: shell.Ui.Toast("Nothing to do", "Drag it somewhere instead.", ToastKind.Info); return;
        }
        if (item.Badge == BadgeKind.Provided) shell.Ui.Toast("Mod dependency", $"{item.Name} comes from {item.BadgeTooltip}; export lists it as a dependency.", ToastKind.Info);
    }

    private Beat? SelectedBeat() => shell.Selection is BeatSelection b && shell.Scene is { } scene && LaneNames.Get(scene, b.Lane) is { } list && b.Index < list.Count ? list[b.Index] : null;

    private string? CharacterFor(AssetItemViewModel sprite) => shell.Scene?.Actors.FirstOrDefault(a => a.Kind == "Npc" && sprite.Key.Contains(a.Npc ?? a.Id ?? "\u0000", StringComparison.OrdinalIgnoreCase))?.Id;

    public MainViewModel Shell => shell;
}

/// <summary>One thumbnail. Pictures load when first shown (the grid is virtualized).</summary>
public sealed partial class AssetItemViewModel : ObservableObject
{
    private readonly Func<ImageSource?> loadImage;
    private ImageSource? image;
    private bool loaded;
    public AssetItemViewModel(AssetBrowserViewModel browser, AssetKind kind, string key, string name, string? detail, Func<ImageSource?> loadImage, OriginInfo? origin)
    {
        Browser = browser; Kind = kind; Key = key; Name = name; Detail = detail; this.loadImage = loadImage; Origin = origin;
        var catalog = browser.Shell.Catalog;
        if (origin?.IsModProvided == true) { Badge = BadgeKind.Provided; BadgeText = Initials(catalog?.ModLabel(origin.ProvidedBy[0]) ?? origin.ProvidedBy[0]); BadgeTooltip = catalog?.ModLabel(origin.ProvidedBy[0]) ?? origin.ProvidedBy[0]; }
        else if (origin?.IsUnknown == true) { Badge = BadgeKind.Unknown; BadgeText = "?"; BadgeTooltip = "a mod the export couldn't identify"; }
        else if (origin?.IsEdited == true) { Badge = BadgeKind.Edited; BadgeText = "•"; BadgeTooltip = string.Join(", ", origin.EditedBy.Select(m => catalog?.ModLabel(m) ?? m)); }
        UseCommand = new RelayCommand(() => browser.Use(this));
    }
    public AssetBrowserViewModel Browser { get; }
    public AssetKind Kind { get; }
    public string Key { get; }
    public string Name { get; }
    public string? Detail { get; }
    public OriginInfo? Origin { get; }
    public BadgeKind Badge { get; }
    public string? BadgeText { get; }
    public string? BadgeTooltip { get; }
    public bool HasBadge => Badge != BadgeKind.None;
    public string OriginText => Badge switch
    {
        BadgeKind.Provided => $"From {BadgeTooltip}. Using it makes the pack depend on that mod.",
        BadgeKind.Edited => $"Vanilla, changed by {BadgeTooltip}. No dependency: without those mods it looks like vanilla.",
        BadgeKind.Unknown => "Not vanilla, and the export couldn't tell which mod adds it. Export will ask you.",
        _ => "Vanilla game content."
    };
    public ImageSource? Image { get { if (!loaded) { loaded = true; image = loadImage(); } return image; } }
    public bool IsSoundLike => Kind is AssetKind.Sound;
    public IRelayCommand UseCommand { get; }
    private static string Initials(string name)
    {
        var words = name.Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries).Where(w => char.IsLetterOrDigit(w[0])).ToArray();
        return words.Length switch { 0 => "M", 1 => words[0][..Math.Min(3, words[0].Length)].ToUpperInvariant(), _ => string.Concat(words.Take(3).Select(w => char.ToUpperInvariant(w[0]))) };
    }
}
