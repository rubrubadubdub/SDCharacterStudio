using System.Globalization;
using System.Text;
using SDCharacterStudio.Editor.Project;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Schema;

/// <summary>What a condition row picks before its comparison: a person, a story flag, an item…</summary>
public enum ConditionSubject { None, Npc, Flag, Scene, Item, Skill, Location, Mod, Number }

/// <summary>How a condition compares: a number range, yes or no, one of some values, a % roll, or the game's text as is.</summary>
public enum ConditionShape { Range, Toggle, Values, Chance, Raw }

/// <summary>One thing a condition can check, and the game state query it writes (doc 03 §6.16).</summary>
public sealed record ConditionKind(string Id, string Label, string Category, string Query, ConditionSubject Subject, ConditionShape Shape)
{
    /// <summary>The query names the player first ("Current").</summary>
    public bool PlayerArg { get; init; }
    /// <summary>The query names the location first ("Here").</summary>
    public bool LocationArg { get; init; }
    /// <summary>Fixed values to pick from (seasons, weather…); null for typed values.</summary>
    public IReadOnlyList<ChoiceItem>? Values { get; init; }
    public string Yes { get; init; } = "is";
    public string No { get; init; } = "isn't";
    public string? Unit { get; init; }
    public string? Hint { get; init; }
    /// <summary>The bottom of the range ("at most 3" is 0 to 3).</summary>
    public int Floor { get; init; }
    /// <summary>PLAYER_HAS_ITEM: a count of at least 1 can be left out.</summary>
    public bool CountOptional { get; init; }
}

/// <summary>One condition as the rows show it. <see cref="Raw"/> holds text the rows can't show, kept as written.</summary>
public sealed class ConditionRow
{
    public ConditionRow(ConditionKind kind) => Kind = kind;
    public ConditionKind Kind { get; set; }
    public bool Not { get; set; }
    public string? Subject { get; set; }
    /// <summary>Range: atLeast, lessThan, atMost, exactly, between.</summary>
    public string Compare { get; set; } = "atLeast";
    public string? Value { get; set; }
    public string? Value2 { get; set; }
    public string? Raw { get; set; }
}

/// <summary>Conditions that must all be true, or of which any one must be.</summary>
public sealed class ConditionGroup
{
    public bool Any { get; set; }
    public List<ConditionRow> Rows { get; } = new();
}

/// <summary>
/// Conditions are stored as the game's own condition text: a game state query, like Stardew's events, shops and mail
/// use (D-51). The editor shows that text as rows and writes it back. Text the rows can't show stays a "game query" row,
/// word for word, so nothing a designer or another mod wrote is lost.
/// </summary>
public static class Conditions
{
    public const string Player = "Current", Here = "Here";

    private static ChoiceItem[] Items(params string[] values) => values.Select(v => new ChoiceItem(v, Words(v))).ToArray();
    private static string Words(string value) => value switch { "GreenRain" => "green rain", "Sun" => "sunny", _ => value.ToLowerInvariant() };

