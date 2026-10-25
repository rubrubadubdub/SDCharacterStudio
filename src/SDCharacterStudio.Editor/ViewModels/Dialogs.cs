using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDCharacterStudio.Editor.Catalog;
using SDCharacterStudio.Editor.Export;
using SDCharacterStudio.Editor.Infrastructure;
using SDCharacterStudio.Editor.Project;
using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>Base for dialogs: the window closes when <see cref="CloseRequested"/> fires.</summary>
public abstract partial class DialogViewModel : ObservableObject
{
    public event Action<bool>? CloseRequested;
    protected void Close(bool accepted) => CloseRequested?.Invoke(accepted);
    [ObservableProperty] private string? error;
}

// ============================ New project ============================

public sealed partial class NewProjectViewModel : DialogViewModel
{
    private readonly MainViewModel shell;
    private bool idEdited;
    public NewProjectViewModel(MainViewModel shell)
    {
        this.shell = shell;
        author = shell.Settings.Author;
        parentFolder = shell.Settings.ProjectsFolder;
        locations = new ObservableCollection<ChoiceItem>(shell.Catalog?.Locations.Select(l => new ChoiceItem(l.Name, l.Name, l.Origin.IsModProvided ? shell.Catalog.ModLabel(l.Origin.ProvidedBy[0]) : null))
            ?? new[] { "Town", "ScienceHouse", "Beach", "Forest", "Mountain", "Farm" }.Select(n => new ChoiceItem(n, n)));
        location = Locations.FirstOrDefault(l => l.Value == "Town")?.Value ?? Locations.FirstOrDefault()?.Value ?? "Town";
        UpdateId();
    }
    [ObservableProperty] private string name = "My Cutscenes";
    [ObservableProperty] private string author;
    [ObservableProperty] private string uniqueId = "";
    [ObservableProperty] private string parentFolder;
    [ObservableProperty] private string firstScene = "First scene";
    [ObservableProperty] private string? location;
    [ObservableProperty] private ObservableCollection<ChoiceItem> locations;
    public PackDocument? Created { get; private set; }
    public string FolderPreview => Path.Combine(ParentFolder, PackDocument.SafeFileName(Name), "[SDCS] " + PackDocument.SafeFileName(Name));

    partial void OnNameChanged(string value) { UpdateId(); OnPropertyChanged(nameof(FolderPreview)); }
    partial void OnAuthorChanged(string value) => UpdateId();
    partial void OnParentFolderChanged(string value) => OnPropertyChanged(nameof(FolderPreview));
    private bool settingId;
    partial void OnUniqueIdChanged(string value) { if (!settingId && value != AutoId()) idEdited = true; }
    private void UpdateId() { if (idEdited) return; settingId = true; UniqueId = AutoId(); settingId = false; }
    private string AutoId() => Regex.Replace(Author ?? "Player", "[^A-Za-z0-9]", "") + "." + Regex.Replace(Name ?? "Pack", "[^A-Za-z0-9]", "");

