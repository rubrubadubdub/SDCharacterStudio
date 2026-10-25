using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>Between the studio and the Scenes workspace: using animations in scenes, opening scene clips here.</summary>
public sealed partial class CharacterStudioViewModel
{
    /// <summary>
    /// Insert a Clip beat that plays a library animation after the selected beat of the current scene, with its roles cast
    /// (the selected actor first), then show it in the Scenes workspace.
    /// </summary>
    public bool UseClipInScene(string clipId, bool mirror = false, IReadOnlyDictionary<string, string>? poseMap = null, string? actor = null, string? lane = null, int? index = null)
    {
        if (shell.Scene is not { } scene || Library?.Clip(clipId) is not { } clip)
        {
            shell.Ui.Toast("Open a scene first", "Pick a cutscene in the Scenes workspace, then use the animation.", ToastKind.Info);
            return false;
        }
        actor ??= shell.Selection is ActorSelection a && a.Index < scene.Actors.Count ? scene.Actors[a.Index].Id : null;
        var preview = Documents.OfType<AnimationDocumentViewModel>().FirstOrDefault(d => d.IsLibrary && d.ItemId == clipId)?.PreviewCast;
        var cast = ClipCasting.Cast(clip, scene, actor, preview);
        lane ??= shell.Selection is BeatSelection b ? b.Lane : shell.Selection is BranchSelection br ? br.Lane : LaneNames.Main;
        int at = index ?? (shell.Selection is BeatSelection s ? s.Index + 1 : LaneNames.Get(scene, lane)?.Count ?? 0);
        shell.Storyboard.Insert(lane, at, BeatCatalog.Kind("Clip"), beat =>
        {
            var c = (ClipBeat)beat;
            c.Clip = clipId; c.Cast = cast; c.Mirror = mirror;
            c.PoseMap = poseMap is { Count: > 0 } ? new Dictionary<string, string>(poseMap, StringComparer.OrdinalIgnoreCase) : null;
        });
        var missing = clip.Roles.Where(r => r.Id != null && !cast.ContainsKey(r.Id)).Select(r => r.Id!).ToList();
        if (missing.Count > 0)
            shell.Ui.Toast("Cast the rest", $"Nobody in the scene fits {string.Join(", ", missing)} yet. Add the actor, then pick them in the beat's Cast.", ToastKind.Warning);
        shell.IsCharacterStudio = false;
        return true;
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void UseInScene(LibraryItemViewModel? item) { if (item?.Kind == LibraryKind.Clip) UseClipInScene(item.Id); }

    /// <summary>Open a scene's own clip in the animation editor (double-click a Clip beat).</summary>
    public void OpenSceneClip(string sceneId, string clipId)
    {
        var open = Documents.OfType<AnimationDocumentViewModel>().FirstOrDefault(d => d.SceneId == sceneId && d.ItemId == clipId);
        if (open == null)
        {
            open = new AnimationDocumentViewModel(this, clipId, sceneId);
            if (!open.Reload()) { shell.Ui.Toast("Can't open", $"The scene has no clip '{clipId}'.", ToastKind.Warning); return; }
            Documents.Add(open);
            OnPropertyChanged(nameof(HasDocuments));
        }
        ActiveDocument = open;
        shell.IsCharacterStudio = true;
    }

    /// <summary>Make a new empty clip in the current scene and open it.</summary>
    public string? NewSceneClip(string? name = null)
    {
        if (shell.Scene is not { } scene) return null;
        name ??= shell.Ui.Prompt("New scene clip", "Name of the clip", "clip");
        if (string.IsNullOrWhiteSpace(name)) return null;
        string id = PackDocument.ToContentId(name), candidate = id;
        for (int n = 2; scene.Clips.ContainsKey(candidate); n++) candidate = $"{id}-{n}";
        shell.Edit("New clip " + candidate, () => scene.Clips[candidate] = new Clip { Duration = 2000 });
        OpenSceneClip(scene.Id!, candidate);
        return candidate;
    }
}