    public static readonly IReadOnlyList<ConditionKind> Kinds = new ConditionKind[]
    {
        new("hearts", "hearts with", "People", "PLAYER_HEARTS", ConditionSubject.Npc, ConditionShape.Range) { PlayerArg = true, Unit = "♥" },
        new("relationship", "relationship with", "People", "PLAYER_NPC_RELATIONSHIP", ConditionSubject.Npc, ConditionShape.Values)
            { PlayerArg = true, Values = Items("Friendly", "Dating", "Engaged", "Married", "Roommate", "Divorced") },
        new("met", "has met", "People", "PLAYER_HAS_MET", ConditionSubject.Npc, ConditionShape.Toggle) { PlayerArg = true, Yes = "yes", No = "not yet" },
        new("flag", "story flag", "Story", "PLAYER_HAS_MAIL", ConditionSubject.Flag, ConditionShape.Toggle) { PlayerArg = true, Yes = "is set", No = "isn't set" },
        new("seen", "has seen a scene", "Story", "PLAYER_HAS_SEEN_EVENT", ConditionSubject.Scene, ConditionShape.Toggle) { PlayerArg = true, Yes = "yes", No = "not yet" },
        new("number", "story number", "Story", "PLAYER_STAT", ConditionSubject.Number, ConditionShape.Range) { PlayerArg = true },
        new("item", "has an item", "Player", "PLAYER_HAS_ITEM", ConditionSubject.Item, ConditionShape.Range) { PlayerArg = true, CountOptional = true, Unit = "of them" },
        new("money", "money", "Player", "PLAYER_CURRENT_MONEY", ConditionSubject.None, ConditionShape.Range) { PlayerArg = true, Unit = "g" },
        new("skill", "skill level", "Player", "PLAYER_{0}_LEVEL", ConditionSubject.Skill, ConditionShape.Range) { PlayerArg = true },
        new("season", "season", "World", "SEASON", ConditionSubject.None, ConditionShape.Values) { Values = Items("Spring", "Summer", "Fall", "Winter") },
        new("weekday", "day of the week", "World", "DAY_OF_WEEK", ConditionSubject.None, ConditionShape.Values)
            { Values = Items("Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday") },
        new("day", "day of the month", "World", "DAY_OF_MONTH", ConditionSubject.None, ConditionShape.Values) { Hint = "1 to 28; several with spaces; or even, odd" },
        new("weather", "weather", "World", "WEATHER", ConditionSubject.None, ConditionShape.Values) { LocationArg = true, Values = Items("Sun", "Rain", "Storm", "Snow", "Wind", "GreenRain") },
        new("time", "time of day", "World", "TIME", ConditionSubject.None, ConditionShape.Range) { Hint = "24-hour: 1830 is 6:30 pm", Floor = 600 },
        new("year", "year", "World", "YEAR", ConditionSubject.None, ConditionShape.Range) { Floor = 1 },
        new("location", "the player is in", "Where", "LOCATION_NAME", ConditionSubject.Location, ConditionShape.Toggle) { LocationArg = true, Yes = "yes", No = "no" },
        new("festival", "festival day", "Where", "IS_FESTIVAL_DAY", ConditionSubject.None, ConditionShape.Toggle) { Yes = "today", No = "not today" },
        new("chance", "chance", "Other", "RANDOM", ConditionSubject.None, ConditionShape.Chance) { Unit = "%" },
        new("mod", "a mod is installed", "Other", InstallVariants.HasMod, ConditionSubject.Mod, ConditionShape.Toggle) { Yes = "installed", No = "not installed" },
        new("query", "a game query", "Other", "", ConditionSubject.None, ConditionShape.Raw),
    };

    public static ConditionKind Kind(string id) => Kinds.First(k => k.Id == id);

    public static readonly IReadOnlyList<ChoiceItem> Skills = Items("Farming", "Fishing", "Foraging", "Mining", "Combat", "Luck");

    public static IReadOnlyList<ChoiceItem> CompareOptions(ConditionKind kind) => kind.Shape switch
    {
        ConditionShape.Range => new[]
        {
            new ChoiceItem("atLeast", "at least"), new ChoiceItem("lessThan", kind.Id == "item" ? "fewer than" : "less than"),
            new ChoiceItem("atMost", "at most"), new ChoiceItem("exactly", "exactly"), new ChoiceItem("between", "between"),
        },
        ConditionShape.Toggle or ConditionShape.Values => new[] { new ChoiceItem("yes", kind.Yes), new ChoiceItem("no", kind.No) },
        _ => Array.Empty<ChoiceItem>(),
    };

    /// <summary>A new row with sensible values, ready to edit.</summary>
    public static ConditionRow NewRow(ConditionKind kind, string? subject = null) => new(kind)
    {
        Subject = subject ?? (kind.Subject == ConditionSubject.Skill ? "Farming" : null),
        Compare = kind.Shape == ConditionShape.Range ? "atLeast" : "yes",
        Value = kind.Id switch
        {
            "hearts" => "4", "item" => "1", "money" => "1000", "number" => "1", "skill" => "5", "time" => "1200", "year" => "2", "chance" => "50", "day" => "1",
            _ => kind.Values?.FirstOrDefault()?.Value,
        },
        Raw = kind.Shape == ConditionShape.Raw ? "" : null,
    };

    // ---------------- reading the game's text ----------------

    public static ConditionGroup Parse(string? text)
    {
        var group = new ConditionGroup();
        if (string.IsNullOrWhiteSpace(text)) return group;
        var parts = SplitTopLevel(text);
        // One ANY "…" "…": any of these. Otherwise commas: all of these.
        if (parts.Count == 1 && SplitArgs(parts[0]) is { Count: > 1 } any && any[0].Equals("ANY", StringComparison.OrdinalIgnoreCase) && !parts[0].TrimStart().StartsWith('!'))
        {
            group.Any = true;
            foreach (string query in any.Skip(1)) group.Rows.Add(ParseRow(query));
            return group;
        }
        foreach (string part in parts) group.Rows.Add(ParseRow(part));
        return group;
    }

