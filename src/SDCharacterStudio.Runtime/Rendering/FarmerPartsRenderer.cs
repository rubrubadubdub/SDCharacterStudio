using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SDCharacterStudio.Core.Compositing;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Tools;
using FarmerSpriteLayers=StardewValley.FarmerRenderer.FarmerSpriteLayers;

namespace SDCharacterStudio.Runtime.Rendering;

/// <summary>Game-thread-only 1.6.15 farmer part renderer. Never changes farmer net fields.</summary>
public sealed partial class FarmerPartsRenderer : IDisposable
{
    private readonly IModHelper helper;
    private readonly Dictionary<(FarmerAppearanceKey Appearance,int Frame,int Facing,FarmerPart Parts),RgbaImage> cache=new();
    private readonly Queue<(FarmerAppearanceKey Appearance,int Frame,int Facing,FarmerPart Parts)> cacheOrder=new();
    private RenderTarget2D? target;
    private SpriteBatch? batch;
    private long generation;
    private const int CacheLimit=128;

    public FarmerPartsRenderer(IModHelper helper)=>this.helper=helper;

    public RgbaImage Render(Farmer who,int frame,int facing,string parts)
    {
        var previousSick=helper.Reflection.GetField<bool>(who.FarmerRenderer,"_sickFrame").GetValue();
        try { return RenderCore(who,frame,facing,parts); }
        finally { PrepareSickness(who,previousSick); }
    }

    private RgbaImage RenderCore(Farmer who,int frame,int facing,string parts)
    {
        Validate(frame,facing);
        var selected=FarmerParts.Parse(parts);
        var renderer=who.FarmerRenderer;
        Prepare(who,frame);
        var key=(AppearanceKey(who),frame,facing,selected);
        if(cache.TryGetValue(key,out var cached)) return cached;
        var device=Game1.graphics.GraphicsDevice;
        target??=new RenderTarget2D(device,128,192,false,SurfaceFormat.Color,DepthFormat.None,0,RenderTargetUsage.PreserveContents);
        batch??=new SpriteBatch(device);
        var oldTargets=device.GetRenderTargets();
        var oldViewport=device.Viewport;
        var oldScissor=device.ScissorRectangle;
        var oldBlend=device.BlendState;
        var oldDepth=device.DepthStencilState;
        var oldRasterizer=device.RasterizerState;
        var oldSampler=device.SamplerStates[0];
        Texture2D? barefoot=null;
        Texture2D? shoes=null;
        try
        {
            var baseTexture=GetBaseTexture(renderer);
            if(!selected.HasFlag(FarmerPart.Shoes) && (selected & (FarmerPart.Base|FarmerPart.Arms))!=0)
                baseTexture=barefoot=CreateBarefootTexture(who,baseTexture);
            if(selected.HasFlag(FarmerPart.Shoes) && !selected.HasFlag(FarmerPart.Body))
                shoes=CreateShoesTexture(who,baseTexture);
            device.SetRenderTarget(target);
            device.Clear(Color.Transparent);
            batch.Begin(SpriteSortMode.FrontToBack,BlendState.AlphaBlend,SamplerState.PointClamp,DepthStencilState.None,RasterizerState.CullNone);
            try { Draw(batch,who,frame,facing,selected,baseTexture,shoes); }
            finally { batch.End(); }
            var result=FarmerCanvas.Downsample(TexturePixels.Read(target));
            while(cache.Count>=CacheLimit) cache.Remove(cacheOrder.Dequeue());
            cache[key]=result;
            cacheOrder.Enqueue(key);
            return result;
        }
        finally
        {
            device.SetRenderTargets(oldTargets);
            device.Viewport=oldViewport;
            device.ScissorRectangle=oldScissor;
            device.BlendState=oldBlend;
            device.DepthStencilState=oldDepth;
            device.RasterizerState=oldRasterizer;
            device.SamplerStates[0]=oldSampler;
            barefoot?.Dispose();
            shoes?.Dispose();
        }
    }

