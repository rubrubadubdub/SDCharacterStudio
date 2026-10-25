using System;
using System.IO;
using SDCharacterStudio.Editor.Project;
using SDCharacterStudio.Editor.Export;

namespace SDCharacterStudio.Editor.Tests;

public sealed class MediaOverlayTests
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    public void ImportReplacementUndoRestoresOriginalFrames()
    {
        using var h = new EditorHarness();
        var doc = h.NewProject();
        var source = Path.Combine(h.Folder, "source"); Directory.CreateDirectory(source);
        var first = Path.Combine(source, "a.png"); var second = Path.Combine(source, "b.png");
        File.WriteAllBytes(first, Png); File.WriteAllBytes(second, Png.Concat(new byte[] { 1 }).ToArray());
        VideoImporter.Import(doc, new[] { first }, "clip", 12);
        var original = doc.ReadMediaBytes("videos/clip/frame-001.png");
        VideoImporter.Import(doc, new[] { second }, "clip", 24);
        Assert.True(doc.Undo());
        Assert.Equal(original, doc.ReadMediaBytes("videos/clip/frame-001.png"));
        Assert.Equal(12, doc.Videos.Single().Settings.Fps);
    }

    [Fact]
    public void AutosaveRecoveryRetainsImportedBytesBeforeSave()
    {
        using var h = new EditorHarness();
        var doc = h.NewProject();
        var source = Path.Combine(h.Folder, "a.png"); File.WriteAllBytes(source, Png);
        VideoImporter.Import(doc, new[] { source }, "clip", 12);
        Autosave.Write(doc);
        var recovery = Autosave.FindRecovery(doc.ProjectPath);
        Assert.NotNull(recovery);
        var reopened = PackDocument.Open(doc.ProjectPath);
        reopened.RestoreSnapshot(recovery!.Value.State, "Recover");
        Assert.Equal(Png, reopened.ReadMediaBytes("videos/clip/frame-001.png"));
    }

    [Fact]
    public void ShorterReplacementSaveDeletesStaleFrames()
    {
        using var h = new EditorHarness(); var doc = h.NewProject();
        var source = Path.Combine(h.Folder, "frames"); Directory.CreateDirectory(source);
        var a = Path.Combine(source, "a.png"); var b = Path.Combine(source, "b.png");
        File.WriteAllBytes(a, Png); File.WriteAllBytes(b, Png.Concat(new byte[] { 7 }).ToArray());
        VideoImporter.Import(doc, new[] { a, b }, "clip", 12); doc.Save();
        VideoImporter.Import(doc, new[] { a }, "clip", 12); doc.Save();
        Assert.False(File.Exists(Path.Combine(doc.PackFolder, "videos", "clip", "frame-002.png")));
        Assert.Throws<FileNotFoundException>(() => doc.ReadMediaBytes("videos/clip/frame-002.png"));
    }

    [Fact]
    public void ReplacementUndoRedoAfterSaveAndReopenKeepsBytes()
    {
        using var h = new EditorHarness(); var doc = h.NewProject();
        var source = Path.Combine(h.Folder, "frames"); Directory.CreateDirectory(source);
        var oldFile = Path.Combine(source, "old.png"); var newFile = Path.Combine(source, "new.png");
        var oldBytes = Png; var newBytes = Png.Concat(new byte[] { 8 }).ToArray();
        File.WriteAllBytes(oldFile, oldBytes); File.WriteAllBytes(newFile, newBytes);
        VideoImporter.Import(doc, new[] { oldFile }, "clip", 12); doc.Save();
        VideoImporter.Import(doc, new[] { newFile }, "clip", 12); Assert.True(doc.Undo());
        doc.Save(); var reopened = PackDocument.Open(doc.ProjectPath);
        Assert.Equal(oldBytes, reopened.ReadMediaBytes("videos/clip/frame-001.png"));
        VideoImporter.Import(reopened, new[] { newFile }, "clip", 12); reopened.Save();
        Assert.Equal(newBytes, reopened.ReadMediaBytes("videos/clip/frame-001.png"));
        Assert.True(reopened.Undo()); Assert.Equal(oldBytes, reopened.ReadMediaBytes("videos/clip/frame-001.png"));
        Assert.True(reopened.Redo()); Assert.Equal(newBytes, reopened.ReadMediaBytes("videos/clip/frame-001.png"));
    }

    [Fact]
    public void FailedImportLeavesCaptureStateAndBytesUntouched()
    {
        using var h = new EditorHarness(); var doc = h.NewProject();
        var source = Path.Combine(h.Folder, "frames"); Directory.CreateDirectory(source);
        var good = Path.Combine(source, "good.png"); var bad = Path.Combine(source, "bad.png");
        File.WriteAllBytes(good, Png); File.WriteAllText(bad, "not png");
        VideoImporter.Import(doc, new[] { good }, "clip", 12); var state = doc.CaptureState(); var bytes = doc.ReadMediaBytes("videos/clip/frame-001.png");
        Assert.Throws<ArgumentException>(() => VideoImporter.Import(doc, new[] { bad }, "clip", 12));
        Assert.Equal(state, doc.CaptureState()); Assert.Equal(bytes, doc.ReadMediaBytes("videos/clip/frame-001.png"));
    }

    [Fact]
    public void AssetEditsUndoRedoRestoreExistingDiskAsset()
    {
        using var h = new EditorHarness(); var doc = h.NewProject();
        var path = Path.Combine(doc.PackFolder, "assets", "x.bin"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var original = new byte[] { 1, 2 }; File.WriteAllBytes(path, original);
        var replacement = new byte[] { 3, 4 };
        doc.Edit("replace", () => doc.SetBinaryAsset("assets/x.bin", replacement));
        doc.Edit("remove", () => doc.RemoveAsset("assets/x.bin"));
        Assert.Throws<FileNotFoundException>(() => doc.ReadBinaryAsset("assets/x.bin"));
        Assert.True(doc.Undo()); Assert.Equal(replacement, doc.ReadBinaryAsset("assets/x.bin"));
        Assert.True(doc.Undo()); Assert.Equal(original, doc.ReadBinaryAsset("assets/x.bin"));
        Assert.True(doc.Redo()); Assert.Equal(replacement, doc.ReadBinaryAsset("assets/x.bin"));
        Assert.True(doc.Redo()); Assert.Throws<FileNotFoundException>(() => doc.ReadBinaryAsset("assets/x.bin"));

        doc.Edit("text", () => doc.SetTextAsset("library/x.json", "one"));
        doc.Edit("text replace", () => doc.SetTextAsset("library/x.json", "two"));
        Assert.Equal("two", doc.CreateOverlayFileSystem().ReadText("library/x.json"));
        Assert.True(doc.Undo()); Assert.Equal("one", doc.CreateOverlayFileSystem().ReadText("library/x.json"));
        Assert.True(doc.Redo()); Assert.Equal("two", doc.CreateOverlayFileSystem().ReadText("library/x.json"));
    }

    [Fact]
    public void AssetPathAndBlobReferencesRejectTraversalAndMalformedHash()
    {
        using var h = new EditorHarness(); var doc = h.NewProject();
        Assert.Throws<ArgumentException>(() => doc.SetBinaryAsset("../outside", new byte[] { 1 }));
        Assert.Throws<ArgumentException>(() => doc.SetTextAsset(Path.GetFullPath("outside"), "x"));
        var state = doc.CaptureState().Replace("\"Media\":{}", "\"Media\":{\"videos/x.png\":\"bad\"}");
        string before = doc.CaptureState();
        Assert.Throws<InvalidDataException>(() => doc.ApplyState(state));
        Assert.Equal(before, doc.CaptureState());
    }

    [Fact]
    public void OverlaySnapshotStaysImmutableAfterDocumentMutation()
    {
        using var h = new EditorHarness(); var doc = h.NewProject();
        doc.SetTextAsset("library/overlay.json", "{\"v\":1}");
        var snapshot = ValidationInput.From(doc, null);
        doc.SetTextAsset("library/overlay.json", "{\"v\":2}");
        Assert.Equal("{\"v\":1}", snapshot.TextAssets!["library/overlay.json"]);
    }

    [Fact]
    public void ReloadLibraryReadsUnsavedOverlayAndTombstone()
    {
        using var h = new EditorHarness(); var doc = h.NewProject();
        doc.SetTextAsset("library/overlay.json", "{}");
        doc.ReloadLibrary();
        Assert.True(doc.CreateOverlayFileSystem().Exists("library/overlay.json"));
        doc.RemoveAsset("library/overlay.json"); doc.ReloadLibrary();
        Assert.False(doc.CreateOverlayFileSystem().Exists("library/overlay.json"));
    }

    [Fact]
    public void UndoAfterSavingNewMediaAndAssetsHidesThemAndRedoRestoresThem()
    {
        using var h = new EditorHarness(); var doc = h.NewProject();
        var frame = Path.Combine(h.Folder, "frame.png"); File.WriteAllBytes(frame, Png);
        VideoImporter.Import(doc, new[] { frame }, "clip", 12); doc.Save();
        Assert.True(doc.Undo());
        Assert.False(doc.CreateOverlayFileSystem().Exists("videos/clip/frame-001.png"));
        doc.Save(); Assert.False(File.Exists(Path.Combine(doc.PackFolder, "videos/clip/frame-001.png")));
        Assert.True(doc.Redo()); Assert.Equal(Png, doc.ReadMediaBytes("videos/clip/frame-001.png"));
        doc.Edit("new asset", () => doc.SetBinaryAsset("assets/test.png", Png)); doc.Save();
        Assert.True(doc.Undo());
        Assert.Throws<FileNotFoundException>(() => doc.ReadBinaryAsset("assets/test.png"));
        Assert.False(doc.CreateOverlayFileSystem().Exists("assets/test.png"));
        doc.Save(); Assert.False(File.Exists(Path.Combine(doc.PackFolder, "assets/test.png")));
        Assert.True(doc.Redo()); Assert.Equal(Png, doc.ReadBinaryAsset("assets/test.png"));
    }

    [Fact]
    public void VideoSettingsEditsAndUnsavedAssetsReachPreviewAndExport()
    {
        using var h = new EditorHarness(); var doc = h.NewProject();
        var frame = Path.Combine(h.Folder, "frame.png"); File.WriteAllBytes(frame, Png);
        VideoImporter.Import(doc, new[] { frame }, "clip", 12);
        doc.Edit("fps", () => doc.Videos.Single().Settings.Fps = 24);
        doc.Edit("PNG", () => doc.SetBinaryAsset("assets/new.png", Png));
        var preview = h.Shell.Preview.PreparePreviewPack(doc, h.Shell.Scene!.Id!);
        Assert.Equal(Png, File.ReadAllBytes(Path.Combine(preview.Folder, "assets/new.png")));
        Assert.Equal(24, (double)Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(preview.Folder, "videos/clip/video.json")))["Fps"]!);
        doc.Save();
        var reopened = PackDocument.Open(doc.ProjectPath);
        Assert.Equal(24, reopened.Videos.Single().Settings.Fps);
        Assert.Equal(Png, reopened.ReadBinaryAsset("assets/new.png"));
        doc.Edit("remove", () => doc.RemoveAsset("assets/new.png"));
        preview = h.Shell.Preview.PreparePreviewPack(doc, h.Shell.Scene!.Id!);
        Assert.False(File.Exists(Path.Combine(preview.Folder, "assets/new.png")));
    }

    [Fact]
    public void RecoveryRejectsEscapingAssetPathsBeforeChangingTheDocument()
    {
        using var h = new EditorHarness(); var doc = h.NewProject();
        string before = doc.CaptureState();
        var state = Newtonsoft.Json.Linq.JObject.Parse(before);
        state["TextAssets"] = new Newtonsoft.Json.Linq.JObject { ["assets/../../escaped.json"] = "bad" };
        Assert.Throws<ArgumentException>(() => doc.ApplyState(state.ToString()));
        Assert.Equal(before, doc.CaptureState());
    }

    [Fact]
    public void LibraryDefinitionsFollowAssetHistoryWithoutAnExtraReloadCommand()
    {
        using var h = new EditorHarness(); var doc = h.NewProject();
        var pose = new SDCharacterStudio.Core.Models.Pose { Id = "author-pose" };
        doc.Edit("add pose", () => doc.SetTextAsset("library/poses/author-pose.json", PackWriter.Serialize(pose)));
        Assert.Contains(doc.Library.Poses, p => p.Value.Id == "author-pose");
        doc.Edit("remove pose", () => doc.RemoveAsset("library/poses/author-pose.json"));
        Assert.DoesNotContain(doc.Library.Poses, p => p.Value.Id == "author-pose");
        Assert.True(doc.Undo()); Assert.Contains(doc.Library.Poses, p => p.Value.Id == "author-pose");
        Assert.True(doc.Undo()); Assert.DoesNotContain(doc.Library.Poses, p => p.Value.Id == "author-pose");
        Assert.True(doc.Redo()); Assert.Contains(doc.Library.Poses, p => p.Value.Id == "author-pose");
    }
}