    public static ConditionRow ParseRow(string text)
    {
        text = text.Trim();
        var raw = new ConditionRow(Kind("query")) { Raw = text };
        if (text.Contains(',')) return raw;
        bool not = text.StartsWith('!');
        var args = SplitArgs(not ? text[1..] : text);
        if (args.Count == 0) return raw;
        string name = args[0];
        var kind = Kinds.FirstOrDefault(k => k.Shape != ConditionShape.Raw && k.Id != "skill" && k.Query.Equals(name, StringComparison.OrdinalIgnoreCase));
        string? skill = null;
        if (kind == null && name.StartsWith("PLAYER_", StringComparison.OrdinalIgnoreCase) && name.EndsWith("_LEVEL", StringComparison.OrdinalIgnoreCase)
            && Skills.FirstOrDefault(s => s.Value!.Equals(name[7..^6], StringComparison.OrdinalIgnoreCase)) is { } found)
        { kind = Kind("skill"); skill = found.Value; }
        if (kind == null) return raw;

        int at = 1;
        if (kind.PlayerArg) { if (args.Count <= at || !args[at].Equals(Player, StringComparison.OrdinalIgnoreCase)) return raw; at++; }
        if (kind.LocationArg) { if (args.Count <= at || !args[at].Equals(Here, StringComparison.OrdinalIgnoreCase)) return raw; at++; }
        var row = new ConditionRow(kind) { Subject = skill };
        if (kind.Subject is not (ConditionSubject.None or ConditionSubject.Skill)) { if (args.Count <= at) return raw; row.Subject = args[at++]; }
        var rest = args.Skip(at).ToList();
        switch (kind.Shape)
        {
            case ConditionShape.Toggle:
                if (rest.Count != 0) return raw;
                row.Compare = not ? "no" : "yes";
                return row;
            case ConditionShape.Values:
                if (rest.Count == 0) return raw;
                if (kind.Values != null && rest.Any(v => !kind.Values.Any(o => o.Value!.Equals(v, StringComparison.OrdinalIgnoreCase)))) return raw;
                row.Compare = not ? "no" : "yes";
                row.Value = string.Join(" ", rest.Select(v => kind.Values?.First(o => o.Value!.Equals(v, StringComparison.OrdinalIgnoreCase)).Value ?? v));
                return row;
            case ConditionShape.Chance:
                if (not || rest.Count != 1 || !double.TryParse(rest[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double chance) || chance < 0 || chance > 1) return raw;
                row.Value = Math.Round(chance * 100, 2).ToString(CultureInfo.InvariantCulture);
                row.Compare = "yes";
                return row;
            case ConditionShape.Range:
                var numbers = new List<int>();
                foreach (string value in rest) { if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) return raw; numbers.Add(n); }
                if (numbers.Count == 0 && kind.CountOptional) numbers.Add(1);
                if (numbers.Count == 1) { row.Compare = not ? "lessThan" : "atLeast"; row.Value = Text(numbers[0]); return row; }
                if (numbers.Count != 2 || not) return raw;
                if (numbers[0] == numbers[1]) { row.Compare = "exactly"; row.Value = Text(numbers[0]); }
                else if (numbers[0] == kind.Floor) { row.Compare = "atMost"; row.Value = Text(numbers[1]); }
                else { row.Compare = "between"; row.Value = Text(numbers[0]); row.Value2 = Text(numbers[1]); }
                return row;
        }
        return raw;
    }

    private static string Text(int n) => n.ToString(CultureInfo.InvariantCulture);

    // ---------------- writing it back ----------------

    /// <summary>The group as the game's condition text; null when it has no rows.</summary>
    public static string? Print(ConditionGroup group)
    {
        var rows = group.Rows.Select(Print).Where(r => r.Length > 0).ToList();
        if (rows.Count == 0) return null;
        if (!group.Any || rows.Count == 1) return string.Join(", ", rows);
        return "ANY " + string.Join(" ", rows.Select(r => "\"" + r.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""));
    }

    public static string Print(ConditionRow row)
    {
        var kind = row.Kind;
        if (kind.Shape == ConditionShape.Raw) return (row.Raw ?? "").Trim();
        var parts = new List<string> { kind.Id == "skill" ? string.Format(CultureInfo.InvariantCulture, kind.Query, (row.Subject ?? "Farming").ToUpperInvariant()) : kind.Query };
        if (kind.PlayerArg) parts.Add(Player);
        if (kind.LocationArg) parts.Add(Here);
        if (kind.Subject is not (ConditionSubject.None or ConditionSubject.Skill)) parts.Add(Arg(row.Subject ?? ""));
        bool not = false;
        switch (kind.Shape)
        {
            case ConditionShape.Toggle: not = row.Compare == "no"; break;
            case ConditionShape.Values:
                not = row.Compare == "no";
                parts.AddRange((row.Value ?? "").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(Arg));
                break;
            case ConditionShape.Chance:
                double percent = double.TryParse(row.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double p) ? Math.Clamp(p, 0, 100) : 0;
                parts.Add((percent / 100).ToString("0.####", CultureInfo.InvariantCulture));
                break;
            case ConditionShape.Range:
                int a = Number(row.Value), b = Number(row.Value2);
                switch (row.Compare)
                {
                    case "lessThan": not = true; if (!(kind.CountOptional && a == 1)) parts.Add(Text(a)); break;
                    case "atMost": parts.Add(Text(kind.Floor)); parts.Add(Text(a)); break;
                    case "exactly": parts.Add(Text(a)); parts.Add(Text(a)); break;
                    case "between": parts.Add(Text(Math.Min(a, b))); parts.Add(Text(Math.Max(a, b))); break;
                    default: if (!(kind.CountOptional && a == 1)) parts.Add(Text(a)); break;
                }
                break;
        }
        return (not ? "!" : "") + string.Join(" ", parts);
    }

