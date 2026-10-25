using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDCharacterStudio.Core.Compiler;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Runtime.Animation;
using Wpf.Ui.Controls;

namespace SDCharacterStudio.Editor.ViewModels;

public enum TrackKind { Position, Facing, Pose, Visible, Parts, Expression, Emote, Sound, Effect, Camera }

/// <summary>Who a track belongs to: a role of a library clip, or an actor of the scene.</summary>
public sealed record ClipOwner(string Id, string Kind, string Label);

public sealed partial class TimelineKey : ObservableObject
{
    public TimelineKey(TimelineRow row, Keyframe key, string? label) { Row = row; Key = key; Label = label; }
    public TimelineRow Row { get; }
    public Keyframe Key { get; }
    public string? Label { get; }
    [ObservableProperty] private bool isSelected;
}

public sealed class TimelineRow
{
    public TimelineRow(ClipOwner owner, TrackKind track, bool first) { Owner = owner; Track = track; IsFirstOfOwner = first; }
    public ClipOwner Owner { get; }
    public TrackKind Track { get; }
    public bool IsFirstOfOwner { get; }
    public string Label => AnimationDocumentViewModel.TrackLabel(Track);
    public List<TimelineKey> Keys { get; } = new();
}

/// <summary>An actor drawn on the preview stage, in game pixels.</summary>
public sealed record StageActor(string Id, string Label, BitmapSource? Image, double Left, double Top, double FeetX, double FeetY, bool Visible, bool IsSelected, string? Emote, string? Expression, string? Effect);

/// <summary>
/// The animation editor (doc 07 §7.2, §15): a preview stage with the cast and a timeline below. Edits library clips
/// (roles, relative positions) and scenes' own clips (actors). Recast and Mirror are preview-only, like a Clip beat's.
/// </summary>
public sealed partial class AnimationDocumentViewModel : StudioDocumentViewModel
{
    private readonly Dictionary<string, int[]> previewTiles = new(StringComparer.OrdinalIgnoreCase);
    private bool rebuildFieldsOnReload = true;
    private double playStartedAt;

    public AnimationDocumentViewModel(CharacterStudioViewModel studio, string id, string? sceneId = null) : base(studio, LibraryKind.Clip, id)
    {
        SceneId = sceneId;
        stageZoom = 3;
    }

    public override SymbolRegular Icon => SymbolRegular.VideoClipMultiple24;

    /// <summary>Set for a scene's own clip (Clips[ItemId] of that cutscene); null for a library clip.</summary>
    public string? SceneId { get; }
    public bool IsLibrary => SceneId == null;
    public LibraryClip? LibraryModel { get; private set; }
    public Clip? SceneClip { get; private set; }
    public Cutscene? Scene => SceneId == null ? null : Studio.Shell.Document?.FindCutscene(SceneId)?.Scene;

    public List<ClipTrack> Tracks => IsLibrary ? LibraryModel!.Tracks : SceneClip!.Tracks;
    public int Duration => IsLibrary ? LibraryModel!.Duration : SceneClip!.Duration;
    public int StepFps => IsLibrary ? LibraryModel!.StepFps : SceneClip!.StepFps;
    public bool Loop => IsLibrary ? LibraryModel!.Loop : SceneClip!.Loop;

    public ObservableCollection<ClipOwner> Owners { get; } = new();
    public ObservableCollection<TimelineRow> Rows { get; } = new();
    public ObservableCollection<StageActor> StageActors { get; } = new();
    /// <summary>Preview casting (role → character key), like a Clip beat's Cast. Not saved.</summary>
    public Dictionary<string, string> PreviewCast { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Preview pose swaps, like a Clip beat's PoseMap. Not saved.</summary>
    public Dictionary<string, string> PreviewPoseMap { get; } = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty] private double playhead;
    [ObservableProperty] private bool isPlaying;
    [ObservableProperty] private bool isRecording;
    [ObservableProperty] private bool previewMirror;
    [ObservableProperty] private bool loopPreview = true;
    [ObservableProperty] private double stageZoom;
    [ObservableProperty] private double timelineScale = 0.12;   // px per ms
    [ObservableProperty] private TimelineKey? selectedKey;
    [ObservableProperty] private string? selectedOwner;
    [ObservableProperty] private int timelineVersion;
    [ObservableProperty] private int stageWidth = 12;
    [ObservableProperty] private int stageHeight = 7;
    [ObservableProperty] private int stageLeft;
    [ObservableProperty] private int stageTop;
    [ObservableProperty] private string? stageHint;

