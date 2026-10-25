using Xunit;

namespace SDCharacterStudio.Core.Tests;

/// <summary>
/// D-44: the framework keeps its published names (saves made with it still restore costumes); an embedded copy's
/// names come from its mod's ID. These read the framework defaults only, so they never race other tests.
/// </summary>
public sealed class EngineNamesTests
{
    [Fact]
    public void TheFrameworkKeepsItsPublishedNames()
    {
        Assert.False(EngineNames.IsEmbedded);
        Assert.Equal("SDCS_Clip", EngineNames.Command("Clip"));
        Assert.Equal("SDCS_SkipCostume", Ids.SkipCostumeAction);
        Assert.Equal("Player.SDCharacterStudio/CostumeBackup", EngineNames.DataKey("CostumeBackup"));
    }

    [Fact]
    public void AnEmbeddedPrefixIsStableForItsMod()
    {
        // The prefixes the benches report in the game (benches/README.md).
        Assert.Equal("SDCS907dfe26", EngineNames.FrameworkPrefix + Ids.PackHash("Bench.SdcsCode"));
        Assert.Equal("SDCS587426e8", EngineNames.FrameworkPrefix + Ids.PackHash("Bench.SdcsNoCode"));
    }
}
