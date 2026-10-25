using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SDCharacterStudio.Runtime.Rendering;
using StardewValley;

namespace SDCharacterStudio.Runtime.Animation;

/// <summary>A render-only farmer replacement when a Parts track hides native layers without an authored pose.</summary>
internal sealed class FarmerPartsSprite : IDisposable
{
    private readonly AnimationRuntime owner;
    private readonly Farmer farmer;
    private readonly GameLocation location;
    private readonly bool hidden;
    private readonly Texture2D texture;
    private readonly TemporaryAnimatedSprite sprite;
    private (FarmerAppearanceKey Appearance,int Frame,int Facing,FarmerPart Parts)? version;
    private long visibilityVersion=-1;
    private string parts="Full";
    private FarmerPart partFlags;
    private bool disposed;
    public FarmerPartsSprite(AnimationRuntime owner,Farmer farmer)
    {
        this.owner=owner;this.farmer=farmer;location=Game1.currentLocation;hidden=farmer.hidden.Value;
        texture=new Texture2D(Game1.graphics.GraphicsDevice,32,48);
        sprite=new TemporaryAnimatedSprite("",new Rectangle(0,0,32,48),999999,1,999999,Vector2.Zero,false,false,0,0,Color.White,4,0,0,0){texture=texture};
        location.temporarySprites.Add(sprite);
    }
    public void Update(bool visible)
    {
        int frame=farmer.FarmerSprite.CurrentFrame,facing=farmer.FacingDirection;
        long currentVisibility=owner.PartVisibility(farmer).Version;
        if(visibilityVersion!=currentVisibility)
        {parts=owner.FilterParts(farmer,"Full");partFlags=FarmerParts.Parse(parts);visibilityVersion=currentVisibility;}
        var next=(owner.Compositor.Farmers.AppearanceKey(farmer),frame,facing,partFlags);
        if(next!=version)
        {
            var image=owner.Compositor.Farmers.Render(farmer,frame,facing,parts);
            var colors=image.Pixels.Select(c=>Color.FromNonPremultiplied((byte)(c>>16),(byte)(c>>8),(byte)c,(byte)(c>>24))).ToArray();
            texture.SetData(colors);version=next;
        }
        sprite.position=BaseTopLeft(farmer)-new Vector2(32,64);
        sprite.layerDepth=farmer.getDrawLayer();sprite.alpha=visible?1:0;
        farmer.hidden.Value=true;
    }
    // Farmer.draw uses this origin at4x: position+origin-origin*4. getLocalPosition already adds one jump;
    // the Farmer.draw call adds a second. Unlike NPC drawing, Farmer does not use spriteHeight*3 positioning.
    internal static Vector2 BaseTopLeft(Farmer farmer)
    {
        var origin=new Vector2(farmer.xOffset,(farmer.yOffset+128-farmer.GetBoundingBox().Height/2)/4f+4);
        return farmer.Position+farmer.drawOffset+farmer.jitter+new Vector2(0,farmer.yJumpOffset*2)-origin*3;
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        location.temporarySprites.Remove(sprite);texture.Dispose();farmer.hidden.Value=hidden;
    }
}
