using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Catalog;
using SDCharacterStudio.Editor.Infrastructure;
using SDCharacterStudio.Editor.Project;
using SDCharacterStudio.Editor.Schema;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>
/// The Stage tab (doc 07 §3.1): the location backdrop on a 16-px tile grid, the actors, the camera frame, and markers for
/// the selected beat (a Move's destination, a Camera target). Coordinates are sprite pixels (1 tile = 16).
/// The stage shows the scene as it is once the selected beat has played (D-50): actors where their walks left them,
/// facing where they turned, in what they wear, with the camera where Camera beats put it.
/// </summary>
public sealed partial class StageViewModel : ObservableObject
{
    public const int Tile = 16;
    /// <summary>The player's view at 1920×1080 and 100% zoom, in sprite pixels (1 sprite px = 4 screen px).</summary>
    public const double ViewWidth = 1920 / 4.0, ViewHeight = 1080 / 4.0;
    private readonly MainViewModel shell;
    private Action<int, int>? pendingPick;

    public StageViewModel(MainViewModel shell) => this.shell = shell;
    private readonly CostumeRenders renders = CostumeRenders.Load();
    /// <summary>Farmer outfits on the stage the game hasn't rendered yet (their item icons show meanwhile).</summary>
    public IReadOnlyList<StageLook> PendingOutfits => Actors.Where(a => a.NeedsRender).Select(a => a.Look).GroupBy(l => l.OutfitKey).Select(g => g.First()).ToList();
    public bool HasPendingOutfits => Actors.Any(a => a.NeedsRender);

    public ObservableCollection<StageActorViewModel> Actors { get; } = new();
    /// <summary>Faint copies of actors where they stood before the selected beat walked them (D-50).</summary>
    public ObservableCollection<StageActorViewModel> Ghosts { get; } = new();
    /// <summary>The selected beat's walks, tile centre to tile centre.</summary>
    public ObservableCollection<StagePathViewModel> Paths { get; } = new();
    /// <summary>Items on the map at the moment (Prop beats, D-50).</summary>
    public ObservableCollection<StagePropViewModel> Props { get; } = new();
    public ObservableCollection<StageMarkerViewModel> Markers { get; } = new();

    /// <summary>
    /// The beat the stage shows the scene after (D-50): the last beat selected, kept while an actor or the scene is
    /// selected, so clicking an actor doesn't send everyone back to their start. Null: the start.
    /// </summary>
    private BeatSelection? moment;
    private IReadOnlyList<PlayedBeat> played = Array.Empty<PlayedBeat>();
    private PlayedBeat? cameraBy;
    /// <summary>"After beat 3 · Move: farmer → (38, 59)" (null at the start).</summary>
    [ObservableProperty] private string? momentText;
    public bool HasMoment => MomentText != null;
    public ObservableCollection<ChoiceItem> LocationOptions { get; } = new();

    [ObservableProperty] private LocationEntry? location;
    [ObservableProperty] private ImageSource? back;
    [ObservableProperty] private ImageSource? front;
    [ObservableProperty] private ImageSource? passable;
    [ObservableProperty] private double width = 40 * Tile;
    [ObservableProperty] private double height = 30 * Tile;
    [ObservableProperty] private double zoom = 3;
    [ObservableProperty] private bool showGrid = true;
    [ObservableProperty] private bool showPassability;
    [ObservableProperty] private bool showFront = true;
    [ObservableProperty] private bool showCamera = true;
    [ObservableProperty] private Rect cameraRect;
    [ObservableProperty] private bool cameraFollows;
    [ObservableProperty] private string? emptyText;
    [ObservableProperty] private string? mapWarning;
    [ObservableProperty] private string? mapWarningMod;
    [ObservableProperty] private string? pickPrompt;
    [ObservableProperty] private string? hoverText;

    public bool HasMapWarning => MapWarning != null;
    /// <summary>The banner is information (this scene is already split by install), not a warning.</summary>
    [ObservableProperty] private bool isMapInfo;
    /// <summary>The banner's button: make a variant, or open the other half of a split scene (null: no button).</summary>
    [ObservableProperty] private string? mapActionText;
    private string? mapPartner;
    /// <summary>Banners closed for a scene in this session (scene id and mod).</summary>
    private readonly HashSet<string> dismissedNotices = new(StringComparer.OrdinalIgnoreCase);
    public bool IsPicking => PickPrompt != null;
    public string LocationName => shell.Scene?.Location ?? "";
    public ChoiceItem? SelectedLocation
    {
        get => LocationOptions.FirstOrDefault(o => string.Equals(o.Value, shell.Scene?.Location, StringComparison.OrdinalIgnoreCase));
        set { if (value?.Value is { } name && shell.Scene is { } scene && !string.Equals(name, scene.Location, StringComparison.OrdinalIgnoreCase)) SetLocation(name); }
    }