    [RelayCommand] private void Browse() { if (shell.Ui.OpenFolder("Where should the project go?", ParentFolder) is { } f) ParentFolder = f; }
    [RelayCommand] private void Cancel() => Close(false);
    [RelayCommand]
    private void Create()
    {
        Error = null;
        if (string.IsNullOrWhiteSpace(Name)) { Error = "Give the pack a name."; return; }
        if (!Regex.IsMatch(UniqueId ?? "", @"^[A-Za-z0-9_.-]+\.[A-Za-z0-9_.-]+$")) { Error = "The ID should look like Author.PackName (letters, digits, dots)."; return; }
        try
        {
            Created = PackDocument.Create(new NewProjectOptions { Name = Name.Trim(), Author = Author.Trim(), UniqueId = UniqueId!, ParentFolder = ParentFolder, FirstCutsceneId = FirstScene, FirstCutsceneTitle = FirstScene, Location = Location ?? "Town" });
            shell.Settings.Author = Author.Trim(); shell.Settings.ProjectsFolder = ParentFolder; shell.Settings.Save();
            Close(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { Error = ex.Message; }
    }
}

// ============================ Dependencies ============================

/// <summary>Pack › Dependencies (doc 07 §8, doc 12): what the pack needs from other mods, and how to declare it.</summary>
public sealed partial class DependenciesViewModel : DialogViewModel
{
    private readonly MainViewModel shell;
    public DependenciesViewModel(MainViewModel shell)
    {
        this.shell = shell;
        Report = DependencyAnalyzer.Analyze(shell.Document!, shell.Catalog);
        foreach (var provider in Report.Providers) Providers.Add(new ProviderRow(this, provider, shell.Document!.Project.ModeFor(provider.ModId) == DependencyMode.Optional));
        foreach (var editor in Report.Editors) Editors.Add(new EditorRow(this, editor, shell.Document!.Project.OptionalEditedBy.Contains(editor.ModId, StringComparer.OrdinalIgnoreCase)));
        var modChoices = new[] { new ChoiceItem("none", "Not a dependency") }.Concat(shell.Catalog?.Mods.Select(m => new ChoiceItem(m.UniqueID, m.Label)) ?? Enumerable.Empty<ChoiceItem>()).ToArray();
        foreach (var group in Report.Unknown.GroupBy(u => DependencyReport.Key(u.Reference)))
            Unknown.Add(new UnknownRow(this, group.Key, group.ToArray(), modChoices, shell.Document!.Project.UnknownProviders.GetValueOrDefault(group.Key)));
        foreach (var missing in Report.Missing) Missing.Add(new UseLink(this, missing));
        foreach (var manual in shell.Document!.Project.ManualDependencies) Manual.Add(new ManualRow(manual.Mod, manual.IsRequired, manual.Reason));
        InstalledMods = shell.Catalog?.Mods.Select(m => new ChoiceItem(m.UniqueID, m.Label)).ToArray() ?? Array.Empty<ChoiceItem>();
    }
    public DependencyReport Report { get; }
    public ObservableCollection<ProviderRow> Providers { get; } = new();
    public ObservableCollection<EditorRow> Editors { get; } = new();
    public ObservableCollection<UnknownRow> Unknown { get; } = new();
    public ObservableCollection<UseLink> Missing { get; } = new();
    public ObservableCollection<ManualRow> Manual { get; } = new();
    public IReadOnlyList<ChoiceItem> InstalledMods { get; }
    public bool HasCatalog => shell.Catalog != null;
    public bool NothingNeeded => Providers.Count == 0 && Editors.Count == 0 && Unknown.Count == 0;
    public void Navigate(DependencyUse use)
    {
        var file = shell.Document!.Cutscenes.FirstOrDefault(c => string.Equals(c.Scene.Id, use.Cutscene, StringComparison.OrdinalIgnoreCase))?.File ?? "";
        var target = ValidationService.Locate(file, use.Reference.JsonPath ?? "", shell.Document.Cutscenes.Select(c => new SDCharacterStudio.Core.Loading.SourceEntry<Cutscene>(c.File, c.Scene)).ToList());
        ApplyChanges();
        shell.Navigate(new Problem { File = file, JsonPath = use.Reference.JsonPath ?? "", Target = target });
    }

    private void ApplyChanges()
    {
        var project = shell.Document!.Project;
        shell.Edit("Change dependencies", () =>
        {
            foreach (var row in Providers) project.SetMode(row.ModId, row.OnlySomeScenes ? DependencyMode.Optional : DependencyMode.Required);
            project.OptionalEditedBy = Editors.Where(e => e.AddOptional).Select(e => e.ModId).ToList();
            foreach (var row in Unknown)
                if (row.Provider != null) project.UnknownProviders[row.Key] = row.Provider; else project.UnknownProviders.Remove(row.Key);
            project.ManualDependencies = Manual.Where(m => !string.IsNullOrWhiteSpace(m.Mod)).Select(m => new ManualDependency { Mod = m.Mod.Trim(), IsRequired = m.IsRequired, Reason = m.Reason }).ToList();
        });
        Close(true);
    }

    [RelayCommand] private void AddManual() => Manual.Add(new ManualRow(InstalledMods.FirstOrDefault()?.Value ?? "Author.Mod", true, null));
    [RelayCommand] private void RemoveManual(ManualRow? row) { if (row != null) Manual.Remove(row); }
    [RelayCommand] private void Cancel() => Close(false);
    [RelayCommand]
    private void Apply() => ApplyChanges();

    public sealed partial class ProviderRow : ObservableObject
    {
        public ProviderRow(DependenciesViewModel owner, ModDependency dependency, bool optional)
        { this.owner = owner; Dependency = dependency; onlySomeScenes = optional; }
        private readonly DependenciesViewModel owner;
        public ModDependency Dependency { get; }
        public string ModId => Dependency.ModId;
        public string Name => Dependency.ModName;
        public string Uses => string.Join("\n", Dependency.Uses.Select(u => u.Describe()).Distinct().Take(8)) + (Dependency.Uses.Count > 8 ? $"\n…and {Dependency.Uses.Count - 8} more" : "");
        public IReadOnlyList<UseLink> UseLinks => Dependency.Uses.Select(u => new UseLink(owner, u)).ToArray();
        public IRelayCommand<DependencyUse> NavigateUseCommand => new RelayCommand<DependencyUse>(u => { if (u != null) owner.Navigate(u); });
        public string Scenes => "Used in: " + string.Join(", ", Dependency.Cutscenes);
        public bool Uncertain => Dependency.Uncertain;
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(ModeText))] private bool onlySomeScenes;
        public string ModeText => OnlySomeScenes ? "Optional: the pack loads without it; scenes that use it skip themselves." : "Required: the pack won't load without it.";
    }
    public sealed partial class EditorRow : ObservableObject
    {
        public EditorRow(DependenciesViewModel owner, ModDependency dependency, bool add) { Dependency = dependency; addOptional = add; UseLinks = dependency.Uses.Select(u => new UseLink(owner, u)).ToArray(); }
        public ModDependency Dependency { get; }
        public string ModId => Dependency.ModId;
        public string Name => Dependency.ModName;
        public string Uses => string.Join("\n", Dependency.Uses.Select(u => u.Describe()).Distinct().Take(6));
        public IReadOnlyList<UseLink> UseLinks { get; }
        [ObservableProperty] private bool addOptional;
    }
    public sealed partial class UnknownRow : ObservableObject
    {
        public UnknownRow(DependenciesViewModel owner, string key, IReadOnlyList<DependencyUse> uses, IReadOnlyList<ChoiceItem> mods, string? provider)
        { Key = key; Use = uses[0]; UseLinks = uses.Select(u => new UseLink(owner, u)).ToArray(); Mods = mods; this.provider = provider; }
        public string Key { get; }
        public DependencyUse Use { get; }
        public string Text => Use.Describe();
        public IReadOnlyList<UseLink> UseLinks { get; }
        public IReadOnlyList<ChoiceItem> Mods { get; }
        [ObservableProperty] private string? provider;
    }
    public sealed partial class ManualRow : ObservableObject
    {
        public ManualRow(string mod, bool required, string? reason) { this.mod = mod; isRequired = required; this.reason = reason; }
        [ObservableProperty] private string mod;
        [ObservableProperty] private bool isRequired;
        [ObservableProperty] private string? reason;
    }
    public sealed class UseLink
    {
        public string Description { get; }
        public IRelayCommand Go { get; }
        public UseLink(DependenciesViewModel owner, DependencyUse use)
        { Description = use.Describe(); Go = new RelayCommand(() => owner.Navigate(use)); }
    }
}

