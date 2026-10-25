using Newtonsoft.Json.Linq;
using SDCharacterStudio.Runtime.Costumes;
using Xunit;

namespace SDCharacterStudio.Core.Tests.RuntimeCostumes;

public class CostumeSetTests
{
    private static Dictionary<string, JToken> Values(string json) => JObject.Parse(json).Properties()
        .ToDictionary(p => p.Name, p => p.Value);

    [Fact]
    public void ParsesEveryPartWithCanonicalKeys()
    {
        var values = CostumeSet.Parse(Values("""
            {"hAt":"(H)Custom.Hat","Shirt":"1010","Pants":"(P)PrismaticPants","Shoes":"(B)504",
             "Hair":100,"HairColor":"#102030","Accessory":-1,"Skin":23,"EyeColor":"#40506080"}
            """));
        Assert.Equal(9, values.Count);
        Assert.Equal("(S)1010", values["Shirt"].ItemId);
        Assert.Equal("(H)Custom.Hat", values["Hat"].ItemId);
        Assert.Equal(0x102030FFu, values["HairColor"].Rgba);
        Assert.Equal(0x40506080u, values["EyeColor"].Rgba);
        Assert.Equal(-1, values["Accessory"].Index);
    }

    [Fact]
    public void UnqualifiedClothingIsResolvedAgainstItsSpecificType()
    {
        var values = CostumeSet.Parse(Values("{\"Shirt\":\"Custom.Shirt\",\"Pants\":\"Custom.Pants\"}"));
        Assert.Equal("(S)Custom.Shirt", values["Shirt"].ItemId);
        Assert.Equal("(P)Custom.Pants", values["Pants"].ItemId);
    }

    [Theory]
    [InlineData("Hat")]
    [InlineData("Shirt")]
    [InlineData("Pants")]
    [InlineData("Shoes")]
    [InlineData("Hair")]
    [InlineData("HairColor")]
    [InlineData("Accessory")]
    [InlineData("Skin")]
    [InlineData("EyeColor")]
    public void EveryPartAcceptsOriginal(string key)
        => Assert.Equal(CostumeValueKind.Original, CostumeSet.Parse(new Dictionary<string, JToken> { [key] = "original" })[key].Kind);

    [Theory]
    [InlineData("{\"Hat\":\"(B)504\"}")]
    [InlineData("{\"Pants\":\"(S)1010\"}")]
    [InlineData("{\"Shoes\":\"504\"}")]
    [InlineData("{\"Hair\":\"1\"}")]
    [InlineData("{\"Hair\":1.5}")]
    [InlineData("{\"Hair\":2147483648}")]
    [InlineData("{\"Hair\":-1}")]
    [InlineData("{\"Skin\":24}")]
    [InlineData("{\"Accessory\":-2}")]
    [InlineData("{\"Accessory\":30}")]
    [InlineData("{\"EyeColor\":\"#00112Z\"}")]
    [InlineData("{\"HairColor\":\"red\"}")]
    [InlineData("{\"Shirt\":null}")]
    [InlineData("{\"Hat\":\"(H)\"}")]
    [InlineData("{\"Hat\":\"(H)one/two\"}")]
    [InlineData("{\"Shirt\":\"none\"}")]
    [InlineData("{\"Pants\":\"none\"}")]
    [InlineData("{\"Skin\":\"none\"}")]
    [InlineData("{\"Unknown\":1}")]
    [InlineData("{\"Hat\":\"original\",\"hat\":\"none\"}")]
    public void RejectsInvalidSetsBeforeReturningAnyPlan(string json)
        => Assert.Throws<ArgumentException>(() => CostumeSet.Parse(Values(json)));

    [Theory]
    [InlineData("Hat")]
    [InlineData("Shoes")]
    [InlineData("Accessory")]
    public void NoneIsOnlyAcceptedWhereNativeRemovalExists(string key)
        => Assert.Equal(CostumeValueKind.None, CostumeSet.Parse(new Dictionary<string, JToken> { [key] = "none" })[key].Kind);
}
