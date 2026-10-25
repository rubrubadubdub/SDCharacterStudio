using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Bridge;
using Xunit;

namespace SDCharacterStudio.Core.Tests.Bridge;

public sealed class OriginIndexTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "sdcs-origins-" + Guid.NewGuid().ToString("N"));
    private static JObject Vanilla => JObject.Parse("""
        {"GameVersion":"1.6.15","Assets":["Characters/Robin"],"DataKeys":{"Data/Characters":["Robin"],"Data/Hats":["0"]}}
        """);

    [Fact]
    public void Vanilla_load_is_an_edit_and_asset_names_are_normalized()
    {
        var index = Index("""{"Changes":[{"Action":"Load","Target":"characters\\Robin, Characters/NewNpc"}]}""");
        var vanilla = index.Asset("CHARACTERS/robin");
        Assert.True((bool)vanilla["Vanilla"]!);
        Assert.Empty((JArray)vanilla["ProvidedBy"]!);
        Assert.Equal(new[] { "Test.Pack" }, Values(vanilla, "EditedBy"));
        Assert.Equal(new[] { "Test.Pack" }, Values(index.Asset("Characters/NewNpc"), "ProvidedBy"));
    }

    [Fact]
    public void Data_entries_use_case_sensitive_keys_and_ignore_deletions()
    {
        var index = Index("""{"Changes":[{"Action":"EditData","Target":"Data/Characters","Entries":{"Robin":{},"robin":{},"Gone":null}}]}""");
        Assert.Equal(new[] { "Test.Pack" }, Values(index.DataKey("data\\characters", "Robin"), "EditedBy"));
        Assert.Equal(new[] { "Test.Pack" }, Values(index.DataKey("Data/Characters", "robin"), "ProvidedBy"));
        Assert.True((bool)index.DataKey("Data/Characters", "Gone")["Unknown"]!);
        Assert.True((bool)index.DataKey("Data/Characters", "ROBIN")["Unknown"]!);
    }

    [Fact]
    public void Includes_are_pack_relative_and_cycles_and_escape_paths_are_ignored()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "outside.json"), """{"Changes":[{"Action":"Load","Target":"Characters/Escaped"}]}""");
        var index = Index("""{"Changes":[{"Action":"Include","FromFile":"nested/a.json, ../outside.json"}]}""",
            ("nested/a.json", """{"Changes":[{"Action":"Load","Target":"Characters/Included"},{"Action":"Include","FromFile":"content.json"}]}"""));
        Assert.Equal(new[] { "Test.Pack" }, Values(index.Asset("Characters/Included"), "ProvidedBy"));
        Assert.True((bool)index.Asset("Characters/Escaped")["Unknown"]!);
    }

    [Fact]
    public void Simple_tokens_expand_while_unresolved_tokens_and_conditions_are_uncertain()
    {
        var index = Index("""{"Changes":[{"Action":"Load","Target":"Mods/{{ModId}}/{{Season}}"},{"Action":"Load","Target":"Characters/{{NpcName}}_Custom"},{"Action":"Load","Target":"Characters/Conditional","When":{"Season":"summer"}}]}""");
        foreach (string season in new[] { "spring", "summer", "fall", "winter" })
            Assert.True((bool)index.Asset("Mods/Test.Pack/" + season)["Certain"]!);
        Assert.Equal(new[] { "Test.Pack" }, Values(index.Asset("Characters/Leah_Custom"), "ProvidedBy"));
        Assert.False((bool)index.Asset("Characters/Leah_Custom")["Certain"]!);
        Assert.False((bool)index.Asset("Characters/Conditional")["Certain"]!);
    }

    [Fact]
    public void Conditional_includes_propagate_uncertainty_to_their_changes()
    {
        var index = Index("""{"Changes":[{"Action":"Include","FromFile":"conditional.json","When":{"HasMod":"Optional.Mod"}}]}""",
            ("conditional.json", """{"Changes":[{"Action":"Load","Target":"Characters/ConditionalChild"}]}"""));
        Assert.Equal(new[] { "Test.Pack" }, Values(index.Asset("Characters/ConditionalChild"), "ProvidedBy"));
        Assert.False((bool)index.Asset("Characters/ConditionalChild")["Certain"]!);
    }

    [Fact]
    public void Malformed_changes_do_not_hide_later_changes_or_other_packs()
    {
        var good = Pack("Good", """{"Changes":[42,{"Action":"Load","Target":{}},{"Action":"Load","Target":"Characters/Good"}]}""");
        var broken = Pack("Broken", "{ invalid json");
        var ignored = Pack("Other", """{"Changes":[{"Action":"Load","Target":"Characters/Ignored"}]}""", "Some.Other.Framework");
        var index = new OriginIndex(Vanilla, new[] { broken, ignored, good });
        Assert.Equal(new[] { "Good" }, Values(index.Asset("Characters/Good"), "ProvidedBy"));
        Assert.True((bool)index.Asset("Characters/Ignored")["Unknown"]!);
    }

    [Fact]
    public void ToJson_contains_concrete_scanned_index_and_queried_entries_as_detached_snapshots()
    {
        var index = Index("""{"Changes":[{"Action":"EditImage","Target":"Characters/Robin"},{"Action":"Load","Target":"Characters/New"}]}""");
        index.Asset("Characters/Missing");
        index.DataKey("Data/Hats", "Missing");
        JObject snapshot = index.ToJson();
        Assert.NotNull(snapshot["Assets"]!["Characters/Robin"]);
        Assert.NotNull(snapshot["Assets"]!["Characters/New"]);
        Assert.True((bool)snapshot["Assets"]!["Characters/Missing"]!["Unknown"]!);
        Assert.True((bool)snapshot["DataKeys"]!["Data/Hats"]!["0"]!["Vanilla"]!);
        Assert.False((bool)snapshot["DataKeys"]!["Data/Hats"]!["Missing"]!["Certain"]!);
        index.Asset("Characters/New")["Certain"] = false;
        Assert.True((bool)index.Asset("Characters/New")["Certain"]!);
    }

    private OriginIndex Index(string json, params (string Path, string Json)[] includes)
    {
        ModSource pack = Pack("Test.Pack", json);
        foreach (var include in includes)
        {
            string path = Path.Combine(pack.Folder, include.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, include.Json);
        }
        return new OriginIndex(Vanilla, new[] { pack });
    }

    private ModSource Pack(string id, string json, string framework = "Pathoschild.ContentPatcher")
    {
        string folder = Path.Combine(root, id);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "content.json"), json);
        return new ModSource(id, folder, framework);
    }

    private static string[] Values(JObject origin, string property) => origin[property]!.Values<string>().ToArray()!;
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