// ============================ Pack settings ============================

public sealed partial class PackSettingsViewModel : DialogViewModel
{
    private readonly MainViewModel shell;
    public PackSettingsViewModel(MainViewModel shell)
    {
        this.shell = shell;
        var doc = shell.Document!;
        name = doc.Manifest.Name ?? ""; author = doc.Manifest.Author ?? ""; version = doc.Manifest.Version ?? "1.0.0";
        description = doc.Manifest.Description ?? ""; uniqueId = doc.Manifest.UniqueID ?? ""; displayName = doc.Studio.Pack.DisplayName ?? name;
        originalId = uniqueId;
    }
    private readonly string originalId;
    [ObservableProperty] private string name;
    [ObservableProperty] private string author;
    [ObservableProperty] private string version;
    [ObservableProperty] private string description;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IdChanged))] private string uniqueId;
    [ObservableProperty] private string displayName;
    public bool IdChanged => UniqueId != originalId;
    public string PackFolder => shell.Document!.PackFolder;

    [RelayCommand] private void Cancel() => Close(false);
    [RelayCommand]
    private void Save()
    {
        if (!Regex.IsMatch(UniqueId, @"^[A-Za-z0-9_.-]+$")) { Error = "The UniqueID may only use letters, digits, dots, dashes and underscores."; return; }
        if (!Regex.IsMatch(Version, @"^\d+\.\d+\.\d+")) { Error = "Version should look like 1.0.0."; return; }
        var doc = shell.Document!;
        shell.Edit("Change pack settings", () =>
        {
            doc.Manifest.Name = Name.Trim(); doc.Manifest.Author = Author.Trim(); doc.Manifest.Version = Version.Trim();
            doc.Manifest.Description = Description.Trim(); doc.Manifest.UniqueID = UniqueId.Trim();
            doc.Studio.Pack.DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? Name.Trim() : DisplayName.Trim();
        });
        Close(true);
    }
}

