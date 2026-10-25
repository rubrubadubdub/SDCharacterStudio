namespace SDCharacterStudio.Runtime.Rendering;

[Flags]
public enum FarmerPart
{
    None=0, Head=1, Body=2, Pants=4, Eyes=8, Shirt=16, Accessory=32,
    Hair=64, Hat=128, Arms=256, Shoes=512,
    Base=Head|Body,
    Full=Base|Pants|Eyes|Shirt|Accessory|Hair|Hat|Arms|Shoes
}

public static class FarmerParts
{
    /// <summary>Head is skin and FaceSkin only; add Eyes+Hair+Hat+Accessory for a complete head.</summary>
    public static FarmerPart Parse(string parts)
    {
        if(string.IsNullOrWhiteSpace(parts)) throw new ArgumentException("Farmer parts cannot be empty.",nameof(parts));
        var result=FarmerPart.None;
        foreach(var token in parts.Split('+'))
        {
            var name=token.Trim();
            if(name.Length==0 || !Enum.TryParse<FarmerPart>(name,true,out var part) ||
                !Enum.GetNames(typeof(FarmerPart)).Any(x=>x.Equals(name,StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"Unknown farmer part '{name}'.",nameof(parts));
            result|=part;
        }
        return result;
    }
}
