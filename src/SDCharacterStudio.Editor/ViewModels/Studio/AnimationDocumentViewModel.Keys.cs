using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Compiler;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Runtime.Animation;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>Keys, recording on the preview stage, and the properties panel of the animation editor.</summary>
public sealed partial class AnimationDocumentViewModel
{
    private string? dragging;

    // ---------------- keys ----------------

    private ClipTrack EnsureTrack(string owner)
    {
        if (TrackOf(owner) is { } track) return track;
        track = IsLibrary ? new ClipTrack { Role = owner } : new ClipTrack { Actor = owner };
        Tracks.Add(track);
        return track;
    }

    private static void Insert<T>(ref List<T>? list, T key) where T : Keyframe
    {
        list ??= new List<T>();
        list.RemoveAll(k => k.At == key.At);
        list.Add(key);
        list.Sort((a, b) => a.At.CompareTo(b.At));
    }

    private ActorSample SampleNow(string owner)
    {
        var clip = PreviewClip();
        var track = clip.Tracks.FirstOrDefault(t => string.Equals(t.Actor, owner, StringComparison.OrdinalIgnoreCase));
        return ClipPreview.Sample(track, owner, Playhead, Origin(owner), StartFacing(owner), id => Library?.Pose(id));
    }


    /// <summary>Add a key on a row at a time, starting from what the actor shows there now.</summary>
    public void AddKey(TimelineRow row, double time)
    {
        int at = (int)Math.Clamp(Math.Round(time / 10) * 10, 0, Math.Max(0, Duration));
        Playhead = at;
        var now = SampleNow(row.Owner.Id);
        Keyframe? added = null;
        Save($"Add {TrackLabel(row.Track).ToLowerInvariant()} key", () =>
        {
            var track = EnsureTrack(row.Owner.Id);
            switch (row.Track)
            {
                case TrackKind.Position:
                    var position = PositionKeyFor(row.Owner.Id, now.Position, at, null);
                    var list = track.Position; Insert(ref list, position); track.Position = list; added = position; break;
                case TrackKind.Facing:
                    var facing = new FacingKey { At = at, Facing = UnMirror(now.Facing) };
                    var f = track.Facing; Insert(ref f, facing); track.Facing = f; added = facing; break;
                case TrackKind.Pose:
                    var pose = new PoseKey { At = at, Pose = DefaultPose(row.Owner.Id), Play = "once-then-loop" };
                    var p = track.Pose; Insert(ref p, pose); track.Pose = p; added = pose; break;
                case TrackKind.Visible:
                    var visible = new VisibleKey { At = at, Value = !now.Visible };
                    var v = track.Visible; Insert(ref v, visible); track.Visible = v; added = visible; break;
                case TrackKind.Parts:
                    var parts = new PartsKey { At = at, Set = new(StringComparer.OrdinalIgnoreCase) { ["Hat"] = now.Parts.GetValueOrDefault("Hat") == "none" ? "original" : "none" } };
                    var pa = track.Parts; Insert(ref pa, parts); track.Parts = pa; added = parts; break;
                case TrackKind.Expression:
                    var expression = new ExpressionKey { At = at, Expression = Library?.Expressions().Expressions.FirstOrDefault()?.Id, Duration = 600 };
                    var e = track.Expression; Insert(ref e, expression); track.Expression = e; added = expression; break;
                case TrackKind.Emote:
                    var emote = new EmoteKey { At = at, Emote = "exclamation" };
                    var em = track.Emote; Insert(ref em, emote); track.Emote = em; added = emote; break;
                case TrackKind.Sound:
                    var sound = new SoundKey { At = at, Cue = "dwop" };
                    var s = track.Sound; Insert(ref s, sound); track.Sound = s; added = sound; break;
                case TrackKind.Effect:
                    var effect = new EffectKey { At = at, Effect = "dust" };
                    var ef = track.Effect; Insert(ref ef, effect); track.Effect = ef; added = effect; break;
                case TrackKind.Camera:
                    var tile = new[] { (int)Math.Floor(now.Position.X / 64), (int)Math.Floor(now.Position.Y / 64) };
                    var camera = new CameraKey { At = at, Center = new Position { Tile = tile } };
                    var c = track.Camera; Insert(ref c, camera); track.Camera = c; added = camera; break;
            }
        });
        RebuildRows();
        SelectedKey = Rows.SelectMany(r => r.Keys).FirstOrDefault(k => ReferenceEquals(k.Key, added));
    }