    public PixelPoint GetNeckAnchor(int frame,int facing)
    {
        Validate(frame,facing);
        return new(16+(facing==3 ? -1 : 1)*FarmerRenderer.featureXOffsetPerFrame[frame],31+FarmerRenderer.featureYOffsetPerFrame[frame]);
    }

    /// <summary>Neck position including the same live animation offsets used by Render.</summary>
    public PixelPoint GetNeckAnchor(Farmer who,int frame,int facing)
    {
        var neck=GetNeckAnchor(frame,facing);
        var animation=AnimationFor(who,frame,facing);
        return new(neck.X+animation.xOffset,neck.Y+animation.positionOffset);
    }

    /// <summary>Appearance plus mutable draw state which can change pixels. Invalidate after asset changes.</summary>
    internal FarmerAppearanceKey AppearanceKey(Farmer who)
    {
        var renderer=who.FarmerRenderer;
        who.GetDisplayShirt(out var shirtTexture,out var shirtIndex);
        who.GetDisplayPants(out var pantsTexture,out var pantsIndex);
        var hair=Farmer.GetHairStyleMetadata(who.getHair());
        var hat=who.hat.Value;
        var animation=who.FarmerSprite.CurrentAnimationFrame;
        var prismatic=who.prismaticHair.Value || hat?.isPrismatic.Value==true || who.shirtItem.Value?.isPrismatic.Value==true || who.pantsItem.Value?.isPrismatic.Value==true
                ? Utility.GetPrismaticColor().PackedValue : 0u;
        return new FarmerAppearanceKey(generation,who,renderer,
            renderer.textureName.Value,renderer.skin.Value,renderer.shoes.Value,renderer.heightOffset.Value,
            who.IsMale,who.hair.Value,who.getHair(),who.hairstyleColor.Value.PackedValue,who.newEyeColor.Value.PackedValue,
            who.accessory.Value,hat?.QualifiedItemId,hat?.hairDrawType.Value,hat?.ignoreHairstyleOffset.Value,hat?.isPrismatic.Value,
            shirtIndex,shirtTexture,who.GetShirtColor().PackedValue,
            pantsIndex,pantsTexture,who.GetPantsColor().PackedValue,
            hair?.texture,
            who.bathingClothes.Value,who.currentEyes,Game1.timeOfDay<2600,who.isInBed.Value,who.timeWentToBed.Value,
            who.FarmerSprite.PauseForSingleAnimation,who.UsingTool,who.CurrentTool is FishingRod rod ? rod.isFishing : null,
            who.FarmerSprite.CurrentFrame,animation.flip,animation.armOffset,animation.positionOffset,animation.xOffset,who.armOffset,
            prismatic);
    }

    /// <summary>Recoloured straight-alpha base sheet, including the native palette row.</summary>
    public RgbaImage ReadBase(Farmer who)
    {
        helper.Reflection.GetMethod(who.FarmerRenderer,"executeRecolorActions").Invoke(who);
        return TexturePixels.Read(GetBaseTexture(who.FarmerRenderer));
    }

    public void Invalidate() { generation++;cache.Clear();cacheOrder.Clear(); }
    public void Invalidate(IEnumerable<string> assets)=>Invalidate();
    public void Clear()=>Invalidate();
    public void Dispose() { Clear();target?.Dispose();batch?.Dispose();target=null;batch=null; }

    private static void Validate(int frame,int facing)
    {
        if((uint)frame>=FarmerRenderer.featureYOffsetPerFrame.Length) throw new ArgumentOutOfRangeException(nameof(frame));
        if((uint)facing>3) throw new ArgumentOutOfRangeException(nameof(facing));
    }
    private Texture2D GetBaseTexture(FarmerRenderer renderer)=>helper.Reflection.GetField<Texture2D>(renderer,"baseTexture").GetValue();
    private void Prepare(Farmer who,int frame)
        =>PrepareSickness(who,frame is 104 or 105);

