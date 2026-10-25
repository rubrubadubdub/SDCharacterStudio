using SDCharacterStudio.Core.Compiler;
using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Serialization;
using SDCharacterStudio.Core.Tests.TestSupport;
using SDCharacterStudio.Core.Validation;
using SDCharacterStudio.Runtime.Loading;
using Xunit;

namespace SDCharacterStudio.Core.Tests.Compiler;

/// <summary>Library clips (doc 03 §17.1, D-39): reusable choreography cast onto scene actors.</summary>
public sealed class LibraryClipTests
{
    private const string BowClip = """
    {"Format":"1.0","Id":"step-and-bow","Title":"Step and bow","Tags":["greeting"],"Duration":3200,"StepFps":12,
     "Roles":[{"Id":"performer","Kind":"Npc"},{"Id":"watcher"}],
     "Tracks":[
       {"Role":"performer",
        "Position":[{"At":0,"Tile":[0,0]},{"At":600,"Tile":[-1,0],"Px":[3,0],"Ease":"out"}],
        "Facing":[{"At":0,"Facing":"left"}],
        "Pose":[{"At":600,"Pose":"bow","Play":"once-then-loop"}],
        "Sound":[{"At":600,"Cue":"thudStep"}]},
       {"Role":"watcher","Emote":[{"At":3000,"Emote":"exclamation"}]}]}
    """;

    private static MemoryPackFileSystem Files(string beats, string actors = """[{"Id":"farmer","Kind":"Farmer","Start":{"Tile":[5,5]}},{"Id":"Abigail","Kind":"Npc","Start":{"Tile":[8,5]}}]""")
    {
        var files = MemoryPackFileSystem.Minimal();
        files.Files["library/clips/step-and-bow.json"] = BowClip;
        files.Files["library/poses/bow.json"] = """{"Format":"1.0","Id":"bow","FrameSize":[16,32],"Frames":[{"Duration":100,"Layers":[]}]}""";
        files.Files["library/poses/abigail-bow.json"] = """{"Format":"1.0","Id":"abigail-bow","FrameSize":[16,32],"Frames":[{"Duration":100,"Layers":[]}]}""";
        files.Files["cutscenes/scene.json"] = $$"""{"Format":"1.0","Id":"scene","Location":"Town","Actors":{{actors}},"Beats":{{beats}}}""";
        return files;
    }

