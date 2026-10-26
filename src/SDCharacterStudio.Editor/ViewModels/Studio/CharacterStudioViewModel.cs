using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Runtime.Rendering;
using Wpf.Ui.Controls;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>Who the Character Studio is working on: an exported character or the player.</summary>
public sealed record CharacterChoice(string Key, string Label, string? Detail, bool IsFarmer, ImageSource? Thumbnail)
{
    public override string ToString() => Label;
}

public sealed partial class LibraryItemViewModel : ObservableObject
{
    public LibraryItemViewModel(CharacterStudioViewModel studio, LibraryItem item, ImageSource? thumbnail)
    {
        Studio = studio; Item = item; this.thumbnail = thumbnail;
        OpenCommand = new RelayCommand(() => studio.Open(item));
    }
    public CharacterStudioViewModel Studio { get; }
    public LibraryItem Item { get; }
    public string Title => Item.Title;
    public string? Detail => Item.Detail;
    public string Id => Item.Id;
    public LibraryKind Kind => Item.Kind;
    public bool IsPose => Item.Kind == LibraryKind.Pose;
    public bool CanSaveToMyLibrary => Item.Kind is LibraryKind.Pose or LibraryKind.Clip;
    public bool IsAnimation => Item.Kind == LibraryKind.Clip;
    public bool CanUseInScene => CharacterStudioViewModel.CanUseInScene(Item.Kind);
    [ObservableProperty] private ImageSource? thumbnail;
    public IRelayCommand OpenCommand { get; }
}

public sealed partial class LibrarySectionViewModel : ObservableObject
{
    public LibrarySectionViewModel(LibraryKind kind, string title, SymbolRegular icon, string newLabel, Action create, Func<PackLibrary, LibraryItem, bool>? shows = null)
    { Kind = kind; Title = title; Icon = icon; NewLabel = newLabel; NewCommand = new RelayCommand(create); Shows = shows; }
    public LibraryKind Kind { get; }
    /// <summary>Which of the kind's items this shelf holds (poses and outfits are both sheet-or-not poses).</summary>
    public Func<PackLibrary, LibraryItem, bool>? Shows { get; }
    public IRelayCommand NewCommand { get; }
    public string Title { get; }
    public SymbolRegular Icon { get; }
    public string NewLabel { get; }
    public ObservableCollection<LibraryItemViewModel> Items { get; } = new();
    /// <summary>Masks and rigs are Advanced (D-48): the simple editor makes and uses them by itself.</summary>
    [ObservableProperty] private bool isShown = true;
    [ObservableProperty] private bool isExpanded = true;
    public string Header => Items.Count == 0 ? Title : $"{Title} ({Items.Count})";
    public void Changed() => OnPropertyChanged(nameof(Header));
}

/// <summary>
/// The Character Studio workspace (doc 07 §15, D-39): pick a character, browse and open the library (poses, animations,
/// masks, rigs, expressions, portraits), and edit open items in tabs. All edits go through the pack document.
/// </summary>
public sealed partial class CharacterStudioViewModel : ObservableObject
{
    private readonly MainViewModel shell;
    private bool refreshing;

    public CharacterStudioViewModel(MainViewModel shell)
    {
        this.shell = shell;
        Context = new StudioContext(() => shell.Document, () => shell.Catalog);
        Settings = StudioSettings.Load();
        Palette = new PaletteViewModel(this);
        Sections = new ObservableCollection<LibrarySectionViewModel>(new[]
        {
            new LibrarySectionViewModel(LibraryKind.Pose, "Poses", SymbolRegular.Accessibility24, "New pose", () => New(LibraryKind.Pose), (lib, item) => lib.Pose(item.Id)?.Kind != "sheet"),
            new LibrarySectionViewModel(LibraryKind.Pose, "Outfits", SymbolRegular.Grid24, "New outfit", () => NewOutfit(), (lib, item) => lib.Pose(item.Id)?.Kind == "sheet"),
            new LibrarySectionViewModel(LibraryKind.Clip, "Animations", SymbolRegular.VideoClipMultiple24, "New animation", () => New(LibraryKind.Clip)),
            new LibrarySectionViewModel(LibraryKind.Mask, "Masks", SymbolRegular.CircleHalfFill24, "New mask", () => New(LibraryKind.Mask)),
            new LibrarySectionViewModel(LibraryKind.Rig, "Rigs", SymbolRegular.PersonEdit24, "New rig", () => New(LibraryKind.Rig)),
            new LibrarySectionViewModel(LibraryKind.Expression, "Expressions", SymbolRegular.EmojiSmileSlight24, "New expression", () => New(LibraryKind.Expression)),
            new LibrarySectionViewModel(LibraryKind.Portrait, "Portraits", SymbolRegular.PersonCircle24, "New portrait", () => New(LibraryKind.Portrait)),
        });
    }