// ============================ Editor settings ============================

public sealed partial class SettingsViewModel : DialogViewModel
{
    private readonly MainViewModel shell;
    public SettingsViewModel(MainViewModel shell)
    {
        this.shell = shell;
        exportFolder = shell.Settings.ExportFolder; modsFolder = shell.Settings.GameModsFolder ?? ""; author = shell.Settings.Author;
        projectsFolder = shell.Settings.ProjectsFolder;
        RefreshProfiles();
        selectedProfile = Profiles.FirstOrDefault(p => string.Equals(p.Value, shell.Settings.ExportProfile, StringComparison.OrdinalIgnoreCase)) ?? Profiles.FirstOrDefault();
    }
    [ObservableProperty] private string exportFolder;
    [ObservableProperty] private string modsFolder;
    [ObservableProperty] private string author;
    [ObservableProperty] private string projectsFolder;
    [ObservableProperty] private ChoiceItem? selectedProfile;
    public ObservableCollection<ChoiceItem> Profiles { get; } = new();

    partial void OnExportFolderChanged(string value) => RefreshProfiles();
    private void RefreshProfiles()
    {
        Profiles.Clear();
        Profiles.Add(new ChoiceItem(null, "Newest export"));
        foreach (var profile in ExportCatalog.FindProfiles(ExportFolder))
            Profiles.Add(new ChoiceItem(profile, Path.GetFileName(profile), File.GetLastWriteTime(Path.Combine(profile, "catalog.json")).ToString("g")));
    }

    [RelayCommand] private void BrowseExport() { if (shell.Ui.OpenFolder("The Bridge's export folder", ExportFolder) is { } f) ExportFolder = f; }
    [RelayCommand] private void BrowseMods() { if (shell.Ui.OpenFolder("Stardew Valley's Mods folder", ModsFolder) is { } f) ModsFolder = f; }
    [RelayCommand] private void BrowseProjects() { if (shell.Ui.OpenFolder("Where new projects go", ProjectsFolder) is { } f) ProjectsFolder = f; }
    [RelayCommand] private void Cancel() => Close(false);
    [RelayCommand]
    private void Save()
    {
        shell.Settings.ExportFolder = ExportFolder; shell.Settings.ExportProfile = SelectedProfile?.Value;
        shell.Settings.GameModsFolder = string.IsNullOrWhiteSpace(ModsFolder) ? null : ModsFolder;
        shell.Settings.Author = Author; shell.Settings.ProjectsFolder = ProjectsFolder;
        shell.Settings.Save();
        Close(true);
    }
}

// ============================ Video import ============================