    partial void OnZoomChanged(double value) { if (shell.Document != null) shell.Document.Project.StageZoom = value; }
    partial void OnMapWarningChanged(string? value) => OnPropertyChanged(nameof(HasMapWarning));
    partial void OnPickPromptChanged(string? value) => OnPropertyChanged(nameof(IsPicking));

    public void Rebuild()
    {
        var scene = shell.Scene;
        var catalog = shell.Catalog;
        Actors.Clear(); Ghosts.Clear(); Paths.Clear(); Props.Clear(); Markers.Clear();
        CaptureMoment(scene);
        RebuildLocationOptions();
        if (scene == null) { EmptyText = "Open or create a project to start staging."; Back = Front = Passable = null; MapWarning = null; OnPropertyChanged(nameof(LocationName)); return; }
        Location = catalog?.Location(scene.Location);
        if (Location != null)
        {
            Back = Images.Load(catalog!.FullPath(Location.Back));
            Front = Images.Load(catalog.FullPath(Location.Front));
            Passable = Images.Load(catalog.FullPath(Location.Passable));
            Width = Location.Width * Tile; Height = Location.Height * Tile;
            EmptyText = Back == null ? $"The export has no picture of {Location.Name}." : null;
            var edited = Location.Origin.EditedBy.FirstOrDefault();
            MapWarningMod = edited;
            MapNotice(scene, edited, catalog);
        }
        else
        {
            Back = Front = Passable = null; MapWarning = null; MapWarningMod = null; MapActionText = null; mapPartner = null;
            int maxX = scene.Actors.Select(a => a.Start?.Tile?[0] ?? 0).DefaultIfEmpty(20).Max() + 10;
            int maxY = scene.Actors.Select(a => a.Start?.Tile?[1] ?? 0).DefaultIfEmpty(15).Max() + 8;
            Width = Math.Max(30, maxX) * Tile; Height = Math.Max(20, maxY) * Tile;
            EmptyText = catalog == null ? "No game export yet: the stage shows a plain grid. Start the game with the SDCS Bridge and load a save to see real maps."
                : !catalog.HasWorld ? "The export has no maps yet (load a save in the game so the Bridge exports locations)."
                : $"'{scene.Location}' isn't in the game export. Pick a location above.";
        }
        AddActors(scene);
        UpdateCamera(scene);
        AddMarkers(scene);
        SyncSelection();
        OnPropertyChanged(nameof(LocationName)); OnPropertyChanged(nameof(SelectedLocation));
    }

    /// <summary>
    /// The actors at the moment: where they are, facing and wearing what the beats so far say (D-49, D-50). One the
    /// moment's own beat walks also shows faintly where they started, with the path between.
    /// </summary>
    private void AddActors(Cutscene scene)
    {
        Actors.Clear(); Ghosts.Clear(); Paths.Clear(); Props.Clear();
        foreach (var (by, prop) in StageTimeline.Props(played))
            Props.Add(new StagePropViewModel(this, by, prop, shell.Catalog, selected: played.Count > 0 && ReferenceEquals(played[^1].Beat, prop)));
        for (int i = 0; i < scene.Actors.Count; i++)
        {
            var look = StageLooks.For(scene, scene.Actors[i], shell.Document, played);
            var place = StageTimeline.Place(scene, scene.Actors[i], played, shell.Document?.Library);
            var actor = new StageActorViewModel(this, i, scene.Actors[i], shell.Catalog, look, renders, place);
            Actors.Add(actor);
            if (place.From is { } from)
            {
                Ghosts.Add(new StageActorViewModel(this, i, scene.Actors[i], shell.Catalog, look, renders, from, ghost: true));
                Paths.Add(new StagePathViewModel(place.Path.Select(p => new Point(p.X * Tile + Tile / 2.0, p.Y * Tile + Tile / 2.0)).ToList(), actor.Label));
            }
        }
        OnPropertyChanged(nameof(HasPendingOutfits)); OnPropertyChanged(nameof(PendingOutfits));
    }

    /// <summary>Take the moment from the selection (a beat) and work out what has played by it. True when it changed.</summary>
    private bool CaptureMoment(Cutscene? scene)
    {
        var before = moment;
        if (shell.Selection is BeatSelection beat) moment = beat.Lane == LaneNames.Skip ? null : beat;
        if (scene == null || moment != null && (moment.Cutscene != scene.Id || LaneNames.Get(scene, moment.Lane) is not { } lane || moment.Index >= lane.Count)) moment = null;
        played = scene == null ? Array.Empty<PlayedBeat>() : StageTimeline.Played(scene, moment);
        MomentText = played.Count == 0 ? null
            : $"After beat {played[^1].Index + 1}{(played[^1].Lane == LaneNames.Main ? "" : " of " + LaneNames.Title(played[^1].Lane))} · {BeatCatalog.Kind(played[^1].Beat.Type).Name}: {BeatCatalog.Summary(played[^1].Beat, shell.Catalog)}";
        OnPropertyChanged(nameof(HasMoment));
        return before != moment;
    }

