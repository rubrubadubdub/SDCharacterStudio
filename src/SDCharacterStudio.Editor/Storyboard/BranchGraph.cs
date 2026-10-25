using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Editor.ViewModels;

namespace SDCharacterStudio.Editor.Storyboard;

public sealed record GraphNode(string Id, string Lane, int Index, string Label, Beat? Beat, bool Missing = false);
public enum GraphEdgeKind { Sequential, Choice, Goto, FallThrough, BranchReturn }
public sealed record GraphEdge(string From, string To, GraphEdgeKind Kind, string? Label = null, bool Missing = false);
public sealed class BranchGraph
{
    public IReadOnlyList<GraphNode> Nodes { get; init; } = Array.Empty<GraphNode>();
    public IReadOnlyList<GraphEdge> Edges { get; init; } = Array.Empty<GraphEdge>();
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
    public GraphNode? Find(string lane, int index) => Nodes.FirstOrDefault(n => n.Lane == lane && n.Index == index && !n.Missing);
}

/// <summary>Finite control flow derived from the compiler's Choice call/return and Goto tail-call rules.
/// Dashed returns show every possible caller context, rather than an unconditional Choice bypass.</summary>
public static class BranchGraphBuilder
{
    public static BranchGraph Build(Cutscene scene)
    {
        var lanes = new List<(string Name, List<Beat> Beats)> { (LaneNames.Main, scene.Beats) };
        lanes.AddRange(scene.Branches.Select(p => (p.Key, p.Value)));
        if (scene.Skippable || scene.OnSkip is { Count: > 0 }) lanes.Add((LaneNames.Skip, scene.OnSkip ?? new()));
        var nodes = new List<GraphNode>();
        var edges = new HashSet<GraphEdge>();
        var diagnostics = new HashSet<string>(StringComparer.Ordinal);
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        var returns = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (lane, beats) in lanes)
        {
            entries[lane] = Id(lane, beats.Count == 0 ? -1 : 0);
            returns[lane] = new(StringComparer.Ordinal);
            if (beats.Count == 0)
            {
                nodes.Add(new(entries[lane], lane, -1, LaneNames.Title(lane) + " (empty)", null));
                if (scene.Branches.ContainsKey(lane)) diagnostics.Add("Empty branch '" + lane + "' returns immediately.");
            }
            else
                for (int i = 0; i < beats.Count; i++)
                    nodes.Add(new(Id(lane, i), lane, i, $"{i + 1}. {BeatCatalog.Summary(beats[i])}", beats[i]));
        }

        // Fixed point over finite terminal-node sets handles Goto cycles without recursion.
        bool changed;
        do
        {
            changed = false;
            foreach (var (lane, beats) in lanes)
            {
                var exits = new HashSet<string>(StringComparer.Ordinal);
                if (beats.Count == 0) exits.Add(entries[lane]);
                else
                    for (int i = beats.Count - 1; i >= 0; i--)
                    {
                        switch (beats[i])
                        {
                            case EndBeat: exits.Clear(); break;
                            case GotoBeat go:
                                exits = TargetReturns(go.Branch); break;
                            case var fork when BeatFlow.IsFork(fork):
                                // A way that keeps going (a Check's otherwise) carries on as a plain beat would.
                                var childExits = new HashSet<string>(StringComparer.Ordinal);
                                bool keepsGoing = false;
                                foreach (var way in BeatFlow.Ways(fork)) { if (way.KeepGoing) keepsGoing = true; else childExits.UnionWith(TargetReturns(way.Branch)); }
                                if (i == beats.Count - 1) { exits = childExits; if (keepsGoing) exits.Add(Id(lane, i)); }
                                else if (childExits.Count == 0 && !keepsGoing) exits.Clear();
                                break;
                            default:
                                if (i == beats.Count - 1) exits.Add(Id(lane, i));
                                break;
                        }
                    }
                int before = returns[lane].Count;
                returns[lane].UnionWith(exits);
                changed |= returns[lane].Count != before;
            }
        } while (changed);

        foreach (var (lane, beats) in lanes)
            for (int i = 0; i < beats.Count; i++)
            {
                string from = Id(lane, i);
                switch (beats[i])
                {
                    case EndBeat: break;
                    case GotoBeat go: AddTarget(from, go.Branch, GraphEdgeKind.Goto, go.Branch); break;
                    case var fork when BeatFlow.IsFork(fork):
                        var ways = BeatFlow.Ways(fork);
                        for (int w = 0; w < ways.Count; w++)
                        {
                            if (ways[w].KeepGoing) { if (i + 1 < beats.Count) edges.Add(new(from, Id(lane, i + 1), GraphEdgeKind.Sequential, "otherwise")); continue; }
                            string? label = fork switch
                            {
                                ChoiceBeat choice => choice.Options[w].Text?.Literal ?? choice.Options[w].Text?.I18n ?? ways[w].Branch,
                                CheckBeat check => w < check.Cases.Count ? "if " + Schema.Conditions.Describe(check.Cases[w].When) : "otherwise",
                                ChanceBeat chance => $"{Schema.BeatCatalog.Percent(chance.Outcomes[w].Weight, chance.Outcomes.Select(o => o.Weight))}% chance",
                                _ => ways[w].Branch,
                            };
                            AddTarget(from, ways[w].Branch, GraphEdgeKind.Choice, label);
                            if (i + 1 < beats.Count)
                                foreach (string exit in TargetReturns(ways[w].Branch))
                                    edges.Add(new(exit, Id(lane, i + 1), GraphEdgeKind.BranchReturn, $"return to {LaneNames.Title(lane)} after {i + 1}"));
                        }
                        break;
                    default:
                        if (i + 1 < beats.Count) edges.Add(new(from, Id(lane, i + 1), GraphEdgeKind.Sequential));
                        break;
                }
            }
        return new() { Nodes = nodes, Edges = edges.ToArray(), Diagnostics = diagnostics.ToArray() };

        string? ResolveBranch(string? target) => target == null ? null : scene.Branches.Keys.FirstOrDefault(k => string.Equals(k, target, StringComparison.OrdinalIgnoreCase));
        HashSet<string> TargetReturns(string? target) =>
            ResolveBranch(target) is { } key && returns.TryGetValue(key, out var value) ? new(value, StringComparer.Ordinal) : new(StringComparer.Ordinal);
        void AddTarget(string from, string? target, GraphEdgeKind kind, string? label)
        {
            if (ResolveBranch(target) is { } key)
            { edges.Add(new(from, entries[key], kind, label)); return; }
            string missing = "missing:" + (target ?? "?");
            if (nodes.All(n => n.Id != missing)) nodes.Add(new(missing, target ?? "?", -1, "Missing branch: " + (target ?? "?"), null, true));
            edges.Add(new(from, missing, kind, label, true));
            diagnostics.Add("Missing target '" + (target ?? "?") + "'.");
        }
    }

    private static string Id(string lane, int index) => $"{lane}:{index}";
}