public sealed partial class VideoImportViewModel : DialogViewModel
{
    private readonly MainViewModel shell;
    public VideoImportViewModel(MainViewModel shell) => this.shell = shell;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Summary), nameof(Preview))] private string? sourceFolder;
    [ObservableProperty] private string id = "my-video";
    [ObservableProperty] private double fps = 12;
    public ObservableCollection<SoundRow> Sounds { get; } = new();
    public VideoEntry? Imported { get; private set; }
    private IReadOnlyList<string> frames = Array.Empty<string>();
    public string Summary => frames.Count == 0 ? "Choose a folder of PNG frames (frame-001.png, frame-002.png…)." : $"{frames.Count} frames · {frames.Count / Math.Max(1, Fps):0.0} seconds at {Fps:0.#} fps";
    public ImageSource? Preview => frames.Count > 0 ? Images.Load(frames[0]) : null;
    partial void OnFpsChanged(double value) => OnPropertyChanged(nameof(Summary));

    [RelayCommand]
    private void Browse()
    {
        if (shell.Ui.OpenFolder("Folder of PNG frames", SourceFolder) is not { } folder) return;
        frames = VideoImporter.FramesIn(folder);
        SourceFolder = folder;
        if (Id == "my-video") Id = PackDocument.ToContentId(Path.GetFileName(folder));
        string json = Path.Combine(folder, "video.json");
        if (File.Exists(json))
            try
            {
                var settings = Newtonsoft.Json.JsonConvert.DeserializeObject<Core.Models.VideoSettings>(File.ReadAllText(json));
                if (settings != null) { Fps = settings.Fps; Sounds.Clear(); foreach (var (frame, cue) in settings.Sounds) Sounds.Add(new SoundRow { Frame = int.TryParse(frame, out int n) ? n : 1, Cue = cue }); }
            }
            catch (Newtonsoft.Json.JsonException) { }
    }
    [RelayCommand] private void AddSound() => Sounds.Add(new SoundRow { Frame = 1, Cue = "dwop" });
    [RelayCommand] private void RemoveSound(SoundRow? row) { if (row != null) Sounds.Remove(row); }
    [RelayCommand] private void Cancel() => Close(false);
    [RelayCommand]
    private void Import()
    {
        try
        {
            if (shell.Document!.Videos.Any(v => v.Id == PackDocument.ToContentId(Id)) && !shell.Ui.Confirm("Replace video?", $"The pack already has a video called '{Id}'. Replace it?", "Replace")) return;
            Imported = VideoImporter.Import(shell.Document!, frames, Id, Fps, Sounds.ToDictionary(s => s.Frame, s => s.Cue ?? ""));
            Close(true);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { Error = ex.Message; }
    }
    public sealed partial class SoundRow : ObservableObject
    {
        [ObservableProperty] private int frame;
        [ObservableProperty] private string? cue;
    }
}

// ============================ Export ============================

public sealed partial class ExportViewModel : DialogViewModel
{
    private readonly MainViewModel shell;
    public ExportViewModel(MainViewModel shell)
    {
        this.shell = shell;
        var doc = shell.Document!;
        zipPath = Path.Combine(Path.GetDirectoryName(doc.PackFolder)!, PackDocument.SafeFileName(doc.DisplayName) + " " + (doc.Manifest.Version ?? "1.0.0") + ".zip");
        outputFolder = ModExporter.DefaultFolder(doc, ExportTarget.CompleteMod);
        modsFolder = shell.Settings.GameModsFolder;
        Refresh();
    }
    public ValidationResult Validation { get; private set; } = null!;
    public DependencyReport? Dependencies { get; private set; }
    public string PackFolder => shell.Document!.PackFolder;
    public bool HasErrors => Validation.Errors > 0;
    public string StatusText => Done ? "Exported." : HasErrors ? $"{Validation.Errors} problem{(Validation.Errors == 1 ? "" : "s")} must be fixed before export." : Validation.Warnings > 0 ? $"Ready. {Validation.Warnings} warning{(Validation.Warnings == 1 ? "" : "s")} (export is allowed)." : "Ready to export.";
    public IEnumerable<string> RequiredMods => Dependencies?.ManifestDependencies.Where(d => d.IsRequired).Select(d => shell.Catalog?.ModLabel(d.UniqueID ?? "") ?? d.UniqueID ?? "") ?? Enumerable.Empty<string>();
    public IEnumerable<string> OptionalMods => Dependencies?.ManifestDependencies.Where(d => !d.IsRequired).Select(d => shell.Catalog?.ModLabel(d.UniqueID ?? "") ?? d.UniqueID ?? "") ?? Enumerable.Empty<string>();
    public string DependencyText => (Dependencies?.ManifestDependencies.Count ?? 0) == 0 ? "No other mods needed." : string.Join("\n",
        RequiredMods.Select(m => "Requires " + m).Concat(OptionalMods.Select(m => "Optional: " + m)));

    /// <summary>Re-check after the designer changes dependencies from here.</summary>
    private void Refresh()
    {
        Validation = ValidationService.Validate(ValidationInput.From(shell.Document!, shell.Catalog));
        Dependencies = Validation.Dependencies;
        foreach (var name in new[] { nameof(Validation), nameof(Dependencies), nameof(HasErrors), nameof(StatusText), nameof(DependencyText) }) OnPropertyChanged(name);
    }
    public bool NoCatalog => shell.Catalog == null;
    /// <summary>What players install (Q-17): nothing else (a complete mod), a C# mod's scenes, or the framework too.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsCompleteMod), nameof(IsCodeMod), nameof(IsContentPack), nameof(TargetText), nameof(ShowsNeverPlays), nameof(NextSteps))] private ExportTarget target = ExportTarget.CompleteMod;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(OutputHint))] private string outputFolder;