    /// <summary>Back to the start: everyone at their start tiles (what dragging edits there).</summary>
    [RelayCommand]
    private void ShowStart()
    {
        if (shell.Selection is BeatSelection && shell.Scene is { } scene) { moment = null; shell.Select(new SceneSelection(scene.Id!)); }
        moment = null;
        Rebuild();
    }

    /// <summary>
    /// Ask the game to draw each outfit on the stage that it hasn't drawn yet (the Bridge's RenderFarmer). Remembered:
    /// the stage shows them from then on, even with the game closed.
    /// </summary>
    [RelayCommand]
    private async Task RenderOutfits()
    {
        if (shell.Catalog is not { } catalog || PendingOutfits is not { Count: > 0 } pending) return;
        await shell.RefreshBridgeStatus();
        if (shell.Bridge.State != Preview.BridgeState.Ready)
        {
            shell.Ui.Toast("Start the game", "The game draws outfits exactly: start Stardew Valley with the SDCS Bridge and load a save, then try again. Until then the stage shows the items as icons.", ToastKind.Info);
            return;
        }
        shell.IsBusy = true;
        try
        {
            int done = 0;
            foreach (var look in pending)
            {
                shell.BusyText = pending.Count == 1 ? "The game is drawing the outfit…" : $"The game is drawing outfit {++done} of {pending.Count}…";
                var loadout = new JObject();
                foreach (var (key, value) in look.Outfit) loadout[key] = value;
                var reply = await shell.Preview.Client.SendAsync(new JObject { ["Op"] = "RenderFarmer", ["Loadout"] = loadout }, timeout: TimeSpan.FromMinutes(5));
                if ((string?)reply["PartsFolder"] is { } folder) renders.Remember(catalog, look.OutfitKey, folder);
            }
        }
        catch (Exception ex) when (ex is Preview.BridgeException or IOException or InvalidOperationException)
        { shell.Ui.Toast("Couldn't draw the outfit", ex.Message, ToastKind.Warning); }
        finally { shell.IsBusy = false; shell.BusyText = null; }
        Rebuild();
    }

    private void RebuildLocationOptions()
    {
        var names = shell.Catalog?.Locations.Select(l => new ChoiceItem(l.Name, l.Name, l.Origin.IsModProvided ? shell.Catalog.ModLabel(l.Origin.ProvidedBy[0]) : null)).ToList() ?? new();
        if (shell.Scene?.Location is { } current && !names.Any(n => string.Equals(n.Value, current, StringComparison.OrdinalIgnoreCase))) names.Insert(0, new ChoiceItem(current, current, "not in the export"));
        if (LocationOptions.Select(o => o.Value).SequenceEqual(names.Select(n => n.Value))) return;
        LocationOptions.Clear();
        foreach (var name in names) LocationOptions.Add(name);
    }

    private void UpdateCamera(Cutscene scene)
    {
        // A Camera beat that has played moves it; until then it's the scene's own camera.
        var moved = StageTimeline.Camera(played);
        cameraBy = moved?.By;
        CameraFollows = moved == null && scene.Camera?.Type == JTokenType.String;
        int[]? tile = moved?.Tile ?? (scene.Camera as JObject)?["Tile"]?.ToObject<int[]>() ?? scene.Actors.FirstOrDefault()?.Start?.Tile;
        tile ??= new[] { 10, 10 };
        double cx = tile[0] * Tile + Tile / 2.0, cy = tile[1] * Tile + Tile / 2.0;
        // Vanilla centres on the tile and clamps to the map; a map smaller than the screen is centred (doc 08 §2.8).
        double x = Width < ViewWidth ? (Width - ViewWidth) / 2 : Math.Clamp(cx - ViewWidth / 2, 0, Width - ViewWidth);
        double y = Height < ViewHeight ? (Height - ViewHeight) / 2 : Math.Clamp(cy - ViewHeight / 2, 0, Height - ViewHeight);
        CameraRect = new Rect(x, y, ViewWidth, ViewHeight);
    }

    private void AddMarkers(Cutscene scene)
    {
        if (shell.Selection is not BeatSelection selected || LaneNames.Get(scene, selected.Lane) is not { } list || selected.Index >= list.Count) return;
        switch (list[selected.Index])
        {
            case MoveBeat { To.Tile: { Length: 2 } to } move:
                Markers.Add(new StageMarkerViewModel(to[0] * Tile, to[1] * Tile, $"{move.Actor} walks here", StageMarkerKind.Destination));
                break;
            case CameraBeat { Center.Tile: { Length: 2 } center }:
                Markers.Add(new StageMarkerViewModel(center[0] * Tile, center[1] * Tile, "camera centre", StageMarkerKind.Camera));
                break;
            case EffectBeat { At.Tile: { Length: 2 } at } effect:
                Markers.Add(new StageMarkerViewModel(at[0] * Tile, at[1] * Tile, effect.Effect ?? "effect", StageMarkerKind.Effect));
                break;
        }
    }

