using System.Text.RegularExpressions;
using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Core;

/// <summary>
/// A pack's own IDs in its conditions (D-53). The editor writes them with the pack's UniqueID: its scenes' event IDs
/// (<c>&lt;pack&gt;.&lt;scene&gt;</c>, "has seen a scene") and its story names (<c>SDCS.&lt;pack&gt;.…</c>: story numbers,
/// remembered random items, suggested story flags). A pack can run under another ID: the scenes folder of a C# mod runs
/// as that mod, so the engine names its events <c>&lt;mod&gt;.&lt;scene&gt;</c> and its numbers <c>SDCS.&lt;mod&gt;.…</c>.
/// <see cref="Rehome"/> rewrites the pack's own IDs, so its conditions match what the engine writes, whatever the ID.
/// </summary>
public static class StoryIds
{
    /// <summary>
    /// Rewrite the IDs the pack was made under (<paramref name="madeAs"/>) to those it runs under (<paramref name="packId"/>)
    /// in the scene's conditions (trigger, Check cases, answers, random items) and in what sets its story flags (Mail and
    /// Raw beats, the day-after flag), so flags it sets and checks stay one name. Returns how many texts changed.
    /// </summary>
    public static int Rehome(Cutscene scene, string? madeAs, string packId, IEnumerable<string?> sceneIds)
    {
        if (string.IsNullOrWhiteSpace(madeAs) || string.Equals(madeAs, Ids.StoryPack(packId), StringComparison.OrdinalIgnoreCase)) return 0;
        var map = new Rewriter(madeAs, packId, sceneIds);
        int changed = 0;
        string? Fix(string? text)
        {
            string? fixedText = map.Rewrite(text);
            if (!string.Equals(fixedText, text, StringComparison.Ordinal)) changed++;
            return fixedText;
        }
        if (scene.Trigger is { } trigger)
        {
            trigger.Conditions = Fix(trigger.Conditions);
            for (int i = 0; i < trigger.Preconditions.Count; i++) trigger.Preconditions[i] = Fix(trigger.Preconditions[i]) ?? "";
            trigger.OnDayAfterFlag = Fix(trigger.OnDayAfterFlag);
        }
        foreach (var beat in scene.Beats.Concat(scene.Branches.Values.SelectMany(b => b)).Concat(scene.OnSkip ?? new List<Beat>()))
            switch (beat)
            {
                case CheckBeat check: foreach (var @case in check.Cases.Where(c => c != null)) @case.When = Fix(@case.When); break;
                case ChoiceBeat choice: foreach (var option in choice.Options.Where(o => o != null)) option.When = Fix(option.When); break;
                case ItemBeat { OneOf: { } picks }: foreach (var pick in picks.Where(p => p != null)) pick.When = Fix(pick.When); break;
                case MailBeat mail: mail.Add = Fix(mail.Add); mail.Remove = Fix(mail.Remove); break;
                case RawBeat raw: raw.Command = Fix(raw.Command); break;
            }
        return changed;
    }

    private sealed class Rewriter
    {
        // A whole argument: after the start, a space, a comma, a quote or a negation; before the end, a space, a comma or a quote.
        private const string Before = "(?<=^|[\\s,\"!])", After = "(?=$|[\\s,\"])";
        private readonly Regex? events;
        private readonly Regex story;
        private readonly string packId;
        private readonly string storyPrefix;

        public Rewriter(string madeAs, string packId, IEnumerable<string?> sceneIds)
        {
            this.packId = packId;
            storyPrefix = "SDCS." + Ids.StoryPack(packId) + ".";
            var scenes = sceneIds.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => Regex.Escape(s!)).OrderByDescending(s => s.Length).ToArray();
            if (scenes.Length > 0)
                events = new Regex(Before + Regex.Escape(madeAs) + "\\.(" + string.Join("|", scenes) + ")" + After, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            story = new Regex(Before + "SDCS\\." + Regex.Escape(madeAs) + "\\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        public string? Rewrite(string? text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (events != null) text = events.Replace(text, m => packId + "." + m.Groups[1].Value);
            return story.Replace(text, storyPrefix);
        }
    }
}
