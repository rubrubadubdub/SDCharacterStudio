namespace SDCharacterStudio.Core.Models;

/// <summary>One way out of a fork: a branch, or straight on to the beat after the fork.</summary>
public readonly record struct BeatWay(string? Branch, bool KeepGoing);

/// <summary>
/// Where beats jump (doc 03 §6): a fork (Choice, Check, Chance) jumps to one of several branches and then continues after
/// itself; a Go-to jumps for good. Everything that follows branches (the compiler, the validator, the storyboard, the
/// stage, preview) asks here, so a new fork is taught in one place.
/// </summary>
public static class BeatFlow
{
    /// <summary>Jumps to one of several branches and then continues with the beats after it.</summary>
    public static bool IsFork(Beat? beat) => beat is ChoiceBeat or CheckBeat or ChanceBeat;

    /// <summary>A fork's ways out, in order (a Check's otherwise last). A Choice answer always names a branch.</summary>
    public static IReadOnlyList<BeatWay> Ways(Beat? beat) => beat switch
    {
        ChoiceBeat choice => (choice.Options ?? new()).Select(o => new BeatWay(o?.Then, false)).ToArray(),
        CheckBeat check => (check.Cases ?? new()).Select(c => new BeatWay(c?.Then, false)).Append(new BeatWay(check.Otherwise, check.Otherwise == null)).ToArray(),
        ChanceBeat chance => (chance.Outcomes ?? new()).Select(o => new BeatWay(o?.Then, false)).ToArray(),
        _ => Array.Empty<BeatWay>()
    };

    /// <summary>Every branch a beat names: a fork's ways and a Go-to's target.</summary>
    public static IEnumerable<string> Targets(Beat? beat) => beat is GotoBeat go
        ? (go.Branch == null ? Array.Empty<string>() : new[] { go.Branch })
        : Ways(beat).Where(w => w.Branch != null).Select(w => w.Branch!);

    /// <summary>Point every reference to a branch at its new name.</summary>
    public static void Rename(Beat? beat, string oldName, string newName)
    {
        bool Same(string? name) => string.Equals(name, oldName, StringComparison.OrdinalIgnoreCase);
        switch (beat)
        {
            case GotoBeat go when Same(go.Branch): go.Branch = newName; break;
            case ChoiceBeat choice: foreach (var option in choice.Options.Where(o => Same(o.Then))) option.Then = newName; break;
            case CheckBeat check:
                foreach (var @case in check.Cases.Where(c => Same(c.Then))) @case.Then = newName;
                if (Same(check.Otherwise)) check.Otherwise = newName;
                break;
            case ChanceBeat chance: foreach (var outcome in chance.Outcomes.Where(o => Same(o.Then))) outcome.Then = newName; break;
        }
    }
}
