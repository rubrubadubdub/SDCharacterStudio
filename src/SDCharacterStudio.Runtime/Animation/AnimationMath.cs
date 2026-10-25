using SDCharacterStudio.Core.Models;
namespace SDCharacterStudio.Runtime.Animation;
public readonly record struct WorldPoint(double X,double Y);
public static class AnimationMath
{
    public static int EmoteOffset(int pivotY,double headOffset)=>(int)Math.Round((31-pivotY+headOffset)*4);
    public static double Ease(string ease,double t)
    {
        t=Math.Clamp(t,0,1);
        return ease switch { "linear"=>t,"in"=>t*t,"out"=>1-(1-t)*(1-t),"inOut"=>(1-Math.Cos(Math.PI*t))/2,"step"=>t>=1?1:0,_=>throw new ArgumentException("Unknown easing: "+ease) };
    }
    public static WorldPoint Position(Position? p,WorldPoint origin=default,bool relative=false) => new((p?.Tile?[0]??0)*64+(p?.Px[0]??0)*4+(relative?origin.X:0),(p?.Tile?[1]??0)*64+(p?.Px[1]??0)*4+(relative?origin.Y:0));
    public static WorldPoint KeyPosition(PositionKey key,WorldPoint origin) => Position(new Position{Tile=key.Tile,Px=key.Px},origin,key.Relative);
    public static WorldPoint Lerp(WorldPoint a,WorldPoint b,double amount) => new(a.X+(b.X-a.X)*amount,a.Y+(b.Y-a.Y)*amount);
    public static WorldPoint PositionAt(IReadOnlyList<PositionKey> keys,double time,WorldPoint origin)
    {
        if(keys.Count==0||time<keys[0].At) return origin;
        int i=0; while(i+1<keys.Count&&keys[i+1].At<=time)i++;
        var a=KeyPosition(keys[i],origin);
        if(i==keys.Count-1) return a;
        var next=keys[i+1];
        return Lerp(a,KeyPosition(next,origin),Ease(next.Ease,(time-keys[i].At)/(next.At-keys[i].At)));
    }
    public static WorldPoint Camera(WorldPoint centre,int width,int height,int mapWidth,int mapHeight) => new(mapWidth<width?(mapWidth-width)/2d:Math.Clamp(centre.X-width/2d,0,mapWidth-width),mapHeight<height?(mapHeight-height)/2d:Math.Clamp(centre.Y-height/2d,0,mapHeight-height));
    public static WorldPoint StandInTopLeft(WorldPoint actor,int positioningWidth,int spriteHeight,int boundingHeight,int width,int height,int pivotX,int pivotY,bool mirror,int jump,int sourceWidth=16)
    {
        double feetX=actor.X+positioningWidth*2-sourceWidth*2+32;
        double feetY=actor.Y+boundingHeight/2d-spriteHeight*3+31*4;
        return new(feetX-(mirror?width-1-pivotX:pivotX)*4,feetY-pivotY*4+jump);
    }
}
