using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SDCharacterStudio.Core.Models;

public abstract class ExtensibleObject
{
    [JsonExtensionData] public IDictionary<string, JToken>? Extra { get; set; }
}
public class VersionedObject : ExtensibleObject { public string? Format { get; set; } }
public class NamedObject : VersionedObject { public string? Id { get; set; } }
public class Manifest : ExtensibleObject
{
    public string? Name { get; set; }
    public string? Author { get; set; }
    public string? Version { get; set; }
    public string? Description { get; set; }
    public string? UniqueID { get; set; }
    public ContentPackTarget? ContentPackFor { get; set; }
    public List<ManifestDependency> Dependencies { get; set; } = new();
}
public class ContentPackTarget { public string? UniqueID { get; set; } public string? MinimumVersion { get; set; } }
public class ManifestDependency { public string? UniqueID { get; set; } public bool IsRequired { get; set; } = true; public string? MinimumVersion { get; set; } }
public class StudioIndex : VersionedObject
{
    public PackInfo Pack { get; set; } = new();
    public List<ModRequirement> Requires { get; set; } = new();
    public List<string>? Cutscenes { get; set; }
    public LibraryIndex? Library { get; set; }
    public PackDefaults Defaults { get; set; } = new();
}
public class PackInfo { public string? DisplayName { get; set; } public string DefaultLanguage { get; set; } = "en"; }
public class ModRequirement { public string? Mod { get; set; } public string? Reason { get; set; } }
public class PackDefaults { public int StepFps { get; set; } public string TextSpeed { get; set; } = "normal"; }
public class LibraryIndex
{
    public List<string>? Characters { get; set; }
    public string? Masks { get; set; }
    public List<string>? Poses { get; set; }
    public string? Expressions { get; set; }
    public List<string>? Portraits { get; set; }
    public string? Costumes { get; set; }
    /// <summary>Reusable choreography (doc 03 §17.1). Default: library/clips/*.json.</summary>
    public List<string>? Clips { get; set; }
}
public class Position { public int[]? Tile { get; set; } public int[] Px { get; set; } = new[] { 0, 0 }; }
public class Cutscene : NamedObject
{
    public string? Title { get; set; }
    public string? Location { get; set; }
    public Trigger? Trigger { get; set; }
    public string Music { get; set; } = "none";
    public JToken? Camera { get; set; }
    public bool Skippable { get; set; }
    public List<Actor> Actors { get; set; } = new();
    public List<Beat> Beats { get; set; } = new();
    public Dictionary<string, List<Beat>> Branches { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Clip> Clips { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public EndSettings OnEnd { get; set; } = new();
    public List<Beat>? OnSkip { get; set; }
}
public class Trigger { public List<string> Preconditions { get; set; } = new(); public string? Conditions { get; set; } public bool Once { get; set; } = true; public string? OnDayAfterFlag { get; set; } }
public class Actor : ExtensibleObject
{
    public string? Id { get; set; }
    public string? Kind { get; set; }
    public string? Npc { get; set; }
    public Position? Start { get; set; }
    public string Facing { get; set; } = "down";
    public JToken? Costume { get; set; }
    public SpriteReference? Sprite { get; set; }
    public int[]? FrameSize { get; set; }
    public bool Visible { get; set; } = true;
}
public class SpriteReference { public string? Asset { get; set; } public string? Pose { get; set; } }
public class EndSettings { public bool RestoreCostume { get; set; } = true; public bool RestoreSprites { get; set; } = true; public string End { get; set; } = "default"; }
public sealed class TextValue
{
    public string? Literal { get; set; }
    public string? I18n { get; set; }
    public static implicit operator TextValue(string text) => new() { Literal = text };
}
public abstract class Beat : ExtensibleObject { public abstract string Type { get; } }
public class UnknownBeat : Beat { [JsonIgnore] public JObject Data { get; set; } = new(); public override string Type => Data["Type"]?.Type == JTokenType.String ? (string)Data["Type"]! : ""; }
public class SayBeat : Beat
{
    public override string Type => "Say"; public string? Actor { get; set; } public TextValue? Text { get; set; } public JToken? Emotion { get; set; } public string? Portrait { get; set; } public string? Expression { get; set; }
    /// <summary>Other lines, one of which (or <see cref="Text"/>) is said at random (D-51).</summary>
    public List<TextValue>? Or { get; set; }
}
public class MessageBeat : Beat
{
    public override string Type => "Message"; public TextValue? Text { get; set; }
    /// <summary>Other messages, one of which (or <see cref="Text"/>) shows at random (D-51).</summary>
    public List<TextValue>? Or { get; set; }
}
public class ChoiceBeat : Beat { public override string Type => "Choice"; public TextValue? Prompt { get; set; } public List<ChoiceOption> Options { get; set; } = new(); }
public class ChoiceOption
{
    public TextValue? Text { get; set; } public string? Then { get; set; }
    /// <summary>Offered only when this is true: the game's condition text (D-51, as a Check case's When).</summary>
    public string? When { get; set; }
}
public class ClipBeat : Beat
{
    public override string Type => "Clip";
    public string? Clip { get; set; }
    public bool Hold { get; set; }
    /// <summary>For a library clip: role ID → actor ID in this scene (doc 03 §17.1).</summary>
    public Dictionary<string,string>? Cast { get; set; }
    /// <summary>For a library clip: flip horizontal offsets and left/right facings.</summary>
    public bool Mirror { get; set; }
    /// <summary>For a library clip: pose substitutions for this cast.</summary>
    public Dictionary<string,string>? PoseMap { get; set; }
}
public class MoveBeat : Beat { public override string Type => "Move"; public string? Actor { get; set; } public Position? To { get; set; } public JToken Speed { get; set; } = "normal"; public string Path { get; set; } = "x-first"; public bool Wait { get; set; } = true; }
public class FaceBeat : Beat { public override string Type => "Face"; public string? Actor { get; set; } public string? Facing { get; set; } }
public class EmoteBeat : Beat { public override string Type => "Emote"; public string? Actor { get; set; } public JToken? Emote { get; set; } public bool Wait { get; set; } = true; }
public class ShakeBeat : Beat { public override string Type => "Shake"; public string? Actor { get; set; } public int Duration { get; set; } }
public class JumpBeat : Beat { public override string Type => "Jump"; public string? Actor { get; set; } public int Strength { get; set; } = 8; public bool Silent { get; set; } }
public class PoseBeat : Beat { public override string Type => "Pose"; public string? Actor { get; set; } public string? Pose { get; set; } public string Play { get; set; } = "once"; public bool Wait { get; set; } public bool Release { get; set; } }
public class CostumeBeat : Beat { public override string Type => "Costume"; public string? Actor { get; set; } public Dictionary<string,JToken>? Set { get; set; } public string? Preset { get; set; } public bool Restore { get; set; } public SpriteReference? Sprite { get; set; } public string? Portrait { get; set; } }
public class WaitBeat : Beat { public override string Type => "Wait"; public int Duration { get; set; } }
public class SoundBeat : Beat { public override string Type => "Sound"; public string? Cue { get; set; } public int? Pitch { get; set; } }
public class MusicBeat : Beat { public override string Type => "Music"; public string? Cue { get; set; } }
public class FadeBeat : Beat { public override string Type => "Fade"; public string? To { get; set; } public int Duration { get; set; } public bool Wait { get; set; } = true; }
public class CameraBeat : Beat { public override string Type => "Camera"; public Position? Center { get; set; } public int Duration { get; set; } public string Ease { get; set; } = "linear"; public bool Wait { get; set; } = true; }
public class MailBeat : Beat { public override string Type => "Mail"; public string? Add { get; set; } public string? Remove { get; set; } }
public class FriendshipBeat : Beat { public override string Type => "Friendship"; public string? Npc { get; set; } public int Points { get; set; } }
public class MoneyBeat : Beat { public override string Type => "Money"; public int Amount { get; set; } public bool HoldUp { get; set; } public TextValue? Message { get; set; } }
public class ItemBeat : Beat
{
    public override string Type => "Item"; public string? Item { get; set; } public int Count { get; set; } = 1; public bool HoldUp { get; set; } public TextValue? Message { get; set; }
    /// <summary>A bucket: one of these is given, by its weight, instead of <see cref="Item"/> (D-51). {item} in the message names it.</summary>
    public List<ItemChoice>? OneOf { get; set; }
    /// <summary>Set a story flag for the item given (<see cref="Ids.PickedItemFlag"/>).</summary>
    public bool Remember { get; set; }
    /// <summary>A new pick every time; otherwise the same pick when a day is replayed.</summary>
    public bool Fresh { get; set; }
}
/// <summary>One item in a random bucket.</summary>
public class ItemChoice
{
    public string? Item { get; set; } public int Count { get; set; } = 1; public int Weight { get; set; } = 1;
    /// <summary>In the bucket only when this is true (the game's condition text).</summary>
    public string? When { get; set; }
}
/// <summary>
/// Let chance pick what happens next (D-51, doc 03 §6.17): one outcome, by its weight, jumps to its branch. The same
/// pick when a day is replayed, unless <see cref="Fresh"/>.
/// </summary>
public class ChanceBeat : Beat { public override string Type => "Chance"; public List<ChanceOutcome> Outcomes { get; set; } = new(); public bool Fresh { get; set; } }
public class ChanceOutcome { public int Weight { get; set; } = 1; public string? Then { get; set; } }
/// <summary>
/// Change a story number (D-51, doc 03 §6.18): add to it, take from it, or set it. Numbers live in the player's save as
/// the stat SDCS.&lt;pack&gt;.&lt;name&gt; (<see cref="Ids.NumberStat"/>), start at 0 and never go below it.
/// </summary>
public class NumberBeat : Beat
{
    public override string Type => "Number";
    public string? Name { get; set; }
    /// <summary>add, subtract or set.</summary>
    public string Change { get; set; } = "add";
    public int Amount { get; set; } = 1;
    /// <summary>A random amount from <see cref="Amount"/> to this (the same on a replayed day).</summary>
    public int? UpTo { get; set; }
}
public class QuestBeat : Beat { public override string Type => "Quest"; public string? Add { get; set; } public string? Remove { get; set; } }
public class VideoBeat : Beat { public override string Type => "Video"; public string? Video { get; set; } }
/// <summary>
/// An item on the map from this beat until the scene ends (D-50): furniture, a crop, a gift on a table. Its picture stands
/// on <see cref="At"/> (the tile under its bottom-left corner). Remove takes the item at that tile away again.
/// </summary>
public class PropBeat : Beat { public override string Type => "Prop"; public string? Item { get; set; } public Position? At { get; set; } public bool Remove { get; set; } }
public class EffectBeat : Beat { public override string Type => "Effect"; public string? Effect { get; set; } public string? Actor { get; set; } public Position? At { get; set; } public int Count { get; set; } = 1; public string? Color { get; set; } public int? Duration { get; set; } }
/// <summary>
/// Check something and go one way or another (D-51, doc 03 §6.16): the first case whose <see cref="CheckCase.When"/> is
/// true jumps to its branch; none true goes to <see cref="Otherwise"/> (null: keep going after the Check).
/// </summary>
public class CheckBeat : Beat { public override string Type => "Check"; public List<CheckCase> Cases { get; set; } = new(); public string? Otherwise { get; set; } }
/// <summary>A case of a Check: the game's own condition text (a game state query) and the branch it leads to.</summary>
public class CheckCase { public string? When { get; set; } public string? Then { get; set; } }
public class GotoBeat : Beat { public override string Type => "Goto"; public string? Branch { get; set; } }
public class EndBeat : Beat { public override string Type => "End"; }
public class RawBeat : Beat { public override string Type => "Raw"; public string? Command { get; set; } }

public class Clip { public int Duration { get; set; } public int StepFps { get; set; } public bool Loop { get; set; } public List<ClipTrack> Tracks { get; set; } = new(); }
public class ClipTrack : ExtensibleObject
{
    public string? Actor { get; set; }
    /// <summary>Library clips address roles instead of actors (doc 03 §17.1).</summary>
    public string? Role { get; set; }
    public List<PositionKey>? Position { get; set; }
    public List<FacingKey>? Facing { get; set; }
    public List<PoseKey>? Pose { get; set; }
    public List<VisibleKey>? Visible { get; set; }
    public List<PartsKey>? Parts { get; set; }
    public List<ExpressionKey>? Expression { get; set; }
    public List<EmoteKey>? Emote { get; set; }
    public List<SoundKey>? Sound { get; set; }
    public List<EffectKey>? Effect { get; set; }
    public List<CameraKey>? Camera { get; set; }
}
/// <summary>Reusable choreography in library/clips (doc 03 §17.1): tracks address roles; positions are relative.</summary>
public class LibraryClip : NamedObject
{
    public string? Title { get; set; }
    public List<string> Tags { get; set; } = new();
    public int Duration { get; set; }
    public int StepFps { get; set; }
    public bool Loop { get; set; }
    public List<ClipRole> Roles { get; set; } = new();
    public List<ClipTrack> Tracks { get; set; } = new();
}
public class ClipRole { public string? Id { get; set; } public string Kind { get; set; } = "Any"; }
public class Keyframe { public int At { get; set; } }
public class PositionKey : Keyframe { public int[]? Tile { get; set; } public int[] Px { get; set; } = new[] {0,0}; public bool Relative { get; set; } public string Ease { get; set; } = "linear"; }
public class FacingKey : Keyframe { public string? Facing { get; set; } }
public class PoseKey : Keyframe { public string? Pose { get; set; } public string Play { get; set; } = "once"; public double Speed { get; set; } = 1; }
public class VisibleKey : Keyframe { public bool Value { get; set; } }
public class PartsKey : Keyframe { public Dictionary<string,JToken>? Set { get; set; } public Dictionary<string,bool>? Visible { get; set; } public bool Persist { get; set; } }
public class ExpressionKey : Keyframe { public string? Expression { get; set; } public int? Duration { get; set; } }
public class EmoteKey : Keyframe { public JToken? Emote { get; set; } }
public class SoundKey : Keyframe { public string? Cue { get; set; } public int? Pitch { get; set; } }
public class EffectKey : Keyframe { public string? Effect { get; set; } public string? Color { get; set; } public int Count { get; set; } = 1; public int? Duration { get; set; } }
public class CameraKey : Keyframe { public Position? Center { get; set; } public string Ease { get; set; } = "linear"; }

public class CharacterRig : NamedObject
{
    public string Kind { get; set; } = "Npc";
    public string? Npc { get; set; }
    public int[] FrameSize { get; set; } = new[] {16,32};
    public Dictionary<string,FacingFrames> Frames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string,string> HeadMask { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string,int[]> NeckPoint { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string,int[]> FaceBox { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string,ColorSample> ColorSamples { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public PortraitRig? Portrait { get; set; }
    /// <summary>
    /// Which body part each pixel of the standing frame is, per facing (D-49): a pack PNG at FrameSize whose colours name
    /// the parts (see the editor's BodyMaps). Editor only: it drives part selection and per-character ready-made poses.
    /// </summary>
    public Dictionary<string,string>? BodyMap { get; set; }
}
public class FacingFrames { public int Stand { get; set; } public int[] Walk { get; set; } = Array.Empty<int>(); }
public class ColorSample { public string? Mode { get; set; } public int Frame { get; set; } public int[]? Box { get; set; } public int[][]? Pixels { get; set; } public string? Fallback { get; set; } }
public class PortraitRig { public int[] FrameSize { get; set; } = new[] {64,64}; public string? HeadMask { get; set; } public int[]? NeckPoint { get; set; } public int[]? FaceBox { get; set; } }
public class MaskLibrary : VersionedObject { public List<Mask> Masks { get; set; } = new(); }
public class Mask : ExtensibleObject { public string? Id { get; set; } public string? DisplayName { get; set; } public int[]? FrameSize { get; set; } public string? Image { get; set; } public Dictionary<string,string> PerFrame { get; set; } = new(); public string Mode { get; set; } = "keep"; public List<string> Tags { get; set; } = new(); }
public class Pose : NamedObject
{
    private int[]? pivot;
    public string Kind { get; set; } = "character";
    public string? For { get; set; }
    public int[]? FrameSize { get; set; }
    public int[]? Pivot { get => pivot ?? (FrameSize is {Length:2} ? new[] {FrameSize[0]/2,FrameSize[1]-1} : null); set => pivot=value; }
    public int[]? SheetSize { get; set; }
    public string? Facing { get; set; }
    public bool MirrorForOppositeFacing { get; set; } = true;
    public int[] Intro { get; set; } = Array.Empty<int>();
    public int[] Loop { get; set; } = Array.Empty<int>();
    public int[] Outro { get; set; } = Array.Empty<int>();
    public List<PoseFrame> Frames { get; set; } = new();
    public int[] GetPivot() => Pivot ?? new[] {(FrameSize?.ElementAtOrDefault(0) ?? 0)/2,(FrameSize?.ElementAtOrDefault(1) ?? 1)-1};
}
public class PoseFrame { public int Duration { get; set; } public List<Layer> Layers { get; set; } = new(); public Dictionary<string,int[]> Anchors { get; set; } = new(StringComparer.OrdinalIgnoreCase); public string? Sound { get; set; } public bool Dust { get; set; } }
public abstract class Layer : ExtensibleObject
{
    public abstract string Type { get; }
    public bool Visible { get; set; } = true;
    public int[] Offset { get; set; } = new[] {0,0};
    public bool Flip { get; set; }
    public string? Mask { get; set; }
    public Dictionary<string,string>? ColorSlots { get; set; }
    public LayerAnchor? AnchorTo { get; set; }
    public double Opacity { get; set; } = 1;
}
public class LayerAnchor { public string? Anchor { get; set; } public JToken? From { get; set; } }
public class UnknownLayer : Layer { [JsonIgnore] public JObject Data { get; set; } = new(); public override string Type => Data["Type"]?.Type == JTokenType.String ? (string)Data["Type"]! : ""; }
public class LiveLayer : Layer
{
    public override string Type => "Live"; public LiveSource? Source { get; set; } public string Part { get; set; } = "Full"; public SourceFrame? SourceFrame { get; set; }
    /// <summary>
    /// Optional pixel map (D-48, the flat pose picture): a PNG the size of the pose frame. Each opaque map pixel shows the
    /// live source pixel at (red, green); a transparent one shows nothing. The pose stays live while pixels are moved,
    /// erased or rearranged freely.
    /// </summary>
    public string? Map { get; set; }
}
public class LiveSource
{
    public string? Kind { get; set; } public string? Npc { get; set; } public string? Asset { get; set; } public int[]? Rect { get; set; }
    /// <summary>Kind "Outfit": one of this pack's outfits (a sheet pose), worn instead of the character's own sheet.</summary>
    public string? Outfit { get; set; }
}
public class SourceFrame { public int? Index { get; set; } public string? Facing { get; set; } public bool Stand { get; set; } }
public class ImageLayer : Layer { public override string Type => "Image"; public string? Image { get; set; } public int Frame { get; set; } public int[]? FrameSize { get; set; } }
public class PaintLayer : Layer { public override string Type => "Paint"; public string? Image { get; set; } }
public class EffectLayer : Layer { public override string Type => "Effect"; public string? Effect { get; set; } public int Every { get; set; } }
public class ExpressionLibrary : VersionedObject { public List<Expression> Expressions { get; set; } = new(); }
public class Expression : ExtensibleObject { public string? Id { get; set; } public string? For { get; set; } public string Target { get; set; } = "Sprite"; public string? Facing { get; set; } public string Anchor { get; set; } = "FaceBox"; public List<ExpressionFrame> Frames { get; set; } = new(); public Dictionary<string,string>? ColorSlots { get; set; } }
public class ExpressionFrame { public string? Image { get; set; } public int[] Offset { get; set; } = new[] {0,0}; public int Duration { get; set; } }
public class Portrait : NamedObject { public string? For { get; set; } public int[] FrameSize { get; set; } = new[] {64,64}; public Dictionary<string,PortraitFrame> Emotions { get; set; } = new(StringComparer.OrdinalIgnoreCase); }
public class PortraitFrame { public List<Layer> Layers { get; set; } = new(); public Dictionary<string,int[]> Anchors { get; set; } = new(StringComparer.OrdinalIgnoreCase); }
public class CostumeLibrary : VersionedObject { public List<Costume> Costumes { get; set; } = new(); }
public class Costume { public string? Id { get; set; } public Dictionary<string,JToken> Set { get; set; } = new(StringComparer.OrdinalIgnoreCase); }
public class VideoSettings { public double Fps { get; set; } = 12; public Dictionary<string,string> Sounds { get; set; } = new(); }
