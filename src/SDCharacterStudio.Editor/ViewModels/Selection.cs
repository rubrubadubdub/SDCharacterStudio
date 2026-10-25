using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Editor.ViewModels;

/// <summary>What the designer has selected, by path, so it survives undo/redo (which rebuilds the models).</summary>
public abstract record Selection(string Cutscene);
public sealed record SceneSelection(string Cutscene) : Selection(Cutscene);
public sealed record BeatSelection(string Cutscene, string Lane, int Index) : Selection(Cutscene);
public sealed record ActorSelection(string Cutscene, int Index) : Selection(Cutscene);
public sealed record BranchSelection(string Cutscene, string Lane) : Selection(Cutscene);
public sealed record VideoSelection(string Cutscene, string Video) : Selection(Cutscene);

/// <summary>Lanes are the main beat list, a branch, or the OnSkip list.</summary>
public static class LaneNames
{
    // Tagged editor identities cannot collide with legal content branch IDs.
    public const string Main = "@main";
    public const string Skip = "@skip";

    public static List<Beat>? Get(Cutscene scene, string lane) => lane switch
    {
        Main => scene.Beats,
        Skip => scene.OnSkip,
        _ => scene.Branches.TryGetValue(lane, out var list) ? list : null
    };

    public static List<Beat> GetOrCreate(Cutscene scene, string lane)
    {
        if (lane == Skip) return scene.OnSkip ??= new List<Beat>();
        return Get(scene, lane) ?? throw new ArgumentException($"No branch '{lane}'.");
    }

    public static string JsonPath(string lane, int index) => lane switch
    {
        Main => $"Beats[{index}]",
        Skip => $"OnSkip[{index}]",
        _ => $"Branches.{lane}[{index}]"
    };

    public static string Title(string lane) => lane switch { Main => "Main", Skip => "If skipped", _ => lane };
}