    private static int Number(string? text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0;
    private static string Arg(string value) => value.Any(char.IsWhiteSpace) ? "\"" + value.Replace("\"", "\\\"") + "\"" : value;

    // ---------------- in words ----------------

    /// <summary>The conditions in a few words, for a storyboard card: "Abigail ≥ 6♥ and flag trusts-you set".</summary>
    public static string Describe(string? text, Func<string, string?>? itemName = null)
    {
        var group = Parse(text);
        if (group.Rows.Count == 0) return "(no condition)";
        return string.Join(group.Any ? " or " : " and ", group.Rows.Select(r => Describe(r, itemName)));
    }

    public static string Describe(ConditionRow row, Func<string, string?>? itemName = null)
    {
        string subject = row.Subject ?? "?";
        string Range(string what, string unit = "") => row.Compare switch
        {
            "lessThan" => $"{what} < {row.Value}{unit}",
            "atMost" => $"{what} ≤ {row.Value}{unit}",
            "exactly" => $"{what} = {row.Value}{unit}",
            "between" => $"{what} {row.Value}–{row.Value2}{unit}",
            _ => $"{what} ≥ {row.Value}{unit}",
        };
        bool no = row.Compare == "no";
        string Values() => string.Join(" or ", (row.Value ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(v => row.Kind.Values?.FirstOrDefault(o => o.Value == v)?.Label ?? v));
        return row.Kind.Id switch
        {
            "hearts" => Range(subject, "♥"),
            "relationship" => $"{subject} {(no ? "not " : "")}{Values()}",
            "met" => $"{(no ? "hasn't met" : "met")} {subject}",
            "flag" => $"{Short(subject)} {(no ? "not set" : "set")}",
            "seen" => $"{(no ? "hasn't seen" : "seen")} {Short(subject)}",
            "number" => Range(Short(subject)),
            "item" => row.Compare == "lessThan" && row.Value == "1" ? $"no {itemName?.Invoke(subject) ?? subject}" : Range(itemName?.Invoke(subject) ?? subject),
            "money" => Range("money", "g"),
            "skill" => Range(subject.ToLowerInvariant()),
            "season" or "weekday" or "weather" => (no ? "not " : "") + Values(),
            "day" => $"{(no ? "not " : "")}day {Values()}",
            "time" => Range("time"),
            "year" => Range("year"),
            "location" => $"{(no ? "not in" : "in")} {subject}",
            "festival" => no ? "not a festival day" : "festival day",
            "chance" => $"{row.Value}% chance",
            "mod" => $"{subject} {(no ? "not installed" : "installed")}",
            _ => BeatCatalog.Shorten(row.Raw ?? "", 40),
        };
    }

    /// <summary>A story flag or scene key without its long prefix.</summary>
    public static string Short(string id) => id.Contains('.') ? id[(id.LastIndexOf('.') + 1)..] : id;

    // ---------------- the game's text rules ----------------

    /// <summary>Split on commas outside quotes (as GameStateQuery does).</summary>
    public static List<string> SplitTopLevel(string text)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\\' && i + 1 < text.Length) { current.Append(c).Append(text[++i]); continue; }
            if (c == '"') quoted = !quoted;
            if (c == ',' && !quoted) { if (current.ToString().Trim().Length > 0) parts.Add(current.ToString().Trim()); current.Clear(); continue; }
            current.Append(c);
        }
        if (current.ToString().Trim().Length > 0) parts.Add(current.ToString().Trim());
        return parts;
    }

    /// <summary>Split on spaces outside quotes; quotes removed, \" kept as a quote (ArgUtility's rules).</summary>
    public static List<string> SplitArgs(string text)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        bool quoted = false, any = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\\' && i + 1 < text.Length && text[i + 1] is '"' or '\\') { current.Append(text[++i]); any = true; continue; }
            if (c == '"') { quoted = !quoted; any = true; continue; }
            if (char.IsWhiteSpace(c) && !quoted) { if (any) args.Add(current.ToString()); current.Clear(); any = false; continue; }
            current.Append(c); any = true;
        }
        if (any) args.Add(current.ToString());
        return args;
    }
}
