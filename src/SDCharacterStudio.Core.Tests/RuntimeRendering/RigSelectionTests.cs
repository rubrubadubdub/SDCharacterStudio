using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Runtime.Rendering;
using Xunit;

namespace SDCharacterStudio.Core.Tests.RuntimeRendering;

public sealed class RigSelectionTests
{
    [Fact]
    public void ExplicitPoseRigSelectsItsCustomNeckAndMaskWhenNpcHasSeveralRigs()
    {
        var defaultRig=new CharacterRig {Id="robin-default",Npc="Robin",NeckPoint=new(){{"left",new[]{7,15}}}};
        var customRig=new CharacterRig {Id="robin-custom",Npc="Robin",NeckPoint=new(){{"left",new[]{11,18}}},HeadMask=new(){{"left","custom-head"}}};
        var selected=RigSelection.Select(new[]{defaultRig,customRig},"ROBIN-CUSTOM","robin");
        Assert.Equal(new[]{11,18},selected.NeckPoint["left"]);
        Assert.Equal("custom-head",selected.HeadMask["left"]);
    }

    [Fact]
    public void LayerNamingAnotherNpcUsesThatNpcRig()
    {
        var robin=new CharacterRig {Id="robin",Npc="Robin"};
        var leah=new CharacterRig {Id="leah",Npc="Leah",FrameSize=new[]{24,40}};
        Assert.Equal(new[]{24,40},RigSelection.Select(new[]{robin,leah},"robin","Leah").FrameSize);
    }

    [Fact]
    public void UnspecifiedSourceNpcUsesTheExplicitPoseRigForSheetSources()
    {
        var robin=new CharacterRig {Id="robin",Npc="Robin"};
        Assert.Same(robin,RigSelection.Select(new[]{robin},"robin",null));
    }

    [Fact]
    public void AmbiguousNpcFallbackFailsInsteadOfDependingOnLibraryOrder()
    {
        var rigs=new[]{new CharacterRig {Id="a",Npc="Robin"},new CharacterRig {Id="b",Npc="Robin"}};
        Assert.Throws<ArgumentException>(()=>RigSelection.Select(rigs,null,"Robin"));
    }

    [Fact]
    public void IncompatiblePoseRigDoesNotSilentlySupplyGeometryForAnUnknownNpc()
    {
        var robin=new CharacterRig {Id="robin",Npc="Robin"};
        Assert.Throws<ArgumentException>(()=>RigSelection.Select(new[]{robin},"robin","Leah"));
    }
}