    public string TimeText => $"{Playhead / 1000:0.00} s / {Duration / 1000.0:0.00} s";

    partial void OnPlayheadChanged(double value) { RenderStage(); OnPropertyChanged(nameof(TimeText)); }
    partial void OnPreviewMirrorChanged(bool value) => RenderStage();
    partial void OnSelectedKeyChanged(TimelineKey? oldValue, TimelineKey? newValue)
    {
        if (oldValue != null) oldValue.IsSelected = false;
        if (newValue != null) { newValue.IsSelected = true; SelectedOwner = newValue.Row.Owner.Id; }
        TimelineVersion++;
        BuildFields();
    }
    partial void OnSelectedOwnerChanged(string? value) => RenderStage();

    // ---------------- loading ----------------

    public override bool Reload()
    {
        if (IsLibrary)
        {
            if (Library?.Clip(ItemId) is not { } fresh) return false;
            if (!IsOwnCommit || LibraryModel == null) LibraryModel = fresh;
            Title = string.IsNullOrWhiteSpace(LibraryModel.Title) ? LibraryModel.Id ?? ItemId : LibraryModel.Title!;
        }
        else
        {
            if (Scene is not { } scene || !scene.Clips.TryGetValue(ItemId, out var clip)) return false;
            SceneClip = clip;   // edits apply to the scene's object directly
            Title = $"{ItemId} ({scene.Title ?? scene.Id})";
        }
        RebuildOwners();
        RebuildRows();
        RebuildSteps();
        if (Playhead > Duration) Playhead = Duration;
        RenderStage();
        if (rebuildFieldsOnReload || !IsOwnCommit) BuildFields();
        OnPropertyChanged(nameof(Duration)); OnPropertyChanged(nameof(StepFps)); OnPropertyChanged(nameof(TimeText)); OnPropertyChanged(nameof(IsLibrary));
        return true;
    }

    private void RebuildOwners()
    {
        Owners.Clear();
        if (IsLibrary)
        {
            int i = 0;
            foreach (var role in LibraryModel!.Roles.Where(r => r.Id != null))
            {
                Owners.Add(new ClipOwner(role.Id!, role.Kind, $"{role.Id} ({KindLabel(role.Kind)})"));
                if (!previewTiles.ContainsKey(role.Id!)) previewTiles[role.Id!] = new[] { 4 + i * 2, 3 };
                if (!PreviewCast.ContainsKey(role.Id!) && DefaultCast(role) is { } who) PreviewCast[role.Id!] = who;
                i++;
            }
        }
        else if (Scene is { } scene)
        {
            foreach (var actor in scene.Actors.Where(a => a.Id != null))
                Owners.Add(new ClipOwner(actor.Id!, actor.Kind ?? "Npc", actor.Kind == "Farmer" ? "Player" : actor.Id!));
        }
    }

    private static string KindLabel(string kind) => kind switch { "Npc" => "a character", "Farmer" => "the player", _ => "anyone" };

    /// <summary>Who stands in for a role on the preview stage: the studio's character when it fits, else someone of the right kind.</summary>
    private string? DefaultCast(ClipRole role)
    {
        if (role.Kind == "Farmer") return "farmer";
        if (Studio.Character is { } who && (role.Kind == "Any" || !who.IsFarmer)) return who.Key;
        return Studio.Characters.FirstOrDefault(c => !c.IsFarmer)?.Key;
    }

