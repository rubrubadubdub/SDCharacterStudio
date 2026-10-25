using System.Text.RegularExpressions;
using SDCharacterStudio.Core.Loading;

namespace SDCharacterStudio.Core.Tests.TestSupport;

public sealed class MemoryPackFileSystem : IPackFileSystem, IImageMetadataProvider
{
    public Dictionary<string,string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string,ImageSize> Images { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Accesses { get; } = new();
    public bool Exists(string path) { Accesses.Add(path); return Files.ContainsKey(path) || Images.ContainsKey(path); }
    public string ReadText(string path) { Accesses.Add(path); return Files[path]; }
    public IEnumerable<string> ListFiles(string glob)
    {
        Accesses.Add(glob);
        var pattern="^"+Regex.Escape(glob).Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]")+"$";
        return Files.Keys.Concat(Images.Keys).Distinct().Where(p=>Regex.IsMatch(p,pattern,RegexOptions.IgnoreCase)).ToArray();
    }
    public ImageSize? GetImageSize(string path) { Accesses.Add(path); return Images.GetValueOrDefault(path); }
    public static MemoryPackFileSystem Minimal()
    {
        var fs=new MemoryPackFileSystem();
        fs.Files["manifest.json"]="""{"UniqueID":"Author.Pack","ContentPackFor":{"UniqueID":"Player.SDCharacterStudio"}}""";
        fs.Files["studio.json"]="""{"Format":"1.0"}""";
        return fs;
    }
}