    private void PrepareSickness(Farmer who,bool requestedSick)
    {
        var renderer=who.FarmerRenderer;
        var sick=helper.Reflection.GetField<bool>(renderer,"_sickFrame");
        if(sick.GetValue()!=requestedSick)
        {
            sick.SetValue(requestedSick);
            helper.Reflection.GetField<bool>(renderer,"_shirtDirty").SetValue(true);
            helper.Reflection.GetField<bool>(renderer,"_spriteDirty").SetValue(true);
        }
        helper.Reflection.GetMethod(renderer,"executeRecolorActions").Invoke(who);
    }

    // Native recolour offsets identify shoes before recolouring, so equal final shoe/skin colours
    // cannot cause false matches. Replacement happens only in this owned texture copy.
    private Texture2D CreateBarefootTexture(Farmer who,Texture2D baseTexture)
    {
        var pixels=new Color[baseTexture.Width*baseTexture.Height];
        baseTexture.GetData(pixels);
        var name=who.FarmerRenderer.textureName.Value;
        if(FarmerRenderer.recolorOffsets.TryGetValue(name,out var indices))
            for(var shade=0;shade<4;shade++)
                if(indices.TryGetValue(268+shade,out var offsets))
                    foreach(var offset in offsets) pixels[offset]=pixels[260+Math.Min(shade,2)];
        var copy=new Texture2D(baseTexture.GraphicsDevice,baseTexture.Width,baseTexture.Height);
        try { copy.SetData(pixels);return copy; }
        catch { copy.Dispose();throw; }
    }

    private Texture2D CreateShoesTexture(Farmer who,Texture2D baseTexture)
    {
        var source=new Color[baseTexture.Width*baseTexture.Height];
        baseTexture.GetData(source);
        var pixels=new Color[source.Length];
        var name=who.FarmerRenderer.textureName.Value;
        if(FarmerRenderer.recolorOffsets.TryGetValue(name,out var indices))
            for(var shade=0;shade<4;shade++)
                if(indices.TryGetValue(268+shade,out var offsets))
                    foreach(var offset in offsets) pixels[offset]=source[offset];
        var copy=new Texture2D(baseTexture.GraphicsDevice,baseTexture.Width,baseTexture.Height);
        try { copy.SetData(pixels);return copy; }
        catch { copy.Dispose();throw; }
    }

    private static FarmerSprite.AnimationFrame AnimationFor(Farmer who,int frame,int facing)
    {
        var animation=who.FarmerSprite.CurrentAnimationFrame;
        // Use live frame-specific offsets only for that same frame. New standalone frames use
        // vanilla AnimationFrame defaults (primary arms, no position offsets).
        if(who.FarmerSprite.CurrentFrame!=frame) animation=new FarmerSprite.AnimationFrame(frame,100);
        animation.flip=facing==3;
        return animation;
    }