    public static string TrackLabel(TrackKind track) => track switch
    {
        TrackKind.Position => "Position", TrackKind.Facing => "Facing", TrackKind.Pose => "Pose", TrackKind.Visible => "Visible",
        TrackKind.Parts => "Clothes/parts", TrackKind.Expression => "Expression", TrackKind.Emote => "Emote", TrackKind.Sound => "Sound",
        TrackKind.Effect => "Effect", _ => "Camera"
    };

    private IEnumerable<TrackKind> TracksFor(ClipOwner owner)
    {
        yield return TrackKind.Position; yield return TrackKind.Facing; yield return TrackKind.Pose; yield return TrackKind.Visible;
        if (owner.Kind is "Farmer" or "Any") yield return TrackKind.Parts;
        if (owner.Kind != "Farmer") yield return TrackKind.Expression;
        yield return TrackKind.Emote; yield return TrackKind.Sound; yield return TrackKind.Effect;
        if (!IsLibrary && ReferenceEquals(owner, Owners.FirstOrDefault())) yield return TrackKind.Camera;
    }

    public ClipTrack? TrackOf(string owner) => Tracks.FirstOrDefault(t => string.Equals(IsLibrary ? t.Role : t.Actor, owner, StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<Keyframe> KeysOf(ClipTrack? track, TrackKind kind) => track == null ? Enumerable.Empty<Keyframe>() : kind switch
    {
        TrackKind.Position => track.Position ?? new(), TrackKind.Facing => track.Facing ?? new(), TrackKind.Pose => track.Pose ?? new(),
        TrackKind.Visible => track.Visible ?? new(), TrackKind.Parts => track.Parts ?? new(), TrackKind.Expression => track.Expression ?? new(),
        TrackKind.Emote => track.Emote ?? new(), TrackKind.Sound => track.Sound ?? new(), TrackKind.Effect => track.Effect ?? new(),
        _ => track.Camera ?? new()
    };

    private static string? KeyLabel(Keyframe key) => key switch
    {
        PoseKey p => p.Pose, FacingKey f => f.Facing, ExpressionKey e => e.Expression, EmoteKey e => e.Emote?.ToString(), SoundKey s => s.Cue,
        EffectKey e => e.Effect, VisibleKey v => v.Value ? "show" : "hide",
        PartsKey p => string.Join(", ", (p.Set ?? new()).Select(s => $"{s.Key} {s.Value}").Concat((p.Visible ?? new()).Select(v => (v.Value ? "show " : "hide ") + v.Key))),
        _ => null
    };

    public void RebuildRows()
    {
        var selected = SelectedKey?.Key;
        Rows.Clear();
        TimelineKey? reselect = null;
        foreach (var owner in Owners)
        {
            bool first = true;
            foreach (var kind in TracksFor(owner))
            {
                var row = new TimelineRow(owner, kind, first);
                first = false;
                foreach (var key in KeysOf(TrackOf(owner.Id), kind).OrderBy(k => k.At))
                {
                    var item = new TimelineKey(row, key, KeyLabel(key));
                    row.Keys.Add(item);
                    if (ReferenceEquals(key, selected)) reselect = item;
                }
                Rows.Add(row);
            }
        }
        if (reselect != null || selected != null) SelectedKey = reselect;
        TimelineVersion++;
    }

    // ---------------- saving ----------------

    /// <summary>Change the clip and save it as one undo step.</summary>
    public void Save(string label, Action mutate, bool rebuildFields = true)
    {
        rebuildFieldsOnReload = rebuildFields;
        try
        {
            Commit(label, lib =>
            {
                mutate();
                if (IsLibrary) lib.SaveClip(LibraryModel!);
            });
        }
        finally { rebuildFieldsOnReload = true; }
    }

    // ---------------- the preview stage ----------------

    /// <summary>The clip the runtime would play for this preview: library clips cast to their roles, mirrored and pose-swapped.</summary>
    public Clip PreviewClip()
    {
        if (!IsLibrary) return SceneClip!;
        var cast = LibraryModel!.Roles.Where(r => r.Id != null).ToDictionary(r => r.Id!, r => r.Id!, StringComparer.OrdinalIgnoreCase);
        return LibraryClips.ToSceneClip(LibraryModel, cast, PreviewMirror, PreviewPoseMap.Count == 0 ? null : PreviewPoseMap);
    }

    private WorldPoint Origin(string owner)
    {
        if (IsLibrary) return ClipPreview.World(previewTiles.GetValueOrDefault(owner) ?? new[] { 4, 3 });
        var actor = Scene?.Actors.FirstOrDefault(a => string.Equals(a.Id, owner, StringComparison.OrdinalIgnoreCase));
        return ClipPreview.World(actor?.Start?.Tile, actor?.Start?.Px);
    }

    private string StartFacing(string owner)
    {
        if (!IsLibrary) return Scene?.Actors.FirstOrDefault(a => string.Equals(a.Id, owner, StringComparison.OrdinalIgnoreCase))?.Facing ?? "down";
        string facing = TrackOf(owner)?.Facing?.OrderBy(k => k.At).FirstOrDefault()?.Facing ?? "down";
        return PreviewMirror ? LibraryClips.MirrorFacing(facing) ?? facing : facing;
    }

    /// <summary>The character shown for an owner: the preview cast for roles, the actor's NPC for scenes.</summary>
    public CharacterChoice? ShownAs(string owner)
    {
        string? key;
        if (IsLibrary) key = PreviewCast.GetValueOrDefault(owner);
        else
        {
            var actor = Scene?.Actors.FirstOrDefault(a => string.Equals(a.Id, owner, StringComparison.OrdinalIgnoreCase));
            key = actor?.Kind == "Farmer" ? "farmer" : actor?.Npc ?? actor?.Id;
        }
        if (key == null) return null;
        return Studio.Characters.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? new CharacterChoice(key, key, null, key == "farmer", null);
    }

    public void RenderStage()
    {
        if ((IsLibrary ? LibraryModel : (object?)SceneClip) == null) return;
        var clip = PreviewClip();
        double time = ClipPreview.VisualTime(Playhead, clip.StepFps);
        var samples = new List<(ClipOwner Owner, ActorSample Sample)>();
        foreach (var owner in Owners)
        {
            var track = clip.Tracks.FirstOrDefault(t => string.Equals(t.Actor, owner.Id, StringComparison.OrdinalIgnoreCase));
            samples.Add((owner, ClipPreview.Sample(track, owner.Id, time, Origin(owner.Id), StartFacing(owner.Id), id => Library?.Pose(id))));
        }
        FitStage(clip);
        StageActors.Clear();
        foreach (var (owner, sample) in samples)
        {
            var (image, pivotX, pivotY) = ActorImage(owner, sample);
            double feetX = sample.Position.X / 4 + 8, feetY = sample.Position.Y / 4 + 15;
            StageActors.Add(new StageActor(owner.Id, ShownAs(owner.Id)?.Label is { } shown && IsLibrary ? $"{owner.Id} · {shown}" : owner.Label,
                image == null ? null : StudioImages.ToBitmap(image), feetX - pivotX - StageLeft * 16, feetY - pivotY - StageTop * 16,
                feetX - StageLeft * 16, feetY - StageTop * 16, sample.Visible, string.Equals(owner.Id, SelectedOwner, StringComparison.OrdinalIgnoreCase),
                sample.Emote, sample.Expression, sample.Effect));
        }
    }

    /// <summary>Show every start tile and every keyed position, with a tile of room around them.</summary>
    private void FitStage(Clip clip)
    {
        var points = new List<WorldPoint>();
        foreach (var owner in Owners)
        {
            var origin = Origin(owner.Id);
            points.Add(origin);
            var track = clip.Tracks.FirstOrDefault(t => string.Equals(t.Actor, owner.Id, StringComparison.OrdinalIgnoreCase));
            foreach (var key in track?.Position ?? new()) points.Add(AnimationMath.KeyPosition(key, origin));
        }
        if (points.Count == 0) points.Add(new WorldPoint(4 * 64, 3 * 64));
        int minX = (int)Math.Floor(points.Min(p => p.X) / 64) - 2, maxX = (int)Math.Floor(points.Max(p => p.X) / 64) + 3;
        int minY = (int)Math.Floor(points.Min(p => p.Y) / 64) - 3, maxY = (int)Math.Floor(points.Max(p => p.Y) / 64) + 2;
        int width = Math.Max(10, maxX - minX), height = Math.Max(6, maxY - minY);
        // Keep the frame steady while editing: only grow, or move when something leaves it.
        if (minX < StageLeft || minY < StageTop || maxX > StageLeft + StageWidth || maxY > StageTop + StageHeight || width > StageWidth || height > StageHeight)
        {
            StageLeft = minX; StageTop = minY; StageWidth = width; StageHeight = height;
        }
    }

    /// <summary>What an actor looks like at this moment: its pose frame, else its standing frame; mirrored like the runtime.</summary>
    private (RgbaImage? Image, int PivotX, int PivotY) ActorImage(ClipOwner owner, ActorSample sample)
    {
        var who = ShownAs(owner.Id);
        if (sample.Pose != null && Library?.Pose(sample.Pose) is { } pose)
        {
            var image = Context.RenderFrame(pose, Math.Clamp(sample.PoseFrame, 0, pose.Frames.Count - 1), sample.Facing).Image;
            var pivot = pose.GetPivot();
            bool mirror = pose.MirrorForOppositeFacing && pose.Facing is "left" or "right" && sample.Facing is "left" or "right" && pose.Facing != sample.Facing;
            if (mirror) { image = StudioImages.FlipHorizontal(image); pivot = new[] { image.Width - 1 - pivot[0], pivot[1] }; }
            return (image, pivot[0], pivot[1]);
        }
        if (who != null && Studio.StandingFrame(who, sample.Facing) is { } standing)
        {
            if (who.IsFarmer && sample.Facing == "left") standing = StudioImages.FlipHorizontal(standing);
            return who.IsFarmer ? (standing, 16, 47) : (standing, standing.Width / 2, standing.Height - 1);
        }
        return (Placeholder(), 8, 31);
    }

    private static RgbaImage? placeholder;
    /// <summary>A grey stand-in figure for a role nobody plays yet.</summary>
    private static RgbaImage Placeholder()
    {
        if (placeholder != null) return placeholder;
        var image = StudioImages.Blank(16, 32);
        const uint body = 0xFF8F9BAA, outline = 0xFF3A3D44;
        PixelTools.DrawRectangle(image, new PixelRect(5, 2, 6, 7), body, true);
        PixelTools.DrawRectangle(image, new PixelRect(5, 2, 6, 7), outline, false);
        PixelTools.DrawRectangle(image, new PixelRect(4, 10, 8, 12), body, true);
        PixelTools.DrawRectangle(image, new PixelRect(4, 10, 8, 12), outline, false);
        PixelTools.DrawRectangle(image, new PixelRect(5, 22, 2, 9), body, true);
        PixelTools.DrawRectangle(image, new PixelRect(9, 22, 2, 9), body, true);
        return placeholder = image;
    }

    // ---------------- playback ----------------

    [RelayCommand]
    private void TogglePlay()
    {
        if (IsPlaying) { IsPlaying = false; return; }
        if (Playhead >= Duration) Playhead = 0;
        playStartedAt = Playhead;
        IsPlaying = true;
    }

    [RelayCommand] private void Rewind() { IsPlaying = false; Playhead = 0; }

    /// <summary>Advance playback (the view's timer calls this).</summary>
    public void Tick(double ms)
    {
        if (!IsPlaying) return;
        double next = Playhead + ms;
        if (next >= Duration)
        {
            if (LoopPreview || Loop) next = Duration <= 0 ? 0 : next % Duration;
            else { next = Duration; IsPlaying = false; }
        }
        Playhead = next;
    }

    public override void Closing() => IsPlaying = false;
}
