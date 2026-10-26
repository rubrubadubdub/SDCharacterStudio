using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>An item in My library (all projects).</summary>
public sealed partial class PersonalItemViewModel : ObservableObject
{
    public PersonalItemViewModel(CharacterStudioViewModel studio, LibraryEntry entry, bool inThisPack)
    {
        Studio = studio; Entry = entry; InThisPack = inThisPack;
        Thumbnail = entry.Thumbnail is { } file ? Images.Load(file) : null;
    }
    public CharacterStudioViewModel Studio { get; }
    public LibraryEntry Entry { get; }
    public string Title => Entry.Title;
    public string Detail => Entry.Detail + (Entry.SourcePack != null ? " · from " + Entry.SourcePack : "");
    public bool IsAnimation => Entry.LibraryKind == LibraryKind.Clip;
    public bool InThisPack { get; }
    public ImageSource? Thumbnail { get; }
    public string Tooltip => $"{Entry.Title} ({Entry.Item})\nSaved {Entry.UpdatedAt.ToLocalTime():g}" + (InThisPack ? "\nThis pack has a copy: Use again updates it or adds another." : "");
}

/// <summary>My library (doc 03 §17.3) and copies for other characters (§17.2).</summary>
public sealed partial class CharacterStudioViewModel
{
    private PersonalLibrary? personal;
    public PersonalLibrary Personal { get => personal ??= new PersonalLibrary(); set => personal = value; }
    public ObservableCollection<PersonalItemViewModel> MyPoses { get; } = new();
    public ObservableCollection<PersonalItemViewModel> MyAnimations { get; } = new();
    [ObservableProperty] private bool showMyLibrary;
    public string CopyForLabel => Character is { IsFarmer: false } who ? $"Make a copy for {who.Label}" : "Make a copy for… (pick a character first)";

    partial void OnShowMyLibraryChanged(bool value) { if (value) RefreshMyLibrary(); }

    public void RefreshMyLibrary()
    {
        MyPoses.Clear(); MyAnimations.Clear();
        var links = shell.Document?.Project.LibraryLinks ?? new();
        foreach (var entry in Personal.Entries())
        {
            if (Search.Length > 0 && !(entry.Title.Contains(Search, StringComparison.OrdinalIgnoreCase) || entry.Item.Contains(Search, StringComparison.OrdinalIgnoreCase)
                || entry.Tags.Any(t => t.Contains(Search, StringComparison.OrdinalIgnoreCase)))) continue;
            bool linked = links.Any(l => l.Kind == entry.Kind && string.Equals(l.Source, entry.Id, StringComparison.OrdinalIgnoreCase));
            (entry.LibraryKind == LibraryKind.Clip ? MyAnimations : MyPoses).Add(new PersonalItemViewModel(this, entry, linked));
        }
    }

    [RelayCommand]
    private void SaveToMyLibrary(LibraryItemViewModel? item) { if (item != null) SaveToMyLibrary(item.Kind, item.Id); }

    /// <summary>Copy a pose or animation (with its art, masks, rigs and poses) into My library.</summary>
    public LibraryEntry? SaveToMyLibrary(LibraryKind kind, string id)
    {
        if (Library is not { } library) return null;
        if (kind is not (LibraryKind.Pose or LibraryKind.Clip)) { shell.Ui.Toast("Poses and animations", "My library holds poses and animations; they bring their masks, rigs and art along.", ToastKind.Info); return null; }
        if (Personal.Find(kind, PackDocument.ToContentId(id)) is { } existing
            && !shell.Ui.Confirm("Replace in My library?", $"My library already has '{existing.Title}' (saved {existing.UpdatedAt.ToLocalTime():g}{(existing.SourcePack != null ? " from " + existing.SourcePack : "")}). Replace it with this one?", "Replace"))
            return null;
        try
        {
            var entry = Personal.Save(library, kind, id, shell.Document?.DisplayName, Thumbnail(library, kind, id), kind == LibraryKind.Clip ? library.Clip(id)?.Title : null);
            shell.Ui.Toast("Saved to My library", $"'{entry.Title}' can now be used in any project (Character Studio › My library).", ToastKind.Success);
            RefreshMyLibrary();
            return entry;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            shell.Ui.Toast("Couldn't save to My library", ex.Message, ToastKind.Error);
            return null;
        }
    }

    /// <summary>Thumbnails for the asset browser's pack categories.</summary>
    public ImageSource? PoseThumbnail(string? id) => Library?.Pose(id) is { Frames.Count: > 0 } pose
        ? StudioImages.ToBitmap(Context.RenderFrame(pose, pose.Kind == "sheet" ? 0 : pose.Loop.FirstOrDefault()).Image) : null;
    public ImageSource? ClipThumbnail(string? id) => PoseThumbnail(Library?.Clip(id)?.Tracks.SelectMany(t => t.Pose ?? new()).Select(k => k.Pose).FirstOrDefault(p => p != null));