    /// <summary>Where the export goes, readably: the box shows the start of a long path, this names the folder.</summary>
    public string OutputHint
    {
        get
        {
            string folder = OutputFolder.TrimEnd('\\', '/');
            string parent = Path.GetDirectoryName(folder) ?? "";
            string projectExport = Path.Combine(Path.GetDirectoryName(shell.Document!.ProjectPath)!, "Export");
            bool next = parent.Length > 0 && string.Equals(Path.GetFullPath(parent), Path.GetFullPath(projectExport), StringComparison.OrdinalIgnoreCase);
            return $"Writes the folder \u201c{Path.GetFileName(folder)}\u201d {(next ? "in the Export folder next to your project" : "in " + parent)}.";
        }
    }

    /// <summary>Scenes nothing would start in a mod on its own: no trigger, and no code of the creator's to call them.</summary>
    public IReadOnlyList<string> NeverPlays => shell.Document!.Cutscenes.Where(c => c.Scene.Trigger == null)
        .Select(c => c.Scene.Title is { Length: > 0 } title ? title : c.Scene.Id ?? c.File).ToList();
    public bool ShowsNeverPlays => Target == ExportTarget.CompleteMod && NeverPlays.Count > 0;
    public string NeverPlaysText => NeverPlays.Count == shell.Document!.Cutscenes.Count
        ? "No scene in this mod plays by itself, so players would never see one. In each scene's settings (click an empty spot on the stage), set When does it play? to By itself."
        : $"{string.Join(", ", NeverPlays.Select(t => "\u201c" + t + "\u201d"))} {(NeverPlays.Count == 1 ? "doesn't" : "don't")} play by itself: only another mod could start {(NeverPlays.Count == 1 ? "it" : "them")}. To change that, set When does it play? to By itself in the scene's settings.";

    /// <summary>What to do with the export once it's written.</summary>
    public string NextSteps => Target switch
    {
        ExportTarget.CompleteMod => "To play it, put this folder in your game's Mods folder and restart the game. To share it, zip the folder. Players need SMAPI and the mods listed under Dependencies, nothing from SDCharacterStudio.",
        ExportTarget.CodeModScenes => "Next: copy Sdcs.cs and the scenes folder into your mod's project, add the line from README.txt to your .csproj, and call Sdcs.Register(this) in Entry.",
        _ => "Players install this pack together with the SDCharacterStudio framework mod.",
    };
    private string? writtenFolder;
    [RelayCommand]
    private void OpenOutput()
    {
        if (writtenFolder != null && Directory.Exists(writtenFolder))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + writtenFolder + "\"") { UseShellExecute = true });
    }
    public bool IsCompleteMod { get => Target == ExportTarget.CompleteMod; set { if (value) Target = ExportTarget.CompleteMod; } }
    public bool IsCodeMod { get => Target == ExportTarget.CodeModScenes; set { if (value) Target = ExportTarget.CodeModScenes; } }
    public bool IsContentPack { get => Target == ExportTarget.ContentPack; set { if (value) Target = ExportTarget.ContentPack; } }
    public string TargetText => Target switch
    {
        ExportTarget.CompleteMod => "One finished mod: your scenes with the engine inside. Players install just this mod.",
        ExportTarget.CodeModScenes => "For your own C# mod: Sdcs.cs (the engine in one file) and a scenes folder. Copy both into your mod's project and call Sdcs.Register(this) in Entry. Players install only your mod.",
        _ => "A content pack. Players also install the SDCharacterStudio framework mod.",
    };
    partial void OnTargetChanged(ExportTarget value)
    {
        if (value != ExportTarget.ContentPack) OutputFolder = ModExporter.DefaultFolder(shell.Document!, value);
        if (value == ExportTarget.CodeModScenes) { MakeZip = false; CopyToMods = false; }
    }
    [RelayCommand] private void BrowseOutput() { if (shell.Ui.OpenFolder("Where to write it", OutputFolder) is { } f) OutputFolder = f; }
    [ObservableProperty] private bool makeZip;
    [ObservableProperty] private string zipPath;
    [ObservableProperty] private bool copyToMods;
    [ObservableProperty] private string? modsFolder;
    [ObservableProperty] private string? result;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(StatusText))] private bool done;

    [RelayCommand] private void BrowseZip() { if (shell.Ui.SaveFile("Save the zip", "Zip file (*.zip)|*.zip", Path.GetFileName(ZipPath), Path.GetDirectoryName(ZipPath)) is { } p) ZipPath = p; }
    [RelayCommand] private void BrowseMods() { if (shell.Ui.OpenFolder("Stardew Valley's Mods folder", ModsFolder) is { } f) ModsFolder = f; }
    [RelayCommand] private void ShowProblems() { shell.Problems.IsOpenRequested = true; Close(false); }
    /// <summary>Open the first scene that never plays by itself, at its settings (When does it play?).</summary>
    [RelayCommand]
    private void ShowNeverPlays()
    {
        if (shell.Document!.Cutscenes.FirstOrDefault(c => c.Scene.Trigger == null)?.Scene.Id is not { } id) return;
        shell.IsCharacterStudio = false;
        shell.Select(new SceneSelection(id));
        Close(false);
    }
    [RelayCommand] private void OpenDependencies() { shell.ShowDependencies(); Refresh(); }
    [RelayCommand] private void Cancel() => Close(Done);
    [RelayCommand]
    private void Export()
    {
        try
        {
            var outcome = PackExporter.Export(shell.Document!, shell.Catalog, new ExportOptions
            {
                Target = Target, OutputFolder = Target == ExportTarget.ContentPack ? null : OutputFolder,
                ZipPath = MakeZip ? ZipPath : null, ModsFolder = CopyToMods ? ModsFolder : null
            });
            Result = outcome.Message + (outcome.Outputs.Count > 0 ? "\n" + string.Join("\n", outcome.Outputs) : "");
            Done = outcome.Success;
            if (outcome.Success)
            {
                writtenFolder = outcome.Outputs.FirstOrDefault();
                if (CopyToMods && ModsFolder != null) { shell.Settings.GameModsFolder = ModsFolder; shell.Settings.Save(); }
                shell.Ui.Toast("Exported", outcome.Message, ToastKind.Success);
            }
            else Error = outcome.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { Error = ex.Message; }
    }
}

