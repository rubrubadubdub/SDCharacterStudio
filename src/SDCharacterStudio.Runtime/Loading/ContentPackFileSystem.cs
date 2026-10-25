using System.Collections.Generic;
using SDCharacterStudio.Core.Loading;
using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Serialization;
using StardewModdingAPI;

namespace SDCharacterStudio.Runtime.Loading;

/// <summary>Read raw JSON through the guarded Core filesystem so schema diagnostics retain source paths.</summary>
public sealed class ContentPackFileSystem : IPackFileSystem, IImageMetadataProvider
{
    private readonly DiskPackFileSystem disk;
    private readonly string? ownerManifest;

    public ContentPackFileSystem(IContentPack pack, bool useOwnerManifest = false)
    {
        disk = new(pack.DirectoryPath);
        if (useOwnerManifest)
            ownerManifest = PackJson.Serialize(new Manifest
            {
                UniqueID = pack.Manifest.UniqueID, Name = pack.Manifest.Name,
                ContentPackFor = new() { UniqueID = "Player.SDCharacterStudio" }
            });
    }

    public bool Exists(string path) => IsVirtualManifest(path) || disk.Exists(path);
    public string ReadText(string path) => IsVirtualManifest(path) ? ownerManifest! : disk.ReadText(path);
    public IEnumerable<string> ListFiles(string glob) => disk.ListFiles(glob);
    public ImageSize? GetImageSize(string path) => disk.GetImageSize(path);
    private bool IsVirtualManifest(string path) => ownerManifest != null && path == "manifest.json";
}
