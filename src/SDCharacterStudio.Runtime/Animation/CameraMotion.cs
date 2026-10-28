using SDCharacterStudio.Core.Models;
using StardewValley;

namespace SDCharacterStudio.Runtime.Animation;

/// <summary>A paused-aware camera pan between viewport top-left positions.</summary>
public sealed class CameraMotion
{
    private readonly WorldPoint start,end;
    private readonly int duration;
    private readonly string ease;
    private double elapsed;
    public bool Finished { get; private set; }

    /// <param name="centre">The beat's centre as a map tile (a place's spot found, D-52).</param>
    public CameraMotion(CameraBeat beat,Position? centre=null)
    {
        if(beat.Center==null || beat.Duration<0) throw new ArgumentException("Camera requires a centre and nonnegative duration.");
        AnimationMath.Ease(beat.Ease,0);
        start=new(Game1.viewport.X,Game1.viewport.Y);
        var point=AnimationMath.Position(centre??beat.Center);
        end=Clamp(new(point.X+32,point.Y+32));
        duration=beat.Duration;ease=beat.Ease;
        Game1.viewportFreeze=true;
        if(duration==0) {Apply(end);Finished=true;}
    }

    public void Update(double ms,bool paused)
    {
        if(ms<0 || !double.IsFinite(ms)) throw new ArgumentOutOfRangeException(nameof(ms));
        if(Finished || paused || Game1.dialogueUp) return;
        elapsed+=ms;
        Apply(AnimationMath.Lerp(start,end,AnimationMath.Ease(ease,elapsed/duration)));
        Finished=elapsed>=duration;
    }

    /// <summary>Convert a world centre into a clamped viewport top-left.</summary>
    public static WorldPoint Clamp(WorldPoint centre)
    {
        var map=Game1.currentLocation?.Map ?? throw new InvalidOperationException("Camera needs a current map.");
        return AnimationMath.Camera(centre,Game1.viewport.Width,Game1.viewport.Height,map.DisplayWidth,map.DisplayHeight);
    }

    public static void Apply(WorldPoint viewport)
    {
        Game1.viewportFreeze=true;
        Game1.viewport.X=(int)Math.Round(viewport.X,MidpointRounding.AwayFromZero);
        Game1.viewport.Y=(int)Math.Round(viewport.Y,MidpointRounding.AwayFromZero);
    }
}
