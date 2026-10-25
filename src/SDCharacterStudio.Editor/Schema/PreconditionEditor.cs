using System.Globalization;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Schema;

/// <summary>
/// Friendly editors for vanilla event preconditions (reference/event-preconditions.md). Each kind reads and writes the
/// exact precondition string; anything unrecognised stays editable as raw text.
/// </summary>
public static class PreconditionEditor
{
    private sealed record Kind(string Id, string Label, string[] Names, string[] Args);

    private static readonly Kind[] Kinds =
    {
        new("hasflag", "Has story flag", new[] { "n", "LocalMail" }, new[] { "flag" }),
        new("noflag", "Doesn't have story flag", new[] { "l", "NotLocalMail" }, new[] { "flag" }),
        new("time", "Between times", new[] { "t", "Time" }, new[] { "from", "to" }),
        new("notfestival", "Not a festival day", new[] { "F", "NotFestivalDay" }, Array.Empty<string>()),
        new("friendship", "Friendship at least", new[] { "f", "Friendship" }, new[] { "npc", "hearts" }),
        new("here", "NPC is in this location", new[] { "p", "NpcVisibleHere" }, new[] { "npc" }),
        new("saw", "Has seen event", new[] { "e", "SawEvent" }, new[] { "event" }),
        new("notsaw", "Hasn't seen event", new[] { "k", "NotSawEvent" }, new[] { "event" }),
        new("season", "Season is", new[] { "Season" }, new[] { "season" }),
        new("notseason", "Season isn't", new[] { "z", "NotSeason" }, new[] { "season" }),
        new("day", "Day of the week is", new[] { "DayOfWeek" }, new[] { "days" }),
        new("notday", "Not on", new[] { "d", "NotDayOfWeek" }, new[] { "days" }),
        new("weather", "Weather is", new[] { "w", "Weather" }, new[] { "weather" }),
        new("year", "Year at least", new[] { "y", "Year" }, new[] { "year" }),
        new("random", "Random chance", new[] { "r", "Random" }, new[] { "percent" }),
        new("raw", "Other (type it)", Array.Empty<string>(), new[] { "raw" }),
    };

