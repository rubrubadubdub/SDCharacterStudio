using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Migration;
using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Tests.TestSupport;
using Xunit;

namespace SDCharacterStudio.Core.Tests.Data;
public class MigrationTests
{
    [Fact] public void FakeLegacyMigrationChangesCloneAndVersionWithoutDestroyingUnknownFields()
    {
        var input=JObject.Parse("""{"Format":"0.9","Name":"old-name","Future":{"x":1}}""");
        var result=new PackMigrator(new[]{new FakeMigration()}).Migrate(input,"legacy.json");
        Assert.Empty(result.Issues);Assert.True(result.Changed);Assert.Equal("1.0",(string?)result.Data!["Format"]);
        Assert.Equal("old-name",(string?)result.Data["Id"]);Assert.Equal(1,(int)result.Data["Future"]!["x"]!);
        Assert.Equal("0.9",(string?)input["Format"]);Assert.NotNull(input["Name"]);
    }
    [Fact] public void UnsupportedVersionAndBrokenMigratorBecomeLocatedErrors()
    {
        var result=new PackMigrator().Migrate(JObject.Parse("""{"Format":"0.8"}"""),"legacy.json");
        Assert.Contains(result.Issues,i=>i.File=="legacy.json"&&i.JsonPath=="Format"&&i.Rule=="V1");Assert.Null(result.Data);
        var broken=new PackMigrator(new[]{new BrokenMigration()}).Migrate(JObject.Parse("""{"Format":"0.9"}"""),"legacy.json");
        Assert.Contains(broken.Issues,i=>i.JsonPath=="Format");Assert.Null(broken.Data);
    }
    [Fact] public void ReaderRunsRegisteredLegacyTransformBeforeSchemaValidation()
    {
        var fs=MemoryPackFileSystem.Minimal();fs.Files["cutscenes/legacy.json"]="""{"Format":"0.9","Name":"legacy","Location":"Town","Beats":[]}""";
        var result=new PackReader(fs,new PackMigrator(new[]{new FakeMigration()})).Read();
        Assert.Empty(result.Issues);Assert.Equal("legacy",Assert.Single(result.Pack.Cutscenes).Value.Id);
        Assert.Contains("0.9",fs.Files["cutscenes/legacy.json"]);
    }
    private sealed class FakeMigration:IFormatMigration
    {
        public string FromVersion=>"0.9";public string ToVersion=>"1.0";
        public void Apply(JObject data){data["Id"]=data["Name"];data.Remove("Name");}
    }
    private sealed class BrokenMigration:IFormatMigration
    {
        public string FromVersion=>"0.9";public string ToVersion=>"0.9";
        public void Apply(JObject data)=>throw new InvalidOperationException("broken migration");
    }
}
