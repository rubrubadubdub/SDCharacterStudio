using SDCharacterStudio.Core.Compositing;
namespace SDCharacterStudio.Runtime.Rendering;
internal sealed record SourceFrameSnapshot(object Texture,string? Asset,long Generation,long Epoch,RgbaImage Image);
internal sealed class SourceVersionCache
{
    private sealed record Sheet(RgbaImage Image,string? Asset,long Generation);
    private readonly Dictionary<object,Sheet> sheets=new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(object Texture,string? Asset,int Width,int Height,int Index),SourceFrameSnapshot> frames=new();
    private readonly Dictionary<string,long> generations=new(StringComparer.OrdinalIgnoreCase);
    private long epoch;

    public SourceFrameSnapshot GetFrame(object texture,string? asset,int width,int height,int index,Func<RgbaImage> load)
    {
        asset=Normalize(asset);
        var key=(texture,asset,width,height,index);
        if(frames.TryGetValue(key,out var cached) && IsCurrent(cached,texture)) return cached;
        var generation=Generation(asset);
        if(!sheets.TryGetValue(texture,out var sheet) || sheet.Generation!=generation || !SameAsset(sheet.Asset,asset))
            sheets[texture]=sheet=new(load(),asset,generation);
        var frame=new SourceFrameSnapshot(texture,asset,generation,epoch,PixelOperations.CropFrame(sheet.Image,width,height,index));
        frames[key]=frame;
        return frame;
    }

    public bool IsCurrent(SourceFrameSnapshot snapshot,object current)
        =>ReferenceEquals(snapshot.Texture,current) && snapshot.Epoch==epoch && snapshot.Generation==Generation(snapshot.Asset);

    public void Invalidate(IEnumerable<string> assets)
    {
        var names=new HashSet<string>(assets.Select(x=>Normalize(x)!),StringComparer.OrdinalIgnoreCase);
        foreach(var name in names) generations[name]=Generation(name)+1;
        foreach(var key in frames.Keys.Where(x=>x.Asset!=null && names.Contains(x.Asset)).ToArray()) frames.Remove(key);
        foreach(var key in sheets.Where(x=>x.Value.Asset!=null && names.Contains(x.Value.Asset)).Select(x=>x.Key).ToArray()) sheets.Remove(key);
    }
    public void Clear() { epoch++;frames.Clear();sheets.Clear();generations.Clear(); }
    public static bool SameAsset(string? a,string? b)
    {
        if(ReferenceEquals(a,b))return true;
        if(a==null||b==null||a.Length!=b.Length)return false;
        int start=0;
        for(int i=0;i<a.Length;i++)
            if(a[i] is '/' or '\\'||b[i] is '/' or '\\')
            {
                if(a[i] is not ('/' or '\\')||b[i] is not ('/' or '\\')||
                    !a.AsSpan(start,i-start).Equals(b.AsSpan(start,i-start),StringComparison.OrdinalIgnoreCase))return false;
                start=i+1;
            }
        return a.AsSpan(start).Equals(b.AsSpan(start),StringComparison.OrdinalIgnoreCase);
    }
    private long Generation(string? asset)=>asset!=null && generations.TryGetValue(asset,out var value)?value:0;
    private static string? Normalize(string? asset)=>asset?.Replace('\\','/').ToLowerInvariant();
}
