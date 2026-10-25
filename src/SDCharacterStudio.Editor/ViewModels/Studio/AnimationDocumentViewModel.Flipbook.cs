using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Project;
using SDCharacterStudio.Editor.Studio;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>One card of the flipbook strip: what the character does, for how long.</summary>
public sealed partial class FlipbookStepViewModel : ObservableObject
{
    private readonly AnimationDocumentViewModel doc;
    public FlipbookStepViewModel(AnimationDocumentViewModel doc, int index, FlipbookStep step) { this.doc = doc; Index = index; Step = step; }
    public int Index { get; }
    public int Number => Index + 1;
    public FlipbookStep Step { get; }
    public bool IsPose => Step.Do == "Pose";
    public bool IsWalk => Step.Do == "Walk";
    public bool HasFacing => Step.Do != "Walk";

    public string Do { get => Step.Do; set { if (value != null && value != Step.Do) doc.EditStep(Index, s => { s.Do = value; if (value == "Walk" && s.Tiles is [0, 0]) { s.Tiles = new[] { -1, 0 }; s.Ms = 500; } }); } }
    public string? Pose
    {
        get => Step.Pose;
        set
        {
            if (value == Step.Pose) return;
            // A ready-made pose becomes her own library pose the first time it's picked.
            if (value?.StartsWith(AnimationDocumentViewModel.ReadyMadePrefix, StringComparison.Ordinal) == true) doc.UseReadyMade(Index, value[AnimationDocumentViewModel.ReadyMadePrefix.Length..]);
            else doc.EditStep(Index, s => s.Pose = value);
        }
    }
    public string Play { get => Step.Play; set { if (value != null && value != Step.Play) doc.EditStep(Index, s => s.Play = value); } }
    public double Seconds { get => Step.Ms / 1000.0; set { int ms = (int)Math.Round(Math.Clamp(value, 0.05, 60) * 1000); if (ms != Step.Ms) doc.EditStep(Index, s => s.Ms = ms); } }
    public string Facing { get => Step.Facing ?? "keep"; set { string? facing = value == "keep" ? null : value; if (facing != Step.Facing) doc.EditStep(Index, s => s.Facing = facing); } }
    public double Distance { get => Math.Max(Math.Abs(Step.Tiles[0]), Math.Abs(Step.Tiles[1])); set { int d = (int)Math.Clamp(Math.Round(value), 1, 40); if (d != (int)Distance) doc.EditStep(Index, s => s.Tiles = Direction(s.Tiles, d)); } }
    public string WalkDirection => Flipbook.WalkFacing(Step.Tiles) ?? "left";

    [RelayCommand] private void WalkTo(string direction) => doc.EditStep(Index, s =>
    {
        int d = Math.Max(1, Math.Max(Math.Abs(s.Tiles[0]), Math.Abs(s.Tiles[1])));
        s.Tiles = direction switch { "left" => new[] { -d, 0 }, "right" => new[] { d, 0 }, "up" => new[] { 0, -d }, _ => new[] { 0, d } };
    });
    [RelayCommand] private void Delete() => doc.RemoveStep(Index);
    [RelayCommand] private void Earlier() => doc.MoveStep(Index, -1);
    [RelayCommand] private void Later() => doc.MoveStep(Index, 1);

    private static int[] Direction(int[] tiles, int distance) => Flipbook.WalkFacing(tiles) switch
    {
        "right" => new[] { distance, 0 }, "up" => new[] { 0, -distance }, "down" => new[] { 0, distance }, _ => new[] { -distance, 0 }
    };
}

/// <summary>
/// Flipbook animations (D-48 stage 3): a row of steps (show a pose, walk, their own look) with a time each. The steps
/// regenerate the animation's tracks; the stage previews it as before. The timeline is under Advanced.
/// </summary>
public sealed partial class AnimationDocumentViewModel
{
    public ObservableCollection<FlipbookStepViewModel> Steps { get; } = new();
    public ObservableCollection<ChoiceItem> PoseChoices { get; } = new();
    public static IReadOnlyList<ChoiceItem> StepKinds { get; } = new[] { new ChoiceItem("Pose", "Show a pose"), new ChoiceItem("Walk", "Walk"), new ChoiceItem("Own", "Their own look") };
    public static IReadOnlyList<ChoiceItem> FacingChoices { get; } = new[] { new ChoiceItem("keep", "Keep facing") }.Concat(StudioOptions.Facings()).ToList();
    public static IReadOnlyList<ChoiceItem> PlayChoices { get; } = StudioOptions.Play().ToList();

