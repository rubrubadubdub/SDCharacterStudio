using System.Globalization;
using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;
using Wpf.Ui.Controls;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>
/// The portrait editor (doc 07 §3.3, doc 05 §11): each emotion is a frame with layers and a Neck anchor, edited with the
/// pose tools. Isolate head floats the live portrait's head (that emotion's frame) over a drawn body.
/// </summary>
public sealed partial class PortraitDocumentViewModel : PoseDocumentViewModel
{
    public static readonly string[] StandardEmotions = { "neutral", "happy", "sad", "unique", "love", "angry" };
    // Frames keep their emotion name across moves and copies; a frame made by Add frame gets the next free emotion.
    private readonly Dictionary<PoseFrame, string> names = new(ReferenceEqualityComparer.Instance);

    public PortraitDocumentViewModel(CharacterStudioViewModel studio, string id) : base(studio, LibraryKind.Portrait, id) { }

    public override SymbolRegular Icon => SymbolRegular.PersonCircle24;
    public override bool IsPortrait => true;

    public static int EmotionIndex(string name) => Array.IndexOf(StandardEmotions, name.ToLowerInvariant()) is >= 0 and var i ? i
        : int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 0;

    public string EmotionOf(int frameIndex) => Model.Frames.ElementAtOrDefault(frameIndex) is { } frame && names.TryGetValue(frame, out var name) ? name : "new";

    protected override Pose? LoadModel()
    {
        if (Library?.Portrait(ItemId) is not { } portrait) return null;
        names.Clear();
        var pose = new Pose
        {
            Format = "1.0", Id = portrait.Id, For = portrait.For, Kind = "portrait", FrameSize = (int[])portrait.FrameSize.Clone(), Facing = "down",
            MirrorForOppositeFacing = false
        };
        foreach (var (name, emotion) in portrait.Emotions.OrderBy(e => EmotionIndex(e.Key)))
        {
            var frame = new PoseFrame { Duration = 100, Layers = emotion.Layers, Anchors = emotion.Anchors };
            names[frame] = name;
            pose.Frames.Add(frame);
        }
        return pose;
    }

    protected override void Persist(PackLibrary library)
    {
        var portrait = library.Portrait(ItemId) ?? new Portrait { Format = "1.0", Id = ItemId };
        portrait.For = Model.For;
        portrait.FrameSize = Model.FrameSize ?? new[] { 64, 64 };
        portrait.Emotions = new(StringComparer.OrdinalIgnoreCase);
        foreach (var frame in Model.Frames)
        {
            if (!names.TryGetValue(frame, out var name) || portrait.Emotions.ContainsKey(name)) names[frame] = name = NextName(portrait.Emotions.Keys);
            portrait.Emotions[name] = new PortraitFrame { Layers = frame.Layers, Anchors = frame.Anchors };
        }
        library.SavePortrait(portrait);
    }

    private string NextName(IEnumerable<string> taken)
    {
        var used = new HashSet<string>(names.Values.Concat(taken), StringComparer.OrdinalIgnoreCase);
        foreach (var name in StandardEmotions) if (!used.Contains(name)) return name;
        for (int n = 6; ; n++) if (!used.Contains(n.ToString(CultureInfo.InvariantCulture))) return n.ToString(CultureInfo.InvariantCulture);
    }

    protected override string? Badges(int index) => EmotionOf(index);

    protected override bool CanDeleteFrame(int index)
    {
        if (EmotionOf(index) != "neutral") return true;
        Ui.Toast("Neutral stays", "A portrait needs its neutral face; other emotions fall back to it.", ToastKind.Info);
        return false;
    }

    /// <summary>Live portrait layers take the same emotion's frame of the character's portrait.</summary>
    protected override LiveLayer LiveFor(CharacterChoice who, int frameIndex, string part, string facing) => new()
    {
        Source = new LiveSource { Kind = "Portrait", Npc = who.Key }, Part = part, SourceFrame = new SourceFrame { Index = EmotionIndex(EmotionOf(frameIndex)) }
    };

    protected override int[] RigNeck(CharacterChoice who, string facing) => (int[]?)Library?.RigFor(who.Key)?.Portrait?.NeckPoint?.Clone() ?? new[] { 32, 44 };

    /// <summary>Isolating a portrait head needs the portrait's head mask: start one above the neck when the rig has none.</summary>
    protected override void PrepareIsolate(PackLibrary library, CharacterChoice who)
    {
        if (library.RigFor(who.Key) is not { } rig) return;
        rig.Portrait ??= new PortraitRig();
        rig.Portrait.NeckPoint ??= new[] { 32, 44 };
        if (rig.Portrait.HeadMask != null && library.Mask(rig.Portrait.HeadMask) != null) return;
        string id = $"{rig.Id}-portrait-head";
        string path = library.UniquePath($"masks/{id}.png");
        library.WriteImage(path, PixelTools.AboveRow(64, 64, rig.Portrait.NeckPoint[1]));
        library.SaveMask(new Mask { Id = id, DisplayName = $"{rig.Npc} portrait head", FrameSize = new[] { 64, 64 }, Image = path, Tags = new() { rig.Id!, "portrait" } });
        rig.Portrait.HeadMask = id;
        library.SaveRig(rig);
    }

    /// <summary>The live portrait's same emotion, behind the drawing.</summary>
    protected override RgbaImage? GhostImage()
    {
        if (PoseCharacter() is not { IsFarmer: false } who || Context.NpcTexture(who.Key, portrait: true) is not { } sheet) return null;
        return StudioImages.Frame(sheet, 64, 64, EmotionIndex(EmotionOf(FrameIndex)));
    }

    protected override void BuildPoseFields(FieldCommit commit)
    {
        Fields.Add(new SectionField("Portrait", "Each emotion is a frame. Say beats use it with Composed portrait; emotions it lacks show its neutral face."));
        Fields.Add(new NoteField($"ID: {Model.Id}"));
        var rigs = (Library?.Items(LibraryKind.Rig) ?? Enumerable.Empty<LibraryItem>()).Select(r => new ChoiceItem(r.Id, r.Id, r.Detail));
        Fields.Add(new ChoiceField("For (rig)", "For", rigs, () => Model.For, v => Model.For = v, commit) { Help = "Its portrait rig gives the neck point and head mask." });
        if (Frame is { } frame)
        {
            var options = StandardEmotions.Select(e => new ChoiceItem(e, e)).Concat(Enumerable.Range(6, 10).Select(n => new ChoiceItem(n.ToString(CultureInfo.InvariantCulture), $"frame {n}")));
            Fields.Add(new ChoiceField("Emotion", "Emotions", options, () => EmotionOf(FrameIndex), v =>
            {
                if (v == null || names.Values.Contains(v, StringComparer.OrdinalIgnoreCase)) return;
                names[frame] = v;
                foreach (var live in frame.Layers.OfType<LiveLayer>().Where(l => l.Source?.Kind == "Portrait")) live.SourceFrame = new SourceFrame { Index = EmotionIndex(v) };
            }, commit) { IsEditable = true, Rebuild = true, Help = "Which portrait frame this is. Its live layers take the same frame of the real portrait." });
        }
        Fields.Add(new NoteField("Frames are 64×64 px, like the game's portraits."));
    }
}
