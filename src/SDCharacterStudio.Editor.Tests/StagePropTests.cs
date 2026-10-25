using System.IO;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Schema;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>
/// D-50 (the owner dropped a Moon Table on the stage: "I place it and it says item, but there's no visual
/// representation"): an item dropped on the stage stands there from its beat on, until a beat takes it away.
/// </summary>
public sealed class StagePropTests
{
    [Fact]
    public void ADroppedItemStandsOnTheMapFromItsBeatOn()
    {
        using var editor = new EditorHarness();
        var doc = editor.NewProject();
        var shell = editor.Shell;
        var scene = shell.Scene!;
        doc.Edit("stage", () => scene.Beats.Add(new WaitBeat { Duration = 100 }));
        shell.Select(new BeatSelection(scene.Id!, LaneNames.Main, 0));

        // Dropped after the beat shown, its picture's bottom-left on the tile, and selected.
        shell.Stage.PlaceItem("(O)709", 12, 9);
        var placed = Assert.IsType<PropBeat>(scene.Beats[1]);
        Assert.Equal(("(O)709", 12, 9), (placed.Item, placed.At!.Tile![0], placed.At.Tile[1]));
        Assert.Equal(new BeatSelection(scene.Id!, LaneNames.Main, 1), shell.Selection);
        var prop = Assert.Single(shell.Stage.Props);
        // The export's picture of it (the small test export has no icon files: the stage shows a token instead).
        Assert.Equal(File.Exists(shell.Catalog!.FullPath(shell.Catalog.Item("(O)709")!.Icon!)), prop.HasImage);
        Assert.Equal("Hardwood", prop.Label);
        Assert.True(prop.IsSelected);
        Assert.Equal((16.0, 16.0, 0.0), (prop.Width, prop.Height, prop.ImageTop));
        Assert.Equal("Hardwood at (12, 9)", BeatCatalog.Summary(placed, shell.Catalog));
        Assert.Equal(0, shell.ValidateNow().Errors);

        // Before its beat it isn't there; after it, it stays.
        shell.Select(new BeatSelection(scene.Id!, LaneNames.Main, 0));
        Assert.Empty(shell.Stage.Props);
        doc.Edit("more", () => scene.Beats.Add(new WaitBeat { Duration = 100 }));
        shell.Select(new BeatSelection(scene.Id!, LaneNames.Main, 2));
        prop = Assert.Single(shell.Stage.Props);
        Assert.False(prop.IsSelected);

        // Dragging moves its beat's tile; clicking it selects that beat.
        shell.Stage.MoveProp(prop, 13, 9);
        Assert.Equal(new[] { 13, 9 }, placed.At!.Tile);
        shell.Stage.SelectProp(shell.Stage.Props[0]);
        Assert.Equal(new BeatSelection(scene.Id!, LaneNames.Main, 1), shell.Selection);

        // Taking it away is a beat too: after it, the map is clear again.
        shell.Select(new BeatSelection(scene.Id!, LaneNames.Main, 2));
        shell.Stage.TakeAway(shell.Stage.Props[0]);
        var removed = Assert.IsType<PropBeat>(scene.Beats[3]);
        Assert.True(removed.Remove);
        Assert.Equal(new[] { 13, 9 }, removed.At!.Tile);
        Assert.Empty(shell.Stage.Props);
        Assert.Equal("take away Hardwood at (13, 9)", BeatCatalog.Summary(removed, shell.Catalog));
    }

    [Fact]
    public void GivingAndPlacingAreTwoBeats()
    {
        Assert.Equal("Give an item", BeatCatalog.Kind("Item").Name);
        Assert.Equal("Place an item", BeatCatalog.Kind("Prop").Name);
        Assert.Contains("inventory", BeatCatalog.Kind("Item").Help);

        using var editor = new EditorHarness();
        editor.NewProject();
        // An item the export doesn't know still gets a one-tile spot to drop on.
        Assert.Equal((16, 16), editor.Shell.Stage.ItemSize("(F)nothing"));
    }
}
