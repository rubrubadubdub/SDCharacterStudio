using SDCharacterStudio.Core.Audio;
using SDCharacterStudio.Core.Compiler;
using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Validation;
using SDCharacterStudio.Core.Tests.TestSupport;
using Xunit;

namespace SDCharacterStudio.Core.Tests;

public sealed class MusicTests
{
    [Fact]
    public void ImportedHeaderAndBeatMusicUseStableNativeCue()
    {
        var scene = Scene();
        scene.Music = "music:Theme";
        var result = new EventCompiler().Compile("Author.Test", scene);
        Assert.True(result.Success, string.Join("; ", result.Issues.Select(x => x.Message)));
        var cue = PackMusic.CueName("Author.Test", "Theme");
        Assert.StartsWith(cue + "/", result.Cutscene!.MainScript);
        scene.Music = "none"; scene.Beats.Add(new MusicBeat { Cue = "music:Theme" });
        var beatResult = new EventCompiler().Compile("Author.Test", scene);
        Assert.Contains("playMusic " + cue, beatResult.Cutscene!.MainScript.Split('/'));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("continue")]
    [InlineData("spring_day")]
    public void VanillaMusicRemainsUnchanged(string cue)
    {
        var scene = Scene(); scene.Music = cue;
        var result = new EventCompiler().Compile("Author.Test", scene);
        Assert.True(result.Success);
        Assert.StartsWith(cue + "/", result.Cutscene!.MainScript);
    }

    [Fact]
    public void CueIdsSeparatePreviewAndNormalizeReferenceCase()
    {
        Assert.NotEqual(PackMusic.CueName("Author.Test", "theme"), PackMusic.CueName("Author.Test.DevPreview", "theme"));
        Assert.Equal(PackMusic.CueName("Author.Test", "theme"), PackMusic.CueName("Author.Test", "THEME"));
        Assert.Equal("Author.Test", PackMusic.ResolveCue("Author.Test", "Author.Test"));
    }

    [Theory]
    [InlineData("assets/audio/theme.ogg", true)]
    [InlineData("../theme.ogg", false)]
    [InlineData("assets/audio/theme.mp3", false)]
    public void MusicDefinitionsValidatePathAndPresence(string path, bool valid)
    {
        var files = MemoryPackFileSystem.Minimal();
        if (valid) files.Files[path] = "audio";
        var pack = new LoadedPack { Manifest = new Manifest { ContentPackFor = new ContentPackTarget { UniqueID = "Player.SDCharacterStudio" } }, Index = new StudioIndex { Format = "1.0", Music = new() { ["theme"] = new MusicTrack { File = path } } } };
        var issues = PackValidator.ValidatePack(pack, files);
        Assert.Equal(valid, !issues.Any(x => x.Rule == "Audio"));
    }

    [Fact]
    public void UnknownImportedMusicReferenceIsRejected()
    {
        var pack = new LoadedPack { Manifest = new Manifest { UniqueID = "Author.Test", ContentPackFor = new ContentPackTarget { UniqueID = "Player.SDCharacterStudio" } }, Index = new StudioIndex { Format = "1.0" } };
        pack.Cutscenes.Add(new("cutscenes/scene.json", Scene("music:missing")));
        Assert.Contains(PackValidator.ValidatePack(pack), x => x.Rule == "Audio" && x.Message.Contains("Unknown imported music"));
    }

    [Fact]
    public void NullMusicDictionaryDoesNotAllowUnregisteredCue()
    {
        var files = MemoryPackFileSystem.Minimal();
        files.Files["studio.json"] = "{\"Format\":\"1.0\",\"Music\":null}";
        files.Files["cutscenes/scene.json"] = SDCharacterStudio.Core.Serialization.PackJson.Serialize(Scene("music:theme"));
        var result = new PackReader(files).Read();
        Assert.Contains(result.Issues, x => x.File == "studio.json" && x.JsonPath == "Music" && x.Severity == ValidationSeverity.Error);
        Assert.Contains(result.Issues, x => x.File == "cutscenes/scene.json" && x.Rule == "Audio");
    }

    [Theory]
    [InlineData("music:")]
    [InlineData("MUSIC:")]
    [InlineData("music: ")]
    public void EmptyImportedMusicReferenceIsRejectedInHeaderAndBranches(string reference)
    {
        var pack = new LoadedPack { Index = new StudioIndex { Format = "1.0" } };
        var scene = Scene(reference);
        scene.Branches["later"] = new() { new MusicBeat { Cue = reference } };
        pack.Cutscenes.Add(new("cutscenes/scene.json", scene));
        var issues = PackValidator.ValidatePack(pack);
        Assert.Contains(issues, x => x.Rule == "Audio" && x.JsonPath == "Music");
        Assert.Contains(issues, x => x.Rule == "Audio" && x.JsonPath == "Branches.later[0].Cue");
    }

    private static Cutscene Scene(string music = "none") => new() { Format = "1.0", Id = "scene", Location = "Town", Music = music, Actors = new() { new Actor { Id = "farmer", Kind = "Farmer", Start = new Position { Tile = new[] { 1, 1 } } } } };
}