    public MainViewModel Shell => shell;
    public StudioContext Context { get; }
    public StudioSettings Settings { get; }
    public PaletteViewModel Palette { get; }
    public PackLibrary? Library => shell.Document is { } doc ? new PackLibrary(doc) : null;
    public ObservableCollection<CharacterChoice> Characters { get; } = new();
    public ObservableCollection<LibrarySectionViewModel> Sections { get; }
    public ObservableCollection<StudioDocumentViewModel> Documents { get; } = new();
    [ObservableProperty] private CharacterChoice? character;
    [ObservableProperty] private StudioDocumentViewModel? activeDocument;
    [ObservableProperty] private string search = "";
    [ObservableProperty] private bool onlyThisCharacter;
    [ObservableProperty] private string? rigStatus;
    [ObservableProperty] private bool hasRig;
    public ObservableCollection<ImageSource> CharacterFrames { get; } = new();
    public bool HasProject => shell.Document != null;
    public bool HasDocuments => Documents.Count > 0;
    public bool ShowsFashionSenseWarning => Character?.IsFarmer == true
        && shell.Catalog?.Mods.Any(m => string.Equals(m.UniqueID, "PeacefulEnd.FashionSense", StringComparison.OrdinalIgnoreCase)) == true;

    partial void OnCharacterChanged(CharacterChoice? value)
    {
        if (value != null && !refreshing) { Settings.LastCharacter = value.Key; Settings.Save(); }
        RefreshOutfits();
        RefreshCharacterCard();
        RefreshLibrary();
        Palette.Refresh();
        OnPropertyChanged(nameof(CopyForLabel));
        OnPropertyChanged(nameof(ShowsFashionSenseWarning));
        OnPropertyChanged(nameof(CanMakeOutfit));
    }
    partial void OnSearchChanged(string value) { RefreshLibrary(); if (ShowMyLibrary) RefreshMyLibrary(); }
    partial void OnOnlyThisCharacterChanged(bool value) => RefreshLibrary();
    partial void OnActiveDocumentChanged(StudioDocumentViewModel? value)
    {
        value?.Activated(); Palette.Refresh();
        OnPropertyChanged(nameof(CanUseActiveInScene)); OnPropertyChanged(nameof(UseActiveInSceneHint));
    }

    // ---------------- refresh ----------------

    /// <summary>After opening a project, loading the export, or any edit/undo.</summary>
    public void Refresh()
    {
        refreshing = true;
        try
        {
            Context.InvalidatePack();
            OnPropertyChanged(nameof(HasProject)); OnPropertyChanged(nameof(CanMakeOutfit));
            RefreshCharacters();
            RefreshOutfits();
            OnPropertyChanged(nameof(ShowsFashionSenseWarning));
            RefreshCharacterCard();
            RefreshLibrary();
            foreach (var document in Documents.ToArray())
                if (!document.Reload()) Close(document);
            Palette.Refresh();
        }
        finally { refreshing = false; }
    }

