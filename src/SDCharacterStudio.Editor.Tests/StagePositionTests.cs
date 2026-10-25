using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>
/// D-50 (the owner: "as I advance the timeline with movement of characters, why aren't they represented as being
/// there?"): the stage shows the scene once the selected beat has played, and edits on the stage go to the beat that put
/// an actor where they are.
/// </summary>
public sealed class StagePositionTests
{
    private static Cutscene Scene() => new()
    {
        Id = "walks",
        Actors =
        {
            new Actor { Id = "farmer", Kind = "Farmer", Start = new Position { Tile = new[] { 10, 10 } }, Facing = "up" },
            new Actor { Id = "Abigail", Kind = "Npc", Npc = "Abigail", Start = new Position { Tile = new[] { 20, 10 } }, Facing = "left" },
        },
        Beats =
        {
            new MoveBeat { Actor = "farmer", To = new Position { Tile = new[] { 14, 7 } } },                     // 0: right 4, then up 3
            new MoveBeat { Actor = "Abigail", To = new Position { Tile = new[] { 20, 12 } }, Path = "y-first" },   // 1: down 2
            new FaceBeat { Actor = "farmer", Facing = "right" },                                                     // 2
            new SayBeat { Actor = "Abigail", Text = "Hi." },                                                          // 3
            new ChoiceBeat { Options = { new ChoiceOption { Text = "Go", Then = "away" } } },                         // 4
        },
        Branches = { ["away"] = new List<Beat> { new MoveBeat { Actor = "farmer", To = new Position { Tile = new[] { 14, 2 } } } } },
        Clips =
        {
            ["hop"] = new Clip
            {
                Duration = 500,
                Tracks = { new ClipTrack { Actor = "Abigail", Position = new() { new PositionKey { At = 0, Tile = new[] { 0, 0 }, Relative = true }, new PositionKey { At = 500, Tile = new[] { -3, 0 }, Relative = true } },
                                                             Facing = new() { new FacingKey { At = 0, Facing = "left" } } } },
            },
        },
    };

    private static StagePlace At(Cutscene scene, string lane, int index, int actor) =>
        StageTimeline.Place(scene, scene.Actors[actor], StageTimeline.Played(scene, new BeatSelection(scene.Id!, lane, index)));

    [Fact]
    public void ActorsAreWhereTheBeatsSoFarPutThem()
    {
        var scene = Scene();
        var start = StageTimeline.Place(scene, scene.Actors[0], StageTimeline.Played(scene, null));
        Assert.Equal((10, 10, "up"), (start.TileX, start.TileY, start.Facing));
        Assert.Null(start.PlacedBy);

        // The walk itself: at the destination, facing along the last leg, with where it began and its corners.
        var walked = At(scene, LaneNames.Main, 0, 0);
        Assert.Equal((14, 7, "up"), (walked.TileX, walked.TileY, walked.Facing));
        Assert.Equal(0, walked.PlacedBy!.Index);
        Assert.Equal((10, 10), (walked.From!.TileX, walked.From.TileY));
        Assert.Equal(new[] { (10, 10), (14, 10), (14, 7) }, walked.Path);

        // Later beats: still there, no longer "walking"; Abigail's y-first walk faces down; a Face beat turns.
        var later = At(scene, LaneNames.Main, 3, 0);
        Assert.Equal((14, 7, "right"), (later.TileX, later.TileY, later.Facing));
        Assert.Null(later.From);
        Assert.Equal(2, later.TurnedBy!.Index);
        var abigail = At(scene, LaneNames.Main, 3, 1);
        Assert.Equal((20, 12, "down"), (abigail.TileX, abigail.TileY, abigail.Facing));
        Assert.Equal((20, 10, "left"), (At(scene, LaneNames.Main, 0, 1).TileX, At(scene, LaneNames.Main, 0, 1).TileY, At(scene, LaneNames.Main, 0, 1).Facing));

        // A branch starts where the main lane left everyone before the choice.
        var branch = At(scene, "away", 0, 0);
        Assert.Equal((14, 2), (branch.TileX, branch.TileY));
        Assert.Equal((14, 7, "right"), (branch.From!.TileX, branch.From.TileY, branch.From.Facing));
        Assert.Equal(4, StageTimeline.Played(scene, new BeatSelection(scene.Id!, "away", 0)).Count(p => p.Lane == LaneNames.Main));

        // A clip's last key: relative to where she stood when it began.
        scene.Beats.Insert(4, new ClipBeat { Clip = "hop" });
        var hopped = At(scene, LaneNames.Main, 4, 1);
        Assert.Equal((17, 12, "left"), (hopped.TileX, hopped.TileY, hopped.Facing));
        Assert.IsType<ClipBeat>(hopped.PlacedBy!.Beat);
    }

