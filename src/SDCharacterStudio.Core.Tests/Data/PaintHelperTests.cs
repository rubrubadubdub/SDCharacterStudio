using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Palette;
using SDCharacterStudio.Core.Tests.TestSupport;
using SDCharacterStudio.Core.Validation;
using Xunit;

namespace SDCharacterStudio.Core.Tests.Data;

/// <summary>The shading brush's steps and live recolours (after Pixelorama's shading tool and colour effects).</summary>
public sealed class PaintHelperTests
{
    private const uint SkinDark = 0xFFB06040, SkinMid = 0xFFE09070, SkinLight = 0xFFF8C0A0;
    private static readonly Shading.Ramp Skin = new(new uint?[] { SkinDark, SkinMid, SkinLight }, new uint?[] { ColorSlots.SkinDark, ColorSlots.SkinMid, ColorSlots.SkinLight });
    private static readonly Shading.Ramp Eyes = new(new uint?[] { 0xFF202040, null, 0xFF6060C0 }, new uint?[] { ColorSlots.EyesDark, null, ColorSlots.EyesLight });

    [Fact]
    public void ShadingStepsAlongHerColoursAndKeepsTheSlot()
    {
        var ramps = new[] { Skin, Eyes };
        // Her own pixel (nothing painted there): one step, painted as the slot so it stays live.
        Assert.Equal(ColorSlots.SkinDark, Shading.Step(SkinMid, 0, lighter: false, ramps));
        Assert.Equal(ColorSlots.SkinLight, Shading.Step(SkinMid, 0, lighter: true, ramps));
        // Already painted with a slot: it steps from what's painted.
        Assert.Equal(ColorSlots.SkinMid, Shading.Step(SkinLight, ColorSlots.SkinLight, lighter: false, ramps));
        // A ramp without a mid steps over the gap.
        Assert.Equal(ColorSlots.EyesDark, Shading.Step(0xFF6060C0, ColorSlots.EyesLight, lighter: false, ramps));
        // Past the end, and any other colour: by eye (darker leans blue and darker).
        uint past = Shading.Step(SkinDark, ColorSlots.SkinDark, lighter: false, ramps)!.Value;
        Assert.DoesNotContain(past, new[] { ColorSlots.SkinDark, SkinDark });
        Assert.True(Shading.Hsv(past).Value < Shading.Hsv(SkinDark).Value);
        Assert.Null(Shading.Step(0, 0, false, ramps));                                  // nothing there to shade
    }

    [Fact]
    public void HueShiftingLeansDarkToBlueAndLightToYellow()
    {
        uint red = 0xFFC03030;
        var (h, s, v) = Shading.Hsv(red);
        var dark = Shading.Hsv(Shading.HueShift(red, lighter: false));
        var light = Shading.Hsv(Shading.HueShift(red, lighter: true));
        Assert.True(dark.Value < v && light.Value > v);
        Assert.True(dark.Hue > 300 || dark.Hue < h);                                    // towards blue (round past 360)
        Assert.True(light.Hue > h && light.Hue <= 60);                                  // towards yellow
        Assert.True(dark.Saturation > s && light.Saturation < s);
        var grey = Shading.Hsv(Shading.HueShift(0xFF808080, false));
        Assert.True(grey.Saturation < 0.01);                                            // greys only get darker
        var ramp = Shading.RampFor(red);
        Assert.Equal(red, ramp.Mid);
        Assert.True(Shading.Hsv(ramp.Dark).Value < v && Shading.Hsv(ramp.Light).Value > v);
    }

    [Fact]
    public void ARecolourMovesEveryShadeOfHerColourAndNothingElse()
    {
        // A blue dress in three shades, her skin, and a green leaf.
        uint dressDark = 0xFF203070, dressMid = 0xFF3050B0, dressLight = 0xFF6080E0, skin = 0xFFF0C8A8, leaf = 0xFF30A040;
        var image = new RgbaImage(5, 1, new[] { dressDark, dressMid, dressLight, skin, leaf });
        Recolor.Apply(image, new[] { new RecolorRule { From = "abigail.Dress", To = "#C03030" } }, source => source == "abigail.Dress" ? new ColorRamp(dressDark, dressMid, dressLight) : throw new ArgumentException("?"));
        var hues = image.Pixels.Take(3).Select(p => Shading.Hsv(p).Hue).ToList();
        Assert.All(hues, hue => Assert.True(hue < 20 || hue > 340, $"hue {hue} isn't red"));   // all three shades are red now
        var values = image.Pixels.Take(3).Select(p => Shading.Hsv(p).Value).ToList();
        Assert.True(values[0] < values[1] && values[1] < values[2]);                   // still dark, mid, light
        Assert.Equal(0xFFC03030u, image.Pixels[1]);                                     // her mid becomes exactly the new colour
        Assert.Equal(skin, image.Pixels[3]);
        Assert.Equal(leaf, image.Pixels[4]);
    }

    [Fact]
    public void AGreyRecolourMatchesByBrightnessAndABadSourceIsSkipped()
    {
        uint greyDark = 0xFF404040, greyMid = 0xFF808080, red = 0xFFC03030;
        var image = new RgbaImage(3, 1, new[] { greyDark, greyMid, red });
        Recolor.Apply(image, new[] { new RecolorRule { From = "#808080", To = "#3060C0" }, new RecolorRule { From = "nobody.Hair", To = "#00FF00" } },
            _ => throw new ArgumentException("No rig matches"));
        Assert.Equal(0xFF3060C0u, image.Pixels[1]);
        Assert.True(Shading.Hsv(image.Pixels[0]).Value < Shading.Hsv(image.Pixels[1]).Value);
        Assert.Equal(red, image.Pixels[2]);
    }

    [Fact]
    public void RecoloursAreValidated()
    {
        var files = MemoryPackFileSystem.Minimal();
        files.Files["library/poses/red.json"] = """
            {"Format":"1.0","Id":"red","FrameSize":[16,32],"Frames":[{"Duration":100,"Layers":[{"Type":"Live","Source":{"Kind":"Npc","Npc":"Abigail"},
              "Recolor":[{"From":"#3050B0","To":"#C03030"},{"From":"nobody.Shirt","To":"#C03030"},{"From":"#3050B0"},{"From":"#3050B0","To":"#C03030","Range":0}]}]}]}
            """;
        var issues = new PackReader(files).Read(exportValidation: false).Issues.Where(i => i.Severity == ValidationSeverity.Error).ToList();
        Assert.DoesNotContain(issues, i => i.JsonPath.Contains("Recolor[0]"));
        Assert.Contains(issues, i => i.JsonPath.EndsWith("Recolor[1].From") && i.Message.Contains("Unknown colour source 'nobody.Shirt'"));
        Assert.Contains(issues, i => i.JsonPath.EndsWith("Recolor[2].To"));
        Assert.Contains(issues, i => i.JsonPath.EndsWith("Recolor[3].Range"));
    }
}
