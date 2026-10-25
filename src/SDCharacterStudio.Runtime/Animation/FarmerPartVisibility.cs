namespace SDCharacterStudio.Runtime.Animation;

/// <summary>Render-only part visibility with per-key clip persistence and invalidatable undo scopes.</summary>
public sealed class FarmerPartVisibility
{
    private static readonly HashSet<string> names=new(new[]{"Base","Head","Body","Eyes","Pants","Shirt","Accessory","Hair","Hat","Arms","Shoes"},StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,bool> values=new(StringComparer.OrdinalIgnoreCase);
    private long epoch;
    public long Version { get; private set; }
    public bool IsDefault=>values.Values.All(x=>x);
    public bool Visible(string part)=>values.GetValueOrDefault(part,true);
    public Scope BeginScope()=>new(this);
    public static void Validate(IReadOnlyDictionary<string,bool> changes)
    {
        foreach(string key in changes.Keys)
            if(!names.Contains(key))throw new ArgumentException($"Unknown farmer visibility part '{key}'.");
    }
    public void Clear(){epoch++;if(!IsDefault)Version++;values.Clear();}
    private void Set(string key,bool value){if(Visible(key)==value)return;values[key]=value;Version++;}

    public sealed class Scope : IDisposable
    {
        private readonly FarmerPartVisibility owner;
        private readonly long epoch;
        private readonly Dictionary<string,bool> baseline=new(StringComparer.OrdinalIgnoreCase);
        private bool disposed;
        internal Scope(FarmerPartVisibility owner){this.owner=owner;epoch=owner.epoch;}
        public void Apply(Dictionary<string,bool> changes,bool persist)
        {
            if(disposed||epoch!=owner.epoch)throw new InvalidOperationException("This part visibility scope has ended.");
            Validate(changes);
            foreach(var pair in changes)
            {
                if(!baseline.ContainsKey(pair.Key))baseline[pair.Key]=owner.Visible(pair.Key);
                if(persist)baseline[pair.Key]=pair.Value;
                owner.Set(pair.Key,pair.Value);
            }
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            if(epoch!=owner.epoch)return;
            foreach(var pair in baseline)owner.Set(pair.Key,pair.Value);
        }
        public void KeepCurrent()
        {
            if(disposed||epoch!=owner.epoch)return;
            foreach(string key in baseline.Keys.ToArray())baseline[key]=owner.Visible(key);
        }
    }
}