    [Fact]
    public void ReaderLoadsLibraryClipsByDefaultGlob()
    {
        var read = new PackReader(Files("[]")).Read(exportValidation: false);
        var clip = Assert.Single(read.Pack.Clips).Value;
        Assert.Equal("step-and-bow", clip.Id);
        Assert.Equal(new[] { "performer", "watcher" }, clip.Roles.Select(r => r.Id));
        Assert.Equal("Any", clip.Roles[1].Kind);
        Assert.Equal("performer", clip.Tracks[0].Role);
        Assert.DoesNotContain(read.Issues, i => i.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void ExpandCastsMirrorsAndSwapsPoses()
    {
        var read = new PackReader(Files("""[{"Type":"Clip","Clip":"step-and-bow","Cast":{"performer":"Abigail","watcher":"farmer"},"Mirror":true,"PoseMap":{"bow":"abigail-bow"},"Hold":true}]""")).Read(exportValidation: false);
        var scene = read.Pack.Cutscenes.Single().Value;
        var expanded = LibraryClips.Expand(scene, read.Pack);
        Assert.NotSame(scene, expanded);
        Assert.Empty(scene.Clips);                                    // the authored scene is untouched
        var beat = Assert.IsType<ClipBeat>(expanded.Beats.Single());
        Assert.Equal("lib.step-and-bow.1", beat.Clip);
        Assert.True(beat.Hold);
        Assert.Null(beat.Cast); Assert.Null(beat.PoseMap); Assert.False(beat.Mirror);
        var clip = expanded.Clips["lib.step-and-bow.1"];
        Assert.Equal(3200, clip.Duration); Assert.Equal(12, clip.StepFps);
        var performer = clip.Tracks.Single(t => t.Actor == "Abigail");
        Assert.All(performer.Position!, k => Assert.True(k.Relative));
        Assert.Equal(new[] { 1, 0 }, performer.Position![1].Tile);    // mirrored
        Assert.Equal(new[] { -3, 0 }, performer.Position[1].Px);
        Assert.Equal("out", performer.Position[1].Ease);
        Assert.Equal("right", performer.Facing![0].Facing);
        Assert.Equal("abigail-bow", performer.Pose![0].Pose);
        Assert.Null(performer.Role);
        Assert.Equal("farmer", clip.Tracks[1].Actor);
    }

    [Fact]
    public void EachUseGetsItsOwnGeneratedClipAndSceneClipsWin()
    {
        var read = new PackReader(Files("""
            [{"Type":"Clip","Clip":"step-and-bow","Cast":{"performer":"Abigail","watcher":"farmer"}},
             {"Type":"Clip","Clip":"step-and-bow","Cast":{"performer":"Abigail","watcher":"farmer"},"Mirror":true}]
            """)).Read(exportValidation: false);
        var scene = read.Pack.Cutscenes.Single().Value;
        var expanded = LibraryClips.Expand(scene, read.Pack);
        Assert.Equal(new[] { "lib.step-and-bow.1", "lib.step-and-bow.2" }, expanded.Beats.Cast<ClipBeat>().Select(b => b.Clip));
        Assert.Equal(2, expanded.Clips.Count);

        scene.Clips["step-and-bow"] = new Clip { Duration = 10 };   // a scene clip of the same name wins
        Assert.Same(scene, LibraryClips.Expand(scene, read.Pack));
    }

    /// <summary>Found by the native M8 run: the copy turned a farmer's absent costume into a JSON-null token, which the
    /// runtime applied as an initial costume and stopped the cutscene.</summary>
    [Fact]
    public void ExpandedCopiesKeepAbsentValuesAbsent()
    {
        var read = new PackReader(Files("""[{"Type":"Clip","Clip":"step-and-bow","Cast":{"performer":"Abigail","watcher":"farmer"}},{"Type":"Say","Actor":"Abigail","Text":"Hi"}]""")).Read(exportValidation: false);
        var expanded = LibraryClips.Expand(read.Pack.Cutscenes.Single().Value, read.Pack);
        Assert.All(expanded.Actors, a => Assert.Null(a.Costume));
        Assert.Null(expanded.Beats.OfType<SayBeat>().Single().Emotion);
        Assert.Null(expanded.Camera);
    }

    [Fact]
    public void ScenesWithoutLibraryClipsAreReturnedAsIs()
    {
        var read = new PackReader(Files("""[{"Type":"Wait","Duration":5}]""")).Read(exportValidation: false);
        var scene = read.Pack.Cutscenes.Single().Value;
        Assert.Same(scene, LibraryClips.Expand(scene, read.Pack));
        Assert.Same(scene, LibraryClips.Expand(scene, null));
    }

    [Fact]
    public void ValidationChecksCastKindsAndPoseSwaps()
    {
        var read = new PackReader(Files("""
            [{"Type":"Clip","Clip":"step-and-bow","Cast":{"performer":"farmer","extra":"Abigail"},"PoseMap":{"bow":"missing-pose"}}]
            """)).Read(exportValidation: false);
        var messages = read.Issues.Where(i => i.File == "cutscenes/scene.json").Select(i => (i.Severity, i.JsonPath, i.Message)).ToArray();
        Assert.Contains(messages, m => m.Severity == ValidationSeverity.Error && m.JsonPath == "Beats[0].Cast.performer" && m.Message.Contains("needs a Npc actor"));
        Assert.Contains(messages, m => m.Severity == ValidationSeverity.Error && m.JsonPath == "Beats[0].Cast.watcher" && m.Message.Contains("isn't cast"));
        Assert.Contains(messages, m => m.Severity == ValidationSeverity.Warning && m.JsonPath == "Beats[0].Cast.extra");
        Assert.Contains(messages, m => m.Severity == ValidationSeverity.Error && m.JsonPath == "Beats[0].PoseMap.bow");
        Assert.DoesNotContain(read.Issues, i => i.Message.Contains("Unknown clip"));
    }

    [Fact]
    public void ValidationChecksLibraryClipFiles()
    {
        var files = Files("[]");
        files.Files["library/clips/bad.json"] = """
            {"Format":"1.0","Id":"bad","Duration":100,"Roles":[{"Id":"a","Kind":"Ghost"},{"Id":"a"}],
             "Tracks":[{"Role":"b","Position":[{"At":50,"Tile":[0,0]},{"At":10,"Tile":[1,0]}]},{"Actor":"Robin"},{"Role":"a","Camera":[{"At":0}]}]}
            """;
        var issues = new PackReader(files).Read(exportValidation: false).Issues.Where(i => i.File == "library/clips/bad.json").ToArray();
        Assert.Contains(issues, i => i.JsonPath == "Roles[0].Kind");
        Assert.Contains(issues, i => i.JsonPath == "Roles[1].Id" && i.Rule == "V3");
        Assert.Contains(issues, i => i.JsonPath == "Tracks[0].Role" && i.Message.Contains("Unknown role"));
        Assert.Contains(issues, i => i.JsonPath == "Tracks[0].Position" && i.Rule == "V10");
        Assert.Contains(issues, i => i.JsonPath == "Tracks[1].Actor");
        Assert.Contains(issues, i => i.JsonPath == "Tracks[2].Camera");
        Assert.DoesNotContain(new PackReader(Files("[]")).Read(exportValidation: false).Issues, i => i.File == "library/clips/step-and-bow.json");
    }

    [Fact]
    public void UnknownClipsAreStillErrors()
    {
        var read = new PackReader(Files("""[{"Type":"Clip","Clip":"nowhere"}]""")).Read(exportValidation: false);
        Assert.Contains(read.Issues, i => i.Message == "Unknown clip 'nowhere'.");
    }

    [Fact]
    public void RuntimeCompilesLibraryClipUsesIntoSceneClips()
    {
        var files = Files("""[{"Type":"Clip","Clip":"step-and-bow","Cast":{"performer":"Abigail","watcher":"farmer"},"Hold":true}]""");
        var result = PackCompilation.Compile(files, "Author.Pack", "Example", _ => null, _ => true);
        var scene = Assert.Single(result.Cutscenes);
        Assert.Contains("SDCS_Clip Author.Pack.scene lib.step-and-bow.1 hold", scene.Compiled.MainScript);
        Assert.True(scene.Source.Clips.ContainsKey("lib.step-and-bow.1"));   // the clip player reads Source.Clips
        Assert.Equal("Abigail", scene.Source.Clips["lib.step-and-bow.1"].Tracks[0].Actor);
    }

    [Fact]
    public void BrokenLibraryClipSkipsOnlyItsUsers()
    {
        var files = Files("""[{"Type":"Clip","Clip":"broken","Cast":{"a":"Abigail"}}]""");
        files.Files["library/clips/broken.json"] = """{"Format":"1.0","Id":"broken","Duration":10,"Roles":[{"Id":"a"}],"Tracks":[{"Role":"a","Pose":[{"At":0,"Pose":"no-such-pose"}]}]}""";
        files.Files["cutscenes/other.json"] = """{"Format":"1.0","Id":"other","Location":"Town","Beats":[{"Type":"Wait","Duration":1}]}""";
        var result = PackCompilation.Compile(files, "Author.Pack", "Example", _ => null, _ => true);
        Assert.Equal("other", Assert.Single(result.Cutscenes).Source.Id);
        Assert.Contains(result.Skipped, s => s.CutsceneId == "scene" && s.Reason.Contains("broken"));
    }

    [Fact]
    public void FromSceneClipMakesRolesAndRelativePositions()
    {
        var clip = new Clip
        {
            Duration = 1000,
            Tracks =
            {
                new ClipTrack { Actor = "Robin", Position = new() { new PositionKey { At = 0, Tile = new[] { 17, 6 } }, new PositionKey { At = 500, Tile = new[] { 15, 7 } } } },
                new ClipTrack { Actor = "farmer", Facing = new() { new FacingKey { At = 0, Facing = "up" } } },
                new ClipTrack { Camera = new() { new CameraKey { At = 0 } } }
            }
        };
        var library = LibraryClips.FromSceneClip("walk-away", clip, new Dictionary<string, string> { ["Robin"] = "Npc", ["farmer"] = "Farmer" });
        Assert.Equal(new[] { "robin", "farmer" }, library.Roles.Select(r => r.Id));
        Assert.Equal(new[] { "Npc", "Farmer" }, library.Roles.Select(r => r.Kind));
        Assert.Equal(2, library.Tracks.Count);
        Assert.Equal(new[] { 0, 0 }, library.Tracks[0].Position![0].Tile);
        Assert.Equal(new[] { -2, 1 }, library.Tracks[0].Position![1].Tile);
        Assert.All(library.Tracks[0].Position!, k => Assert.True(k.Relative));
        // And back again, cast on the same actors, positions stay relative to wherever Robin stands.
        var scene = LibraryClips.ToSceneClip(library, new Dictionary<string, string> { ["robin"] = "Robin", ["farmer"] = "farmer" }, false, null);
        Assert.Equal("Robin", scene.Tracks[0].Actor);
        Assert.Equal(new[] { -2, 1 }, scene.Tracks[0].Position![1].Tile);
    }

    [Fact]
    public void ClipBeatCastRoundTripsThroughJson()
    {
        var beat = new ClipBeat { Clip = "x", Cast = new() { ["a"] = "Robin" }, Mirror = true, PoseMap = new() { ["p"] = "q" } };
        var json = PackJson.Serialize(beat);
        var back = Assert.IsType<ClipBeat>(PackJson.Deserialize<Beat>(json));
        Assert.Equal("Robin", back.Cast!["a"]); Assert.True(back.Mirror); Assert.Equal("q", back.PoseMap!["p"]);
    }
}
