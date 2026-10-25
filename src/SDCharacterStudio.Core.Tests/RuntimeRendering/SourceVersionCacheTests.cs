using SDCharacterStudio.Core.Compositing;
using SDCharacterStudio.Runtime.Rendering;
using Xunit;

namespace SDCharacterStudio.Core.Tests.RuntimeRendering;
public sealed class SourceVersionCacheTests
{
    [Fact]
    public void InvalidationReloadsSameTextureInstanceWithoutDisposingBorrowedSource()
    {
        var cache=new SourceVersionCache();
        var source=new object();
        uint color=0xFF112233;
        var first=cache.GetFrame(source,"Characters/Robin",1,1,0,()=>new RgbaImage(1,1,new[]{color}));
        color=0xFF445566;
        Assert.Same(first,cache.GetFrame(source,"Characters/Robin",1,1,0,()=>throw new Exception("should be cached")));
        cache.Invalidate(new[]{"characters\\ROBIN"});
        Assert.False(cache.IsCurrent(first,source));
        var second=cache.GetFrame(source,"Characters/Robin",1,1,0,()=>new RgbaImage(1,1,new[]{color}));
        Assert.Equal(0xFF445566u,second.Image.Pixels[0]);
        Assert.True(cache.IsCurrent(second,source));
    }
    [Fact]
    public void TextureReplacementAndClearMakeOldSnapshotsStale()
    {
        var cache=new SourceVersionCache();var source=new object();
        var snapshot=cache.GetFrame(source,null,1,1,0,()=>new(1,1,new uint[]{1}));
        Assert.False(cache.IsCurrent(snapshot,new object()));
        Assert.True(cache.IsCurrent(snapshot,source));
        cache.Clear();
        Assert.False(cache.IsCurrent(snapshot,source));
    }
    [Fact]
    public void UnrelatedAssetInvalidationPreservesCachedFrames()
    {
        var cache=new SourceVersionCache();var source=new object();
        var snapshot=cache.GetFrame(source,"Characters/Robin",1,1,0,()=>new(1,1,new uint[]{1}));
        cache.Invalidate(new[]{"Portraits/Robin"});
        Assert.True(cache.IsCurrent(snapshot,source));
    }
    [Fact]
    public void AssetComparisonHandlesNativeSeparatorsWithoutAllocatingEachTick()
    {
        for(int i=0;i<1000;i++)SourceVersionCache.SameAsset("characters/robin","Characters\\ROBIN");
        long before=GC.GetAllocatedBytesForCurrentThread();
        bool equal=true;
        for(int i=0;i<10000;i++)equal&=SourceVersionCache.SameAsset("characters/robin","Characters\\ROBIN");
        long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        Assert.True(equal);
        Assert.Equal(0,allocated);
        Assert.False(SourceVersionCache.SameAsset("Characters/Robin", "Characters/Robinn"));
        Assert.False(SourceVersionCache.SameAsset(null, ""));
        Assert.True(SourceVersionCache.SameAsset(null, null));
    }
}