    private void RefreshCharacters()
    {
        string? keep = Character?.Key ?? Settings.LastCharacter;
        var list = new List<CharacterChoice> { new("farmer", "Player", "you", true, PlayerThumbnail()) };
        if (shell.Catalog is { } catalog)
            list.AddRange(catalog.Characters.Select(c => new CharacterChoice(c.Name, c.DisplayName ?? c.Name,
                c.Origin.IsModProvided ? catalog.ModLabel(c.Origin.ProvidedBy[0]) : c.DisplayName != null && c.DisplayName != c.Name ? c.Name : null, false, null)));
        foreach (var rig in Library?.Items(LibraryKind.Rig) ?? Enumerable.Empty<LibraryItem>())
            if (Library!.Rig(rig.Id) is { Kind: "Npc", Npc: { } npc } && !list.Any(c => string.Equals(c.Key, npc, StringComparison.OrdinalIgnoreCase)))
                list.Add(new CharacterChoice(npc, npc, "rig only", false, null));
        if (!Characters.Select(c => c.Key).SequenceEqual(list.Select(c => c.Key)))
        {
            Characters.Clear();
            foreach (var c in list) Characters.Add(c);
        }
        Character = Characters.FirstOrDefault(c => string.Equals(c.Key, keep, StringComparison.OrdinalIgnoreCase)) ?? Characters.FirstOrDefault();
    }

    private ImageSource? PlayerThumbnail() => Context.Farmer?.Render(0, 2, FarmerPart.Full) is { } image ? StudioImages.ToBitmap(StudioImages.Crop(image, 8, 16, 16, 32)) : null;

    /// <summary>Standing frames (down, right, up, left) and rig status for the chosen character.</summary>
    private void RefreshCharacterCard()
    {
        CharacterFrames.Clear();
        if (Character == null) { RigStatus = null; HasRig = false; return; }
        foreach (var facing in RigFactory.Facings)
            if (StandingFrame(Character, facing) is { } image) CharacterFrames.Add(StudioImages.ToBitmap(image));
        var rig = Character.IsFarmer ? Library?.FarmerRig() : Library?.RigFor(Character.Key);
        HasRig = rig != null || Character.IsFarmer;
        RigStatus = Character.IsFarmer ? "The player is layered: no rig needed for parts."
            : rig != null ? $"Rig '{rig.Id}' · {rig.FrameSize[0]}×{rig.FrameSize[1]} · head masks: {rig.HeadMask.Count}/4"
            : "No rig yet. One is made automatically the first time you use this character, or make it now.";
    }

    /// <summary>The character's standing frame for a facing, from the export (sprite space, cropped to its frame).</summary>
    public Core.Compositing.RgbaImage? StandingFrame(CharacterChoice choice, string facing)
    {
        if (choice.IsFarmer)
        {
            int dir = FarmerExport.FacingIndex(facing);
            var image = Context.Farmer?.Render(dir switch { 0 => 12, 1 or 3 => 6, _ => 0 }, dir, FarmerPart.Full);
            return image;
        }
        var character = shell.Catalog?.Character(choice.Key);
        var sheet = Context.NpcTexture(choice.Key, false);
        if (character == null || sheet == null) return null;
        var rig = Library?.RigFor(choice.Key);
        int index = rig != null && rig.Frames.TryGetValue(facing, out var frames) ? frames.Stand : facing switch { "right" => 4, "up" => 8, "left" => 12, _ => 0 };
        return StudioImages.Frame(sheet, character.FrameWidth, character.FrameHeight, index);
    }

    /// <summary>The simple studio (D-48): no masks, rigs or layers in sight.</summary>
    public bool IsSimple => !Settings.AdvancedPoseTools;

    public void RefreshLibrary()
    {
        OnPropertyChanged(nameof(IsSimple));
        var library = Library;
        foreach (var section in Sections)
        {
            section.Items.Clear();
            section.IsShown = Settings.AdvancedPoseTools || section.Kind is not (LibraryKind.Mask or LibraryKind.Rig);
            if (library == null) { section.Changed(); continue; }
            foreach (var item in library.Items(section.Kind).OrderBy(i => i.Title, StringComparer.OrdinalIgnoreCase))
            {
                // A pose's own edit masks (erased and moved pixels) belong to the pose, not the mask shelf.
                if (section.Kind == LibraryKind.Mask && library.Mask(item.Id)?.Tags.Contains(PoseDocumentViewModel.EditMaskTag) == true) continue;
                if (section.Shows != null && !section.Shows(library, item)) continue;
                if (Search.Length > 0 && !(item.Title.Contains(Search, StringComparison.OrdinalIgnoreCase) || item.Id.Contains(Search, StringComparison.OrdinalIgnoreCase) || (item.Detail?.Contains(Search, StringComparison.OrdinalIgnoreCase) ?? false))) continue;
                if (OnlyThisCharacter && Character != null && !BelongsTo(library, item, Character)) continue;
                section.Items.Add(new LibraryItemViewModel(this, item, Thumbnail(library, item)));
            }
            section.Changed();
        }
    }

