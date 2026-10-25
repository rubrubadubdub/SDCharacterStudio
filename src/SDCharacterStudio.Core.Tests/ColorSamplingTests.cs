using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Palette;
using Xunit;

namespace SDCharacterStudio.Core.Tests;

public sealed class ColorSamplingTests
{
    [Fact]
    public void SkinUsesMostCommonWarmOpaqueColorAndItsActualShades()
    {
        var rig = Rig("Skin", new() { Mode = "auto-skin", Box = new[] {0,0,7,1} });
        var image = Image(0xFFFFDCB4, 0xFFFFDCB4, 0xFFAA8866, 0xFFFFEEDD, 0xFF00FFFF, 0x80FFDCB4, 0);
        Assert.Equal(new ColorRamp(0xFFAA8866, 0xFFFFDCB4, 0xFFFFEEDD), ColorSampler.Resolve(rig,"Skin", _ => image));
    }

    [Fact]
    public void SkinFallsBackToPeachAndDerivesTheDocumentedDarkShade()
    {
        var rig = Rig("Skin", new() { Mode = "auto-skin" });
        Assert.Equal(new ColorRamp(0xFFD9BB99,0xFFFFDCB4,0xFFFFE8CE), ColorSampler.Resolve(rig,"Skin", _ => Image(0)));
    }

    [Fact]
    public void EdgeCountsFrameBordersAndTransparentNeighboursButNotInterior()
    {
        var rig = Rig("Outline", new() { Mode = "auto-edge" });
        var image = new RgbaImage(3,3,new uint[] {0xFF112233,0xFF112233,0xFF112233,0xFF112233,0xFFFFDCB4,0xFF112233,0xFF112233,0xFF112233,0xFF112233});
        Assert.Equal(new ColorRamp(0xFF112233,0xFF112233,0xFF112233), ColorSampler.Resolve(rig,"Outline", _ => image));
    }

    [Fact]
    public void CommonExcludesOutlineAndSkinAndCanFollowNamedFallback()
    {
        var rig = Rig("Outline", new() { Mode = "pixel", Pixels = new[] {new[]{0,0}} });
        rig.ColorSamples["Shirt"] = new() {Mode="auto-common",Box=new[]{0,0,4,1}};
        rig.ColorSamples["Pants"] = new() {Mode="auto-common",Box=new[]{0,0,2,1},Fallback="Shirt"};
        var image=Image(0xFF102030,0xFFFFDCB4,0xFF6496C8,0xFF6496C8);
        Assert.Equal(new ColorRamp(0xFF486C90,0xFF6496C8,0xFF9ABBDB),ColorSampler.Resolve(rig,"Pants",_=>image));
    }

    [Fact]
    public void PixelSamplesSortExactColorsAndUseTheSpecifiedSourceFrame()
    {
        var rig=Rig("Hair",new(){Mode="pixel",Frame=12,Pixels=new[]{new[]{0,0},new[]{1,0},new[]{2,0}}});
        var seen=-1;
        var ramp=ColorSampler.Resolve(rig,"Hair",index=>{seen=index;return Image(0xFFEEEEEE,0xFF111111,0xFF888888);});
        Assert.Equal(12,seen);
        Assert.Equal(new ColorRamp(0xFF111111,0xFF888888,0xFFEEEEEE),ramp);
    }

    [Fact]
    public void EmptyFallbackCyclesFailRatherThanRecursingForever()
    {
        var rig=Rig("A",new(){Mode="auto-common",Fallback="B"});
        rig.ColorSamples["B"]=new(){Mode="auto-common",Fallback="A"};
        Assert.Throws<ArgumentException>(()=>ColorSampler.Resolve(rig,"A",_=>Image(0)));
    }

    private static CharacterRig Rig(string name,ColorSample sample)=>new(){ColorSamples=new(StringComparer.OrdinalIgnoreCase){{name,sample}}};
    private static RgbaImage Image(params uint[] pixels)=>new(pixels.Length,1,pixels);
}