    /// <summary>Pose keys start with a pose for the character shown in that role (its rig), else any character pose.</summary>
    private string? DefaultPose(string owner)
    {
        var poses = Library?.Items(LibraryKind.Pose).Select(i => Library.Pose(i.Id)!).Where(p => p.Kind == "character").ToList() ?? new();
        var who = ShownAs(owner);
        var rig = who == null ? null : who.IsFarmer ? Library?.FarmerRig() : Library?.RigFor(who.Key);
        return poses.FirstOrDefault(p => rig != null && string.Equals(p.For, rig.Id, StringComparison.OrdinalIgnoreCase))?.Id ?? poses.FirstOrDefault()?.Id;
    }

    /// <summary>Library clips store facings as authored: undo the preview mirror.</summary>
    private string? UnMirror(string? facing) => IsLibrary && PreviewMirror ? LibraryClips.MirrorFacing(facing) : facing;

    /// <summary>A position key for a world point: relative to the role's start for library clips, absolute tiles for scenes.</summary>
    private PositionKey PositionKeyFor(string owner, WorldPoint point, int at, bool? pixels)
    {
        int tileX = (int)Math.Floor(point.X / 64), tileY = (int)Math.Floor(point.Y / 64);
        int pxX = (int)Math.Round((point.X - tileX * 64) / 4), pxY = (int)Math.Round((point.Y - tileY * 64) / 4);
        if (pixels == false) { pxX = 0; pxY = 0; }
        var existing = TrackOf(owner)?.Position?.FirstOrDefault(k => k.At == at);
        string ease = existing?.Ease ?? "linear";
        var origin = Origin(owner);
        int originX = (int)Math.Floor(origin.X / 64), originY = (int)Math.Floor(origin.Y / 64);
        if (IsLibrary)
        {
            int dx = tileX - originX, dy = tileY - originY;
            if (PreviewMirror) { dx = -dx; pxX = -pxX; }
            return new PositionKey { At = at, Tile = new[] { dx, dy }, Px = new[] { pxX, pxY }, Ease = ease };
        }
        bool relative = TrackOf(owner)?.Position?.Any(k => k.Relative) == true;
        return relative
            ? new PositionKey { At = at, Tile = new[] { tileX - originX, tileY - originY }, Px = new[] { pxX, pxY }, Relative = true, Ease = ease }
            : new PositionKey { At = at, Tile = new[] { tileX, tileY }, Px = new[] { pxX, pxY }, Ease = ease };
    }

    public void MoveKey(TimelineKey key, double time)
    {
        int at = (int)Math.Clamp(Math.Round(time / 10) * 10, 0, Math.Max(0, Duration));
        if (at == key.Key.At) return;
        Save("Move key", () =>
        {
            // Two keys of a track can't share a time: the moved one replaces the other.
            var siblings = KeysOf(TrackOf(key.Row.Owner.Id), key.Row.Track).Where(k => k.At == at && !ReferenceEquals(k, key.Key)).ToList();
            foreach (var other in siblings) RemoveKey(key.Row.Owner.Id, key.Row.Track, other);
            key.Key.At = at;
            SortTrack(TrackOf(key.Row.Owner.Id)!);
        }, rebuildFields: false);
        RebuildRows();
        BuildFields();
    }

    private static void SortTrack(ClipTrack track)
    {
        track.Position?.Sort((a, b) => a.At.CompareTo(b.At)); track.Facing?.Sort((a, b) => a.At.CompareTo(b.At)); track.Pose?.Sort((a, b) => a.At.CompareTo(b.At));
        track.Visible?.Sort((a, b) => a.At.CompareTo(b.At)); track.Parts?.Sort((a, b) => a.At.CompareTo(b.At)); track.Expression?.Sort((a, b) => a.At.CompareTo(b.At));
        track.Emote?.Sort((a, b) => a.At.CompareTo(b.At)); track.Sound?.Sort((a, b) => a.At.CompareTo(b.At)); track.Effect?.Sort((a, b) => a.At.CompareTo(b.At));
        track.Camera?.Sort((a, b) => a.At.CompareTo(b.At));
    }