    private static bool BelongsTo(PackLibrary library, LibraryItem item, CharacterChoice character)
    {
        string key = character.Key;
        bool Match(string? rigOrNpc) => rigOrNpc != null && (string.Equals(rigOrNpc, key, StringComparison.OrdinalIgnoreCase)
            || string.Equals(library.Rig(rigOrNpc)?.Npc, key, StringComparison.OrdinalIgnoreCase) || (character.IsFarmer && string.Equals(library.Rig(rigOrNpc)?.Kind, "Farmer", StringComparison.OrdinalIgnoreCase)));
        return item.Kind switch
        {
            LibraryKind.Pose => Match(library.Pose(item.Id)?.For),
            LibraryKind.Rig => Match(item.Id),
            LibraryKind.Portrait => Match(library.Portrait(item.Id)?.For),
            LibraryKind.Expression => Match(library.Expressions().Expressions.FirstOrDefault(e => e.Id == item.Id)?.For),
            LibraryKind.Mask => library.Mask(item.Id)?.Tags.Any(t => Match(t)) == true || item.Id.StartsWith(key, StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    private ImageSource? Thumbnail(PackLibrary library, LibraryItem item)
    {
        try
        {
            switch (item.Kind)
            {
                case LibraryKind.Pose when library.Pose(item.Id) is { Frames.Count: > 0 } pose:
                    return StudioImages.ToBitmap(Context.RenderFrame(pose, pose.Kind == "sheet" ? 0 : pose.Loop.FirstOrDefault()).Image);
                case LibraryKind.Clip:
                    return ClipThumbnail(item.Id);
                case LibraryKind.Mask when library.Mask(item.Id)?.Image is { } path && Context.PackImage(path) is { } mask:
                    return StudioImages.ToBitmap(mask);
                case LibraryKind.Rig when library.Rig(item.Id) is { Npc: { } npc }:
                    return StandingFrame(new CharacterChoice(npc, npc, null, false, null), "down") is { } frame ? StudioImages.ToBitmap(frame) : null;
                case LibraryKind.Expression when library.Expressions().Expressions.FirstOrDefault(e => string.Equals(e.Id, item.Id, StringComparison.OrdinalIgnoreCase)) is { } expression:
                    return ExpressionDocumentViewModel.Picture(Context, library.Rig(expression.For), expression) is { } face ? StudioImages.ToBitmap(face) : null;
                case LibraryKind.Portrait when library.Portrait(item.Id) is { Emotions.Count: > 0 } portrait:
                    // Its neutral face (or the first one it has), drawn like the editor draws it.
                    var first = portrait.Emotions.OrderBy(e => PortraitDocumentViewModel.EmotionIndex(e.Key)).First().Value;
                    var faces = new Pose
                    {
                        Format = "1.0", Id = portrait.Id, For = portrait.For, Kind = "portrait", FrameSize = (int[])portrait.FrameSize.Clone(), Facing = "down", MirrorForOppositeFacing = false,
                        Frames = { new PoseFrame { Duration = 100, Layers = first.Layers, Anchors = first.Anchors } }
                    };
                    return StudioImages.ToBitmap(Context.RenderFrame(faces, 0).Image);
                default: return null;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return null; }
    }

    // ---------------- documents ----------------

    public void Open(LibraryItem item)
    {
        var open = Documents.FirstOrDefault(d => d.Kind == item.Kind && string.Equals(d.ItemId, item.Id, StringComparison.OrdinalIgnoreCase));
        if (open == null)
        {
            open = item.Kind switch
            {
                LibraryKind.Pose => new PoseDocumentViewModel(this, item.Id),
                LibraryKind.Clip => new AnimationDocumentViewModel(this, item.Id),
                LibraryKind.Mask => new MaskDocumentViewModel(this, item.Id),
                LibraryKind.Rig => new RigDocumentViewModel(this, item.Id),
                LibraryKind.Expression => new ExpressionDocumentViewModel(this, item.Id),
                LibraryKind.Portrait => new PortraitDocumentViewModel(this, item.Id),
                _ => null
            };
            if (open == null || !open.Reload()) { shell.Ui.Toast("Can't open", $"'{item.Id}' couldn't be read. The Problems panel says why.", ToastKind.Warning); return; }
            Documents.Add(open);
            OnPropertyChanged(nameof(HasDocuments));
        }
        ActiveDocument = open;
    }

    public void OpenItem(LibraryKind kind, string id) => Open(new LibraryItem(kind, id, "", id, null));

    [RelayCommand]
    public void Close(StudioDocumentViewModel? document)
    {
        if (document == null) return;
        int index = Documents.IndexOf(document);
        document.Closing();
        Documents.Remove(document);
        if (ReferenceEquals(ActiveDocument, document)) ActiveDocument = Documents.ElementAtOrDefault(Math.Max(0, index - 1));
        OnPropertyChanged(nameof(HasDocuments));
    }

    /// <summary>Apply a studio change as one undo step; the document's Changed event refreshes the studio.</summary>
    public void Edit(string label, Action<PackLibrary> apply)
    {
        if (shell.Document is not { } doc) return;
        var library = new PackLibrary(doc);
        shell.Edit(label, () => apply(library));
    }

    // ---------------- new items ----------------

    [RelayCommand]
    public void New(LibraryKind kind)
    {
        if (shell.Document == null) { shell.Ui.Toast("No project", "Open or create a project first.", ToastKind.Info); return; }
        switch (kind)
        {
            case LibraryKind.Pose: NewPose("character"); break;
            case LibraryKind.Clip: NewAnimation(); break;
            case LibraryKind.Mask: NewMask(); break;
            case LibraryKind.Rig: MakeRig(); break;
            case LibraryKind.Expression: NewExpression(); break;
            case LibraryKind.Portrait: NewPortrait(); break;
        }
    }

    [RelayCommand] private void NewSheetPose() => NewOutfit();
    [RelayCommand] private void NewEffect() => NewPose("effect");

    /// <summary>A pose for the chosen character: standing frame to start from, its rig, facing and frame size.</summary>
    public string? NewPose(string kind, string? name = null, string? facing = null)
    {
        if (Character is not { } who || Library is not { } library) return null;
        name ??= shell.Ui.Prompt(kind switch { "sheet" => "New sheet pose", "effect" => "New effect", _ => "New pose" },
            kind switch { "sheet" => $"Name of the replacement walk sheet for {who.Label} (e.g. \"raincoat\")", "effect" => "Name of the effect (e.g. \"sparkle\")", _ => $"What is {who.Label} doing? (e.g. \"wave hello\")" },
            kind == "effect" ? "effect" : $"{who.Label} pose");
        if (string.IsNullOrWhiteSpace(name)) return null;
        string id = library.UniqueId(LibraryKind.Pose, kind == "effect" ? name : (name.StartsWith(who.Key, StringComparison.OrdinalIgnoreCase) ? name : who.Key + " " + name));
        facing ??= kind == "sheet" ? null : "left";
        // Made while one of your outfits is shown: the pose starts from her in it, so it wears it.
        string? wearing = kind == "character" && !who.IsFarmer ? StudioContext.OutfitId(Outfit?.Asset) : null;
        Edit("New pose " + id, lib =>
        {
            var pose = PoseFactory.Create(this, lib, who, kind, id, facing);
            if (wearing != null)
                foreach (var live in pose.Frames.SelectMany(f => f.Layers).OfType<LiveLayer>().Where(l => l.Source?.Kind == "Npc"))
                    live.Source = new LiveSource { Kind = Core.Compositing.Outfits.SourceKind, Npc = live.Source!.Npc ?? who.Key, Outfit = wearing };
            if (!Settings.AdvancedPoseTools) FlatPose.MakeFlat(lib, pose);   // the simple editor works on one flat picture
            lib.SavePose(pose);
        });
        OpenItem(LibraryKind.Pose, id);
        if (wearing != null) shell.Ui.Toast("Wears your outfit", $"This pose wears {Outfit!.Label}, the outfit shown. Change it under Outfit, on the right.", ToastKind.Info);
        return id;
    }

    public string? NewAnimation(string? name = null)
    {
        if (Library is not { } library) return null;
        name ??= shell.Ui.Prompt("New animation", "What happens? (e.g. \"step and bow\")", "animation");
        if (string.IsNullOrWhiteSpace(name)) return null;
        string id = library.UniqueId(LibraryKind.Clip, name);
        string role = Character is { IsFarmer: true } ? "player" : LibraryClipsRole(Character);
        var clip = new LibraryClip
        {
            Format = "1.0", Id = id, Title = name.Trim(), Duration = 2000,
            Roles = { new ClipRole { Id = role, Kind = Character is { IsFarmer: true } ? "Farmer" : "Npc" } },
            Tracks = { new ClipTrack { Role = role, Position = new() { new PositionKey { At = 0, Tile = new[] { 0, 0 } } } } }
        };
        string? who = Character?.Key;
        Edit("New animation " + id, lib =>
        {
            if (!Settings.AdvancedPoseTools)
            {
                // The simple editor makes flipbooks (D-48): start with the character's first pose, else their own look.
                clip.Extra = new Dictionary<string, Newtonsoft.Json.Linq.JToken> { [Flipbook.CharacterKey] = who };
                string? firstPose = lib.Items(LibraryKind.Pose).Select(i => lib.Pose(i.Id)).FirstOrDefault(p => p?.Kind == "character" && who != null
                    && string.Equals(lib.Rig(p.For)?.Npc ?? p.For, who, StringComparison.OrdinalIgnoreCase) && p.Id?.Contains("-walk-", StringComparison.Ordinal) != true)?.Id;
                var first = firstPose != null ? new FlipbookStep { Do = "Pose", Pose = firstPose, Ms = 1000 } : new FlipbookStep { Do = "Own", Ms = 1000 };
                Flipbook.Write(clip, new[] { first }, facing => who == null ? null : Flipbook.EnsureWalkPose(lib, who, facing));
            }
            lib.SaveClip(clip);
        });
        OpenItem(LibraryKind.Clip, id);
        return id;
    }

    private static string LibraryClipsRole(CharacterChoice? who) => who == null ? "actor" : Core.Compiler.LibraryClips.RoleId(who.Key);

    public string? NewMask(string? name = null)
    {
        if (Character is not { IsFarmer: false } who || Library is not { } library) { shell.Ui.Toast("Pick a character", "Masks cut parts out of a character's sprite. Pick an NPC first.", ToastKind.Info); return null; }
        name ??= shell.Ui.Prompt("New mask", $"What part of {who.Label}? (e.g. \"head left\", \"body upper left\")", "head left");
        if (string.IsNullOrWhiteSpace(name)) return null;
        string id = PackDocument.ToContentId(who.Key + " " + name);
        for (int n = 2; library.Mask(id) != null; n++) id = PackDocument.ToContentId($"{who.Key} {name} {n}");
        var character = shell.Catalog?.Character(who.Key);
        int width = character?.FrameWidth ?? 16, height = character?.FrameHeight ?? 32;
        string maskId = id;
        Edit("New mask " + maskId, lib =>
        {
            string rigId = RigFactory.EnsureNpcRig(lib, shell.Catalog, who.Key, Context);
            var rig = lib.Rig(rigId)!;
            int neck = rig.NeckPoint.Values.FirstOrDefault()?[1] ?? height / 2 - 1;
            string path = lib.UniquePath($"masks/{maskId}.png");
            lib.WriteImage(path, PixelTools.AboveRow(width, height, neck));
            lib.SaveMask(new Mask { Id = maskId, DisplayName = $"{who.Label} {name.Trim()}", FrameSize = new[] { width, height }, Image = path, Mode = "keep", Tags = new() { rigId } });
        });
        OpenItem(LibraryKind.Mask, maskId);
        return maskId;
    }

    [RelayCommand]
    public void MakeRig()
    {
        if (Character is not { } who || shell.Document == null) return;
        string? id = null;
        if (who.IsFarmer) Edit("Make player rig", lib => id = RigFactory.EnsureFarmerRig(lib));
        else Edit("Make rig for " + who.Label, lib => id = RigFactory.EnsureNpcRig(lib, shell.Catalog, who.Key, Context));
        if (id != null) OpenItem(LibraryKind.Rig, id);
    }

    public string? NewExpression(string? name = null)
    {
        if (Character is not { IsFarmer: false } who || Library is not { } library) { shell.Ui.Toast("Pick a character", "Expressions are painted over an NPC's face. Pick an NPC first.", ToastKind.Info); return null; }
        name ??= shell.Ui.Prompt("New expression", $"Name of {who.Label}'s expression (e.g. \"eyes closed\", \"blush\")", "eyes closed");
        if (string.IsNullOrWhiteSpace(name)) return null;
        string id = PackDocument.ToContentId(who.Key + " " + name + " left");
        for (int n = 2; library.Expressions().Expressions.Any(e => e.Id == id); n++) id = PackDocument.ToContentId($"{who.Key} {name} left {n}");
        string expressionId = id;
        Edit("New expression " + expressionId, lib =>
        {
            string rigId = RigFactory.EnsureNpcRig(lib, shell.Catalog, who.Key, Context);
            var rig = lib.Rig(rigId)!;
            var box = rig.FaceBox.TryGetValue("left", out var b) ? b : new[] { 2, 9, 6, 6 };
            string path = lib.UniquePath($"art/expressions/{expressionId}.png");
            lib.WriteImage(path, StudioImages.Blank(box[2], box[3]));
            lib.SaveExpression(new Expression { Id = expressionId, For = rigId, Target = "Sprite", Facing = "left", Anchor = "FaceBox", Frames = { new ExpressionFrame { Image = path, Duration = 0 } }, ColorSlots = new() { ["Skin"] = $"{rigId}.Skin", ["Outline"] = $"{rigId}.Outline" } });
        });
        OpenItem(LibraryKind.Expression, expressionId);
        return expressionId;
    }

    public string? NewPortrait(string? name = null)
    {
        if (Character is not { IsFarmer: false } who || Library is not { } library) { shell.Ui.Toast("Pick a character", "Portraits are built on an NPC's live portrait. Pick an NPC first.", ToastKind.Info); return null; }
        name ??= shell.Ui.Prompt("New portrait", $"Name of {who.Label}'s portrait set (e.g. \"raincoat\")", "outfit");
        if (string.IsNullOrWhiteSpace(name)) return null;
        string id = library.UniqueId(LibraryKind.Portrait, who.Key + " " + name);
        Edit("New portrait " + id, lib =>
        {
            string rigId = RigFactory.EnsureNpcRig(lib, shell.Catalog, who.Key, Context);
            var portrait = new Portrait { Format = "1.0", Id = id, For = rigId, FrameSize = new[] { 64, 64 } };
            foreach (var (emotion, index) in new[] { ("neutral", 0), ("happy", 1), ("sad", 2), ("unique", 3), ("love", 4), ("angry", 5) })
                portrait.Emotions[emotion] = new PortraitFrame { Layers = { new LiveLayer { Source = new LiveSource { Kind = "Portrait", Npc = who.Key }, Part = "Full", SourceFrame = new SourceFrame { Index = index } } } };
            lib.SavePortrait(portrait);
        });
        OpenItem(LibraryKind.Portrait, id);
        return id;
    }

    // ---------------- item actions ----------------

    [RelayCommand]
    private void Delete(LibraryItemViewModel? item)
    {
        if (item == null) return;
        if (!shell.Ui.Confirm("Delete?", $"Delete {item.Kind.ToString().ToLowerInvariant()} '{item.Title}' from the pack? Scenes using it will show a problem. (Undo brings it back.)", "Delete")) return;
        foreach (var open in Documents.Where(d => d.Kind == item.Kind && d.ItemId == item.Id).ToArray()) Close(open);
        Edit("Delete " + item.Id, lib => lib.Delete(item.Kind, item.Id));
    }

    [RelayCommand]
    private void Duplicate(LibraryItemViewModel? item)
    {
        if (item == null || Library is not { } library) return;
        string id = library.UniqueId(item.Kind, item.Id + "-copy");
        Edit("Duplicate " + item.Id, lib => LibraryCopies.Duplicate(lib, item.Kind, item.Id, id));
        OpenItem(item.Kind, id);
    }

    public void ShowScenes() => shell.IsCharacterStudio = false;
}
