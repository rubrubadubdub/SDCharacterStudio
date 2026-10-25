using System.IO;
using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>Preview in game from the Character Studio (doc 07 §15): a temporary scene at the open scene's location.</summary>
public sealed partial class CharacterStudioViewModel
{
    public const string PreviewSceneId = "sdcs-studio-preview";

    /// <summary>
    /// The temporary cutscene that shows the open pose, animation, expression or portrait: the cast stands where the
    /// current scene's player starts, plays it, holds a moment, and the scene ends. Null with a reason when it can't.
    /// </summary>
    public Cutscene? PreviewScene(StudioDocumentViewModel? document, out string? why)
    {
        why = null;
        if (document == null) { why = "Open a pose or animation first."; return null; }
        if (shell.Scene is not { Location: { } location } scene) { why = "Open a scene first: the preview plays at its location."; return null; }
        var tile = scene.Actors.FirstOrDefault(a => a.Kind == "Farmer")?.Start?.Tile ?? scene.Actors.FirstOrDefault()?.Start?.Tile ?? new[] { 10, 10 };
        var preview = PackDocument.NewCutscene(PreviewSceneId, location);
        preview.Title = "Character Studio preview";
        var farmer = preview.Actors[0];
        farmer.Start = new Position { Tile = new[] { tile[0], tile[1] + 2 } };
        farmer.Facing = "up";

        string AddNpc(string npc, int dx, string facing)
        {
            if (preview.Actors.FirstOrDefault(a => string.Equals(a.Npc ?? a.Id, npc, StringComparison.OrdinalIgnoreCase)) is { } existing) return existing.Id!;
            preview.Actors.Add(new Actor { Id = npc, Kind = "Npc", Npc = npc, Start = new Position { Tile = new[] { tile[0] + dx, tile[1] } }, Facing = facing });
            return npc;
        }

        switch (document)
        {
            case PortraitDocumentViewModel portrait when portrait.PoseCharacter() is { IsFarmer: false } who:
                string speaker = AddNpc(who.Key, 0, "down");
                foreach (var emotion in portrait.Model.Frames.Select((_, i) => portrait.EmotionOf(i)).Take(6))
                    preview.Beats.Add(new SayBeat { Actor = speaker, Portrait = portrait.ItemId, Emotion = emotion == "neutral" ? null : emotion, Text = $"({emotion})" });
                break;
            case PoseDocumentViewModel pose:
                var who2 = pose.PoseCharacter();
                if (pose.Model.Kind == "effect")
                {
                    preview.Beats.Add(new EffectBeat { Effect = pose.ItemId, Actor = "farmer", Count = 3 });
                    preview.Beats.Add(new WaitBeat { Duration = 2500 });
                    break;
                }
                if (who2 == null) { why = "Pick whose pose this is (For)."; return null; }
                string actor = who2.IsFarmer ? "farmer" : AddNpc(who2.Key, 0, pose.Model.Facing ?? "down");
                if (pose.IsSheet)
                {
                    preview.Beats.Add(new CostumeBeat { Actor = actor, Sprite = new SpriteReference { Pose = pose.ItemId } });
                    preview.Beats.Add(new MoveBeat { Actor = actor, To = new Position { Tile = new[] { tile[0] - 3, tile[1] } } });
                    preview.Beats.Add(new MoveBeat { Actor = actor, To = new Position { Tile = new[] { tile[0], tile[1] } } });
                    preview.Beats.Add(new FaceBeat { Actor = actor, Facing = "down" });
                    preview.Beats.Add(new WaitBeat { Duration = 1500 });
                    break;
                }
                if (!who2.IsFarmer) preview.Actors.Single(a => a.Id == actor).Facing = pose.Model.Facing ?? "down";
                preview.Beats.Add(new PoseBeat { Actor = actor, Pose = pose.ItemId, Play = pose.Model.Loop.Length > 0 ? "once-then-loop" : "loop" });
                preview.Beats.Add(new WaitBeat { Duration = 3500 });
                preview.Beats.Add(new PoseBeat { Actor = actor, Release = true, Wait = true });
                break;
            case AnimationDocumentViewModel { IsLibrary: true } animation:
                var cast = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                int offset = 0;
                foreach (var role in animation.LibraryModel!.Roles.Where(r => r.Id != null))
                {
                    var shown = animation.ShownAs(role.Id!);
                    if (shown == null || shown.IsFarmer || role.Kind == "Farmer") { cast[role.Id!] = "farmer"; continue; }
                    cast[role.Id!] = AddNpc(shown.Key, offset, "down");
                    offset += 2;
                }
                preview.Beats.Add(new ClipBeat
                {
                    Clip = animation.ItemId, Cast = cast, Mirror = animation.PreviewMirror,
                    PoseMap = animation.PreviewPoseMap.Count > 0 ? new Dictionary<string, string>(animation.PreviewPoseMap, StringComparer.OrdinalIgnoreCase) : null
                });
                preview.Beats.Add(new WaitBeat { Duration = 800 });
                break;
            case AnimationDocumentViewModel:
                why = "This clip belongs to a scene: preview the scene itself (Scenes › Preview in game).";
                return null;
            case ExpressionDocumentViewModel expression when expression.Rig?.Npc is { } npc:
                string face = AddNpc(npc, 0, expression.Model.Facing ?? "down");
                if (expression.IsPortrait) { preview.Beats.Add(new SayBeat { Actor = face, Text = "(expression preview)", Expression = expression.ItemId }); break; }
                preview.Clips["expression"] = new Clip
                {
                    Duration = 3000,
                    Tracks = { new ClipTrack { Actor = face, Facing = new() { new FacingKey { At = 0, Facing = expression.Model.Facing ?? "down" } }, Expression = new() { new ExpressionKey { At = 0, Expression = expression.ItemId, Duration = 3000 } } } }
                };
                preview.Beats.Add(new ClipBeat { Clip = "expression" });
                break;
            default:
                why = "Masks and rigs show through the poses that use them: preview one of those poses.";
                return null;
        }
        preview.Beats.Add(new EndBeat());
        return preview;
    }

    /// <summary>F5 or Preview in game while the studio shows.</summary>
    public async Task PreviewInGameAsync()
    {
        if (shell.Document is not { } doc) return;
        var scene = PreviewScene(ActiveDocument, out string? why);
        if (scene == null) { shell.Ui.Toast("Nothing to preview", why ?? "", ToastKind.Info); return; }
        shell.Ui.FlushPendingEdits();
        var validation = await shell.ValidateAsync();
        if (validation is { Errors: > 0 })
        {
            shell.Problems.IsOpenRequested = true;
            shell.Ui.Toast("Can't preview yet", $"Fix {validation.Errors} problem{(validation.Errors == 1 ? "" : "s")} first (Problems panel).", ToastKind.Warning);
            return;
        }
        shell.IsBusy = true; shell.BusyText = "Starting the preview in the game…";
        shell.Output.Add("Info", $"Character Studio preview: {ActiveDocument!.Title} at {scene.Location}");
        try
        {
            await shell.Preview.PlayTemporaryAsync(doc, scene);
            shell.Ui.Toast("Playing in the game", "Switch to Stardew Valley to watch.", ToastKind.Success);
        }
        catch (Exception ex) when (ex is Preview.BridgeException or IOException or ArgumentException)
        {
            shell.Output.Add("Error", ex.Message);
            shell.Ui.Toast("Preview failed", ex.Message, ToastKind.Error);
        }
        finally { shell.IsBusy = false; shell.BusyText = null; await shell.RefreshBridgeStatus(); }
    }
}