    private void RemoveKey(string owner, TrackKind kind, Keyframe key)
    {
        if (TrackOf(owner) is not { } track) return;
        switch (kind)
        {
            case TrackKind.Position: track.Position?.Remove((PositionKey)key); if (track.Position?.Count == 0) track.Position = null; break;
            case TrackKind.Facing: track.Facing?.Remove((FacingKey)key); if (track.Facing?.Count == 0) track.Facing = null; break;
            case TrackKind.Pose: track.Pose?.Remove((PoseKey)key); if (track.Pose?.Count == 0) track.Pose = null; break;
            case TrackKind.Visible: track.Visible?.Remove((VisibleKey)key); if (track.Visible?.Count == 0) track.Visible = null; break;
            case TrackKind.Parts: track.Parts?.Remove((PartsKey)key); if (track.Parts?.Count == 0) track.Parts = null; break;
            case TrackKind.Expression: track.Expression?.Remove((ExpressionKey)key); if (track.Expression?.Count == 0) track.Expression = null; break;
            case TrackKind.Emote: track.Emote?.Remove((EmoteKey)key); if (track.Emote?.Count == 0) track.Emote = null; break;
            case TrackKind.Sound: track.Sound?.Remove((SoundKey)key); if (track.Sound?.Count == 0) track.Sound = null; break;
            case TrackKind.Effect: track.Effect?.Remove((EffectKey)key); if (track.Effect?.Count == 0) track.Effect = null; break;
            case TrackKind.Camera: track.Camera?.Remove((CameraKey)key); if (track.Camera?.Count == 0) track.Camera = null; break;
        }
    }

    [RelayCommand]
    public void DeleteKey()
    {
        if (SelectedKey is not { } key) return;
        Save("Delete key", () => RemoveKey(key.Row.Owner.Id, key.Row.Track, key.Key));
        SelectedKey = null;
        RebuildRows();
    }

    public void SetDuration(double ms)
    {
        int duration = (int)Math.Clamp(Math.Round(ms / 50) * 50, 100, 600000);
        if (duration == Duration) return;
        Save("Clip length", () => { if (IsLibrary) LibraryModel!.Duration = duration; else SceneClip!.Duration = duration; }, rebuildFields: false);
        TimelineVersion++;
        BuildFields();
    }

    [RelayCommand]
    private void AddKeyAtPlayhead()
    {
        var owner = Owners.FirstOrDefault(o => o.Id == SelectedOwner) ?? Owners.FirstOrDefault();
        var row = SelectedKey?.Row ?? Rows.FirstOrDefault(r => owner != null && r.Owner == owner && r.Track == TrackKind.Pose);
        if (row != null) AddKey(row, Playhead);
    }

    // ---------------- the preview stage: select, move, record ----------------

    public void SelectActor(string? id) { SelectedOwner = id; if (SelectedKey?.Row.Owner.Id != id) SelectedKey = null; }

    public void BeginDrag(string id) { dragging = id; SelectActor(id); }

    /// <summary>Drop the actor's feet at a stage point (game pixels from the stage's top-left).</summary>
    public void EndDrag(double feetX, double feetY, bool pixels)
    {
        if (dragging is not { } owner) return;
        dragging = null;
        double worldX = (feetX + StageLeft * 16 - 8) * 4, worldY = (feetY + StageTop * 16 - 15) * 4;
        if (!pixels) { worldX = Math.Round(worldX / 64) * 64; worldY = Math.Round(worldY / 64) * 64; }
        var point = new WorldPoint(worldX, worldY);
        if (IsRecording)
        {
            int at = (int)Math.Clamp(Math.Round(Playhead / 10) * 10, 0, Duration);
            PositionKey? added = null;
            Save("Record position", () =>
            {
                var track = EnsureTrack(owner);
                var key = PositionKeyFor(owner, point, at, pixels);
                // The first key of a role's path starts where it stands.
                if (IsLibrary && (track.Position == null || track.Position.Count == 0) && at > 0)
                {
                    var start = new List<PositionKey> { new() { At = 0, Tile = new[] { 0, 0 } } };
                    track.Position = start;
                }
                var list = track.Position; Insert(ref list, key); track.Position = list; added = key;
            });
            RebuildRows();
            SelectedKey = Rows.SelectMany(r => r.Keys).FirstOrDefault(k => ReferenceEquals(k.Key, added));
            StageHint = null;
            return;
        }
        if (IsLibrary)
        {
            // Not recording: move where the role stands for the preview.
            previewTiles[owner] = new[] { (int)Math.Floor(worldX / 64), (int)Math.Floor(worldY / 64) };
            RenderStage();
            StageHint = IsFlipbook ? "Moved where they start in this preview. Walk steps move them in the animation."
                : "Moved where they start in this preview. Turn on Record to make a position key at the playhead.";
        }
        else StageHint = "Turn on Record, then drag to make a position key at the playhead. The actor's start is set on the Scenes stage.";
    }

