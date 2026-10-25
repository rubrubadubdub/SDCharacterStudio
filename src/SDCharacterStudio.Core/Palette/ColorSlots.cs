namespace SDCharacterStudio.Core.Palette;

public static class ColorSlots
{
    public static IReadOnlyList<string> Names { get; } = Array.AsReadOnly(new[] { "Skin", "Outline", "Hair", "Eyes", "Shirt", "Pants", "Shoes", "Custom1", "Custom2", "Erase" });

    public const uint SkinDark = 0xFF7F007F, SkinMid = 0xFFBF00BF, SkinLight = 0xFFFF00FF;
    public const uint Outline = 0xFF400040;
    public const uint HairDark = 0xFF007F7F, HairMid = 0xFF00BFBF, HairLight = 0xFF00FFFF;
    public const uint EyesDark = 0xFF7F7F00, EyesLight = 0xFFFFFF00;
    public const uint ShirtDark = 0xFF7F3F00, ShirtMid = 0xFFBF5F00, ShirtLight = 0xFFFF7F00;
    public const uint PantsDark = 0xFF003F7F, PantsMid = 0xFF005FBF, PantsLight = 0xFF007FFF;
    public const uint ShoesDark = 0xFF3F7F00, ShoesMid = 0xFF5FBF00, ShoesLight = 0xFF7FFF00;
    public const uint Custom1Dark = 0xFF7F0000, Custom1Mid = 0xFFBF0000, Custom1Light = 0xFFFF0001;
    public const uint Custom2Dark = 0xFF00007F, Custom2Mid = 0xFF0000BF, Custom2Light = 0xFF0000FE;
    public const uint Erase = 0xFF00FF01;

    internal static readonly IReadOnlyDictionary<uint, (string Slot, int Shade)> Reserved =
        new Dictionary<uint, (string, int)>
        {
            [SkinDark] = ("Skin", 0), [SkinMid] = ("Skin", 1), [SkinLight] = ("Skin", 2),
            [Outline] = ("Outline", 1),
            [HairDark] = ("Hair", 0), [HairMid] = ("Hair", 1), [HairLight] = ("Hair", 2),
            [EyesDark] = ("Eyes", 0), [EyesLight] = ("Eyes", 2),
            [ShirtDark] = ("Shirt", 0), [ShirtMid] = ("Shirt", 1), [ShirtLight] = ("Shirt", 2),
            [PantsDark] = ("Pants", 0), [PantsMid] = ("Pants", 1), [PantsLight] = ("Pants", 2),
            [ShoesDark] = ("Shoes", 0), [ShoesMid] = ("Shoes", 1), [ShoesLight] = ("Shoes", 2),
            [Custom1Dark] = ("Custom1", 0), [Custom1Mid] = ("Custom1", 1), [Custom1Light] = ("Custom1", 2),
            [Custom2Dark] = ("Custom2", 0), [Custom2Mid] = ("Custom2", 1), [Custom2Light] = ("Custom2", 2)
        };
}
