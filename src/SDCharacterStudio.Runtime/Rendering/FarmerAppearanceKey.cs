using Microsoft.Xna.Framework.Graphics;

namespace SDCharacterStudio.Runtime.Rendering;

internal readonly record struct FarmerAppearanceKey(
    long Generation, object Farmer, object Renderer, string? TextureName, int Skin, string? Shoes, int HeightOffset,
    bool IsMale, int Hair, int HairStyle, uint HairColor, uint EyeColor, int Accessory,
    string? HatId, int? HatDrawType, bool? HatIgnoreOffset, bool? HatPrismatic,
    int ShirtIndex, Texture2D? ShirtTexture, uint ShirtColor, int PantsIndex, Texture2D? PantsTexture, uint PantsColor,
    object? HairTexture, bool Bathing, int Eyes, bool TimeAllowsEyes, bool InBed, int TimeWentToBed,
    bool PauseAnimation, bool UsingTool, bool? Fishing, int CurrentFrame, bool Flip, int ArmOffset, int PositionOffset,
    int XOffset, Microsoft.Xna.Framework.Vector2 FarmerArmOffset, uint PrismaticColor);