    public void CancelDrag() { dragging = null; RenderStage(); }

    // ---------------- properties ----------------

    public override void BuildFields()
    {
        Fields.Clear();
        if ((IsLibrary ? LibraryModel : (object?)SceneClip) == null) return;
        FieldCommit commit = (label, apply, rebuild) =>
        {
            Save(label, apply, rebuildFields: rebuild);
            RebuildRows();
            RenderStage();
            if (rebuild) BuildFields();
        };
        if (SelectedKey is { } key) BuildKeyFields(key, commit);
        else if (IsFlipbook) BuildFlipbookFields(commit);
        else BuildClipFields(commit);
    }

    private void BuildKeyFields(TimelineKey item, FieldCommit commit)
    {
        var key = item.Key;
        Fields.Add(new SectionField($"{TrackLabel(item.Row.Track)} key · {item.Row.Owner.Label}", item.Row.Track switch
        {
            TrackKind.Position => IsLibrary ? "Tiles from where the role starts (positive x is right, positive y is down)." : "Where the actor is at this time; it moves smoothly from the previous key.",
            TrackKind.Pose => "Plays a pose from this time on. Make poses in the Poses shelf.",
            TrackKind.Parts => "Keep the hat, lose the hat, change the shirt: from this time on. Reverts after the clip unless it persists.",
            TrackKind.Expression => "Paints an expression on the face for a while.",
            _ => null
        }));
        Fields.Add(new NumberField("Time", "At", () => key.At, v => { key.At = Math.Clamp(v ?? 0, 0, Duration); SortTrack(TrackOf(item.Row.Owner.Id)!); }, commit) { Minimum = 0, Maximum = Duration, Suffix = "ms", Step = 50 });
        switch (key)
        {
            case PositionKey position:
                Fields.Add(new NumberField(IsLibrary || position.Relative ? "Tiles right" : "Tile X", "Tile", () => position.Tile?[0] ?? 0, v => position.Tile = new[] { v ?? 0, position.Tile?[1] ?? 0 }, commit));
                Fields.Add(new NumberField(IsLibrary || position.Relative ? "Tiles down" : "Tile Y", "Tile", () => position.Tile?[1] ?? 0, v => position.Tile = new[] { position.Tile?[0] ?? 0, v ?? 0 }, commit));
                Fields.Add(new NumberField("Nudge x", "Px", () => position.Px[0], v => position.Px = new[] { v ?? 0, position.Px[1] }, commit) { Minimum = -15, Maximum = 15, Suffix = "px" });
                Fields.Add(new NumberField("Nudge y", "Px", () => position.Px[1], v => position.Px = new[] { position.Px[0], v ?? 0 }, commit) { Minimum = -15, Maximum = 15, Suffix = "px" });
                Fields.Add(new ChoiceField("Getting here", "Ease", StudioOptions.Ease(), () => position.Ease, v => position.Ease = v ?? "linear", commit) { Help = "How the move from the previous key speeds up or slows down." });
                if (!IsLibrary) Fields.Add(new BoolField("From where they stand", "Relative", () => position.Relative, v => position.Relative = v, commit) { Description = "Tiles are counted from the actor's position when the clip starts." });
                break;
            case FacingKey facing:
                Fields.Add(new ChoiceField("Facing", "Facing", StudioOptions.Facings(), () => facing.Facing, v => facing.Facing = v, commit));
                break;
            case PoseKey pose:
                var poses = (Library?.Items(LibraryKind.Pose) ?? Enumerable.Empty<LibraryItem>()).Where(p => Library!.Pose(p.Id)?.Kind == "character").Select(p => new ChoiceItem(p.Id, p.Title, p.Detail));
                Fields.Add(new ChoiceField("Pose", "Pose", new[] { new ChoiceItem(null, "None: back to their sprite") }.Concat(poses), () => pose.Pose, v => pose.Pose = v, commit));
                Fields.Add(new ChoiceField("Play", "Play", StudioOptions.Play(), () => pose.Play, v => pose.Play = v ?? "once", commit));
                Fields.Add(new NumberField("Speed", "Speed", () => (int)Math.Round(pose.Speed * 100), v => pose.Speed = Math.Max(1, v ?? 100) / 100.0, commit) { Minimum = 10, Maximum = 400, Suffix = "%", Step = 10 });
                if (pose.Pose != null) Fields.Add(new ActionField("Edit this pose", () => Studio.OpenItem(LibraryKind.Pose, pose.Pose)));
                break;
            case VisibleKey visible:
                Fields.Add(new BoolField("Visible", "Value", () => visible.Value, v => visible.Value = v, commit));
                break;
            case PartsKey parts:
                Fields.Add(StudioOptions.LookList("Change clothes or look", "Set", Studio.Shell.Catalog, () => parts.Set, v => parts.Set = v, commit));
                foreach (var part in StudioOptions.RenderParts)
                {
                    string name = part;
                    Fields.Add(new ChoiceField(name, $"Visible.{name}", new[] { new ChoiceItem(null, "Unchanged"), new ChoiceItem("show", "Show"), new ChoiceItem("hide", "Hide") },
                        () => parts.Visible?.TryGetValue(name, out var shown) == true ? shown ? "show" : "hide" : null,
                        v =>
                        {
                            parts.Visible ??= new(StringComparer.OrdinalIgnoreCase);
                            if (v == null) parts.Visible.Remove(name); else parts.Visible[name] = v == "show";
                            if (parts.Visible.Count == 0) parts.Visible = null;
                        }, commit));
                }
                Fields.Add(new BoolField("Keep after the clip", "Persist", () => parts.Persist, v => parts.Persist = v, commit) { Description = "Otherwise the look reverts when the clip ends (unless its beat holds)." });
                break;
            case ExpressionKey expression:
                var expressions = Library?.Expressions().Expressions.Where(e => e.Id != null).Select(e => new ChoiceItem(e.Id, e.Id!, e.For)) ?? Enumerable.Empty<ChoiceItem>();
                Fields.Add(new ChoiceField("Expression", "Expression", new[] { new ChoiceItem(null, "None") }.Concat(expressions), () => expression.Expression, v => expression.Expression = v, commit));
                Fields.Add(new NumberField("For", "Duration", () => expression.Duration, v => expression.Duration = v, commit) { Nullable = true, Minimum = 0, Suffix = "ms", Help = "Empty: until the next expression key." });
                break;
            case EmoteKey emote:
                Fields.Add(new ChoiceField("Emote", "Emote", StudioOptions.Emotes(Studio.Shell.Catalog), () => emote.Emote?.ToString(), v => emote.Emote = v, commit) { IsEditable = true });
                break;
            case SoundKey sound:
                Fields.Add(new ChoiceField("Sound", "Cue", StudioOptions.Sounds(Studio.Shell.Catalog), () => sound.Cue, v => sound.Cue = v, commit) { IsEditable = true });
                Fields.Add(new NumberField("Pitch", "Pitch", () => sound.Pitch, v => sound.Pitch = v, commit) { Nullable = true, Minimum = 0, Maximum = 2400 });
                break;
            case EffectKey effect:
                Fields.Add(new ChoiceField("Effect", "Effect", StudioOptions.Effects(Library), () => effect.Effect, v => effect.Effect = v, commit) { IsEditable = true });
                Fields.Add(new NumberField("Count", "Count", () => effect.Count, v => effect.Count = Math.Max(1, v ?? 1), commit) { Minimum = 1, Maximum = 20 });
                Fields.Add(new NumberField("For", "Duration", () => effect.Duration, v => effect.Duration = v, commit) { Nullable = true, Minimum = 0, Suffix = "ms" });
                Fields.Add(new TextField("Colour", "Color", () => effect.Color, v => effect.Color = v, commit) { Placeholder = "#RRGGBB (optional)" });
                break;
            case CameraKey camera:
                Fields.Add(new NumberField("Centre tile X", "Center.Tile", () => camera.Center?.Tile?[0] ?? 0, v => camera.Center = new Position { Tile = new[] { v ?? 0, camera.Center?.Tile?[1] ?? 0 } }, commit));
                Fields.Add(new NumberField("Centre tile Y", "Center.Tile", () => camera.Center?.Tile?[1] ?? 0, v => camera.Center = new Position { Tile = new[] { camera.Center?.Tile?[0] ?? 0, v ?? 0 } }, commit));
                Fields.Add(new ChoiceField("Getting here", "Ease", StudioOptions.Ease(), () => camera.Ease, v => camera.Ease = v ?? "linear", commit));
                break;
        }
        Fields.Add(new ActionField("Delete key", DeleteKey));
    }

