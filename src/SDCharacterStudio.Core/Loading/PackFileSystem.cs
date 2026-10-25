using System.Buffers.Binary;
using System.Text.RegularExpressions;

namespace SDCharacterStudio.Core.Loading;

public interface IPackFileSystem
{
    bool Exists(string path);
    string ReadText(string path);
    IEnumerable<string> ListFiles(string glob);
}
public readonly record struct ImageSize(int Width, int Height);
public interface IImageMetadataProvider { ImageSize? GetImageSize(string path); }

public static class PackPath
{
    public static bool IsSafe(string? path) => !string.IsNullOrWhiteSpace(path) && !path.StartsWith('/') && !path.Contains('\\') && !path.Contains(':') && !path.Contains('\0') && !path.Split('/').Any(p=>p is ".." or "." or "");
}

/// <summary>A rooted, read-only adapter. Pack paths use '/', even on Windows.</summary>
public sealed class DiskPackFileSystem : IPackFileSystem, IImageMetadataProvider
{
    private readonly string root;
    public DiskPackFileSystem(string root) => this.root=Path.GetFullPath(root);
    private string Resolve(string path)
    {
        if(!PackPath.IsSafe(path)) throw new ArgumentException("Unsafe pack path.",nameof(path));
        var full=Path.GetFullPath(Path.Combine(root,path.Replace('/',Path.DirectorySeparatorChar)));
        var prefix=root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(!full.StartsWith(prefix,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal)) throw new ArgumentException("Pack path escapes root.",nameof(path));
        // Do not follow directory/file links out of the pack, even if their lexical path is safe.
        var current=root;
        foreach(var segment in path.Split('/'))
        {
            current=Path.Combine(current,segment);
            if((File.Exists(current)||Directory.Exists(current)) && (File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0) throw new IOException("Pack paths may not traverse symbolic links.");
        }
        return full;
    }
    public bool Exists(string path) => File.Exists(Resolve(path));
    public string ReadText(string path) => File.ReadAllText(Resolve(path));
    public byte[] ReadBytes(string path) => File.ReadAllBytes(Resolve(path));
    public IEnumerable<string> ListFiles(string glob)
    {
        Resolve(glob);
        if(!Directory.Exists(root)) return Array.Empty<string>();
        var regex=new Regex("^"+Regex.Escape(glob).Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]")+"$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        var options=new EnumerationOptions {RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint,IgnoreInaccessible=false};
        return Directory.EnumerateFiles(root,"*",options).Select(p=>Path.GetRelativePath(root,p).Replace('\\','/')).Where(p=>regex.IsMatch(p)).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
    }
    public ImageSize? GetImageSize(string path)
    {
        using var stream=File.OpenRead(Resolve(path));
        Span<byte> header=stackalloc byte[24];
        var count=0;while(count<header.Length){var read=stream.Read(header[count..]);if(read==0)return null;count+=read;}
        if(!header[..8].SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}) || BinaryPrimitives.ReadInt32BigEndian(header.Slice(8,4))!=13 || !header.Slice(12,4).SequenceEqual(new byte[]{73,72,68,82})) return null;
        var width=BinaryPrimitives.ReadInt32BigEndian(header.Slice(16,4));var height=BinaryPrimitives.ReadInt32BigEndian(header.Slice(20,4));
        return width>0 && height>0 ? new ImageSize(width,height) : null;
    }
}
