using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Audio;
using SDCharacterStudio.Core.Serialization;
using SDCharacterStudio.Core.Validation;

namespace SDCharacterStudio.Core.Compiler;

public sealed class EventCompiler
{
    /// <summary>Compiles a scene with its pack's library clips expanded into scene clips (doc 03 §17.1).</summary>
    public CompilationResult Compile(string packUniqueId, Cutscene? cutscene, Loading.LoadedPack? library, Func<string, string?>? translate = null)
        => Compile(packUniqueId, cutscene == null ? null : LibraryClips.Expand(cutscene, library), translate);

    public CompilationResult Compile(string packUniqueId, Cutscene? cutscene, Func<string, string?>? translate = null)
    {
        string file = $"cutscenes/{cutscene?.Id ?? "unknown"}.json";
        var issues = new List<ValidationIssue>();
        if (cutscene is null)
            return new(null, new[] { new ValidationIssue(ValidationSeverity.Error, file, "", "Cutscene is required.") });
        issues.AddRange(PackValidator.ValidateCutscene(cutscene, file));
        if (string.IsNullOrWhiteSpace(packUniqueId) || !Regex.IsMatch(packUniqueId, "^[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant))
            issues.Add(new(ValidationSeverity.Error, file, "Id", "Pack UniqueID must be a nonempty event-safe identifier.", "V17"));
        if (issues.Any(x => x.Severity == ValidationSeverity.Error)) return new(null, issues);
        try
        {
            var session = new Session(packUniqueId, JObject.FromObject(cutscene, PackJson.CreateSerializer()), translate, issues, file, Places.Used(cutscene));
            return new(session.Run(), issues);
        }
        catch (Problem problem)
        {
            issues.Add(new(ValidationSeverity.Error, file, problem.Path, problem.Message, problem.Rule));
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or OverflowException)
        {
            issues.Add(new(ValidationSeverity.Error, file, "", $"Cannot compile malformed cutscene: {ex.Message}", "Schema"));
        }
        return new(null, issues);
    }

    private sealed class Problem : Exception
    {
        public string Path { get; }
        public string Rule { get; }
        public Problem(string path, string message, string rule = "Compiler") : base(message) { Path = path; Rule = rule; }
    }

    private sealed class Block
    {
        public string Name { get; }
        public string KeyName { get; }
        public string Path { get; }
        public int Offset { get; }
        public JArray Beats { get; }
        public Block? Rest { get; set; }
        public bool CanReturn { get; set; }
        public Block(string name, string keyName, string path, int offset, JArray beats) { Name = name; KeyName = keyName; Path = path; Offset = offset; Beats = beats; }
        public string BeatPath(int index) => Path + "[" + (Offset + index) + "]";
        public JObject? Last => Beats.Last as JObject;
        public string LastType => (string?)Last?["Type"] ?? "";
    }

    private sealed class Session
    {
        private readonly string key;
        private readonly string packId;
        private readonly string packHash;
        /// <summary>The places the scene counts positions from (D-52): its trigger waits until the game has them.</summary>
        private readonly IReadOnlyList<string> places;
        private readonly JObject scene;
        private readonly Func<string, string?>? translate;
        private readonly List<ValidationIssue> issues;
        private readonly string file;
        private readonly List<Block> blocks = new();
        private readonly Dictionary<string, Block> branches = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> reservedNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<(Block Block, string Return), string> emitted = new();
        private readonly HashSet<Block> active = new();
        private readonly Dictionary<string, string> scripts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, BeatInfo> beats = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> scriptKeys = new(StringComparer.OrdinalIgnoreCase);
        private int nextContinuation;
        private string? endScript;

        public Session(string pack, JObject scene, Func<string, string?>? translate, List<ValidationIssue> issues, string file, IReadOnlyList<string> places)
        { this.places = places; packId = pack; key = pack + "." + (string?)scene["Id"]; packHash = Ids.PackHash(pack); this.scene = scene; this.translate = translate; this.issues = issues; this.file = file; }

        public CompiledCutscene Run()
        {
            var branchData = scene["Branches"] as JObject ?? new JObject();
            foreach (var property in branchData.Properties()) reservedNames.Add(property.Name);
            reservedNames.Add("main");
            reservedNames.Add("skip");
            var main = Split("main", "main", "Beats", scene["Beats"] as JArray ?? new JArray());
            foreach (var property in branchData.Properties())
            {
                string beatName = property.Name;
                if (beatName.Equals("main", StringComparison.OrdinalIgnoreCase) || beatName.Equals("skip", StringComparison.OrdinalIgnoreCase))
                    do { beatName = "branch." + beatName; } while (reservedNames.Contains(beatName));
                reservedNames.Add(beatName);
                branches.Add(property.Name, Split(beatName, property.Name, "Branches." + property.Name, (JArray)property.Value));
            }
            ResolveReturnability();
            foreach (var block in blocks)
                for (int i = 0; i < block.Beats.Count; i++)
                {
                    var beat = (JObject)block.Beats[i]!;
                    ResolveBeatText(beat, block.BeatPath(i));
                    string id = block.Name + "." + i;
                    beats.Add(id, new(id, block.BeatPath(i), beat.ToObject<Beat>(PackJson.CreateSerializer())!));
                }
            var mainParts = Header();
            if (Bool(scene, "Skippable"))
            {
                mainParts.Add("skippable");
                var skip = SkipActions();
                if (skip.Count > 0) mainParts.Add("setSkipActions " + string.Join("#", skip));
            }
            AddTemporaryActors(mainParts);
            mainParts.Add(EngineNames.Command("Begin") + " " + key);
            mainParts.AddRange(Body(main, null));
            // Native choice replacement makes this unreachable, but every main script retains the cleanup safety net.
            if (main.LastType is not "End") mainParts.AddRange(End());
            string mainScript = Join(mainParts, "Beats");
            string mainKey = MainKey();
            return new(key, (string?)scene["Location"] ?? "", mainKey, mainScript,
                new ReadOnlyDictionary<string, string>(scripts), new ReadOnlyDictionary<string, BeatInfo>(beats));
        }

        private Block Split(string name, string keyName, string path, JArray source)
        {
            Block? first = null;
            Block? previous = null;
            var remaining = source;
            int offset = 0;
            while (true)
            {
                if (blocks.Count >= 4096) throw new Problem(path, "Cutscene has too many continuation blocks.");
                int stop = remaining.Count;
                for (int i = 0; i < remaining.Count; i++)
                    if ((string?)remaining[i]?["Type"] is "Choice" or "Check" or "Chance" or "Goto" or "End") { stop = i + 1; break; }
                var prefix = new JArray(remaining.Take(stop).Select(x => x.DeepClone()));
                var block = new Block(name, keyName, path, offset, prefix);
                blocks.Add(block);
                first ??= block;
                if (previous is not null) previous.Rest = block;
                if (!IsFork(block.LastType) || stop == remaining.Count) return first;
                do { name = "c" + ++nextContinuation; } while (!reservedNames.Add(name));
                keyName = name;
                offset += stop;
                remaining = new JArray(remaining.Skip(stop).Select(x => x.DeepClone()));
                previous = block;
            }
        }

        private void ResolveReturnability()
        {
            bool changed;
            do
            {
                changed = false;
                foreach (var block in blocks)
                {
                    bool returns = block.LastType switch
                    {
                        "End" => false,
                        "Goto" => Branch((string?)block.Last!["Branch"], block.Path).CanReturn,
                        "Choice" => (block.Rest?.CanReturn ?? true) && Options(block.Last!, block.Path)
                            .Any(x => Branch((string?)x["Then"], block.Path).CanReturn),
                        "Chance" => (block.Rest?.CanReturn ?? true) && Outcomes(block.Last!, block.Path).Any(x => Branch((string?)x["Then"], block.Path).CanReturn),
                        // A Check's otherwise with no branch keeps going: it returns whenever the beats after it do.
                        "Check" => (block.Rest?.CanReturn ?? true) && (Present(block.Last!, "Otherwise") is false
                            || Branch((string?)block.Last!["Otherwise"], block.Path).CanReturn
                            || Cases(block.Last!, block.Path).Any(x => Branch((string?)x["Then"], block.Path).CanReturn)),
                        _ => true
                    };
                    if (returns && !block.CanReturn) { block.CanReturn = true; changed = true; }
                }
            } while (changed);
        }

        private Block Branch(string? name, string path) => name is not null && branches.TryGetValue(name, out var block)
            ? block : throw new Problem(path, $"Unknown branch '{name}'.", "V8");

        private string Emit(Block block, string? returnTo)
        {
            string context = block.CanReturn ? returnTo ?? "" : "";
            if (emitted.TryGetValue((block, context), out var cached)) return cached;
            if (active.Contains(block))
                throw new Problem(block.Path, "Recursive choices would create an unbounded return stack. Use a terminal Choice or Goto for a repeating question.", "V9");
            if (emitted.Count >= 4096 || active.Count >= 256)
                throw new Problem(block.Path, "Branch graph exceeds the safe compilation limit.");
            string candidate = key + "." + block.KeyName + (context.Length > 0 ? "." + context[(key.Length + 1)..] : "");
            string scriptKey = candidate;
            for (int variant = 2; !scriptKeys.Add(scriptKey); variant++) scriptKey = candidate + ".v" + variant;
            emitted.Add((block, context), scriptKey);
            active.Add(block);
            try { scripts.Add(scriptKey, Join(new[] { "pause 1" }.Concat(Body(block, returnTo)), block.Path)); }
            finally { active.Remove(block); }
            return scriptKey;
        }

        private IEnumerable<string> Body(Block block, string? returnTo)
        {
            var result = new List<string>();
            for (int i = 0; i < block.Beats.Count; i++)
            {
                var beat = (JObject)block.Beats[i]!;
                string path = block.BeatPath(i);
                string id = block.Name + "." + i;
                string type = (string?)beat["Type"] ?? "";
                if (type == "Choice")
                {
                    string? continuation = block.Rest is null ? returnTo : Emit(block.Rest, returnTo);
                    var options = Options(beat, path);
                    var targets = options.Select(x => Emit(Branch((string?)x["Then"], path), continuation)).ToArray();
                    // Answers offered only if… (D-51): the runtime builds the question from the answers whose conditions
                    // are true; when none is, it goes on after the Choice.
                    if (options.Any(x => Present(x, "When")))
                    {
                        result.Add(EngineNames.Command("Choice") + " " + key + " " + id + " " + string.Join(" ", targets) + " " + (continuation ?? EndScript()));
                        return result;
                    }
                    result.Add("quickQuestion " + ((string?)beat["Prompt"] ?? "") + "#" + string.Join("#", options.Select(x => (string?)x["Text"] ?? ""))
                        + string.Concat(targets.Select(x => "(break)switchEvent " + x)));
                    return result;
                }
                if (type == "Check")
                {
                    // The runtime checks the cases in order and switches to the first true case's script, else the otherwise one.
                    string? continuation = block.Rest is null ? returnTo : Emit(block.Rest, returnTo);
                    var cases = Cases(beat, path);
                    var targets = cases.Select((x, n) => Emit(Branch((string?)x["Then"], $"{path}.Cases[{n}].Then"), continuation)).ToList();
                    targets.Add(Present(beat, "Otherwise") ? Emit(Branch((string?)beat["Otherwise"], path + ".Otherwise"), continuation) : continuation ?? EndScript());
                    result.Add(EngineNames.Command("Check") + " " + key + " " + id + " " + string.Join(" ", targets));
                    return result;
                }
                if (type == "Chance")
                {
                    // The runtime picks an outcome by its weight and switches to its branch's script.
                    string? continuation = block.Rest is null ? returnTo : Emit(block.Rest, returnTo);
                    var targets = Outcomes(beat, path).Select((x, n) => Emit(Branch((string?)x["Then"], $"{path}.Outcomes[{n}].Then"), continuation));
                    result.Add(EngineNames.Command("Chance") + " " + key + " " + id + " " + string.Join(" ", targets));
                    return result;
                }
                if (type == "Goto") { result.Add("switchEvent " + Emit(Branch((string?)beat["Branch"], path + ".Branch"), returnTo)); return result; }
                if (type == "End") { result.AddRange(End()); return result; }
                result.Add(Command(beat, id, path));
            }
            if (block.Name != "main") result.AddRange(returnTo is null ? End() : new[] { "switchEvent " + returnTo });
            return result;
        }

        private string Command(JObject beat, string id, string path)
        {
            string Arg(string field) => Argument((string?)beat[field], path + "." + field);
            string Custom(string name) => name + " " + key + " " + id;
            string Number(string field, int fallback = 0) => Int(beat, field, fallback).ToString(CultureInfo.InvariantCulture);
            switch ((string?)beat["Type"])
            {
                case "Say":
                    // Other lines to pick from (D-51) go through the runtime too.
                    if (Present(beat, "Portrait") || Present(beat, "Expression") || beat["Or"] is JArray { Count: > 0 }) return Custom(EngineNames.Command("Say"));
                    if (!Ids.TryEmotion((string?)beat["Emotion"], out var code)) throw new Problem(path + ".Emotion", "Unknown portrait emotion.");
                    return "speak " + Arg("Actor") + " \"" + (string?)beat["Text"] + code + "\"";
                case "Message": return beat["Or"] is JArray { Count: > 0 } ? Custom(EngineNames.Command("Message")) : "message \"" + (string?)beat["Text"] + "\"";
                case "Clip": return EngineNames.Command("Clip") + " " + key + " " + Arg("Clip") + (Bool(beat, "Hold") ? " hold" : "");
                case "Move": return Custom(EngineNames.Command("Move"));
                case "Face": return "faceDirection " + Arg("Actor") + " " + Facing((string?)beat["Facing"], path + ".Facing");
                case "Emote":
                    if (!Ids.TryEmote((string?)beat["Emote"], out int emote)) throw new Problem(path + ".Emote", "Unknown emote.");
                    return "emote " + Arg("Actor") + " " + emote + (Bool(beat, "Wait", true) ? "" : " true");
                case "Pose": return Custom(EngineNames.Command("Pose"));
                case "Costume": return Custom(EngineNames.Command("Costume"));
                case "Wait": return "pause " + Nonnegative(beat, "Duration", path);
                case "Sound": return Present(beat, "Pitch") ? Custom(EngineNames.Command("Sound")) : "playSound " + Arg("Cue");
                case "Music": return "playMusic " + PackMusic.ResolveCue(packId, Arg("Cue"));
                case "Fade":
                    string direction = Arg("To");
                    if (direction is not ("black" or "clear")) throw new Problem(path + ".To", "Fade target must be black or clear.");
                    int duration = Nonnegative(beat, "Duration", path);
                    double speed = duration == 0 ? 1 : Math.Min(1, 1000d / (60 * (double)duration));
                    return (direction == "black" ? "globalFade " : "globalFadeToClear ") + speed.ToString("0.########", CultureInfo.InvariantCulture)
                        + (Bool(beat, "Wait", true) ? "" : " true");
                // A place is found when the scene starts (D-52), so a cut to a spot there goes through the runtime too.
                case "Camera": return Int(beat, "Duration") > 0 || Anchored(beat["Center"]) ? Custom(EngineNames.Command("Camera")) : "viewport " + Tile(beat["Center"]?["Tile"], path + ".Center.Tile");
                case "Mail": return Present(beat, "Add") ? "addMailReceived " + Arg("Add") : "addMailReceived " + Arg("Remove") + " false";
                case "Friendship": return "friendship " + Arg("Npc") + " " + Number("Points");
                case "Money": return Bool(beat, "HoldUp") ? Custom(EngineNames.Command("Give")) : "money " + Number("Amount");
                case "Item": return Bool(beat, "HoldUp") || beat["OneOf"] is JArray { Count: > 0 } ? Custom(EngineNames.Command("Give")) : "addItem " + Arg("Item") + " " + Number("Count", 1);
                case "Quest": return Present(beat, "Add") ? "addQuest " + Arg("Add") : "removeQuest " + Arg("Remove");
                case "Video": return EngineNames.Command("Video") + " " + packHash + "." + Arg("Video");
                case "Effect": return Custom(EngineNames.Command("Effect"));
                case "Prop": return Custom(EngineNames.Command("Prop"));
                case "Number": return Custom(EngineNames.Command("Number"));
                case "Shake":
                {
                    var shakeMs = Nonnegative(beat, "Duration", path);
                    // Vanilla shake takes NPCs only; the player shakes with the game's own jitter (SDCS_Shake).
                    return string.Equals((string?)beat["Actor"], "farmer", StringComparison.OrdinalIgnoreCase)
                        ? Custom(EngineNames.Command("Shake")) : "shake " + Arg("Actor") + " " + shakeMs;
                }
                case "Jump": return "jump " + Arg("Actor") + " " + Number("Strength", 8) + (Bool(beat, "Silent") ? " true" : "");
                case "Raw": return SafeSegment((string?)beat["Command"] ?? "", path + ".Command");
                default: throw new Problem(path + ".Type", "Unknown beat type.", "Type");
            }
        }

        private List<string> Header()
        {
            var actors = scene["Actors"] as JArray ?? new JArray();
            var headerActors = actors.OfType<JObject>().Where(x => !string.Equals((string?)x["Kind"], "Temporary", StringComparison.OrdinalIgnoreCase)).ToList();
            if (!headerActors.Any(x => string.Equals((string?)x["Id"], "farmer", StringComparison.OrdinalIgnoreCase)))
                headerActors.Insert(0, new JObject { ["Id"] = "farmer", ["Start"] = actors.FirstOrDefault()?["Start"]?.DeepClone() ?? new JObject { ["Tile"] = new JArray(0, 0) }, ["Facing"] = "down" });
            string camera = scene["Camera"]?.Type == JTokenType.String
                ? ((string?)scene["Camera"] == "follow" ? "follow" : throw new Problem("Camera", "Expected follow or a camera position."))
                : scene["Camera"] is JObject sceneCamera && (sceneCamera["Tile"] != null || Anchored(sceneCamera)) ? HeaderTile(sceneCamera, "Camera")
                : actors.FirstOrDefault()?["Start"] is JObject firstStart && (firstStart["Tile"] != null || Anchored(firstStart)) ? HeaderTile(firstStart, "Camera") : "0 0";
            var entries = headerActors.Select((actor, i) => Argument((string?)actor["Id"], $"Actors[{i}].Id") + " "
                + HeaderTile(actor["Start"], $"Actors[{i}].Start") + " " + Facing((string?)actor["Facing"] ?? "down", $"Actors[{i}].Facing"));
            return new() { Argument(PackMusic.ResolveCue(packId, (string?)scene["Music"] ?? "none"), "Music"), camera, string.Join(" ", entries) };
        }

        private void AddTemporaryActors(List<string> parts)
        {
            foreach (var actor in (scene["Actors"] as JArray ?? new JArray()).OfType<JObject>())
            {
                if (!string.Equals((string?)actor["Kind"], "Temporary", StringComparison.OrdinalIgnoreCase)) continue;
                string asset = (string?)actor["Sprite"]?["Asset"] ?? "";
                string kind;
                string relative;
                if (asset.StartsWith("Characters/Monsters/", StringComparison.OrdinalIgnoreCase)) { kind = "monster"; relative = asset[20..]; }
                else if (asset.StartsWith("Characters/", StringComparison.OrdinalIgnoreCase)) { kind = "character"; relative = asset[11..]; }
                else if (asset.StartsWith("Animals/", StringComparison.OrdinalIgnoreCase)) { kind = "animal"; relative = asset[8..]; }
                else throw new Problem("Actors.Sprite.Asset", "Temporary sprite must be under Characters/ or Animals/.");
                relative = relative.Replace('/', '\\');
                string dimensions = Tile(actor["FrameSize"] ?? new JArray(16, 32), "Actors.FrameSize");
                parts.Add("addTemporaryActor " + Argument(relative, "Actors.Sprite.Asset") + " " + dimensions + " "
                    + HeaderTile(actor["Start"], "Actors.Start") + " " + Facing((string?)actor["Facing"] ?? "down", "Actors.Facing")
                    + " true " + kind + " " + Argument((string?)actor["Id"], "Actors.Id"));
            }
        }

        private string MainKey()
        {
            if (scene["Trigger"] is not JObject trigger) return key;
            var conditions = (trigger["Preconditions"] as JArray ?? new JArray()).Select((x, i) => SafeSegment((string?)x ?? "", $"Trigger.Preconditions[{i}]")).ToList();
            if ((string?)trigger["Conditions"] is { Length: > 0 } gsq) conditions.Add("G " + SafeSegment(gsq, "Trigger.Conditions"));
            if ((string?)trigger["OnDayAfterFlag"] is { Length: > 0 } flag) conditions.Add("n " + Argument(flag + "_Ready", "Trigger.OnDayAfterFlag"));
            // A scene placed in the spouse's room (D-52) waits until the player has one: the same check the engine places it with.
            foreach (string place in places) conditions.Add("G " + EngineNames.Command(Places.Query) + " " + place);
            // Named keys need a slash to be naturally triggerable even with no explicit restrictions.
            if (conditions.Count == 0) conditions.Add("G TRUE");
            return key + "/" + string.Join("/", conditions);
        }

        private List<string> SkipActions()
        {
            var result = new List<string>();
            var onSkip = scene["OnSkip"] as JArray ?? new JArray();
            for (int i = 0; i < onSkip.Count; i++)
            {
                var token = (JObject)onSkip[i]!;
                string path = $"OnSkip[{i}]";
                ResolveBeatText(token, path);
                string id = "skip." + i;
                beats.Add(id, new(id, path, token.ToObject<Beat>(PackJson.CreateSerializer())!));
                string Arg(string field) => Argument((string?)token[field], path + "." + field);
                string action = (string?)token["Type"] switch
                {
                    "Mail" => Present(token, "Add") ? "AddMail Current " + Arg("Add") + " received" : "RemoveMail Current " + Arg("Remove"),
                    "Money" => "AddMoney " + Int(token, "Amount"),
                    "Item" => "AddItem " + Arg("Item") + " " + Int(token, "Count", 1),
                    "Friendship" => "AddFriendshipPoints " + Arg("Npc") + " " + Int(token, "Points"),
                    "Quest" => Present(token, "Add") ? "AddQuest " + Arg("Add") : "RemoveQuest " + Arg("Remove"),
                    "Costume" => Ids.SkipCostumeAction + " " + key + " " + id,
                    "Number" => Ids.SkipNumberAction + " " + key + " " + id,
                    _ => throw new Problem("OnSkip", "Only logic beats are allowed on skip.")
                };
                if (action.Contains('#')) throw new Problem("OnSkip", "Skip action arguments cannot contain #.", "V6");
                if (action.Length > 0) result.Add(action);
            }
            return result;
        }

        private void ResolveBeatText(JObject beat, string path)
        {
            switch ((string?)beat["Type"])
            {
                case "Say": case "Message":
                    beat["Text"] = Text(beat["Text"], path + ".Text");
                    if (beat["Or"] is JArray lines) for (int i = 0; i < lines.Count; i++) lines[i] = Text(lines[i], path + $".Or[{i}]");
                    break;
                case "Money": case "Item": if (Present(beat, "Message")) beat["Message"] = Text(beat["Message"], path + ".Message"); break;
                case "Choice":
                    if (Present(beat, "Prompt")) beat["Prompt"] = Text(beat["Prompt"], path + ".Prompt", true);
                    var options = Options(beat, path);
                    for (int i = 0; i < options.Count; i++) options[i]["Text"] = Text(options[i]["Text"], path + $".Options[{i}].Text", true);
                    break;
            }
        }

        private string Text(JToken? token, string path, bool choice = false)
        {
            string? value;
            if (token is JObject reference)
            {
                string lookup = (string?)reference["i18n"] ?? throw new Problem(path, "Invalid translation reference.");
                try { value = translate?.Invoke(lookup); }
                catch (Exception ex) { throw new Problem(path, "Cannot resolve translation: " + ex.Message); }
                if (value is null) throw new Problem(path, $"Missing translation '{lookup}'.");
            }
            else value = token?.Type == JTokenType.String ? (string?)token : null;
            if (value is null) throw new Problem(path, "Text must be a string or a translation reference.");
            if (value.Contains('/') || value.Contains('"')) throw new Problem(path, "Text cannot contain / or straight double quotes.", "V5");
            if (choice && (value.Contains('#') || value.Contains('\\') || value.Contains("(break)", StringComparison.Ordinal)))
                throw new Problem(path, "Choice text cannot contain #, backslash, or (break).", "V6");
            return value;
        }

        private string Join(IEnumerable<string> parts, string path)
        {
            var segments = parts.ToArray();
            foreach (string part in segments) SafeSegment(part, path);
            string script = string.Join("/", segments);
            if (script.Split('/').Length != segments.Length) throw new Problem(path, "Compiled command count changed after slash splitting.", "V5");
            if (Encoding.UTF8.GetByteCount(script) >= 65536) issues.Add(new(ValidationSeverity.Warning, file, path, "Compiled script is at least 64 KB.", "V18"));
            return script;
        }

        private static bool IsFork(string type) => type is "Choice" or "Check" or "Chance";

        /// <summary>A script that only ends the scene: where a Check with nothing after it goes when no case is true.</summary>
        private string EndScript()
        {
            if (endScript != null) return endScript;
            string candidate = key + ".finish", scriptKey = candidate;
            for (int variant = 2; !scriptKeys.Add(scriptKey); variant++) scriptKey = candidate + ".v" + variant;
            scripts.Add(scriptKey, Join(new[] { "pause 1" }.Concat(End()), "OnEnd"));
            return endScript = scriptKey;
        }

        private string[] End()
        {
            string variant = (string?)scene["OnEnd"]?["End"] ?? "default";
            // "stay": the player is left where the scene put them (SDCS_End sets the game's return position), not sent back.
            if (variant == "stay") return new[] { EngineNames.Command("End") + " " + key + " stay", "end" };
            return new[] { EngineNames.Command("End") + " " + key, variant == "default" || variant.Length == 0 ? "end" : "end " + SafeSegment(variant, "OnEnd.End") };
        }
        private static string SafeSegment(string value, string path)
        { if (value.Contains('/')) throw new Problem(path, "Event arguments cannot contain /.", "V5"); return value; }
        private static string Argument(string? value, string path)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new Problem(path, "Command argument is required.");
            SafeSegment(value, path);
            if (value.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0) throw new Problem(path, "Unsafe command argument.", "V5");
            return value.Any(char.IsWhiteSpace) ? "\"" + value + "\"" : value;
        }
        private static int Facing(string? value, string path) => Ids.Facings.TryGetValue(value ?? "", out int facing)
            ? facing : throw new Problem(path, "Unknown facing direction.");
        private static string Tile(JToken? token, string path)
        {
            if (token is not JArray array || array.Count != 2 || array.Any(x => x.Type != JTokenType.Integer)) throw new Problem(path, "Expected two integer coordinates.");
            return array[0]!.Value<int>().ToString(CultureInfo.InvariantCulture) + " " + array[1]!.Value<int>().ToString(CultureInfo.InvariantCulture);
        }
        private static bool Anchored(JToken? position) => position is JObject p && p["From"]?.Type == JTokenType.String && ((string?)p["From"])!.Length > 0;
        /// <summary>
        /// A position's tile in the event header. One that counts from a place (D-52) starts at the place's fallback spot
        /// plus its offset; the engine moves it to the real spot when the scene begins, before anything shows.
        /// </summary>
        private static string HeaderTile(JToken? position, string path)
        {
            if (!Anchored(position)) return Tile(position?["Tile"], path + ".Tile");
            string place = (string)position!["From"]!;
            if (!Places.IsKnown(place)) throw new Problem(path + ".From", $"Unknown place '{place}'.", "V20");
            var spot = Places.Fallback(place);
            var offset = position["Tile"] ?? new JArray(0, 0);
            Tile(offset, path + ".Tile");
            return (spot.X + offset[0]!.Value<int>()).ToString(CultureInfo.InvariantCulture) + " " + (spot.Y + offset[1]!.Value<int>()).ToString(CultureInfo.InvariantCulture);
        }
        private static List<JObject> Outcomes(JObject chance, string path)
        {
            if (chance["Outcomes"] is not JArray outcomes || outcomes.Count == 0 || outcomes.Any(x => x is not JObject)) throw new Problem(path + ".Outcomes", "Chance requires outcomes.");
            return outcomes.Cast<JObject>().ToList();
        }
        private static List<JObject> Cases(JObject check, string path)
        {
            if (check["Cases"] is not JArray cases || cases.Count == 0 || cases.Any(x => x is not JObject)) throw new Problem(path + ".Cases", "Check requires cases.");
            return cases.Cast<JObject>().ToList();
        }
        private static List<JObject> Options(JObject choice, string path)
        {
            if (choice["Options"] is not JArray options || options.Count == 0 || options.Any(x => x is not JObject)) throw new Problem(path + ".Options", "Choice requires options.");
            return options.Cast<JObject>().ToList();
        }
        private static bool Bool(JObject obj, string field, bool fallback = false) => obj[field]?.Type == JTokenType.Boolean ? obj[field]!.Value<bool>() : fallback;
        private static bool Present(JObject obj, string field) => obj[field] is { Type: not (JTokenType.Null or JTokenType.Undefined) };
        private static int Int(JObject obj, string field, int fallback = 0) => obj[field]?.Type == JTokenType.Integer ? obj[field]!.Value<int>() : fallback;
        private static int Nonnegative(JObject obj, string field, string path)
        { int value = Int(obj, field); return value >= 0 ? value : throw new Problem(path + "." + field, "Duration must be nonnegative."); }
    }
}
