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
    private void UseInScene(LibraryItemViewModel? item) { if (item != null) UseItemInScene(item.Kind, item.Id); }

    /// <summary>What "Use in the scene" can put in a scene: poses, outfits, animations, portraits and expressions.</summary>
    public static bool CanUseInScene(LibraryKind kind) => kind is LibraryKind.Pose or LibraryKind.Clip or LibraryKind.Portrait or LibraryKind.Expression;

    /// <summary>The open item can go in the scene (a scene's own clip is already in one).</summary>
    public bool CanUseActiveInScene => ActiveDocument is { } doc && CanUseInScene(doc.Kind) && doc is not AnimationDocumentViewModel { IsLibrary: false };

    public string UseActiveInSceneHint => ActiveDocument switch
    {
        PoseDocumentViewModel { IsSheet: true } => "Adds a Costume beat that puts this outfit on them, after the selected beat, and shows the scene",
        PortraitDocumentViewModel => "Adds a Say beat with this portrait after the selected beat, and shows the scene",
        PoseDocumentViewModel => "Adds a Pose beat that plays this pose after the selected beat, and shows the scene",
        AnimationDocumentViewModel => "Adds a Clip beat that plays this animation after the selected beat, and shows the scene",
        ExpressionDocumentViewModel => "Adds a Say beat with this expression after the selected beat, and shows the scene",
        _ => "",
    };

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void UseActiveInScene()
    {
        if (ActiveDocument is AnimationDocumentViewModel { IsLibrary: true } animation) animation.UseInScene();
        else if (ActiveDocument is { } doc) UseItemInScene(doc.Kind, doc.ItemId);
    }

    /// <summary>
    /// Put a studio item in the current scene after the selected beat, on its character (added to the scene when it isn't
    /// in it yet), then show the scene: a pose plays (Pose), an outfit is worn (Costume), a portrait or an expression
    /// shows in a line (Say), an animation plays (Clip).
    /// </summary>
    public bool UseItemInScene(LibraryKind kind, string id)
    {
        if (kind == LibraryKind.Clip) return UseClipInScene(id);
        if (shell.Scene is not { } scene || Library is not { } library)
        {
            shell.Ui.Toast("Open a scene first", "Pick a cutscene in the Scenes workspace, then use it.", ToastKind.Info);
            return false;
        }
        var pose = kind == LibraryKind.Pose ? library.Pose(id) : null;
        string? rigId = kind switch
        {
            LibraryKind.Pose => pose?.For,
            LibraryKind.Portrait => library.Portrait(id)?.For,
            LibraryKind.Expression => library.Pack.Expressions?.Value.Expressions.FirstOrDefault(e => string.Equals(e?.Id, id, StringComparison.OrdinalIgnoreCase))?.For,
            _ => null,
        };
        var rig = library.Rig(rigId);
        string? npc = rig?.Kind == "Farmer" ? "farmer" : rig?.Npc;
        if (npc == null) { shell.Ui.Toast("Whose is it?", "It doesn't name a character yet. Open it and pick one.", ToastKind.Info); return false; }

        string actor = ActorFor(scene, npc);
        string lane = shell.Selection is BeatSelection b ? b.Lane : shell.Selection is BranchSelection br ? br.Lane : LaneNames.Main;
        int at = shell.Selection is BeatSelection s ? s.Index + 1 : LaneNames.Get(scene, lane)?.Count ?? 0;
        if (pose?.Kind == "sheet")
            shell.Storyboard.Insert(lane, at, BeatCatalog.Kind("Costume"), beat =>
            {
                var c = (CostumeBeat)beat;
                c.Actor = actor; c.Set = null; c.Sprite = new SpriteReference { Pose = id };
            });
        else if (kind == LibraryKind.Pose)
            shell.Storyboard.Insert(lane, at, BeatCatalog.Kind("Pose"), beat =>
            {
                var p = (PoseBeat)beat;
                p.Actor = actor; p.Pose = id; p.Play = pose is { Frames.Count: > 1 } ? "loop" : "once";
            });
        else
            shell.Storyboard.Insert(lane, at, BeatCatalog.Kind("Say"), beat =>
            {
                var say = (SayBeat)beat;
                say.Actor = actor;
                if (kind == LibraryKind.Portrait) say.Portrait = id; else say.Expression = id;
            });
        shell.IsCharacterStudio = false;
        return true;
    }

    /// <summary>The scene's actor for a character: the selected one, else the first, else they join the scene.</summary>
    private string ActorFor(Cutscene scene, string npc)
    {
        bool Plays(Actor a) => npc == "farmer" ? a.Kind == "Farmer" : string.Equals(a.Npc, npc, StringComparison.OrdinalIgnoreCase);
        if (shell.Selection is ActorSelection selected && selected.Index < scene.Actors.Count && Plays(scene.Actors[selected.Index])) return scene.Actors[selected.Index].Id!;
        if (scene.Actors.FirstOrDefault(Plays) is { Id: { } id }) return id;
        var (lastX, lastY) = scene.Actors.LastOrDefault()?.Start is { } last ? shell.Stage.Staged.Tile(last) : (10, 10);
        shell.Stage.DropCharacter(npc, lastX + 1, lastY);
        shell.Ui.Toast("Added to the scene", $"{npc} wasn't in the scene, so they stand next to the last actor. Drag them where you want.", ToastKind.Info);
        return npc;
    }

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