    private void BuildClipFields(FieldCommit commit)
    {
        if (!IsLibrary)
        {
            var clip = SceneClip!;
            Fields.Add(new SectionField("Scene clip", "This clip belongs to one scene and moves its actors. Save it to the library to reuse it in other scenes."));
            Fields.Add(new NumberField("Length", "Duration", () => clip.Duration, v => clip.Duration = Math.Max(100, v ?? 1000), commit) { Minimum = 100, Suffix = "ms", Step = 100 });
            Fields.Add(new ChoiceField("Steps", "StepFps", StudioOptions.StepFps(), () => clip.StepFps.ToString(), v => clip.StepFps = int.TryParse(v, out int n) ? n : 0, commit) { Help = "Smooth, or on steps like hand-drawn animation." });
            Fields.Add(new BoolField("Loop", "Loop", () => clip.Loop, v => clip.Loop = v, commit));
            Fields.Add(new ActionField("Save to library…", SaveSceneClipToLibrary, primary: true));
            return;
        }
        var model = LibraryModel!;
        Fields.Add(new SectionField("Animation", "Reusable choreography: roles instead of actors, positions counted from where each role starts."));
        Fields.Add(new TextField("Title", "Title", () => model.Title, v => model.Title = v, commit));
        Fields.Add(new TextField("ID", "Id", () => model.Id, v => Rename(v), (l, a, r) => a()) { AllowNull = false, Help = "How scenes refer to it. Renaming updates the scenes that use it." });
        Fields.Add(new TextField("Tags", "Tags", () => string.Join(", ", model.Tags), v => model.Tags = (v ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(), commit) { Placeholder = "greeting, drama" });
        Fields.Add(new NumberField("Length", "Duration", () => model.Duration, v => model.Duration = Math.Max(100, v ?? 1000), commit) { Minimum = 100, Suffix = "ms", Step = 100 });
        Fields.Add(new ChoiceField("Steps", "StepFps", StudioOptions.StepFps(), () => model.StepFps.ToString(), v => model.StepFps = int.TryParse(v, out int n) ? n : 0, commit) { Help = "Smooth, or on steps like hand-drawn animation." });
        Fields.Add(new BoolField("Loop", "Loop", () => model.Loop, v => model.Loop = v, commit));

        var roles = new ListField("Roles", "Roles", "Add role", () => commit("Add role", () =>
        {
            string id = "actor"; for (int n = 2; model.Roles.Any(r => r.Id == id); n++) id = "actor" + n;
            model.Roles.Add(new ClipRole { Id = id, Kind = "Npc" });
        }, true)) { EmptyText = "No roles. Add one for each character the animation moves." };
        foreach (var role in model.Roles)
        {
            var r = role;
            var row = new ListRowViewModel(() =>
            {
                if (model.Roles.Count <= 1) { Ui.Toast("Last role", "An animation needs at least one role.", ToastKind.Info); return; }
                commit("Remove role", () => { model.Roles.Remove(r); model.Tracks.RemoveAll(t => string.Equals(t.Role, r.Id, StringComparison.OrdinalIgnoreCase)); }, true);
            });
            row.Cells.Add(new TextField("", "Id", () => r.Id, v =>
            {
                string? next = v == null ? null : LibraryClips.RoleId(v);
                if (next == null || next == r.Id || model.Roles.Any(o => o.Id == next)) return;
                foreach (var t in model.Tracks.Where(t => string.Equals(t.Role, r.Id, StringComparison.OrdinalIgnoreCase))) t.Role = next;
                if (r.Id != null && PreviewCast.Remove(r.Id, out var cast)) PreviewCast[next] = cast;
                r.Id = next;
            }, (l, a, rb) => commit(l, a, true)) { AllowNull = false });
            row.Cells.Add(new ChoiceField("", "Kind", StudioOptions.RoleKinds(), () => r.Kind, v => r.Kind = v ?? "Any", commit) { Rebuild = true });
            roles.Rows.Add(row);
        }
        Fields.Add(roles);

        Fields.Add(new SectionField("Preview", "Try it on other characters and mirrored. This doesn't change the animation; a Clip beat sets these per scene."));
        FieldCommit preview = (label, apply, rebuild) => { apply(); RenderStage(); if (rebuild) BuildFields(); };
        foreach (var role in model.Roles.Where(r => r.Id != null))
        {
            string id = role.Id!;
            var options = Studio.Characters.Where(c => role.Kind == "Any" || (role.Kind == "Farmer") == c.IsFarmer).Select(c => new ChoiceItem(c.Key, c.Label, c.Detail));
            Fields.Add(new ChoiceField($"Show {id} as", "", options, () => PreviewCast.GetValueOrDefault(id), v => { if (v == null) PreviewCast.Remove(id); else PreviewCast[id] = v; }, preview));
        }
        Fields.Add(new BoolField("Mirror", "", () => PreviewMirror, v => PreviewMirror = v, preview) { Description = "Flip left and right, like a Clip beat's Mirror." });
        var poseIds = model.Tracks.SelectMany(t => t.Pose ?? new()).Select(k => k.Pose).Where(p => p != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var pose in poseIds)
        {
            string from = pose!;
            var targets = new[] { new ChoiceItem(null, "Same pose") }.Concat((Library?.Items(LibraryKind.Pose) ?? Enumerable.Empty<LibraryItem>()).Where(p => p.Id != from).Select(p => new ChoiceItem(p.Id, p.Title, p.Detail)));
            Fields.Add(new ChoiceField($"Use instead of {from}", "", targets, () => PreviewPoseMap.GetValueOrDefault(from), v => { if (v == null) PreviewPoseMap.Remove(from); else PreviewPoseMap[from] = v; }, preview)
            { Help = "A pose made for the character you preview it on (Make a copy for… in the pose's menu)." });
        }
        Fields.Add(new SectionField("Use it"));
        Fields.Add(new ActionField("Use in the current scene", UseInScene, primary: true));
        Fields.Add(new ActionField("Save to My library", () => Studio.SaveToMyLibrary(LibraryKind.Clip, model.Id!)));
    }

    private void Rename(string? value)
    {
        string? next = value == null ? null : PackDocument.ToContentId(value);
        var model = LibraryModel!;
        if (next == null || next == model.Id || Library == null) return;
        if (Library.Exists(LibraryKind.Clip, next)) { Ui.Toast("Name taken", $"There's already an animation called '{next}'.", ToastKind.Warning); return; }
        string old = model.Id!;
        Commit($"Rename animation {old}", lib =>
        {
            lib.Delete(LibraryKind.Clip, old);
            model.Id = next;
            lib.SaveClip(model);
            References.RenameClip(lib, Studio.Shell.Document!, old, next);
        });
        ItemId = next;
        Reload();
    }

    // ---------------- scenes ----------------

    /// <summary>Insert a Clip beat that plays this animation in the scene open in the Scenes workspace (doc 07 §15).</summary>
    public void UseInScene() => Studio.UseClipInScene(LibraryModel!.Id!, PreviewMirror, PreviewPoseMap);

    /// <summary>Right-click › Save to library for a scene's own clip: actors become roles, positions relative (doc 03 §17.1).</summary>
    private void SaveSceneClipToLibrary()
    {
        if (Scene is not { } scene || Library is not { } library) return;
        string? name = Ui.Prompt("Save to library", "Name of the reusable animation", ItemId);
        if (string.IsNullOrWhiteSpace(name)) return;
        string id = library.UniqueId(LibraryKind.Clip, name);
        var kinds = scene.Actors.Where(a => a.Id != null).ToDictionary(a => a.Id!, a => a.Kind ?? "Npc", StringComparer.OrdinalIgnoreCase);
        var staged = StagedMap.For(scene, Studio.Shell.Catalog, Studio.Shell.Document?.Project);
        var starts = scene.Actors.Where(a => a.Id != null && a.Start?.Tile != null).ToDictionary(a => a.Id!, a => { var (x, y) = staged.Tile(a.Start); return new[] { x, y }; }, StringComparer.OrdinalIgnoreCase);
        var made = LibraryClips.FromSceneClip(id, SceneClip!, kinds, starts);
        made.Title = name.Trim();
        Commit("Save clip to library", lib => lib.SaveClip(made));
        if (made.Tracks.Any(t => t.Camera is { Count: > 0 })) Ui.Toast("Camera left out", "Library animations don't move the camera; the scene's Camera beats or its own clip do.", ToastKind.Info);
        Studio.OpenItem(LibraryKind.Clip, id);
    }
}