// ============================ Item picker ============================

public sealed partial class ItemPickerViewModel : DialogViewModel
{
    private readonly List<ItemOption> all;
    public ItemPickerViewModel(MainViewModel shell, string? current, string? type)
    {
        var catalog = shell.Catalog;
        all = catalog == null ? new() : catalog.ItemsByType.Where(g => type == null || g.Key == type).SelectMany(g => g.Value)
            .Select(i => new ItemOption(i.Qualified, i.Label, catalog.FullPath(i.Icon), i.Origin.IsModProvided ? catalog.ModLabel(i.Origin.ProvidedBy[0]) : null)).ToList();
        manual = current ?? "";
        Refill();
        selected = Items.FirstOrDefault(i => i.Id == current);
    }
    public ObservableCollection<ItemOption> Items { get; } = new();
    [ObservableProperty] private string search = "";
    [ObservableProperty] private ItemOption? selected;
    [ObservableProperty] private string manual;
    public string? Chosen { get; private set; }
    partial void OnSearchChanged(string value) => Refill();
    partial void OnSelectedChanged(ItemOption? value) { if (value != null) Manual = value.Id; }
    private void Refill()
    {
        Items.Clear();
        var matching = all.Where(i => Search.Length == 0 || i.Label.Contains(Search, StringComparison.OrdinalIgnoreCase) || i.Id.Contains(Search, StringComparison.OrdinalIgnoreCase));
        // The current item first, so it shows selected even in a long list.
        if (Search.Length == 0 && all.FirstOrDefault(i => i.Id == Manual) is { } current) matching = matching.Where(i => i != current).Prepend(current);
        foreach (var item in matching.Take(600)) Items.Add(item);
    }
    [RelayCommand] private void Cancel() => Close(false);
    [RelayCommand] private void Choose() { Chosen = string.IsNullOrWhiteSpace(Manual) ? Selected?.Id : Manual.Trim(); if (Chosen != null) Close(true); }
    public sealed record ItemOption(string Id, string Label, string IconPath, string? Mod)
    {
        public ImageSource? Icon => Images.Load(IconPath);
        public string Detail => Mod == null ? Id : $"{Id} · {Mod}";
    }
}