    public void SyncSelection()
    {
        var scene = shell.Scene;
        // Where actors are and what they wear depend on the selected beat: redraw them when it changes.
        if (scene != null && CaptureMoment(scene)) { AddActors(scene); UpdateCamera(scene); }
        HashSet<string> involved = new(StringComparer.OrdinalIgnoreCase);
        if (scene != null && shell.Selection is BeatSelection b && LaneNames.Get(scene, b.Lane) is { } list && b.Index < list.Count)
            if (list[b.Index].GetType().GetProperty("Actor")?.GetValue(list[b.Index]) is string actor) involved.Add(actor);
        foreach (var actor in Actors)
        {
            actor.IsSelected = shell.Selection is ActorSelection a && a.Index == actor.Index;
            actor.IsInvolved = involved.Contains(actor.Id);
        }
        Markers.Clear();
        if (scene != null) AddMarkers(scene);
    }

    // ---------- interactions ----------

    public void SetLocation(string name)
    {
        var scene = shell.Scene;
        if (scene == null) return;
        shell.Edit("Change location to " + name, () => scene.Location = name);
        var origin = shell.Catalog?.Location(name)?.Origin;
        if (origin?.IsModProvided == true) shell.Ui.Toast("Mod location", $"Using {name} adds a dependency on {shell.Catalog!.ModLabel(origin.ProvidedBy[0])}.", ToastKind.Info);
    }

    /// <summary>Drop a character from the asset browser onto a tile: adds an actor (or moves the existing one).</summary>
    public void DropCharacter(string npc, int tileX, int tileY)
    {
        var scene = shell.Scene;
        if (scene == null) return;
        int existing = scene.Actors.FindIndex(a => string.Equals(a.Id, npc, StringComparison.Ordinal));
        if (existing >= 0) { MoveActor(existing, tileX, tileY, 0, 0); shell.Select(new ActorSelection(scene.Id!, existing)); return; }
        bool isFarmer = npc == "farmer";
        shell.Edit("Add " + npc, () => scene.Actors.Add(new Actor
        {
            Id = npc, Kind = isFarmer ? "Farmer" : "Npc", Npc = isFarmer ? null : npc, Start = new Position { Tile = new[] { tileX, tileY } }, Facing = "down"
        }));
        shell.Select(new ActorSelection(scene.Id!, scene.Actors.Count - 1));
        var origin = shell.Catalog?.Character(npc)?.Origin;
        if (origin?.IsModProvided == true) shell.Ui.Toast("Mod character", $"{npc} comes from {shell.Catalog!.ModLabel(origin.ProvidedBy[0])}, which becomes a dependency.", ToastKind.Info);
    }

    /// <summary>
    /// Put an actor on a tile. At a moment after a walk, that changes where the walk takes them (the beat that put them
    /// there); a clip's positions are edited in the Animation tab. Otherwise it's their start.
    /// </summary>
    public void MoveActor(int index, int tileX, int tileY, int pxX, int pxY)
    {
        var scene = shell.Scene;
        if (scene == null || index >= scene.Actors.Count) return;
        var actor = scene.Actors[index];
        switch (StageTimeline.Place(scene, actor, played, shell.Document?.Library).PlacedBy)
        {
            case { Beat: MoveBeat move } by:
                if (move.To?.Tile is { Length: 2 } to && to[0] == tileX && to[1] == tileY) return;
                shell.Edit($"{actor.Id} walks to {tileX}, {tileY} ({by.Title})", () => move.To = new Position { Tile = new[] { tileX, tileY } });
                return;
            case { Beat: ClipBeat clip } by:
                shell.Ui.Toast("A clip puts them here", $"{actor.Id} is where the clip '{clip.Clip}' ({by.Title}) leaves them. Change it in the Animation tab, or right-click a tile for “{actor.Id} walks here”.", ToastKind.Info);
                return;
        }
        if (actor.Start?.Tile is { Length: 2 } t && t[0] == tileX && t[1] == tileY && actor.Start.Px[0] == pxX && actor.Start.Px[1] == pxY) return;
        shell.Edit($"Move {actor.Id}", () => actor.Start = new Position { Tile = new[] { tileX, tileY }, Px = new[] { pxX, pxY } });
    }