    private bool Advanced => Studio.Settings.AdvancedPoseTools;
    /// <summary>The flipbook strip shows (a library animation made as a flipbook, in the simple editor).</summary>
    public bool IsFlipbook => IsLibrary && !Advanced && LibraryModel is { } clip && Flipbook.Has(clip) && StillMatches(clip);
    /// <summary>A library animation made on the timeline, opened in the simple editor.</summary>
    public bool IsTimelineInSimple => IsLibrary && !Advanced && LibraryModel is { } clip && !(Flipbook.Has(clip) && StillMatches(clip));
    public bool ShowsTimeline => !IsFlipbook && !IsTimelineInSimple;
    public string FlipbookLength => $"{Duration / 1000.0:0.0} s{(Loop ? ", repeats" : "")}";

    /// <summary>Whether the tracks are still what the steps make (an edit on the timeline turns it into a timeline animation).</summary>
    private bool StillMatches(LibraryClip clip)
    {
        var copy = PackWriter.Clone(clip);
        string? who = Flipbook.Character(clip);
        Flipbook.Write(copy, Flipbook.Steps(clip), facing => who != null && Library?.RigFor(who) is { Id: { } rig } && Library.Pose(PackDocument.ToContentId($"{rig}-walk-{facing}")) != null
            ? PackDocument.ToContentId($"{rig}-walk-{facing}") : null);
        return PackWriter.Serialize(copy.Tracks) == PackWriter.Serialize(clip.Tracks) && copy.Duration == clip.Duration;
    }

    private void RebuildSteps()
    {
        Steps.Clear();
        PoseChoices.Clear();
        if (!IsFlipbook) { RaiseFlipbook(); return; }
        var steps = Flipbook.Steps(LibraryModel!);
        for (int i = 0; i < steps.Count; i++) Steps.Add(new FlipbookStepViewModel(this, i, steps[i]));
        string? who = Flipbook.Character(LibraryModel!);
        if (Library is { } library)
            foreach (var item in library.Items(LibraryKind.Pose).OrderBy(i => i.Title, StringComparer.OrdinalIgnoreCase))
            {
                var pose = library.Pose(item.Id);
                if (pose?.Kind != "character" || item.Id.Contains("-walk-", StringComparison.Ordinal)) continue;
                bool theirs = who == null || string.Equals(library.Rig(pose.For)?.Npc ?? pose.For, who, StringComparison.OrdinalIgnoreCase);
                if (theirs) PoseChoices.Add(new ChoiceItem(item.Id, item.Title));
            }
        // The ready-made poses, made for her when picked (the owner: "I don't see where I can select from the ready-made
        // poses, only the ones I make").
        if (Character(who) is { IsFarmer: false })
            foreach (var preset in PosePresets.All)
                PoseChoices.Add(new ChoiceItem(ReadyMadePrefix + preset.Id, $"{preset.Title} (ready-made)", preset.IsAnimated ? "a little animation" : "a pose"));
        RaiseFlipbook();
    }

    private void RaiseFlipbook()
    {
        foreach (var name in new[] { nameof(IsFlipbook), nameof(IsTimelineInSimple), nameof(ShowsTimeline), nameof(FlipbookLength) }) OnPropertyChanged(name);
    }

    /// <summary>Change the steps and rebuild the animation from them, as one undo step.</summary>
    public void SaveFlipbook(string label, Action<List<FlipbookStep>> change)
    {
        if (LibraryModel is not { } clip) return;
        var steps = Flipbook.Steps(clip);
        change(steps);
        string? who = Flipbook.Character(clip) ?? PreviewCast.Values.FirstOrDefault();
        Commit(label, lib =>
        {
            Flipbook.Write(clip, steps, facing => who == null ? null : Flipbook.EnsureWalkPose(lib, who, facing));
            lib.SaveClip(clip);
        });
        RebuildOwners(); RebuildRows(); RebuildSteps(); RenderStage();
        OnPropertyChanged(nameof(Duration)); OnPropertyChanged(nameof(TimeText));
    }

    public const string ReadyMadePrefix = "ready:";

