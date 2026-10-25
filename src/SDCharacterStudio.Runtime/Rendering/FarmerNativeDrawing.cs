using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Objects;
using StardewValley.ItemTypeDefinitions;
using static StardewValley.FarmerRenderer;
using FarmerSpriteLayers=StardewValley.FarmerRenderer.FarmerSpriteLayers;

namespace SDCharacterStudio.Runtime.Rendering;

public sealed partial class FarmerPartsRenderer
{
    // Ported from the installed Stardew Valley 1.6.15 drawHairAndAccesories.
    // The per-draw toggles intentionally leave native position adjustments intact.
	private void DrawHairAndAccessories(SpriteBatch b, int facingDirection, Farmer who, Vector2 position, Vector2 origin, float scale, int currentFrame, float rotation, Color overrideColor, float layerDepth, FarmerPart parts, Vector2 positionOffset, bool frameFlip)
	{
        var skin=who.FarmerRenderer.skin;
        var heightOffset=who.FarmerRenderer.heightOffset;
        Rectangle shirtSourceRect,hairstyleSourceRect,hatSourceRect=default,accessorySourceRect=default;
        var rotationAdjustment=Vector2.Zero;
		int num = who.getHair();
		float num2 = 4f * scale;
		int num3 = featureXOffsetPerFrame[currentFrame];
		int num4 = featureYOffsetPerFrame[currentFrame];
		HairStyleMetadata hairStyleMetadata = Farmer.GetHairStyleMetadata(num);
		Hat value = who.hat.Value;
		if (value != null && value.hairDrawType.Value == 1 && hairStyleMetadata != null && hairStyleMetadata.coveredIndex != -1)
		{
			num = hairStyleMetadata.coveredIndex;
			hairStyleMetadata = Farmer.GetHairStyleMetadata(num);
		}
		who.GetDisplayShirt(out var texture, out var spriteIndex);
		Color color = (who.prismaticHair.Value ? Utility.GetPrismaticColor() : who.hairstyleColor.Value);
		shirtSourceRect = new Rectangle(spriteIndex * 8 % 128, spriteIndex * 8 / 128 * 32, 8, 8);
		Texture2D texture2 = hairStyleMetadata?.texture ?? hairStylesTexture;
		hairstyleSourceRect = ((hairStyleMetadata != null) ? new Rectangle(hairStyleMetadata.tileX * 16, hairStyleMetadata.tileY * 16, 16, 32) : new Rectangle(num * 16 % hairStylesTexture.Width, num * 16 / hairStylesTexture.Width * 96, 16, 32));
		if (who.accessory.Value >= 0)
		{
			accessorySourceRect = new Rectangle(who.accessory.Value * 16 % accessoriesTexture.Width, who.accessory.Value * 16 / accessoriesTexture.Width * 32, 16, 16);
		}
		Texture2D texture3 = hatsTexture;
		bool flag = false;
		if (who.hat.Value != null)
		{
			ParsedItemData dataOrErrorItem = ItemRegistry.GetDataOrErrorItem(who.hat.Value.QualifiedItemId);
			int spriteIndex2 = dataOrErrorItem.SpriteIndex;
			texture3 = dataOrErrorItem.GetTexture();
			hatSourceRect = new Rectangle(20 * spriteIndex2 % texture3.Width, 20 * spriteIndex2 / texture3.Width * 20 * 4, 20, 20);
			if (dataOrErrorItem.IsErrorItem)
			{
				hatSourceRect = dataOrErrorItem.GetSourceRect();
				flag = true;
			}
		}
		FarmerSpriteLayers layer = FarmerSpriteLayers.Accessory;
		if (who.accessory.Value >= 0 && who.FarmerRenderer.drawAccessoryBelowHair(who.accessory.Value))
		{
			layer = FarmerSpriteLayers.AccessoryUnderHair;
		}
		switch (facingDirection)
		{
		case 0:
		{
			shirtSourceRect.Offset(0, 24);
			hairstyleSourceRect.Offset(0, 64);
			Rectangle value2 = shirtSourceRect;
			value2.Offset(128, 0);
			if (!flag && who.hat.Value != null)
			{
				hatSourceRect.Offset(0, 60);
			}
			if (!who.bathingClothes.Value && (skin.Value != -12345 || who.shirtItem.Value != null))
			{
				Vector2 position4 = position + origin + positionOffset + new Vector2(16f * scale + (float)(num3 * 4), (float)(56 + num4 * 4) + (float)heightOffset.Value * scale);
				if (parts.HasFlag(FarmerPart.Shirt)) b.Draw(texture, position4, shirtSourceRect, overrideColor.Equals(Color.White) ? Color.White : overrideColor, rotation, origin, num2, SpriteEffects.None, GetLayerDepth(layerDepth, FarmerSpriteLayers.Shirt));
				if (parts.HasFlag(FarmerPart.Shirt)) b.Draw(texture, position4, value2, overrideColor.Equals(Color.White) ? Utility.MakeCompletelyOpaque(who.GetShirtColor()) : overrideColor, rotation, origin, num2, SpriteEffects.None, GetLayerDepth(layerDepth, FarmerSpriteLayers.Shirt, dyeLayer: true));
			}
			if (parts.HasFlag(FarmerPart.Hair) && num>=0) b.Draw(texture2, position + origin + positionOffset + new Vector2(num3 * 4, num4 * 4 + 4 + ((who.IsMale && num >= 16) ? (-4) : ((!who.IsMale && num < 16) ? 4 : 0))), hairstyleSourceRect, overrideColor.Equals(Color.White) ? color : overrideColor, rotation, origin, num2, SpriteEffects.None, GetLayerDepth(layerDepth, FarmerSpriteLayers.Hair));
			break;
		}
		case 1:
		{
			shirtSourceRect.Offset(0, 8);
			hairstyleSourceRect.Offset(0, 32);
			Rectangle value2 = shirtSourceRect;
			value2.Offset(128, 0);
			if (!flag && who.hat.Value != null)
			{
				hatSourceRect.Offset(0, 20);
			}
			if (rotation != -(float)Math.PI / 32f)
			{
				if (rotation == (float)Math.PI / 32f)
				{
					rotationAdjustment.X = -6f;
					rotationAdjustment.Y = 1f;
				}
			}
			else
			{
				rotationAdjustment.X = 6f;
				rotationAdjustment.Y = -2f;
			}
			if (!who.bathingClothes.Value && (skin.Value != -12345 || who.shirtItem.Value != null))
			{
				Vector2 position5 = position + origin + positionOffset + rotationAdjustment + new Vector2(16f * scale + (float)(num3 * 4), 56f * scale + (float)(num4 * 4) + (float)heightOffset.Value * scale);
				if (parts.HasFlag(FarmerPart.Shirt)) b.Draw(texture, position5, shirtSourceRect, overrideColor.Equals(Color.White) ? Color.White : overrideColor, rotation, origin, num2, SpriteEffects.None, GetLayerDepth(layerDepth, FarmerSpriteLayers.Shirt));
				if (parts.HasFlag(FarmerPart.Shirt)) b.Draw(texture, position5, value2, overrideColor.Equals(Color.White) ? Utility.MakeCompletelyOpaque(who.GetShirtColor()) : overrideColor, rotation, origin, num2, SpriteEffects.None, GetLayerDepth(layerDepth, FarmerSpriteLayers.Shirt, dyeLayer: true));
			}
			if (who.accessory.Value >= 0)
			{
				accessorySourceRect.Offset(0, 16);
				if (parts.HasFlag(FarmerPart.Accessory)) b.Draw(accessoriesTexture, position + origin + positionOffset + rotationAdjustment + new Vector2(num3 * 4, 4 + num4 * 4 + heightOffset.Value), accessorySourceRect, (overrideColor.Equals(Color.White) && who.FarmerRenderer.isAccessoryFacialHair(who.accessory.Value)) ? color : overrideColor, rotation, origin, num2, SpriteEffects.None, GetLayerDepth(layerDepth, layer));
			}
			if (parts.HasFlag(FarmerPart.Hair) && num>=0) b.Draw(texture2, position + origin + positionOffset + new Vector2(num3 * 4, num4 * 4 + ((who.IsMale && who.hair.Value >= 16) ? (-4) : ((!who.IsMale && who.hair.Value < 16) ? 4 : 0))), hairstyleSourceRect, overrideColor.Equals(Color.White) ? color : overrideColor, rotation, origin, num2, SpriteEffects.None, GetLayerDepth(layerDepth, FarmerSpriteLayers.Hair));
			break;
		}
		case 2:
		{
			Rectangle value2 = shirtSourceRect;
			value2.Offset(128, 0);
			if (!who.bathingClothes.Value && (skin.Value != -12345 || who.shirtItem.Value != null))
			{
				Vector2 position3 = position + origin + positionOffset + new Vector2(16 + num3 * 4, (float)(56 + num4 * 4) + (float)heightOffset.Value * scale);
				if (parts.HasFlag(FarmerPart.Shirt)) b.Draw(texture, position3, shirtSourceRect, overrideColor.Equals(Color.White) ? Color.White : overrideColor, rotation, origin, num2, SpriteEffects.None, GetLayerDepth(layerDepth, FarmerSpriteLayers.Shirt));
				if (parts.HasFlag(FarmerPart.Shirt)) b.Draw(texture, position3, value2, overrideColor.Equals(Color.White) ? Utility.MakeCompletelyOpaque(who.GetShirtColor()) : overrideColor, rotation, origin, num2, SpriteEffects.None, GetLayerDepth(layerDepth, FarmerSpriteLayers.Shirt, dyeLayer: true));
			}
			if (who.accessory.Value >= 0)
			{
				if (who.accessory.Value == 26)
				{
					switch (currentFrame)
					{
					case 24:
					case 25:
					case 26:
					case 70:
						positionOffset.Y += 4f;
						break;
					}
				}
				if (parts.HasFlag(FarmerPart.Accessory)) b.Draw(accessoriesTexture, position + origin + positionOffset + rotationAdjustment + new Vector2(num3 * 4, 8 + num4 * 4 + heightOffset.Value - 4), accessorySourceRect, (overrideColor.Equals(Color.White) && who.FarmerRenderer.isAccessoryFacialHair(who.accessory.Value)) ? color : overrideColor, rotation, origin, num2, SpriteEffects.None, GetLayerDepth(layerDepth, layer));
			}
			if (parts.HasFlag(FarmerPart.Hair) && num>=0) b.Draw(texture2, position + origin + positionOffset + new Vector2(num3 * 4, num4 * 4 + ((who.IsMale && who.hair.Value >= 16) ? (-4) : ((!who.IsMale && who.hair.Value < 16) ? 4 : 0))), hairstyleSourceRect, overrideColor.Equals(Color.White) ? color : overrideColor, rotation, origin, num2, SpriteEffects.None, GetLayerDepth(layerDepth, FarmerSpriteLayers.Hair));
			break;
		}
		case 3:
		{
			bool flag2 = true;
			shirtSourceRect.Offset(0, 16);
			Rectangle value2 = shirtSourceRect;
			value2.Offset(128, 0);
			if (hairStyleMetadata != null && hairStyleMetadata.usesUniqueLeftSprite)
			{
				flag2 = false;
				hairstyleSourceRect.Offset(0, 96);
			}
			else
			{
				hairstyleSourceRect.Offset(0, 32);
			}
			if (!flag && who.hat.Value != null)
			{
				hatSourceRect.Offset(0, 40);
			}
			if (rotation != -(float)Math.PI / 32f)
			{
				if (rotation == (float)Math.PI / 32f)
				{
					rotationAdjustment.X = -5f;
					rotationAdjustment.Y = 1f;
				}
			}
			else
			{
				rotationAdjustment.X = 6f;
				rotationAdjustment.Y = -2f;
			}
			if (!who.bathingClothes.Value && (skin.Value != -12345 || who.shirtItem.Value != null))
			{
				Vector2 position2 = position + origin + positionOffset + rotationAdjustment + new Vector2(16f * scale - (float)(num3 * 4), 56f * scale + (float)(num4 * 4) + (float)heightOffset.Value * scale);
				if (parts.HasFlag(FarmerPart.Shirt)) b.Draw(texture, position2, shirtSourceRect, overrideColor.Equals(Color.White) ? Color.White : overrideColor, rotation, origin, num2, SpriteEffects.None, GetLayerDepth(layerDepth, FarmerSpriteLayers.Shirt));
				if (parts.HasFlag(FarmerPart.Shirt)) b.Draw(texture, position2, value2, overrideColor.Equals(Color.White) ? Utility.MakeCompletelyOpaque(who.GetShirtColor()) : overrideColor, rotation, origin, num2, SpriteEffects.None, GetLayerDepth(layerDepth, FarmerSpriteLayers.Shirt, dyeLayer: true));
			}
			if (who.accessory.Value >= 0)
			{
				accessorySourceRect.Offset(0, 16);
				if (parts.HasFlag(FarmerPart.Accessory)) b.Draw(accessoriesTexture, position + origin + positionOffset + rotationAdjustment + new Vector2(-num3 * 4, 4 + num4 * 4 + heightOffset.Value), accessorySourceRect, (overrideColor.Equals(Color.White) && who.FarmerRenderer.isAccessoryFacialHair(who.accessory.Value)) ? color : overrideColor, rotation, origin, num2, SpriteEffects.FlipHorizontally, GetLayerDepth(layerDepth, layer));
			}
			if (parts.HasFlag(FarmerPart.Hair) && num>=0) b.Draw(texture2, position + origin + positionOffset + new Vector2(-num3 * 4, num4 * 4 + ((who.IsMale && who.hair.Value >= 16) ? (-4) : ((!who.IsMale && who.hair.Value < 16) ? 4 : 0))), hairstyleSourceRect, overrideColor.Equals(Color.White) ? color : overrideColor, rotation, origin, num2, flag2 ? SpriteEffects.FlipHorizontally : SpriteEffects.None, GetLayerDepth(layerDepth, FarmerSpriteLayers.Hair));
			break;
		}
		}
		if (parts.HasFlag(FarmerPart.Hat) && who.hat.Value != null && !who.bathingClothes.Value)
		{
			bool flip = frameFlip;
			int num5 = ((!who.hat.Value.ignoreHairstyleOffset.Value) ? hairstyleHatOffset[who.hair.Value % 16] : 0);
			Vector2 position6 = position + origin + positionOffset + new Vector2((0f - num2) * 2f + (float)(((!flip) ? 1 : (-1)) * num3) * num2, (0f - num2) * 4f + (float)(num4 * 4) + (float)num5 + 4f + (float)heightOffset.Value);
			Color color2 = (who.hat.Value.isPrismatic.Value ? Utility.GetPrismaticColor() : overrideColor);
			if (!flag && who.hat.Value.isMask && facingDirection == 0)
			{
				Rectangle value3 = hatSourceRect;
				value3.Height -= 11;
				value3.Y += 11;
				b.Draw(texture3, position + origin + positionOffset + new Vector2(0f, 11f * num2) + new Vector2((0f - num2) * 2f + (float)(((!flip) ? 1 : (-1)) * num3 * 4), -16 + num4 * 4 + num5 + 4 + heightOffset.Value), value3, overrideColor, rotation, origin, num2, SpriteEffects.None, GetLayerDepth(layerDepth, FarmerSpriteLayers.Hat));
				value3 = hatSourceRect;
				value3.Height = 11;
				b.Draw(texture3, position6, value3, color2, rotation, origin, num2, SpriteEffects.None, GetLayerDepth(layerDepth, FarmerSpriteLayers.HatMaskUp));
			}
			else
			{
				b.Draw(texture3, position6, hatSourceRect, color2, rotation, origin, num2, SpriteEffects.None, GetLayerDepth(layerDepth, FarmerSpriteLayers.Hat));
			}
		}
	}
}
