using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Objects;

namespace SDCharacterStudio.Runtime.Costumes;

/// <summary>Exact display state; actual shirtItem and pantsItem are never replaced or modified.</summary>
internal sealed class CostumeSnapshot
{
    public Hat? Hat;
    public Boots? Boots;
    public string? Shirt, Pants, Shoes;
    public int Hair, Accessory, Skin;
    public Color HairColor, EyeColor;

    public static CostumeSnapshot Capture(Farmer farmer) => new()
    {
        Hat = farmer.hat.Value, Boots = farmer.boots.Value,
        Shirt = farmer.shirt.Value, Pants = farmer.pants.Value, Shoes = farmer.shoes.Value,
        Hair = farmer.hair.Value, Accessory = farmer.accessory.Value, Skin = farmer.skin.Value,
        HairColor = farmer.hairstyleColor.Value, EyeColor = farmer.newEyeColor.Value
    };

    public CostumeSnapshot Clone() => (CostumeSnapshot)MemberwiseClone();

    public void CopyPart(CostumeSnapshot source, string key)
    {
        switch (key)
        {
            case "Hat": Hat = source.Hat; break;
            case "Shoes": Boots = source.Boots; Shoes = source.Shoes; break;
            case "Shirt": Shirt = source.Shirt; break;
            case "Pants": Pants = source.Pants; break;
            case "Hair": Hair = source.Hair; break;
            case "Accessory": Accessory = source.Accessory; break;
            case "Skin": Skin = source.Skin; break;
            case "HairColor": HairColor = source.HairColor; break;
            case "EyeColor": EyeColor = source.EyeColor; break;
        }
    }

    public void Write(Farmer farmer, IEnumerable<string>? keys = null)
    {
        foreach (string key in keys ?? CostumeSet.Keys)
        {
            switch (key)
            {
                case "Hat": farmer.hat.Value = Hat; break;
                case "Shoes": farmer.boots.Value = Boots; farmer.changeShoeColor(Shoes!); break;
                case "Shirt": farmer.shirt.Value = Shirt; break;
                case "Pants": farmer.pants.Value = Pants; break;
                case "Hair": farmer.changeHairStyle(Hair); farmer.hair.Value = Hair; break;
                case "Accessory": farmer.accessory.Value = Accessory; break;
                case "Skin": farmer.changeSkinColor(Skin, force: true); farmer.skin.Value = Skin; break;
                case "HairColor": farmer.changeHairColor(HairColor); break;
                case "EyeColor": farmer.changeEyeColor(EyeColor); break;
            }
        }
        farmer.FarmerRenderer.MarkSpriteDirty();
    }
}