    public static IEnumerable<FieldViewModel> Cells(string precondition, Action<string> set, FieldCommit commit, IEnumerable<ChoiceItem> npcs)
    {
        var (kind, args) = Parse(precondition);
        string current = kind.Id;
        yield return new ChoiceField("", "", Kinds.Select(k => new ChoiceItem(k.Id, k.Label)), () => current, v =>
        {
            var next = Kinds.First(k => k.Id == v);
            set(Default(next));
        }, commit) { Rebuild = true };

        string[] values = args;
        void Write(int index, string value)
        {
            var copy = values.Length > index ? (string[])values.Clone() : values.Concat(Enumerable.Repeat("", index + 1 - values.Length)).ToArray();
            copy[index] = value.Trim();
            values = copy;
            set(Build(kind, copy));
        }
        string Arg(int index) => values.Length > index ? values[index] : "";
        switch (kind.Id)
        {
            case "hasflag": case "noflag": case "saw": case "notsaw":
                yield return new TextField("", "", () => Arg(0), v => Write(0, v ?? ""), commit) { AllowNull = false, Placeholder = kind.Id is "saw" or "notsaw" ? "event ID" : "flag name" };
                break;
            case "time":
                yield return new NumberField("from", "", () => int.TryParse(Arg(0), out int a) ? a : 600, v => Write(0, (v ?? 600).ToString(CultureInfo.InvariantCulture)), commit) { Minimum = 600, Maximum = 2600, Step = 10, Describe = Clock };
                yield return new NumberField("to", "", () => int.TryParse(Arg(1), out int b) ? b : 2600, v => Write(1, (v ?? 2600).ToString(CultureInfo.InvariantCulture)), commit) { Minimum = 600, Maximum = 2600, Step = 10, Describe = Clock };
                break;
            case "friendship":
                yield return new ChoiceField("", "", npcs, () => Arg(0), v => Write(0, v ?? ""), commit) { IsEditable = true };
                yield return new NumberField("hearts", "", () => int.TryParse(Arg(1), out int p) ? p / 250 : 0, v => Write(1, ((v ?? 0) * 250).ToString(CultureInfo.InvariantCulture)), commit) { Minimum = 0, Maximum = 14 };
                break;
            case "here":
                yield return new ChoiceField("", "", npcs, () => Arg(0), v => Write(0, v ?? ""), commit) { IsEditable = true };
                break;
            case "season": case "notseason":
                yield return new ChoiceField("", "", new[] { "spring", "summer", "fall", "winter" }.Select(s => new ChoiceItem(s, s)), () => Arg(0), v => Write(0, v ?? "spring"), commit);
                break;
            case "day": case "notday":
                yield return new TextField("", "", () => string.Join(' ', values), v => { values = (v ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries); set(Build(kind, values)); }, commit) { AllowNull = false, Placeholder = "Mon Tue Wed…" };
                break;
            case "weather":
                yield return new ChoiceField("", "", new[] { "sunny", "rainy", "stormy", "snowy", "windy" }.Select(s => new ChoiceItem(s, s)), () => Arg(0), v => Write(0, v ?? "sunny"), commit) { IsEditable = true };
                break;
            case "year":
                yield return new NumberField("", "", () => int.TryParse(Arg(0), out int y) ? y : 1, v => Write(0, (v ?? 1).ToString(CultureInfo.InvariantCulture)), commit) { Minimum = 1, Maximum = 99 };
                break;
            case "random":
                yield return new NumberField("%", "", () => double.TryParse(Arg(0), NumberStyles.Float, CultureInfo.InvariantCulture, out double r) ? (int)Math.Round(r * 100) : 50,
                    v => Write(0, ((v ?? 50) / 100.0).ToString("0.##", CultureInfo.InvariantCulture)), commit) { Minimum = 1, Maximum = 100, Suffix = "%" };
                break;
            case "raw":
                yield return new TextField("", "", () => precondition, v => set(v ?? ""), commit) { AllowNull = false, Placeholder = "vanilla precondition, e.g. j 10" };
                break;
        }
    }

    private static (Kind Kind, string[] Args) Parse(string text)
    {
        var parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return (Kinds[^1], new[] { text });
        var kind = Kinds.FirstOrDefault(k => k.Names.Contains(parts[0], StringComparer.Ordinal));
        var args = parts.Skip(1).ToArray();
        if (kind == null || (kind.Args.Length > 0 && args.Length < kind.Args.Length && kind.Id is not ("day" or "notday")) || (kind.Id is "friendship" && args.Length > 2))
            return (Kinds[^1], new[] { text });
        return (kind, args);
    }

    private static string Build(Kind kind, string[] args) => kind.Id == "raw" ? string.Join(' ', args) : string.Join(' ', new[] { kind.Names[0] }.Concat(args)).Trim();

    private static string Default(Kind kind) => kind.Id switch
    {
        "hasflag" or "noflag" => kind.Names[0] + " SomeFlag",
        "time" => "t 600 2600",
        "notfestival" => "F",
        "friendship" => "f Robin 1000",
        "here" => "p Robin",
        "saw" or "notsaw" => kind.Names[0] + " SomeEvent",
        "season" or "notseason" => kind.Names[0] + " spring",
        "day" or "notday" => kind.Names[0] + " Mon",
        "weather" => "w sunny",
        "year" => "y 1",
        "random" => "r 0.5",
        _ => "F"
    };

    private static string? Clock(int? value)
    {
        if (value is not { } v) return null;
        int hours = v / 100 % 24, minutes = v % 100;
        return $"{(hours % 12 == 0 ? 12 : hours % 12)}:{minutes:00} {(hours < 12 ? "am" : "pm")}";
    }
}