    public static int BeatsUsing(Cutscene scene, string? actorId) => StoryboardViewModel.AllBeats(scene)
        .Count(b => string.Equals(b.GetType().GetProperty("Actor")?.GetValue(b) as string, actorId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Every scene keeps at least one farmer (the player is always in vanilla events).</summary>
    public static bool CanRemove(Cutscene scene, Actor actor) => actor.Kind != "Farmer" || scene.Actors.Count(a => a.Kind == "Farmer") > 1;

    public void RemoveActor(int index)
    {
        var scene = shell.Scene;
        if (scene == null || index < 0 || index >= scene.Actors.Count) return;
        var actor = scene.Actors[index];
        if (!CanRemove(scene, actor)) { shell.Ui.Toast("The player stays", "Every cutscene needs the player. Hide them with a Visible beat instead.", ToastKind.Info); return; }
        int uses = BeatsUsing(scene, actor.Id);
        if (uses > 0 && !shell.Ui.Confirm("Remove actor?", $"{uses} beat(s) use {actor.Id}. They'll show a problem until you change them. Remove anyway?", "Remove")) return;
        shell.Edit("Remove " + actor.Id, () => scene.Actors.Remove(actor));
        shell.Select(new SceneSelection(scene.Id!));
    }

    /// <summary>Rename an actor and every beat that names it.</summary>
    public static void RenameActor(Cutscene scene, Actor actor, string? name)
    {
        string? old = actor.Id;
        if (string.IsNullOrWhiteSpace(name) || name == old) return;
        actor.Id = name.Trim();
        foreach (var beat in StoryboardViewModel.AllBeats(scene))
            if (beat.GetType().GetProperty("Actor") is { } property && string.Equals(property.GetValue(beat) as string, old, StringComparison.OrdinalIgnoreCase)) property.SetValue(beat, actor.Id);
    }

    /// <summary>
    /// Turn an actor a quarter. At their start, or where a Face beat turned them, that beat (or the start) changes; after
    /// a walk or a clip turned them, a new Face beat follows the moment's beat.
    /// </summary>
    public void TurnActor(int index)
    {
        var scene = shell.Scene;
        if (scene == null || index >= scene.Actors.Count) return;
        var actor = scene.Actors[index];
        var place = StageTimeline.Place(scene, actor, played, shell.Document?.Library);
        string next = place.Facing switch { "down" => "left", "left" => "up", "up" => "right", _ => "down" };
        switch (place.TurnedBy)
        {
            case null: shell.Edit($"Turn {actor.Id} to face {next}", () => actor.Facing = next); break;
            case { Beat: FaceBeat face } by: shell.Edit($"Turn {actor.Id} to face {next} ({by.Title})", () => face.Facing = next); break;
            default: InsertAfterMoment("Face", beat => { var face = (FaceBeat)beat; face.Actor = actor.Id; face.Facing = next; }); break;
        }
    }

    /// <summary>Right-click › "… walks here": a new Move beat after the moment's beat (the end of the scene from the start).</summary>
    public void WalkHere(int index, int tileX, int tileY)
    {
        if (shell.Scene is not { } scene || index >= scene.Actors.Count) return;
        string? id = scene.Actors[index].Id;
        InsertAfterMoment("Move", beat => { var move = (MoveBeat)beat; move.Actor = id; move.To = new Position { Tile = new[] { tileX, tileY } }; });
    }

    /// <summary>Insert a beat right after the one the stage shows (before it when that one ends the lane: an End or a Go-to), and select it.</summary>
    private void InsertAfterMoment(string type, Action<Beat> configure)
    {
        if (shell.Scene is not { } scene) return;
        string lane = moment?.Lane ?? LaneNames.Main;
        var beats = LaneNames.Get(scene, lane) ?? scene.Beats;
        int at = moment == null ? beats.Count : beats[moment.Index] is EndBeat or GotoBeat ? moment.Index : moment.Index + 1;
        if (moment == null && beats.Count > 0 && beats[^1] is EndBeat or GotoBeat) at = beats.Count - 1;
        shell.Storyboard.Insert(lane, at, BeatCatalog.Kind(type), configure);
    }

    /// <summary>
    /// Drop an item from the browser on a tile: a Place beat after the moment's beat, its picture's bottom-left corner on
    /// that tile (D-50). The owner dropped a Moon Table and got "give the player a Moon Table", with nothing on the map.
    /// </summary>
    public void PlaceItem(string item, int tileX, int tileY) =>
        InsertAfterMoment("Prop", beat => { var prop = (PropBeat)beat; prop.Item = item; prop.At = new Position { Tile = new[] { tileX, tileY } }; });

    /// <summary>Drag a placed item: its Place beat's tile changes.</summary>
    public void MoveProp(StagePropViewModel prop, int tileX, int tileY)
    {
        if (prop.Prop.At?.Tile is { Length: 2 } at && at[0] == tileX && at[1] == tileY) return;
        shell.Edit($"Move {prop.Label} to {tileX}, {tileY} ({prop.By.Title})", () => prop.Prop.At = new Position { Tile = new[] { tileX, tileY } });
    }

    /// <summary>Right-click › take it away: a Place beat with Remove after the moment's beat.</summary>
    public void TakeAway(StagePropViewModel prop) =>
        InsertAfterMoment("Prop", beat => { var remove = (PropBeat)beat; remove.Item = prop.Prop.Item; remove.Remove = true; remove.At = new Position { Tile = new[] { prop.TileX, prop.TileY } }; });

    public void SelectProp(StagePropViewModel prop) { if (shell.Scene is { } scene) shell.Select(new BeatSelection(scene.Id!, prop.By.Lane, prop.By.Index)); }

    /// <summary>The picture an item will have on the stage (for the drop preview), in sprite pixels.</summary>
    public (int Width, int Height) ItemSize(string item) => shell.Catalog?.Item(item) is { Size: { Length: 2 } size } ? (size[0], size[1]) : (Tile, Tile);

    /// <summary>Describes where an actor is at the moment, and what dragging them changes.</summary>
    public string? PlaceNote(StagePlace place, string who) => place.PlacedBy switch
    {
        { Beat: MoveBeat } by => $"{who} walked here in {by.Title}. Dragging changes where that walk ends.",
        { Beat: ClipBeat } by => $"The clip in {by.Title} left {who} here.",
        _ when moment != null => $"{who} hasn't moved yet. Dragging changes where they start.",
        _ => null,
    };

    public void SelectActor(int index) { if (shell.Scene is { } scene) shell.Select(new ActorSelection(scene.Id!, index)); }

    /// <summary>Move the camera frame: the Camera beat that put it there, or else the scene's camera.</summary>
    public void MoveCamera(int tileX, int tileY)
    {
        var scene = shell.Scene;
        if (scene == null) return;
        if (cameraBy is { Beat: CameraBeat camera } by) { shell.Edit($"Move the camera ({by.Title})", () => camera.Center = new Position { Tile = new[] { tileX, tileY } }); return; }
        shell.Edit("Move camera", () => scene.Camera = new JObject { ["Tile"] = new JArray(tileX, tileY) });
    }

    /// <summary>Ask the designer to click a tile (for a Move destination, camera target…).</summary>
    public void BeginPick(string prompt, Action<int, int> picked) { pendingPick = picked; PickPrompt = prompt + " — click a tile (Esc to cancel)"; }
    [RelayCommand] public void CancelPick() { pendingPick = null; PickPrompt = null; }
    /// <summary>A click on an empty tile. Returns true if a pick consumed it.</summary>
    public bool TileClicked(int tileX, int tileY)
    {
        if (pendingPick == null) return false;
        var pick = pendingPick; CancelPick(); pick(tileX, tileY);
        return true;
    }

    [RelayCommand] private void ZoomIn() => Zoom = Math.Min(8, Math.Floor(Zoom) + 1);
    [RelayCommand] private void ZoomOut() => Zoom = Math.Max(1, Math.Ceiling(Zoom) - 1);
    [RelayCommand] private void MakeVariant() { if (MapWarningMod != null) shell.MakeInstallVariant(MapWarningMod); }

    /// <summary>
    /// The banner for a map a mod redraws (doc 15 §3): a warning with "Make a variant" until the scene is split by
    /// install; then it says which half this is and opens the other one. It can be closed for this scene.
    /// </summary>
    private void MapNotice(Cutscene scene, string? edited, ExportCatalog catalog)
    {
        MapWarning = null; MapActionText = null; mapPartner = null; IsMapInfo = false;
        if (edited == null || dismissedNotices.Contains(scene.Id + "|" + edited)) return;
        string mod = catalog.ModLabel(edited);
        if (InstallVariants.Of(scene) is { } split && string.Equals(split.Mod, edited, StringComparison.OrdinalIgnoreCase))
        {
            var partner = shell.Document is { } doc ? InstallVariants.Partner(doc, scene) : null;
            mapPartner = partner?.Scene.Id;
            IsMapInfo = true;
            if (split.Without)
            {
                MapWarning = $"This variant plays when {mod} isn't installed. The stage still shows {Location!.Name} as your game has it (with {mod}): place the actors for the vanilla map.";
                MapActionText = partner != null ? "Open the original" : null;
            }
            else
            {
                MapWarning = partner != null ? $"This scene plays only with {mod} installed. Its variant, '{partner.Scene.Title ?? partner.Scene.Id}', plays without it."
                    : $"This scene plays only with {mod} installed.";
                MapActionText = partner != null ? "Open the variant" : "Make a variant for installs without it";
            }
            return;
        }
        MapWarning = $"{Location!.Name} is staged on a map edited by {mod}. Without that mod the tiles may differ.";
        MapActionText = "Make a variant for installs without it";
    }

    [RelayCommand]
    private void MapAction()
    {
        if (mapPartner != null) shell.OpenCutscene(mapPartner);
        else MakeVariant();
    }

    [RelayCommand]
    private void DismissMapNotice()
    {
        if (shell.Scene?.Id is { } id && MapWarningMod != null) dismissedNotices.Add(id + "|" + MapWarningMod);
        MapWarning = null; MapActionText = null;
    }
    [RelayCommand] private void AddPlayer() { if (shell.Scene is { } s && !s.Actors.Any(a => a.Id == "farmer")) DropCharacter("farmer", (int)(CameraRect.X + ViewWidth / 2) / Tile, (int)(CameraRect.Y + ViewHeight / 2) / Tile); }
    public MainViewModel Shell => shell;
}

public enum StageMarkerKind { Destination, Camera, Effect }
public sealed record StageMarkerViewModel(double X, double Y, string Label, StageMarkerKind Kind);

/// <summary>An item on the map at the stage's moment: its picture standing on its tile (bottom-left corner), as the game draws it.</summary>
public sealed partial class StagePropViewModel : ObservableObject
{
    private readonly StageViewModel stage;
    public StagePropViewModel(StageViewModel stage, PlayedBeat by, PropBeat prop, ExportCatalog? catalog, bool selected)
    {
        this.stage = stage; By = by; Prop = prop; IsSelected = selected;
        TileX = prop.At?.Tile?.ElementAtOrDefault(0) ?? 0; TileY = prop.At?.Tile?.ElementAtOrDefault(1) ?? 0;
        var item = prop.Item == null ? null : catalog?.Item(prop.Item);
        Label = item?.Label ?? prop.Item ?? "item";
        if (item?.Icon != null && catalog != null) Image = Images.Load(catalog.FullPath(item.Icon));
        if (item?.Size is { Length: 2 } size) { Width = size[0]; Height = size[1]; }
        HasImage = Image != null;
    }
    public PlayedBeat By { get; }
    public PropBeat Prop { get; }
    public string Label { get; }
    public ImageSource? Image { get; }
    public bool HasImage { get; }
    public int TileX { get; }
    public int TileY { get; }
    public double Width { get; } = StageViewModel.Tile;
    public double Height { get; } = StageViewModel.Tile;
    public double TileLeft => TileX * StageViewModel.Tile;
    public double TileTop => TileY * StageViewModel.Tile;
    /// <summary>The picture rises from the bottom of its tile.</summary>
    public double ImageTop => StageViewModel.Tile - Height;
    public string Note => $"{Label}, placed in {By.Title}. Drag to move it, click to select its beat, right-click for more.";
    /// <summary>Placed by the beat the stage shows.</summary>
    public bool IsSelected { get; }
    [RelayCommand] private void Select() => stage.SelectProp(this);
}

/// <summary>A walk on the stage: its corners in sprite pixels (tile centres).</summary>
public sealed class StagePathViewModel
{
    public StagePathViewModel(IReadOnlyList<Point> corners, string who)
    {
        Points = new PointCollection(corners); Points.Freeze();
        Who = who;
        var end = corners.Count > 0 ? corners[^1] : default;
        EndX = end.X; EndY = end.Y;
    }
    public PointCollection Points { get; }
    public string Who { get; }
    public double EndX { get; }
    public double EndY { get; }
}

/// <summary>An actor drawn where they are at the stage's moment (their start, or where the beats so far put them) with the standing frame for their facing.</summary>
public sealed partial class StageActorViewModel : ObservableObject
{
    private readonly StageViewModel stage;
    public StageActorViewModel(StageViewModel stage, int index, Actor actor, ExportCatalog? catalog, StageLook? look = null, CostumeRenders? renders = null, StagePlace? place = null, bool ghost = false)
    {
        this.stage = stage; Index = index; Id = actor.Id ?? "?"; Kind = actor.Kind ?? "Npc";
        Look = look ?? StageLook.None;
        IsGhost = ghost;
        Facing = place?.Facing ?? actor.Facing ?? "down";
        TileX = place?.TileX ?? actor.Start?.Tile?[0] ?? 0; TileY = place?.TileY ?? actor.Start?.Tile?[1] ?? 0;
        int pxX = place?.PxX ?? actor.Start?.Px?.ElementAtOrDefault(0) ?? 0, pxY = place?.PxY ?? actor.Start?.Px?.ElementAtOrDefault(1) ?? 0;
        PxX = pxX; PxY = pxY;
        IsHidden = !actor.Visible;
        int frameWidth = 16, frameHeight = 32;
        if (Kind == "Farmer" && catalog?.Player is { } player && (string?)player["PartsFolder"] is { } playerParts)
        {
            // An outfit from the scene: the game's drawing of it when there is one, else the player as they are with the items as badges.
            string parts = playerParts;
            if (Look.Outfit.Count > 0)
            {
                Outfit = string.Join(", ", Look.Outfit.Select(p => OutfitPart(catalog, p.Key, p.Value)));
                if (renders?.Find(catalog, Look.OutfitKey) is { } rendered) parts = rendered;
                else
                {
                    NeedsRender = true;
                    Badges = Look.Outfit.Values.Select(v => catalog.Item(v)?.Icon).Where(i => i != null).Select(i => Images.Load(catalog.FullPath(i))).Where(i => i != null).Cast<ImageSource>().ToList();
                }
            }
            // Native farmer standing frames: down 0, right/left 6 (left is mirrored), up 12; canvas 32×48 with the frame at (8,16).
            int frame = Facing switch { "up" => 12, "right" or "left" => 6, _ => 0 };
            string file = Path.Combine(catalog.FullPath(parts), $"full_{(Facing == "left" ? "right" : Facing)}_{frame:000}.png");
            if (!File.Exists(file)) file = Path.Combine(catalog.FullPath(parts), $"full_down_{0:000}.png");
            Sprite = Images.Load(file);
            Flip = Facing == "left";
            DrawX = TileX * StageViewModel.Tile - 8 + pxX; DrawY = TileY * StageViewModel.Tile - 16 - 16 + pxY;
            DrawWidth = 32; DrawHeight = 48;
        }
        else
        {
            string? asset = Look.SpriteAsset ?? actor.Sprite?.Asset;
            string? starting = actor.Sprite?.Pose is { } startOutfit ? StudioContext.OutfitKey(startOutfit) : actor.Sprite?.Asset;
            if (Look.SpriteAsset != null && Look.SpriteAsset != starting)
                Outfit = "Wearing " + (StudioContext.OutfitId(Look.SpriteAsset) is { } yours ? OutfitFactory.Label(yours, actor.Npc ?? Id) : Look.SpriteAsset.Split('/').Last());
            if (Kind == "Npc")
            {
                var character = catalog?.Character(actor.Npc ?? Id);
                asset ??= character?.Current?.Sprite ?? character?.Sprite;
                frameWidth = character?.FrameWidth ?? 16; frameHeight = character?.FrameHeight ?? 32;
                Label = character?.DisplayName ?? Id;
            }
            else if (actor.FrameSize is { Length: 2 } size) { frameWidth = size[0]; frameHeight = size[1]; }
            int facingRow = Facing switch { "right" => 1, "up" => 2, "left" => 3, _ => 0 };
            // One of the pack's outfits is drawn by the studio (edits included); a game sheet comes from the export.
            Sprite = StudioContext.OutfitId(asset) is { } own
                ? stage.Shell.CharacterStudio.Context.OutfitSheet(own) is { } sheet && StudioImages.Frame(sheet, frameWidth, frameHeight, facingRow * 4) is { } frame ? StudioImages.ToBitmap(frame) : null
                : Images.Frame(catalog?.TextureFile(asset), frameWidth, frameHeight, facingRow * 4);
            // NPC.draw: the frame's bottom sits on the tile's bottom edge, centred on the tile (doc 03 §1.3).
            DrawX = TileX * StageViewModel.Tile + StageViewModel.Tile / 2.0 - frameWidth / 2.0 + pxX;
            DrawY = TileY * StageViewModel.Tile + StageViewModel.Tile - frameHeight + pxY;
            DrawWidth = frameWidth; DrawHeight = frameHeight;
        }
        Label ??= Kind == "Farmer" ? "Player" : Id;
        HasSprite = Sprite != null;
        PlaceNote = place == null || ghost ? null : stage.PlaceNote(place, Label);
    }

    /// <summary>A faint copy where the selected beat's walk starts (not interactive).</summary>
    public bool IsGhost { get; }
    /// <summary>Where they are at the stage's moment and what a drag changes (null at the start).</summary>
    public string? PlaceNote { get; }
    public bool HasPlaceNote => PlaceNote != null;

    public int Index { get; }
    public StageLook Look { get; }
    /// <summary>What they wear at the selected beat, in words (null: their own look).</summary>
    public string? Outfit { get; }
    public bool HasOutfit => Outfit != null;
    /// <summary>An outfit the game hasn't drawn yet: the stage shows the player as they are, with the items as badges.</summary>
    public bool NeedsRender { get; }
    public IReadOnlyList<ImageSource> Badges { get; } = Array.Empty<ImageSource>();
    public bool HasBadges => Badges.Count > 0;

    private static string OutfitPart(ExportCatalog catalog, string key, string value) => value switch
    {
        "none" => $"no {key.ToLowerInvariant()}",
        "original" => $"their own {key.ToLowerInvariant()}",
        _ => catalog.Item(value)?.DisplayName ?? $"{key} {value}",
    };
    public string Id { get; }
    public string Kind { get; }
    public string Label { get; }
    public string Facing { get; }
    public int TileX { get; }
    public int TileY { get; }
    public int PxX { get; }
    public int PxY { get; }
    public double DrawX { get; }
    public double DrawY { get; }
    public double DrawWidth { get; }
    public double DrawHeight { get; }
    public ImageSource? Sprite { get; }
    public bool HasSprite { get; }
    public bool Flip { get; }
    public bool IsHidden { get; }
    public double TileLeft => TileX * StageViewModel.Tile;
    public double TileTop => TileY * StageViewModel.Tile;
    /// <summary>The sprite's position relative to its tile (the view positions each actor at its tile).</summary>
    public double SpriteLeft => DrawX - TileLeft;
    public double SpriteTop => DrawY - TileTop;
    public string Initial => Label.Length > 0 ? Label[..1].ToUpperInvariant() : "?";
    public string FacingArrow => Facing switch { "up" => "▲", "right" => "▶", "left" => "◀", _ => "▼" };
    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private bool isInvolved;
    [RelayCommand] private void Turn() => stage.TurnActor(Index);
    [RelayCommand] private void Select() => stage.SelectActor(Index);
}
