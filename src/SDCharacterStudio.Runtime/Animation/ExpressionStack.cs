using SDCharacterStudio.Core.Animation;
using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Runtime.Animation;

/// <summary>Expression owners can overlap without taking over each other's clocks or restoration.</summary>
public sealed class ExpressionStack
{
    private readonly List<Entry> entries=new();
    public Entry? Active=>entries.LastOrDefault();
    public void Set(object owner,Expression definition,double elapsed=0,int? duration=null,bool external=false)
    {
        if(duration<0)throw new ArgumentOutOfRangeException(nameof(duration));
        _=ExpressionTimeline.SelectFrame(definition,elapsed);
        Remove(owner);
        if(duration.HasValue&&elapsed>=duration.Value)return;
        entries.Add(new(owner,definition,elapsed,duration,external));
    }
    public void Seek(object owner,double elapsed)
    {
        if(elapsed<0||!double.IsFinite(elapsed))throw new ArgumentOutOfRangeException(nameof(elapsed));
        var entry=entries.FirstOrDefault(x=>ReferenceEquals(x.Owner,owner));
        if(entry==null)return;
        entry.Elapsed=elapsed;Expire();
    }
    public void Advance(double ms)
    {
        if(ms<0||!double.IsFinite(ms))throw new ArgumentOutOfRangeException(nameof(ms));
        foreach(var entry in entries)if(!entry.External)entry.Elapsed+=ms;
        Expire();
    }
    public void ReleaseClock(object owner)
    {var entry=entries.FirstOrDefault(x=>ReferenceEquals(x.Owner,owner));if(entry!=null)entry.External=false;}
    public void Remove(object owner)=>entries.RemoveAll(x=>ReferenceEquals(x.Owner,owner));
    public void Clear()=>entries.Clear();
    private void Expire()=>entries.RemoveAll(x=>x.Duration.HasValue&&x.Elapsed>=x.Duration.Value);

    public sealed class Entry(object owner,Expression definition,double elapsed,int? duration,bool external)
    {
        public object Owner {get;}=owner;
        public Expression Definition {get;}=definition;
        public double Elapsed {get;internal set;}=elapsed;
        public int? Duration {get;}=duration;
        public bool External {get;internal set;}=external;
        public int Frame=>ExpressionTimeline.SelectFrame(Definition,Elapsed);
    }
}
