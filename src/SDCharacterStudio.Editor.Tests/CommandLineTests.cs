using System.IO;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.Export;
using SDCharacterStudio.Editor.Infrastructure;

namespace SDCharacterStudio.Editor.Tests;

/// <summary>sdcs check / sdcs export: the editor's checks and exports without its window, with exit codes for scripts.</summary>
public sealed class CommandLineTests
{
    private static (int Code, string Output, string Error) Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int code = CommandLine.Run(args, output, error, new EditorSettings { ExportFolder = Path.GetDirectoryName(TestEnvironment.MiniExport)!, ExportProfile = TestEnvironment.MiniExport });
        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public void CheckPrintsProblemsAndExitsByErrors()
    {
        using var editor = new EditorHarness();
        var doc = editor.NewProject("Lab", "Player.Lab", "ScienceHouse");
        doc.Save();

        var clean = Run("check", doc.ProjectPath);
        Assert.Equal(CommandLine.Ok, clean.Code);
        Assert.Contains("Game export: ", clean.Output);
        Assert.Contains("0 errors", clean.Output);

        // A walk off the map is a warning (still 0); a walk by nobody is an error (1), printed with where it is.
        doc.Edit("break", () =>
        {
            doc.Cutscenes[0].Scene.Beats.Add(new MoveBeat { Actor = "farmer", To = new Position { Tile = new[] { 99, 99 } } });
            doc.Cutscenes[0].Scene.Beats.Add(new MoveBeat { Actor = "Nobody", To = new Position { Tile = new[] { 1, 1 } } });
        });
        doc.Save();
        var broken = Run("check", doc.ProjectPath);
        Assert.Equal(CommandLine.HasErrors, broken.Code);
        Assert.Contains("warning V21 cutscenes/first-scene.json › Beats[0].To.Tile: The player walks to 99,99, which is off the ScienceHouse map (32×25).", broken.Output);
        Assert.Contains("error Reference cutscenes/first-scene.json › Beats[1].Actor: Unknown actor 'Nobody'.", broken.Output);

        // Export refuses while there are errors.
        string to = Path.Combine(editor.Folder, "out");
        var refused = Run("export", doc.ProjectPath, "--as", "csharp", "--to", to);
        Assert.Equal(CommandLine.HasErrors, refused.Code);
        Assert.False(Directory.Exists(to));
    }

    [Fact]
    public void ExportWritesWhatTheEditorWould()
    {
        using var editor = new EditorHarness();
        var doc = editor.NewProject("Lab", "Player.Lab", "ScienceHouse");
        doc.Save();
        string to = Path.Combine(editor.Folder, "for my mod");
        var csharp = Run("export", doc.ProjectPath, "--as", "csharp", "--to", to);
        Assert.Equal(CommandLine.Ok, csharp.Code);
        Assert.True(File.Exists(Path.Combine(to, "Sdcs.cs")));
        Assert.True(File.Exists(Path.Combine(to, "scenes", "studio.json")));

        string mod = Path.Combine(editor.Folder, "mod");
        Assert.Equal(CommandLine.Ok, Run("export", doc.ProjectPath, "--as", "mod", "--to", mod).Code);
        Assert.True(File.Exists(Path.Combine(mod, "Player.Lab.dll")));

        // Usage and bad input: exit 2 with a message.
        Assert.Equal(CommandLine.Failed, Run().Code);
        Assert.Equal(CommandLine.Failed, Run("export", doc.ProjectPath, "--as", "zip", "--to", to).Code);
        var missing = Run("check", Path.Combine(editor.Folder, "nope.sdcs"));
        Assert.Equal(CommandLine.Failed, missing.Code);
        Assert.Contains("No project file", missing.Error);
    }
}