    private void Draw(SpriteBatch b,Farmer who,int frame,int facing,FarmerPart parts,Texture2D baseTexture,Texture2D? shoes)
    {
        var animation=AnimationFor(who,frame,facing);
        var source=new Rectangle(frame*16%96,frame*16/96*32,16,32);
        var position=new Vector2(32,64);
        var offset=new Vector2(animation.xOffset*4,animation.positionOffset*4);
        var effects=animation.flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
        var neck=15+FarmerRenderer.featureYOffsetPerFrame[frame];
        if((parts&FarmerPart.Base)==FarmerPart.Base)
            b.Draw(baseTexture,position+offset,source,Color.White,0,Vector2.Zero,4,effects,Depth(FarmerSpriteLayers.Base));
        else if(parts.HasFlag(FarmerPart.Head))
        {
            var rows=FarmerCanvas.BaseRows(neck,true);
            var head=source;head.Height=rows.Height;
            if(head.Height>0) b.Draw(baseTexture,position+offset,head,Color.White,0,Vector2.Zero,4,effects,Depth(FarmerSpriteLayers.Base));
        }
        else if(parts.HasFlag(FarmerPart.Body))
        {
            var rows=FarmerCanvas.BaseRows(neck,false);
            var body=source;body.Y+=rows.Start;body.Height=rows.Height;
            if(body.Height>0) b.Draw(baseTexture,position+offset+new Vector2(0,rows.Start*4),body,Color.White,0,Vector2.Zero,4,effects,Depth(FarmerSpriteLayers.Base));
        }
        if(shoes!=null)
            b.Draw(shoes,position+offset,source,Color.White,0,Vector2.Zero,4,effects,Depth(FarmerSpriteLayers.Base));
        if(parts.HasFlag(FarmerPart.Pants) && (who.FarmerRenderer.skin.Value!=-12345 || who.pantsItem.Value!=null))
        {
            who.GetDisplayPants(out var texture,out var index);
            var pants=source;pants.Offset(index%10*192+(!who.IsMale ? 96 : 0),index/10*688);
            b.Draw(texture,position+offset,pants,Utility.MakeCompletelyOpaque(who.GetPantsColor()),0,Vector2.Zero,4,effects,Depth(frame==5 ? FarmerSpriteLayers.PantsPassedOut : FarmerSpriteLayers.Pants));
        }
        if(who.currentEyes!=0 && facing!=0 && (Game1.timeOfDay<2600 || (who.isInBed.Value && who.timeWentToBed.Value!=0)) &&
            ((!who.FarmerSprite.PauseForSingleAnimation && !who.UsingTool) || (who.UsingTool && who.CurrentTool is FishingRod)) &&
            (!who.UsingTool || who.CurrentTool is not FishingRod {isFishing:false}))
        {
            var x=5+(animation.flip ? -1 : 1)*FarmerRenderer.featureXOffsetPerFrame[frame]+(facing==1 ? 3 : facing==3 ? 1 : 0);
            var y=FarmerRenderer.featureYOffsetPerFrame[frame]*4;
            var width=facing==2 ? 6 : 2;
            if(parts.HasFlag(FarmerPart.Head)) b.Draw(baseTexture,position+offset+new Vector2(x*4,y+(who.IsMale && facing!=2 ? 36 : 40)),new Rectangle(5,16,width,2),Color.White,0,Vector2.Zero,4,SpriteEffects.None,Depth(FarmerSpriteLayers.FaceSkin));
            if(parts.HasFlag(FarmerPart.Eyes)) b.Draw(baseTexture,position+offset+new Vector2(x*4,y+(facing is 1 or 3 ? 40 : 44)),new Rectangle(264+(facing==3 ? 4 : 0),2+(who.currentEyes-1)*2,width,2),Color.White,0,Vector2.Zero,4,SpriteEffects.None,Depth(FarmerSpriteLayers.Eyes));
        }
        DrawHairAndAccessories(b,facing,who,position,Vector2.Zero,1,frame,0,Color.White,0.1f,parts,offset,animation.flip);
        if(parts.HasFlag(FarmerPart.Arms) && animation.armOffset>0)
        {
            source.Offset(animation.armOffset*16,0);
            b.Draw(baseTexture,position+offset+who.armOffset,source,Color.White,0,Vector2.Zero,4,effects,Depth(facing==0 ? FarmerSpriteLayers.ArmsUp : FarmerSpriteLayers.Arms));
        }
    }

    // The off-screen batch always sorts front-to-back; do not inherit the world batch's sort direction.
    private static float Depth(FarmerSpriteLayers layer)=>GetLayerDepth(0.1f,layer);
    private static float GetLayerDepth(float depth,FarmerSpriteLayers layer,bool dyeLayer=false)=>depth+(int)layer*0.000001f+(dyeLayer ? 0.0000001f : 0);
}
