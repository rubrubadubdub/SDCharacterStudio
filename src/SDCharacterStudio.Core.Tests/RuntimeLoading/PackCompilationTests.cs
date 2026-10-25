using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Serialization;
using SDCharacterStudio.Core.Tests.TestSupport;
using SDCharacterStudio.Runtime;
using SDCharacterStudio.Runtime.Loading;
using Xunit;

namespace SDCharacterStudio.Core.Tests.RuntimeLoading;

public sealed class PackCompilationTests
{
    [Fact]
    public void BadSceneAndUnusedLibraryDoNotSuppressValidSibling()
    {
        var files = Files();
        files.Files["cutscenes/bad.json"] = """{"Format":"1.0","Id":"bad","Location":"Town","Beats":[{"Type":"Goto","Branch":"missing"}]}""";
        files.Files["library/poses/broken.json"] = """{"Format":"1.0","Id":"broken","FrameSize":[0,0],"Frames":[]}""";
        var result = Compile(files);
        Assert.Equal("Author.Pack.good", Assert.Single(result.Cutscenes).Compiled.Key);
        Assert.Contains(result.Skipped, x => x.CutsceneId == "bad");
    }

    [Fact]
    public void BrokenReferencedLibraryOnlyRejectsItsUsers()
    {
        var files = Files();
        files.Files["library/poses/broken.json"] = """{"Format":"1.0","Id":"broken","FrameSize":[0,0],"Frames":[]}""";
        files.Files["cutscenes/uses.json"] = """{"Format":"1.0","Id":"uses","Location":"Town","Actors":[{"Id":"Robin","Kind":"Npc","Start":{"Tile":[1,1]}}],"Beats":[{"Type":"Pose","Actor":"Robin","Pose":"broken"}]}""";
        var result = Compile(files);
        Assert.Equal("good", Assert.Single(result.Cutscenes).Source.Id);
        Assert.Contains(result.Skipped, x => x.CutsceneId == "uses" && x.Reason.Contains("broken", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SharedMissingVideoRejectsEveryUserButKeepsUnrelatedScene()
    {
        var files = Files();
        files.Files["cutscenes/video-a.json"] = """{"Format":"1.0","Id":"video-a","Location":"Town","Beats":[{"Type":"Video","Video":"missing"}]}""";
        files.Files["cutscenes/video-b.json"] = """{"Format":"1.0","Id":"video-b","Location":"Town","Beats":[{"Type":"Video","Video":"missing"}]}""";
        var result = Compile(files);
        Assert.Equal("good", Assert.Single(result.Cutscenes).Source.Id);
        Assert.Equal(2, result.Skipped.Count);
    }

    [Fact]
    public void MissingPackRequirementSkipsScenesWithNamedReason()
    {
        var files = Files();
        files.Files["studio.json"] = """{"Format":"1.0","Requires":[{"Mod":"Example.Missing","Reason":"Robin sprites"}]}""";
        var result = Compile(files, isModLoaded: _ => false);
        Assert.Empty(result.Cutscenes);
        Assert.Contains(result.Skipped, x => x.CutsceneId == "good" && x.Reason.Contains("Example.Missing"));
    }

    [Fact]
    public void MalformedCustomCutscenePathIsReportedAsSkipped()
    {
        var files = Files();
        files.Files["studio.json"] = """{"Format":"1.0","Cutscenes":["cutscenes/*.json","stories/bad.json"]}""";
        files.Files["stories/bad.json"] = "{ broken";
        var result = Compile(files);
        Assert.Single(result.Cutscenes);
        Assert.Contains(result.Skipped, x => x.CutsceneId == "stories/bad.json");
    }

    [Fact]
    public void BrokenMaskDoesNotSuppressSiblingMaskUsers()
    {
        var files = Files();
        files.Images["masks/good.png"] = new(16, 32);
        files.Files["library/masks.json"] = """{"Format":"1.0","Masks":[{"Id":"good-mask","FrameSize":[16,32],"Image":"masks/good.png"},{"Id":"bad-mask","FrameSize":[16,32],"Image":"masks/missing.png"}]}""";
        files.Files["library/characters/robin.json"] = """{"Format":"1.0","Id":"robin","Npc":"Robin","HeadMask":{"left":"good-mask"}}""";
        files.Files["library/poses/valid.json"] = """{"Format":"1.0","Id":"valid","For":"robin","FrameSize":[16,32],"Frames":[{"Duration":100,"Layers":[]}]}""";
        files.Files["cutscenes/uses.json"] = """{"Format":"1.0","Id":"uses","Location":"Town","Actors":[{"Id":"Robin","Kind":"Npc","Start":{"Tile":[1,1]}}],"Beats":[{"Type":"Pose","Actor":"Robin","Pose":"valid"}]}""";
        var result = Compile(files);
        Assert.Equal(2, result.Cutscenes.Count);
        Assert.Empty(result.Skipped);
    }

    [Fact]
    public void TranslationIsCompiledAndInvalidTranslationOnlyRejectsItsScene()
    {
        var files = Files();
        files.Files["cutscenes/good.json"] = """{"Format":"1.0","Id":"good","Location":"Town","Beats":[{"Type":"Message","Text":{"i18n":"hello"}}]}""";
        var result = Compile(files, _ => "Welcome home!");
        Assert.Contains("message \"Welcome home!\"", Assert.Single(result.Cutscenes).Compiled.MainScript);
        var invalid = Compile(files, _ => "bad/slash");
        Assert.Empty(invalid.Cutscenes);
        Assert.Single(invalid.Skipped);
    }

    [Fact]
    public void RegistryReplacesPackAndTracksBothOldAndNewLocations()
    {
        var registry = new CutsceneRegistry();
        var old = Assert.Single(Compile(Files()).Cutscenes);
        registry.ReplacePack("Author.Pack", new[] { old });
        Assert.Same(old, registry.Find("author.pack.GOOD"));
        Assert.Same(old, Assert.Single(registry.ForLocation("town")));
        var updated = old with { Compiled = old.Compiled with { Location = "Forest" } };
        var locations = registry.ReplacePack("AUTHOR.PACK", new[] { updated });
        Assert.Contains("Town", locations);
        Assert.Contains("Forest", locations);
        Assert.Empty(registry.ForLocation("Town"));
        Assert.Same(updated, Assert.Single(registry.ForPack("author.pack")));
        Assert.Equal("Forest", Assert.Single(registry.RemovePack("author.pack")));
        Assert.Empty(registry.All);
    }

    [Fact]
    public void RegistryRejectsCrossPackKeyCollisionWithoutRemovingPriorEntries()
    {
        var registry = new CutsceneRegistry();
        var original = Assert.Single(Compile(Files()).Cutscenes);
        var other = original with { PackId = "Other.Pack", Compiled = original.Compiled with { Key = "Other.Pack.scene", MainKey = "Other.Pack.scene" } };
        registry.ReplacePack(original.PackId, new[] { original });
        registry.ReplacePack(other.PackId, new[] { other });
        var collision = original with { Compiled = original.Compiled with { Key = other.Compiled.Key } };
        Assert.Throws<ArgumentException>(() => registry.ReplacePack(original.PackId, new[] { collision }));
        Assert.Same(original, registry.Find(original.Compiled.Key));
        Assert.Same(other, registry.Find(other.Compiled.Key));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MainSceneBranchCollisionSkipsOnlyConflictingScene(bool branchFirst)
    {
        var files = Files();
        files.Files[branchFirst ? "cutscenes/a-foo.json" : "cutscenes/b-foo.json"] = BranchScene;
        files.Files[branchFirst ? "cutscenes/b-foo-bar.json" : "cutscenes/a-foo-bar.json"] = """{"Format":"1.0","Id":"foo.bar","Location":"Town","Trigger":{},"Beats":[{"Type":"Wait","Duration":1}]}""";
        var result = Compile(files);
        Assert.Equal(branchFirst ? new[] { "foo", "good" } : new[] { "foo.bar", "good" }, result.Cutscenes.Select(x => x.Source.Id).ToArray());
        var skipped = Assert.Single(result.Skipped);
        Assert.Equal(branchFirst ? "foo.bar" : "foo", skipped.CutsceneId);
        Assert.Contains("Author.Pack.foo.bar", skipped.Reason);
        Assert.Contains("Author.Pack.foo", skipped.Reason);
    }

    [Fact]
    public void CrossPackBranchCollisionSkipsOnlyConflictingSceneAndAllowsReload()
    {
        var originalFiles = Files();
        originalFiles.Files["cutscenes/a-foo.json"] = BranchScene;
        var original = Compile(originalFiles).Cutscenes;
        var files = Files();
        files.Files["cutscenes/collision.json"] = """{"Format":"1.0","Id":"bar","Location":"Town","Trigger":{},"Beats":[{"Type":"Wait","Duration":1}]}""";
        var result = PackCompilation.Compile(files, "author.pack.FOO", "Other Pack", _ => null, _ => true, original);
        Assert.Equal("good", Assert.Single(result.Cutscenes).Source.Id);
        var skipped = Assert.Single(result.Skipped);
        Assert.Equal("bar", skipped.CutsceneId);
        Assert.Contains("author.pack.FOO.bar", skipped.Reason, StringComparison.OrdinalIgnoreCase);
        var reload = PackCompilation.Compile(originalFiles, "Author.Pack", "Example Pack", _ => null, _ => true, original);
        Assert.Equal(2, reload.Cutscenes.Count);
        Assert.Empty(reload.Skipped);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RegistryBranchCollisionRejectsReplacementAtomically(bool crossPack)
    {
        var files = Files();
        files.Files["cutscenes/a-foo.json"] = BranchScene;
        var entries = Compile(files).Cutscenes;
        var original = entries.Single(x => x.Source.Id == "good");
        var branchOwner = entries.Single(x => x.Source.Id == "foo");
        var registry = new CutsceneRegistry();
        registry.ReplacePack(original.PackId, entries);
        var collision = original with
        {
            PackId = crossPack ? "author.pack.FOO" : original.PackId,
            Compiled = original.Compiled with { Key = "author.pack.FOO.BAR", MainKey = "author.pack.FOO.BAR/G TRUE" }
        };
        var replacements = crossPack ? new[] { collision } : new[] { branchOwner, collision };
        var ex = Assert.Throws<ArgumentException>(() => registry.ReplacePack(collision.PackId, replacements));
        Assert.Contains("author.pack.FOO.BAR", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Author.Pack.foo", ex.Message);
        Assert.Same(original, registry.Find(original.Compiled.Key));
        Assert.Same(branchOwner, registry.Find(branchOwner.Compiled.Key));
        Assert.Equal(2, registry.All.Count);
    }

    private const string BranchScene = """{"Format":"1.0","Id":"foo","Location":"Town","Beats":[{"Type":"Goto","Branch":"bar"}],"Branches":{"bar":[{"Type":"End"}]}}""";

    private static MemoryPackFileSystem Files()
    {
        var files = MemoryPackFileSystem.Minimal();
        files.Files["cutscenes/good.json"] = """{"Format":"1.0","Id":"good","Location":"Town","Beats":[{"Type":"Wait","Duration":1}]}""";
        return files;
    }

    private static PackCompilationResult Compile(IPackFileSystem files, Func<string,string?>? translate = null, Func<string,bool>? isModLoaded = null)
        => PackCompilation.Compile(files, "Author.Pack", "Example Pack", translate ?? (_ => null), isModLoaded ?? (_ => true));
}
