using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Catalog;
using SDCharacterStudio.Editor.Export;
using SDCharacterStudio.Editor.Infrastructure;
using SDCharacterStudio.Editor.Preview;
using SDCharacterStudio.Editor.Project;

namespace SDCharacterStudio.Editor.ViewModels;

public sealed record CutsceneItem(string Id, string Title, string Location)
{
    public string Display => string.IsNullOrWhiteSpace(Title) ? Id : $"{Title}";
}

/// <summary>The editor shell: open project, game export, selection, undo/redo, validation, preview and export (doc 07).</summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private bool inspectorEdit;
    private bool rebuildInspectorAfterEdit;
    private int validationVersion;

    public EditorSettings Settings { get; }
    public IUserInterface Ui { get; }
    public AssetBrowserViewModel Assets { get; }
    public StageViewModel Stage { get; }
    public StoryboardViewModel Storyboard { get; }
    public InspectorViewModel Inspector { get; }
    public ProblemsViewModel Problems { get; }
    public OutputViewModel Output { get; }
    /// <summary>The Story panel: story flags and numbers across the project (D-51).</summary>
    public StoryViewModel Story { get; }
    /// <summary>"Try it as…": how this scene's Checks, Chances and random items land in previews (D-51).</summary>
    public TryItAsViewModel TryItAs { get; }
    public PreviewService Preview { get; }
    public ObservableCollection<CutsceneItem> Cutscenes { get; } = new();
    public ObservableCollection<string> RecentProjects { get; } = new();

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsProjectOpen), nameof(Title), nameof(PackName))] private PackDocument? document;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CatalogStatus), nameof(HasCatalog))] private ExportCatalog? catalog;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CatalogStatus))] private string? catalogError;
    [ObservableProperty] private CutsceneItem? currentCutsceneItem;
    [ObservableProperty] private Selection? selection;
    [ObservableProperty] private BridgeStatus bridge = new(BridgeState.NotRunning, "Game not running");
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? busyText;
    [ObservableProperty] private ValidationResult? lastValidation;

    public MainViewModel(IUserInterface ui, EditorSettings settings)
    {
        Ui = ui; Settings = settings;
        Output = new OutputViewModel();
        Problems = new ProblemsViewModel(this);
        Story = new StoryViewModel(this);
        TryItAs = new TryItAsViewModel(this);
        Assets = new AssetBrowserViewModel(this);
        Stage = new StageViewModel(this);
        Storyboard = new StoryboardViewModel(this);
        Inspector = new InspectorViewModel(this);
        Preview = new PreviewService(new BridgeClient(settings.PipeName), settings.PreviewFolder);
        Preview.Log += (level, message) => Ui.Post(() => Output.Add(level, message));
        foreach (var recent in settings.RecentProjects.Where(File.Exists)) RecentProjects.Add(recent);
    }

    public bool IsProjectOpen => Document != null;
    public bool HasCatalog => Catalog != null;
    public string PackName => Document?.DisplayName ?? "";
    public CutsceneDocument? Current => Document?.FindCutscene(CurrentCutsceneItem?.Id);
    public Cutscene? Scene => Current?.Scene;
    public string Title => Document == null ? "SDCharacterStudio" : $"{Document.DisplayName}{(Document.IsDirty ? " •" : "")} — SDCharacterStudio";
    public string CatalogStatus => Catalog != null
        ? $"Game export: {Catalog.Profile} · {Catalog.Characters.Count} characters · {Catalog.Locations.Count} locations · {Catalog.Mods.Count} mods · {Age(Catalog.ExportedAt)}"
        : CatalogError ?? "No game export yet";
    public bool CanUndo => Document?.History.CanUndo == true;
    public bool CanRedo => Document?.History.CanRedo == true;
    public string UndoText => Document?.History.UndoLabel is { } l ? "Undo " + l : "Undo";
    public string RedoText => Document?.History.RedoLabel is { } l ? "Redo " + l : "Redo";

    // ---------- game export ----------

    [RelayCommand]
    public void LoadCatalog()
    {
        try
        {
            string? profile = Settings.ExportProfile is { } chosen && File.Exists(Path.Combine(chosen, "catalog.json")) ? chosen
                : ExportCatalog.FindProfiles(Settings.ExportFolder).FirstOrDefault();
            seenCatalog = profile == null ? null : (Path.GetFullPath(profile), CatalogStamp(profile));
            if (profile == null)
            {
                Catalog = null;
                CatalogError = "No game export yet. Start Stardew Valley with the SDCS Bridge installed and load a save; it exports the game's characters, maps and items for the editor.";
            }
            else
            {
                Images.Clear();
                Catalog = ExportCatalog.Load(profile);
                CatalogError = null;
                Output.Add("Info", $"Loaded game export '{Catalog.Profile}' from {Catalog.Root}.");
            }
        }
        catch (Exception ex) when (ex is IOException or Newtonsoft.Json.JsonException or UnauthorizedAccessException)
        {
            Catalog = null;
            CatalogError = "Couldn't read the game export: " + ex.Message;
        }
        ConfigureAudio();
        Assets.Rebuild();
        Stage.Rebuild();
        Inspector.Rebuild();
        ScheduleValidation();
    }

    // ---------- projects ----------

    [RelayCommand]
    private void NewProject()
    {
        if (!ConfirmDiscard()) return;
        var dialog = new NewProjectViewModel(this);
        if (!Ui.ShowDialog(dialog) || dialog.Created == null) return;
        Attach(dialog.Created);
        Ui.Toast("Project created", $"Your pack is in {dialog.Created.PackFolder}.", ToastKind.Success);
    }

    [RelayCommand]
    private void OpenProject()
    {
        if (!ConfirmDiscard()) return;
        string? path = Ui.OpenFile("Open a project", "SDCS project (*.sdcs)|*.sdcs|Pack manifest (studio.json)|studio.json", Settings.ProjectsFolder);
        if (path == null) return;
        if (Path.GetFileName(path).Equals("studio.json", StringComparison.OrdinalIgnoreCase)) OpenPack(Path.GetDirectoryName(path)!);
        else OpenProjectFile(path);
    }

    [RelayCommand]
    private void OpenPackFolder()
    {
        if (!ConfirmDiscard()) return;
        string? folder = Ui.OpenFolder("Open a pack folder (the one with studio.json)", Settings.ProjectsFolder);
        if (folder != null) OpenPack(folder);
    }

    [RelayCommand]
    public void OpenRecent(string? path) { if (path != null && ConfirmDiscard()) OpenProjectFile(path); }

    public void OpenProjectFile(string path)
    {
        try { Attach(PackDocument.Open(path)); }
        catch (Exception ex) when (ex is IOException or Newtonsoft.Json.JsonException or UnauthorizedAccessException or ArgumentException)
        { Ui.Toast("Couldn't open the project", ex.Message, ToastKind.Error); RecentProjects.Remove(path); }
    }

    public void OpenPack(string folder)
    {
        try { Attach(PackDocument.OpenPackFolder(folder, Settings.OpenedPacksProjectsFolder)); }
        catch (Exception ex) when (ex is IOException or Newtonsoft.Json.JsonException or UnauthorizedAccessException or ArgumentException)
        { Ui.Toast("Couldn't open the pack", ex.Message, ToastKind.Error); }
    }

    /// <summary>Make a document the open project (after open, create or tests).</summary>
    public void Attach(PackDocument doc)
    {
        if (Document != null) Document.Changed -= OnDocumentChanged;
        Document = doc;
        doc.Changed += OnDocumentChanged;
        doc.History.Changed += (_, _) => { OnPropertyChanged(nameof(CanUndo)); OnPropertyChanged(nameof(CanRedo)); OnPropertyChanged(nameof(UndoText)); OnPropertyChanged(nameof(RedoText)); };
        Settings.AddRecent(doc.ProjectPath);
        RecentProjects.Clear(); foreach (var recent in Settings.RecentProjects.Where(File.Exists)) RecentProjects.Add(recent);
        if (Autosave.FindRecovery(doc.ProjectPath) is { } recovery
            && Ui.Confirm("Recover unsaved work?", $"SDCharacterStudio found work from {recovery.SavedAt:g} that wasn't saved (the editor may have closed unexpectedly). Restore it?", "Restore", "Discard"))
            doc.RestoreSnapshot(recovery.State, "Recover autosave");
        else Autosave.Delete(doc.ProjectPath);
        foreach (var issue in doc.LoadIssues) Output.Add("Error", $"{issue.File}: {issue.Message}");
        RefreshCutsceneList(doc.Project.OpenCutscene);
        Stage.Zoom = doc.Project.StageZoom;
        Selection = Current != null ? new SceneSelection(Current.Scene.Id!) : null;
        RefreshAll(rebuildInspector: true);
        OnPropertyChanged(nameof(Title));
    }

    [RelayCommand]
    private void CloseProject()
    {
        if (!ConfirmDiscard()) return;
        if (Document != null) Document.Changed -= OnDocumentChanged;
        Document = null; Selection = null; Cutscenes.Clear(); CurrentCutsceneItem = null;
        RefreshAll(rebuildInspector: true);
        Problems.Update(Array.Empty<Problem>());
    }

    /// <summary>True when it's fine to replace the open project (saved, discarded, or nothing open).</summary>
    public bool ConfirmDiscard()
    {
        Ui.FlushPendingEdits();
        if (Document is not { IsDirty: true }) return true;
        bool? save = Ui.Ask("Save changes?", $"Save the changes to {Document.DisplayName} first?", "Save", "Don't save");
        if (save == null) return false;
        if (save == true) Save();
        else Autosave.Delete(Document.ProjectPath);
        return true;
    }

    [RelayCommand]
    public void Save()
    {
        if (Document == null) return;
        Ui.FlushPendingEdits();
        try
        {
            Document.Save();
            OnPropertyChanged(nameof(Title));
            Ui.Toast("Saved", Document.PackFolder, ToastKind.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Ui.Toast("Couldn't save", ex.Message, ToastKind.Error); }
    }

    public void AutosaveTick()
    {
        if (Document is { IsDirty: true }) try { Autosave.Write(Document); } catch (IOException) { }
    }

    // ---------- editing ----------

    /// <summary>Apply a user action as one undo step and refresh the views.</summary>
    public void Edit(string label, Action apply)
    {
        if (Document == null) return;
        Document.Edit(label, apply);
    }

    /// <summary>Commit from an inspector field: refresh everything except the inspector (keeps focus), unless asked.</summary>
    public void EditFromInspector(string label, Action apply, bool rebuild)
    {
        if (Document == null) return;
        inspectorEdit = !rebuild;
        rebuildInspectorAfterEdit = rebuild;
        try { Document.Edit(label, apply); }
        finally { inspectorEdit = false; rebuildInspectorAfterEdit = false; }
    }

    private void OnDocumentChanged(object? sender, string label)
    {
        bool idChanged = Document != null && (Cutscenes.Count != Document.Cutscenes.Count || Cutscenes.Zip(Document.Cutscenes).Any(p => p.First.Id != p.Second.Scene.Id || p.First.Title != (p.Second.Scene.Title ?? "")));
        if (idChanged) RefreshCutsceneList(CurrentCutsceneItem?.Id);
        RefreshAll(rebuildInspector: !inspectorEdit || rebuildInspectorAfterEdit);
        OnPropertyChanged(nameof(Title));
    }

    /// <summary>Redraw storyboard, stage, inspector and the asset browser's pack categories from the models.</summary>
    public void RefreshAll(bool rebuildInspector)
    {
        Selection = Resolve(Selection);
        Storyboard.Rebuild();
        Stage.Rebuild();
        Story.Refresh();
        Assets.RefreshPackCategories();
        if (rebuildInspector) Inspector.Rebuild();
        else Inspector.RefreshValues();
        ScheduleValidation();
    }

    private void RefreshCutsceneList(string? keep)
    {
        Cutscenes.Clear();
        if (Document == null) return;
        foreach (var c in Document.Cutscenes) Cutscenes.Add(new CutsceneItem(c.Scene.Id ?? c.File, c.Scene.Title ?? "", c.Scene.Location ?? ""));
        CurrentCutsceneItem = Cutscenes.FirstOrDefault(c => string.Equals(c.Id, keep, StringComparison.OrdinalIgnoreCase)) ?? Cutscenes.FirstOrDefault();
    }

    partial void OnCurrentCutsceneItemChanged(CutsceneItem? value)
    {
        if (Document == null || value == null) return;
        Document.Project.OpenCutscene = value.Id;
        if (Selection?.Cutscene != value.Id) Selection = new SceneSelection(value.Id);
        RefreshAll(rebuildInspector: true);
    }

    partial void OnSelectionChanged(Selection? value)
    {
        Inspector.Rebuild();
        Storyboard.SyncSelection();
        Stage.SyncSelection();
    }

    public void Select(Selection? next)
    {
        Ui.FlushPendingEdits();
        if (next != null && next.Cutscene != CurrentCutsceneItem?.Id)
            CurrentCutsceneItem = Cutscenes.FirstOrDefault(c => c.Id == next.Cutscene) ?? CurrentCutsceneItem;
        Selection = next;
    }

    /// <summary>Keep a selection valid after edits (indices can shift or vanish).</summary>
    private Selection? Resolve(Selection? selection)
    {
        var scene = Scene;
        if (scene == null) return null;
        if (selection == null || !string.Equals(selection.Cutscene, scene.Id, StringComparison.OrdinalIgnoreCase)) return new SceneSelection(scene.Id!);
        return selection switch
        {
            BeatSelection b when LaneNames.Get(scene, b.Lane) is { } list => list.Count == 0 ? new SceneSelection(scene.Id!) : b with { Index = Math.Clamp(b.Index, 0, list.Count - 1) },
            BeatSelection => new SceneSelection(scene.Id!),
            ActorSelection a when a.Index < scene.Actors.Count => a,
            ActorSelection => new SceneSelection(scene.Id!),
            BranchSelection br when scene.Branches.ContainsKey(br.Lane) || br.Lane is LaneNames.Main or LaneNames.Skip => br,
            BranchSelection => new SceneSelection(scene.Id!),
            _ => selection
        };
    }

    [RelayCommand]
    private void Undo() { Ui.FlushPendingEdits(); if (Document?.Undo() == true) Ui.Toast("Undone", Document.History.RedoLabel ?? "", ToastKind.Info); }
    [RelayCommand]
    private void Redo() { Ui.FlushPendingEdits(); if (Document?.Redo() == true) Ui.Toast("Redone", Document.History.UndoLabel ?? "", ToastKind.Info); }

    // ---------- cutscenes ----------

    [RelayCommand]
    private void AddCutscene()
    {
        if (Document == null) return;
        string? title = Ui.Prompt("New cutscene", "What is this scene? (e.g. \"Robin asks for help\")", "New scene");
        if (string.IsNullOrWhiteSpace(title)) return;
        string id = UniqueCutsceneId(PackDocument.ToContentId(title));
        string location = Scene?.Location ?? "Town";
        Edit("Add cutscene", () =>
        {
            var scene = PackDocument.NewCutscene(id, location, playsByItself: true);
            scene.Title = title;
            Document.Cutscenes.Add(new CutsceneDocument("cutscenes/" + id + ".json", scene));
        });
        RefreshCutsceneList(id);
    }

    [RelayCommand]
    private void DuplicateCutscene()
    {
        if (Document == null || Current is not { } current) return;
        string id = UniqueCutsceneId(current.Scene.Id + "-copy");
        Edit("Duplicate cutscene", () =>
        {
            var copy = PackWriter.Clone(current.Scene);
            copy.Id = id; copy.Title = (current.Scene.Title ?? current.Scene.Id) + " (copy)";
            Document.Cutscenes.Add(new CutsceneDocument("cutscenes/" + id + ".json", copy));
        });
        RefreshCutsceneList(id);
    }

    [RelayCommand]
    private void DeleteCutscene()
    {
        if (Document == null || Current is not { } current) return;
        if (Document.Cutscenes.Count == 1) { Ui.Toast("Can't delete", "A pack needs at least one cutscene.", ToastKind.Warning); return; }
        if (!Ui.Confirm("Delete cutscene?", $"Delete '{current.Scene.Title ?? current.Scene.Id}'? Its file is removed when you save. (Undo brings it back.)", "Delete")) return;
        Edit("Delete cutscene", () => Document.Cutscenes.Remove(current));
        RefreshCutsceneList(null);
    }

    public string UniqueCutsceneId(string id)
    {
        string candidate = id; int n = 2;
        while (Document?.FindCutscene(candidate) != null) candidate = $"{id}-{n++}";
        return candidate;
    }

    /// <summary>
    /// For a scene staged on a map another mod redraws (SVE), make a copy gated on that mod being absent so the
    /// original can be gated on it being present (doc 15 §3.1, D-32).
    /// </summary>
    public void MakeInstallVariant(string modId)
    {
        if (Document == null || Current is not { } current) return;
        // Already split on this mod (the original or its variant): go to the other half instead of making another.
        if (InstallVariants.Of(current.Scene) is { } split && string.Equals(split.Mod, modId, StringComparison.OrdinalIgnoreCase))
        {
            if (InstallVariants.Partner(Document, current.Scene) is { } partner) OpenCutscene(partner.Scene.Id);
            return;
        }
        string query = InstallVariants.Query(modId);
        string id = UniqueCutsceneId(current.Scene.Id + "-without-" + PackDocument.ToContentId(Catalog?.ModLabel(modId) ?? modId));
        Edit("Make a variant for installs without " + modId, () =>
        {
            var original = current.Scene;
            original.Trigger ??= new Trigger();
            original.Trigger.Conditions = Combine(original.Trigger.Conditions, query);
            var copy = PackWriter.Clone(original);
            copy.Id = id; copy.Title = (original.Title ?? original.Id) + $" (without {Catalog?.ModLabel(modId) ?? modId})";
            copy.Trigger!.Conditions = original.Trigger.Conditions!.Replace(query, "!" + query, StringComparison.Ordinal);
            Document.Cutscenes.Add(new CutsceneDocument("cutscenes/" + id + ".json", copy));
        });
        RefreshCutsceneList(id);
        Ui.Toast("Variant created", $"'{id}' plays when {Catalog?.ModLabel(modId) ?? modId} isn't installed. Restage its actors for the vanilla map. Share story progress through story flags.", ToastKind.Success);
        static string Combine(string? existing, string query) => string.IsNullOrWhiteSpace(existing) ? query : existing.Contains(query, StringComparison.Ordinal) ? existing : existing + ", " + query;
    }

    /// <summary>Switch the editor to another cutscene of the project.</summary>
    public void OpenCutscene(string? id)
    {
        if (Cutscenes.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)) is { } item) CurrentCutsceneItem = item;
    }

    // ---------- validation ----------

    public void ScheduleValidation()
    {
        if (Document == null) return;
        Ui.Debounce("validate", TimeSpan.FromMilliseconds(500), () => _ = ValidateAsync());
    }

    public async Task<ValidationResult?> ValidateAsync()
    {
        if (Document == null) return null;
        int version = ++validationVersion;
        var input = ValidationInput.From(Document, Catalog);
        var result = await Task.Run(() => ValidationService.Validate(input));
        if (version != validationVersion) return result;
        ApplyValidation(result);
        return result;
    }

    [RelayCommand]
    private async Task CheckNow()
    {
        if (Document == null) return;
        Ui.FlushPendingEdits();
        var result = await ValidateAsync();
        if (result == null) return;
        Problems.IsOpenRequested = true;
        Ui.Toast(result.Errors > 0 ? "Problems found" : "Checked", Problems.Summary, result.Errors > 0 ? ToastKind.Warning : ToastKind.Success);
    }

    public ValidationResult ValidateNow()
    {
        var result = ValidationService.Validate(ValidationInput.From(Document!, Catalog));
        ApplyValidation(result);
        return result;
    }

    private void ApplyValidation(ValidationResult result)
    {
        LastValidation = result;
        Problems.Update(result.Problems);
        Storyboard.ApplyProblems(result.Problems);
        Inspector.ApplyProblems(result.Problems);
    }

    /// <summary>Jump to a problem's beat, actor or scene setting.</summary>
    public void Navigate(Problem problem)
    {
        var target = problem.Target;
        if (target.Cutscene == null) { if (target.Kind == TargetKind.Pack) EditPackSettings(); return; }
        Select(target.Kind switch
        {
            TargetKind.Beat when target.Lane != null => new BeatSelection(target.Cutscene, target.Lane, target.Index),
            TargetKind.Actor => new ActorSelection(target.Cutscene, target.Index),
            TargetKind.Scene when target.Lane != null => new BranchSelection(target.Cutscene, target.Lane),
            _ => new SceneSelection(target.Cutscene)
        });
    }

    /// <summary>One-click fixes from the Problems panel (doc 07 §12).</summary>
    public void Fix(Problem problem)
    {
        if (problem.Rule == "V14") { ShowDependencies(); return; }
        if (problem.Suggestion is { } npc && Document?.Cutscenes.FirstOrDefault(c => string.Equals(c.File, problem.File, StringComparison.OrdinalIgnoreCase)) is { } named)
        {
            FixNpcName(named, problem.JsonPath, npc);
            return;
        }
        if (problem.Rule is not ("V5" or "V10") || Document?.Cutscenes.FirstOrDefault(c => string.Equals(c.File, problem.File, StringComparison.OrdinalIgnoreCase)) is not { } doc) return;
        Edit(problem.Rule == "V5" ? "Replace / and quotes" : "Sort keyframes", () =>
        {
            var json = Newtonsoft.Json.Linq.JObject.Parse(PackWriter.Serialize(doc.Scene));
            if (problem.Rule == "V5" && json.SelectToken(problem.JsonPath) is Newtonsoft.Json.Linq.JValue { Type: Newtonsoft.Json.Linq.JTokenType.String } text)
                text.Value = FixText((string)text.Value!);
            else if (problem.Rule == "V10" && json.SelectToken(problem.JsonPath) is Newtonsoft.Json.Linq.JArray keys)
            {
                var sorted = keys.OrderBy(k => (int?)k["At"] ?? 0).ToArray();
                keys.Clear(); foreach (var key in sorted) keys.Add(key);
            }
            doc.Scene = Core.Serialization.PackJson.Deserialize<Cutscene>(json.ToString())!;
        });
    }

    /// <summary>Use the NPC the validator suggested: renames an actor (and the beats that use it) or replaces one reference.</summary>
    private void FixNpcName(CutsceneDocument doc, string path, string npc)
    {
        var actorId = System.Text.RegularExpressions.Regex.Match(path, @"^Actors\[(\d+)\]\.(?:Id|Npc)$");
        if (actorId.Success && int.Parse(actorId.Groups[1].Value) is int index && index < doc.Scene.Actors.Count)
        {
            var actor = doc.Scene.Actors[index];
            Edit($"Use NPC {npc}", () => { StageViewModel.RenameActor(doc.Scene, actor, npc); actor.Npc = npc; });
            return;
        }
        Edit($"Use NPC {npc}", () =>
        {
            var json = Newtonsoft.Json.Linq.JObject.Parse(PackWriter.Serialize(doc.Scene));
            if (json.SelectToken(path) is Newtonsoft.Json.Linq.JValue { Type: Newtonsoft.Json.Linq.JTokenType.String } value) value.Value = npc;
            doc.Scene = Core.Serialization.PackJson.Deserialize<Cutscene>(json.ToString())!;
        });
    }

    public static string FixText(string text)
    {
        bool open = true;
        return string.Concat(text.Select(c => c == '/' ? '⁄' : c == '"' ? ((open = !open) ? '”' : '“') : c));
    }

    // ---------- dialogs ----------

    [RelayCommand]
    public void ShowDependencies()
    {
        if (Document == null) return;
        Ui.FlushPendingEdits();
        Ui.ShowDialog(new DependenciesViewModel(this));
        ScheduleValidation();
    }

    [RelayCommand]
    public void EditPackSettings()
    {
        if (Document == null) return;
        Ui.FlushPendingEdits();
        Ui.ShowDialog(new PackSettingsViewModel(this));
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var vm = new SettingsViewModel(this);
        if (Ui.ShowDialog(vm)) LoadCatalog();
    }

    [RelayCommand]
    public void ImportVideo()
    {
        if (Document == null) return;
        var vm = new VideoImportViewModel(this);
        if (Ui.ShowDialog(vm) && vm.Imported is { } video)
        {
            Document.Commit("Import video " + video.Id);
            Assets.RefreshPackCategories();
            Ui.Toast("Video imported", $"{video.Id}: {video.FrameCount} frames at {video.Settings.Fps:0.#} fps. Add a Video beat to play it.", ToastKind.Success);
        }
    }

    [RelayCommand]
    private void Export()
    {
        if (Document == null) return;
        Ui.FlushPendingEdits();
        Ui.ShowDialog(new ExportViewModel(this));
    }

    [RelayCommand]
    private void OpenPackFolderInExplorer()
    {
        if (Document != null) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + Document.PackFolder + "\"") { UseShellExecute = true });
    }

    // ---------- preview ----------

    [RelayCommand]
    private Task PreviewInGame() => RunPreview(null, -1);

    [RelayCommand]
    private Task PreviewFromHere() => Selection is BeatSelection beat ? RunPreview(beat.Lane, beat.Index) : RunPreview(null, -1);

    private async Task RunPreview(string? lane, int index)
    {
        if (IsCharacterStudio) { await CharacterStudio.PreviewInGameAsync(); return; }   // M8: the studio previews its open item
        if (Document == null || Current == null) return;
        Ui.FlushPendingEdits();
        var validation = await ValidateAsync();
        if (validation is { Errors: > 0 })
        {
            Problems.IsOpenRequested = true;
            Ui.Toast("Can't preview yet", $"Fix {validation.Errors} problem{(validation.Errors == 1 ? "" : "s")} first (Problems panel).", ToastKind.Warning);
            return;
        }
        IsBusy = true; BusyText = "Starting the preview in the game…";
        Output.Add("Info", lane == null ? $"Preview {Current.Scene.Id}" : $"Preview {Current.Scene.Id} from {LaneNames.Title(lane)} beat {index + 1}");
        try
        {
            int forced = TryItAs.Count;
            await Preview.PlayAsync(Document, Current.Scene.Id!, lane, index, adjust: TryItAs.Apply);
            Ui.Toast("Playing in the game", forced == 0 ? "Switch to Stardew Valley to watch."
                : $"Switch to Stardew Valley to watch. {forced} pick{(forced == 1 ? "" : "s")} from Try it as… decide how it lands.", ToastKind.Success);
        }
        catch (BridgeException ex) { Output.Add("Error", ex.Message); Ui.Toast("Preview failed", ex.Message, ToastKind.Error); }
        catch (IOException ex) { Output.Add("Error", ex.Message); Ui.Toast("Preview failed", ex.Message, ToastKind.Error); }
        finally { IsBusy = false; BusyText = null; await RefreshBridgeStatus(); }
    }

    [RelayCommand]
    private async Task StopPreview()
    {
        try { await Preview.StopAsync(); Output.Add("Info", "Preview stopped."); }
        catch (BridgeException ex) { Ui.Toast("Couldn't stop", ex.Message, ToastKind.Warning); }
    }

    [RelayCommand]
    private async Task RefreshGameExport()
    {
        IsBusy = true; BusyText = "The game is exporting its characters, maps and items…";
        try
        {
            await Preview.ExportAsync(progress => Ui.Post(() => BusyText = $"The game is exporting… {(double?)progress["Percent"] ?? 0:0}%"));
            LoadCatalog();
            Ui.Toast("Game export refreshed", CatalogStatus, ToastKind.Success);
        }
        catch (BridgeException ex) { Ui.Toast("Couldn't refresh the export", ex.Message, ToastKind.Error); }
        finally { IsBusy = false; BusyText = null; }
    }

    public async Task RefreshBridgeStatus()
    {
        try { Bridge = await Preview.PingAsync(); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { Bridge = new BridgeStatus(BridgeState.NotRunning, "Game not running"); }
        ReloadCatalogIfChanged();
    }

    /// <summary>The export profile and catalog time last loaded (or tried): a broken catalog isn't retried until it changes.</summary>
    private (string Profile, DateTime? Stamp)? seenCatalog;
    private static DateTime? CatalogStamp(string profile) =>
        File.Exists(Path.Combine(profile, "catalog.json")) ? File.GetLastWriteTimeUtc(Path.Combine(profile, "catalog.json")) : null;

    /// <summary>Pick up a game export that finished (or changed) while the editor is open: no Reload needed.</summary>
    public void ReloadCatalogIfChanged()
    {
        if (IsBusy) return;
        string? profile = Settings.ExportProfile is { } chosen && File.Exists(Path.Combine(chosen, "catalog.json")) ? chosen
            : ExportCatalog.FindProfiles(Settings.ExportFolder).FirstOrDefault();
        if (profile == null || seenCatalog == (Path.GetFullPath(profile), CatalogStamp(profile))) return;
        bool first = Catalog == null;
        LoadCatalog();
        if (Catalog != null) Ui.Toast(first ? "Game export ready" : "Game export updated", CatalogStatus, ToastKind.Success);
    }

    public void Dispose()
    {
        Audio?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Preview.Dispose();
    }

    private static string Age(DateTime when)
    {
        var age = DateTime.Now - when.ToLocalTime();
        return age.TotalMinutes < 2 ? "just now" : age.TotalHours < 1 ? $"{age.TotalMinutes:0} min ago" : age.TotalDays < 1 ? $"{age.TotalHours:0} h ago" : $"{age.TotalDays:0} days ago";
    }
}
