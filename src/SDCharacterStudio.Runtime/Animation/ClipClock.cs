using SDCharacterStudio.Core.Models;
namespace SDCharacterStudio.Runtime.Animation;
public sealed class ClipClock
{
    private readonly int duration,fps;
    private readonly bool loop;
    public double Elapsed { get; private set; }
    public double Time => duration==0?0:loop?Elapsed%duration:Math.Min(Elapsed,duration);
    public double VisualTime => Finished?duration:fps>0?Math.Floor(Time/(1000d/fps))*(1000d/fps):Time;
    public bool Finished => duration==0||!loop&&Elapsed>=duration;
    public long Cycle => loop&&duration>0?(long)(Elapsed/duration):0;
    public ClipClock(int duration,int fps,bool loop)
    { if(duration<0||fps<0)throw new ArgumentException("Invalid clip duration/fps."); this.duration=duration;this.fps=fps;this.loop=loop; }
    public void Advance(double ms,bool paused)
    { if(ms<0||!double.IsFinite(ms))throw new ArgumentOutOfRangeException(nameof(ms));if(!paused)Elapsed+=ms; }
    public static IEnumerable<T> Crossings<T>(IEnumerable<T> keys,double from,double to,int duration,bool loop) where T:Keyframe
    {
        var list=keys.OrderBy(x=>x.At).ToArray();
        if(duration==0)
        {foreach(var key in list)if(key.At==0&&from<0&&to>=0)yield return key;yield break;}
        long first=loop?Math.Max(0,(long)Math.Floor(from/duration)):0;
        long last=loop?(long)Math.Floor(to/duration):0;
        for(long cycle=first;cycle<=last;cycle++)
            foreach(var key in list)
            { double at=cycle*(double)duration+key.At; if(at>from&&at<=to)yield return key; }
    }
}