    private byte[]? Thumbnail(PackLibrary library, LibraryKind kind, string id)
    {
        string? poseId = kind == LibraryKind.Pose ? id : library.Clip(id)?.Tracks.SelectMany(t => t.Pose ?? new()).Select(k => k.Pose).FirstOrDefault(p => p != null);
        if (library.Pose(poseId) is not { Frames.Count: > 0 } pose) return null;
        var image = Context.RenderFrame(pose, pose.Kind == "sheet" ? 0 : pose.Loop.FirstOrDefault()).Image;
        return StudioImages.Encode(image);
    }

    /// <summary>Use a My library item in this pack: copies it in (renaming on clashes) and records where it came from.</summary>
    [RelayCommand]
    public void UseFromMyLibrary(PersonalItemViewModel? item)
    {
        if (item == null || shell.Document is not { } doc) { shell.Ui.Toast("No project", "Open or create a project first.", ToastKind.Info); return; }
        var entry = item.Entry;
        var link = doc.Project.LibraryLinks.FirstOrDefault(l => l.Kind == entry.Kind && string.Equals(l.Source, entry.Id, StringComparison.OrdinalIgnoreCase));
        bool update = false;
        if (link != null)
        {
            bool? answer = shell.Ui.Ask("Already in this pack", $"This pack has a copy of '{entry.Title}'. Update that copy from My library, or add another copy?", "Update it", "Add a copy");
            if (answer == null) return;
            update = answer == true;
        }
        string? id = null;
        try
        {
            Edit((update ? "Update " : "Use ") + entry.Title + " from My library", lib =>
            {
                var (made, newLink) = PersonalLibrary.Use(entry, lib, update ? link : null);
                id = made;
                // One link per library item: the newest copy is the one Update refreshes.
                if (link != null) doc.Project.LibraryLinks.Remove(link);
                doc.Project.LibraryLinks.Add(newLink);
            });
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            shell.Ui.Toast("Couldn't use it", ex.Message, ToastKind.Error);
            return;
        }
        if (id == null) return;
        RefreshMyLibrary();
        ShowMyLibrary = false;
        OpenItem(entry.LibraryKind, id);
        if (!string.Equals(id, entry.Item, StringComparison.OrdinalIgnoreCase))
            shell.Ui.Toast("Renamed in this pack", $"The pack already had a different '{entry.Item}', so the copy is '{id}'.", ToastKind.Info);
    }

    [RelayCommand]
    private void DeleteFromMyLibrary(PersonalItemViewModel? item)
    {
        if (item == null || !shell.Ui.Confirm("Delete from My library?", $"Delete '{item.Title}' from My library? Packs that use it keep their copies.", "Delete")) return;
        try { Personal.Delete(item.Entry); } catch (IOException ex) { shell.Ui.Toast("Couldn't delete", ex.Message, ToastKind.Error); }
        RefreshMyLibrary();
    }

    [RelayCommand]
    private void ShowMyLibraryFolder() { Directory.CreateDirectory(Personal.Root); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Personal.Root) { UseShellExecute = true }); }

    // ---------------- copies for other characters ----------------

    [RelayCommand]
    private void CopyForCharacter(LibraryItemViewModel? item) { if (item != null) CopyPoseFor(item.Id, Character); }

    /// <summary>"Make a copy for &lt;character&gt;" (doc 03 §17.2). Says what the target lacks instead of making a broken pose.</summary>
    public string? CopyPoseFor(string poseId, CharacterChoice? target)
    {
        if (Library is not { } library) return null;
        if (target is not { IsFarmer: false } who) { shell.Ui.Toast("Pick a character", "Choose who the copy is for in the character picker, then use Make a copy again.", ToastKind.Info); return null; }
        var pose = library.Pose(poseId);
        if (pose == null || pose.Kind == "effect") { shell.Ui.Toast("Poses only", "Effects aren't tied to a character; use them as they are.", ToastKind.Info); return null; }
        if (string.Equals(library.Rig(pose.For)?.Npc, who.Key, StringComparison.OrdinalIgnoreCase)) { shell.Ui.Toast("Same character", $"This pose is already {who.Label}'s. Pick someone else in the character picker.", ToastKind.Info); return null; }
        var plan = PoseCopies.Plan(library, shell.Catalog, poseId, who.Key);
        if (plan.Problems.Count > 0 && !shell.Ui.Confirm("Some things need you", string.Join("\n", plan.Problems) + "\n\nMake the copy anyway and fix these in it?", "Make the copy")) return null;
        bool copyMasks = false;
        if (plan.MissingMasks.Count > 0)
        {
            bool? answer = shell.Ui.Ask("Masks needed", $"{who.Label} has no masks named {string.Join(", ", plan.MissingMasks.Select(m => m.To))}. Start them as copies of {string.Join(", ", plan.MissingMasks.Select(m => m.From))}? Check them with the fit check afterwards.",
                "Copy the masks", "Don't make the copy");
            if (answer != true) return null;
            copyMasks = true;
        }
        string? id = null;
        Edit($"Copy {poseId} for {who.Label}", lib => id = PoseCopies.Make(lib, shell.Catalog, poseId, who.Key, copyMasks, Context));
        if (id != null) OpenItem(LibraryKind.Pose, id);
        return id;
    }

}