    private CharacterChoice? Character(string? who) => who == null ? null : Studio.Characters.FirstOrDefault(c => string.Equals(c.Key, who, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Show a ready-made pose in a step: it becomes her library pose (made once, from her standing frame, facing the way
    /// the step faces or she last walked), and the step shows it.
    /// </summary>
    public void UseReadyMade(int index, string presetId)
    {
        if (LibraryModel is not { } clip || PosePresets.Find(presetId) is not { } preset) return;
        var steps = Flipbook.Steps(clip);
        if (index < 0 || index >= steps.Count) return;
        string? who = Flipbook.Character(clip) ?? PreviewCast.Values.FirstOrDefault();
        if (Character(who) is not { IsFarmer: false } character)
        { Ui.Toast("Ready-made poses", "Ready-made poses are made for a villager from their standing sprite.", ToastKind.Info); RebuildSteps(); return; }
        string facing = steps[index].Facing ?? steps.Take(index).Reverse().Select(s => s.Do == "Walk" ? Flipbook.WalkFacing(s.Tiles) : s.Facing).FirstOrDefault(f => f != null) ?? "down";
        if (Studio.StandingFrame(character, facing) == null)
        { Ui.Toast("No standing frame", $"Ready-made poses start from {character.Label}'s standing sprite facing {facing}, which the game export doesn't have.", ToastKind.Info); RebuildSteps(); return; }
        string? made = null;
        Commit("Ready-made pose: " + preset.Title, lib =>
        {
            made = ReadyMadePoses.Ensure(Studio, lib, character, presetId, facing);
            if (made == null) return;
            steps[index].Do = "Pose";
            steps[index].Pose = made;
            steps[index].Play = preset.Plays == "loop" ? "loop" : "once";
            if (preset.IsAnimated) steps[index].Ms = Math.Max(steps[index].Ms, preset.Frames.Sum(f => f.Ms));
            Flipbook.Write(clip, steps, f => Flipbook.EnsureWalkPose(lib, character.Key, f));
            lib.SaveClip(clip);
        });
        if (made == null) Ui.Toast("Couldn't make it", $"The ready-made pose couldn't be made for {character.Label} facing {facing} (her sprite isn't the size her rig expects).", ToastKind.Info);
        RebuildOwners(); RebuildRows(); RebuildSteps(); RenderStage();
        OnPropertyChanged(nameof(Duration)); OnPropertyChanged(nameof(TimeText));
    }

    public void EditStep(int index, Action<FlipbookStep> change) => SaveFlipbook("Change step", steps => change(steps[index]));
    public void RemoveStep(int index) => SaveFlipbook("Delete step", steps => { if (steps.Count > 1) steps.RemoveAt(index); });
    public void MoveStep(int index, int by) => SaveFlipbook("Move step", steps =>
    {
        int to = index + by;
        if (to < 0 || to >= steps.Count) return;
        (steps[index], steps[to]) = (steps[to], steps[index]);
    });

    [RelayCommand]
    private void AddStep() => SaveFlipbook("Add step", steps =>
    {
        var last = steps.LastOrDefault();
        steps.Add(last is { Do: "Pose" } ? new FlipbookStep { Do = "Pose", Pose = last.Pose, Ms = last.Ms, Play = last.Play }
            : new FlipbookStep { Do = "Pose", Pose = PoseChoices.FirstOrDefault()?.Value, Ms = 1000 });
    });

    /// <summary>A timeline animation in the simple editor: start it again as a flipbook (its tracks are replaced).</summary>
    [RelayCommand]
    private void StartFlipbook()
    {
        if (LibraryModel is not { } clip) return;
        if (clip.Tracks.Any(t => KeysOfAll(t) > 1) && !Ui.Confirm("Start over as a flipbook?", "The timeline's keys are replaced by flipbook steps. Undo brings them back.", "Start over")) return;
        clip.Extra ??= new Dictionary<string, Newtonsoft.Json.Linq.JToken>();
        clip.Extra[Flipbook.CharacterKey] = PreviewCast.Values.FirstOrDefault() ?? Studio.Character?.Key;
        SaveFlipbook("Start a flipbook", steps => { steps.Clear(); steps.Add(new FlipbookStep { Do = "Own", Ms = 1000 }); });
    }

    [RelayCommand] private void OpenTimeline() { Studio.Settings.AdvancedPoseTools = true; Studio.Settings.Save(); Studio.Palette.Simple = false; Reload(); }

    private static int KeysOfAll(ClipTrack track) =>
        (track.Position?.Count ?? 0) + (track.Facing?.Count ?? 0) + (track.Pose?.Count ?? 0) + (track.Visible?.Count ?? 0) + (track.Parts?.Count ?? 0)
        + (track.Expression?.Count ?? 0) + (track.Emote?.Count ?? 0) + (track.Sound?.Count ?? 0) + (track.Effect?.Count ?? 0);

    private void BuildFlipbookFields(FieldCommit commit)
    {
        var clip = LibraryModel!;
        Fields.Add(new SectionField("Flipbook", "A row of steps, left to right. Each shows a pose, walks, or the character's own look, for a time."));
        Fields.Add(new TextField("Name", "Title", () => clip.Title, v => clip.Title = v, commit));
        Fields.Add(new BoolField("Repeat", "Loop", () => clip.Loop, v => clip.Loop = v, commit) { Description = "Plays again from the first step until the scene moves on." });
        Fields.Add(new SectionField("Use it", "In Scenes, choose My animations in the asset browser, then Use, or drop it on a character."));
    }
}