    [Fact]
    public void TheStageFollowsTheSelectedBeatAndEditsTheBeatThatPlacedThem()
    {
        using var editor = new EditorHarness();
        var doc = editor.NewProject();
        var shell = editor.Shell;
        var scene = shell.Scene!;
        doc.Edit("stage", () =>
        {
            scene.Actors.Clear();
            scene.Actors.AddRange(Scene().Actors);
            scene.Beats.AddRange(Scene().Beats.Take(3));
            scene.Beats.Add(new CameraBeat { Center = new Position { Tile = new[] { 30, 20 } }, Duration = 0 });
        });
        StageActorViewModel Farmer() => shell.Stage.Actors.First(a => a.Id == "farmer");
        Assert.False(shell.Stage.HasMoment);
        Assert.Equal((10, 10), (Farmer().TileX, Farmer().TileY));

        // Select the walk: she's at its end, with a faint copy and the path where it began.
        shell.Select(new BeatSelection(scene.Id!, LaneNames.Main, 0));
        Assert.True(shell.Stage.HasMoment);
        Assert.Contains("After beat 1", shell.Stage.MomentText);
        Assert.Equal((14, 7), (Farmer().TileX, Farmer().TileY));
        Assert.Single(shell.Stage.Ghosts);
        Assert.Equal((10, 10), (shell.Stage.Ghosts[0].TileX, shell.Stage.Ghosts[0].TileY));
        Assert.Equal(3, shell.Stage.Paths[0].Points.Count);
        Assert.Contains("walked here in beat 1", Farmer().PlaceNote);

        // Picking an actor keeps the moment (nobody jumps back to their start).
        shell.Stage.SelectActor(0);
        Assert.True(shell.Stage.HasMoment);
        Assert.Equal((14, 7), (Farmer().TileX, Farmer().TileY));

        // Dragging them changes where that walk ends, not where they start.
        shell.Stage.MoveActor(0, 16, 8, 0, 0);
        Assert.Equal(new[] { 16, 8 }, ((MoveBeat)scene.Beats[0]).To!.Tile);
        Assert.Equal(new[] { 10, 10 }, scene.Actors[0].Start!.Tile);
        Assert.Equal((16, 8), (Farmer().TileX, Farmer().TileY));

        // After the camera beat: the frame is there, and dragging it moves that beat's centre.
        shell.Select(new BeatSelection(scene.Id!, LaneNames.Main, 3));
        double centreX = shell.Stage.CameraRect.X + shell.Stage.CameraRect.Width / 2;
        Assert.InRange(centreX, 30 * 16, 31 * 16);
        shell.Stage.MoveCamera(32, 20);
        Assert.Equal(new[] { 32, 20 }, ((CameraBeat)scene.Beats[3]).Center!.Tile);
        Assert.Null(scene.Camera);

        // Turning: a Face beat turned them (beat 3), so that beat changes.
        shell.Stage.TurnActor(0);
        Assert.Equal("down", ((FaceBeat)scene.Beats[2]).Facing);
        Assert.Equal("up", scene.Actors[0].Facing);

        // Abigail's walk turned her, so turning her adds a Face beat after the one shown, and selects it.
        int beats = scene.Beats.Count;
        shell.Stage.TurnActor(1);
        Assert.Equal(beats + 1, scene.Beats.Count);
        var added = Assert.IsType<FaceBeat>(scene.Beats[4]);
        Assert.Equal(("Abigail", "left"), (added.Actor, added.Facing));
        Assert.Equal(new BeatSelection(scene.Id!, LaneNames.Main, 4), shell.Selection);

        // Right-click › "walks here": a new Move after the moment, from where she is now.
        shell.Stage.WalkHere(1, 25, 12);
        var walk = Assert.IsType<MoveBeat>(scene.Beats[5]);
        Assert.Equal(("Abigail", 25, 12), (walk.Actor, walk.To!.Tile![0], walk.To.Tile[1]));
        var abigail = shell.Stage.Actors.First(a => a.Id == "Abigail");
        Assert.Equal((25, 12, "right"), (abigail.TileX, abigail.TileY, abigail.Facing));
        Assert.Equal((20, 12), (shell.Stage.Ghosts[0].TileX, shell.Stage.Ghosts[0].TileY));

        // Back to the start: everyone at their start tiles; a drag there moves the start.
        shell.Stage.ShowStartCommand.Execute(null);
        Assert.False(shell.Stage.HasMoment);
        Assert.Equal((10, 10, "up"), (Farmer().TileX, Farmer().TileY, Farmer().Facing));
        shell.Stage.MoveActor(0, 11, 10, 0, 0);
        Assert.Equal(new[] { 11, 10 }, scene.Actors[0].Start!.Tile);
        Assert.Equal(new[] { 16, 8 }, ((MoveBeat)scene.Beats[0]).To!.Tile);
    }

    [Fact]
    public void TheMomentResetsForAnotherSceneOrARemovedBeat()
    {
        using var editor = new EditorHarness();
        var doc = editor.NewProject();
        var shell = editor.Shell;
        var scene = shell.Scene!;
        doc.Edit("stage", () => scene.Beats.Add(new MoveBeat { Actor = "farmer", To = new Position { Tile = new[] { 3, 3 } } }));
        shell.Select(new BeatSelection(scene.Id!, LaneNames.Main, 0));
        Assert.True(shell.Stage.HasMoment);
        shell.Select(new SceneSelection(scene.Id!));
        Assert.True(shell.Stage.HasMoment);   // clicking the empty stage keeps it
        doc.Edit("remove", () => scene.Beats.Clear());
        shell.Stage.Rebuild();
        Assert.False(shell.Stage.HasMoment);
        Assert.Empty(shell.Stage.Ghosts);
    }
}
