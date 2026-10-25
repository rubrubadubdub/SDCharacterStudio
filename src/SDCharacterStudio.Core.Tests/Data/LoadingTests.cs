using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Tests.TestSupport;
using SDCharacterStudio.Core.Validation;
using Xunit;

namespace SDCharacterStudio.Core.Tests.Data;
public class LoadingTests
{
    [Fact] public void LoadsDefaultGlobsAndCommentsAndRecordsSourceFiles()
    {
        var fs=MemoryPackFileSystem.Minimal();
        fs.Files["cutscenes/talk.json"]="""{/*note*/"Format":"1.0","Id":"talk","Location":"Town","Beats":[]}""";
        var result=new PackReader(fs).Read();
        Assert.Empty(result.Issues); Assert.Equal("talk",Assert.Single(result.Pack.Cutscenes).Value.Id);
        Assert.Equal("cutscenes/talk.json",result.Pack.Cutscenes[0].File);
    }
    [Theory] [InlineData("../outside.json")] [InlineData("C:/outside.json")] [InlineData("/outside.json")] [InlineData("cutscenes/../outside.json")] [InlineData("cutscenes\\outside.json")]
    public void UnsafeIndexPathsAreRejectedBeforeFileSystemAccess(string path)
    {
        var fs=MemoryPackFileSystem.Minimal();fs.Files["studio.json"]="{\"Format\":\"1.0\",\"Cutscenes\":["+System.Text.Json.JsonSerializer.Serialize(path)+"]}";
        var result=new PackReader(fs).Read();
        Assert.Contains(result.Issues,i=>i.Rule=="V4" && i.File=="studio.json" && i.JsonPath=="Cutscenes[0]");
        Assert.DoesNotContain(path,fs.Accesses);
    }
    [Theory] [InlineData("{")] [InlineData("null")] [InlineData("[]")] [InlineData("{\"Cutscenes\":1}")] [InlineData("{} {}")]
    public void MalformedIndexBecomesLocatedIssue(string json)
    {
        var fs=MemoryPackFileSystem.Minimal(); fs.Files["studio.json"]=json;
        var result=new PackReader(fs).Read();
        Assert.Contains(result.Issues,i=>i.Severity==ValidationSeverity.Error && i.File=="studio.json");
    }
    [Fact] public void MissingDefaultsAreOptionalButExplicitReferencesMustExist()
    {
        var fs=MemoryPackFileSystem.Minimal(); Assert.Empty(new PackReader(fs).Read().Issues);
        fs.Files["studio.json"]="""{"Format":"1.0","Library":{"Masks":"missing.json"},"Cutscenes":["missing-cutscene.json"]}""";
        var result=new PackReader(fs).Read();
        Assert.Contains(result.Issues,i=>i.Rule=="V4" && i.JsonPath=="Library.Masks");
        Assert.Contains(result.Issues,i=>i.Rule=="V4" && i.JsonPath=="Cutscenes[0]");
    }

    [Fact] public void MissingRequiredBeatListAndCaseAliasedBranchKeysAreErrors()
    {
        var fs=MemoryPackFileSystem.Minimal();
        fs.Files["cutscenes/missing.json"]="""{"Format":"1.0","Id":"missing","Location":"Town"}""";
        fs.Files["cutscenes/duplicates.json"]="""{"Format":"1.0","Id":"duplicates","Location":"Town","Beats":[],"Branches":{"one":[],"ONE":[]}}""";
        var result=new PackReader(fs).Read();
        Assert.Contains(result.Issues,i=>i.File=="cutscenes/missing.json"&&i.JsonPath=="Beats");
        Assert.Contains(result.Issues,i=>i.File=="cutscenes/duplicates.json"&&i.Rule=="V3");
    }

    [Fact] public void UnknownDiscriminatorWithObjectValueIsLocatedWithoutThrowing()
    {
        var fs=MemoryPackFileSystem.Minimal();
        fs.Files["cutscenes/unknown.json"]="""{"Format":"1.0","Id":"unknown","Location":"Town","Beats":[{"Type":{"unknown":1}}]}""";
        var result=new PackReader(fs).Read();
        Assert.Contains(result.Issues,i=>i.File=="cutscenes/unknown.json"&&i.JsonPath=="Beats[0].Type");
    }

    [Fact] public void InvalidRigKindAndNonstringVersionBecomeSchemaIssues()
    {
        var fs=MemoryPackFileSystem.Minimal();fs.Files["studio.json"]="""{"Format":1}""";
        fs.Files["library/characters/robin.json"]="""{"Format":"1.0","Id":"robin","Kind":{"bad":true}}""";
        var result=new PackReader(fs).Read();
        Assert.Contains(result.Issues,i=>i.File=="studio.json"&&i.JsonPath=="Format");
        Assert.Contains(result.Issues,i=>i.File=="library/characters/robin.json"&&i.JsonPath=="Kind");
    }

    [Theory]
    [InlineData("{\"Type\":\"Wait\",\"Duration\":\"100\"}","Beats[0].Duration")]
    [InlineData("{\"Type\":\"Item\",\"Item\":\"(O)709\",\"Count\":1.2}","Beats[0].Count")]
    [InlineData("{\"Type\":\"Emote\",\"Actor\":\"Robin\",\"Emote\":{\"bad\":1}}","Beats[0].Emote")]
    public void WrongScalarAndUnionTypesAreLocatedInsteadOfCoerced(string beat,string path)
    {
        var fs=MemoryPackFileSystem.Minimal();fs.Files["cutscenes/wrong.json"]="{\"Format\":\"1.0\",\"Id\":\"wrong\",\"Location\":\"Town\",\"Beats\":["+beat+"]}";
        var result=new PackReader(fs).Read();
        Assert.Contains(result.Issues,i=>i.File=="cutscenes/wrong.json"&&i.JsonPath==path&&i.Severity==ValidationSeverity.Error);
    }
}
