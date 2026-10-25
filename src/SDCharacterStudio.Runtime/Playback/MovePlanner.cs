using SDCharacterStudio.Core.Models;
using Newtonsoft.Json.Linq;
using System.Globalization;

namespace SDCharacterStudio.Runtime.Playback;

public static class MovePlanner
{
    public static IReadOnlyList<string> Plan(MoveBeat beat, int tileX, int tileY)
    {
        if (beat.To?.Tile is not { Length: 2 } target || string.IsNullOrWhiteSpace(beat.Actor))
            throw new ArgumentException("Movement requires an actor and destination tile.");
        int speed = beat.Speed.Type == JTokenType.Integer ? (int)beat.Speed : ((string?)beat.Speed)?.ToLowerInvariant() switch
        { "slow" => 1, "normal" => 2, "fast" => 4, _ => throw new ArgumentException("Unknown movement speed.") };
        int dx = target[0] - tileX, dy = target[1] - tileY;
        var moves = new List<string>();
        string? x = dx == 0 ? null : FormattableString.Invariant($"move {beat.Actor} {dx} 0 {(dx > 0 ? 1 : 3)}");
        string? y = dy == 0 ? null : FormattableString.Invariant($"move {beat.Actor} 0 {dy} {(dy > 0 ? 2 : 0)}");
        foreach (var move in beat.Path == "y-first" ? new[] { y, x } : new[] { x, y })
            if (move != null) moves.Add(move);
        if (!beat.Wait && moves.Count > 0) moves[^1] += " true";
        moves.Insert(0, $"speed {beat.Actor} {speed.ToString(CultureInfo.InvariantCulture)}");
        return moves;
    }
}
