using SDCharacterStudio.Core.Models;

namespace SDCharacterStudio.Runtime.Animation;

/// <summary>Advance returns every entered frame in a reused buffer, valid until the next Advance.
/// Consume by index to avoid boxing its enumerator on each game tick.</summary>
public sealed class PoseTimeline
{
    private readonly Pose pose;
    private readonly double speed;
    private int[] sequence;
    private int[]? loop;
    private int index;
    private double elapsed;
    private bool hold;
    private readonly List<int> enteredFrames=new();
    public int Frame => sequence[index];
    public bool Finished { get; private set; }
    public bool Releasing { get; private set; }
    public PoseTimeline(Pose pose,string play,double speed=1)
    {
        this.pose=pose;
        if (pose.Frames.Count==0 || pose.Frames.Any(x=>x.Duration<=0) || speed<=0 || !double.IsFinite(speed)) throw new ArgumentException("Pose requires positive frame durations and speed.");
        this.speed=speed;
        int[] all=Enumerable.Range(0,pose.Frames.Count).ToArray();
        int[] main=pose.Intro.Concat(pose.Loop).ToArray();
        if(main.Length==0) main=all;
        sequence=play switch { "loop" => pose.Loop.Length>0?pose.Loop:all, "once-then-loop" when pose.Intro.Length>0 => pose.Intro, "once" or "hold-first" or "once-then-loop"=>main, _=>throw new ArgumentException("Unknown pose play mode.") };
        loop=play=="loop"?sequence:play=="once-then-loop" && pose.Loop.Length>0?pose.Loop:null;
        hold=play=="hold-first";
        if(sequence.Concat(pose.Outro).Any(x=>x<0||x>=pose.Frames.Count)) throw new ArgumentException("Pose frame index outside its frames.");
    }
    public IReadOnlyList<int> Advance(double ms)
    {
        if(ms<0||!double.IsFinite(ms)) throw new ArgumentOutOfRangeException(nameof(ms));
        enteredFrames.Clear();
        if(Finished||hold) return enteredFrames;
        elapsed+=ms*speed;
        while(elapsed>=pose.Frames[Frame].Duration)
        {
            elapsed-=pose.Frames[Frame].Duration;
            if(++index>=sequence.Length)
            {
                if(loop==null) { index=sequence.Length-1; Finished=true; break; }
                sequence=loop; index=0;
            }
            enteredFrames.Add(Frame);
        }
        return enteredFrames;
    }
    public void Release()
    {
        if(Releasing) return;
        Releasing=true; hold=false; loop=null; elapsed=0;
        if(pose.Outro.Length==0) { Finished=true; return; }
        sequence=pose.Outro; index=0; Finished=false;
    }
}
