namespace SDCharacterStudio.Core.Validation;

public enum ValidationSeverity { Warning,Error }
public sealed record ValidationIssue(ValidationSeverity Severity,string File,string JsonPath,string Message,string? Rule=null,Action? AutoFix=null);

public sealed record AssetOrigin(bool Vanilla,string? ProvidedBy=null);
/// <summary>What the running game (or an export of it) contains. Unknown kinds default to "exists".</summary>
public interface ICatalog
{
    bool NpcExists(string name);
    bool LocationExists(string name);
    AssetOrigin? FindAsset(string asset) => null;
    bool ColorSourceExists(string source) => false;
    /// <summary>The NPC's exact internal name. Event actors match names case-sensitively.</summary>
    string? FindNpc(string name) => NpcExists(name) ? name : null;
    /// <summary>An existing NPC the author probably meant, e.g. by display name ("Jade" for "JadeMalic").</summary>
    string? SuggestNpc(string name) => null;
    bool AssetExists(string asset) => true;
    bool ItemExists(string qualifiedItemId) => true;
    bool HairstyleExists(int index) => true;
}
public sealed class Catalog : ICatalog
{
    public HashSet<string> Npcs { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Locations { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string,AssetOrigin> Assets { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ColorSources { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Texture assets that exist, or null when the catalog doesn't track them.</summary>
    public HashSet<string>? KnownAssets { get; set; }
    /// <summary>Qualified item IDs that exist, or null when the catalog doesn't track them.</summary>
    public HashSet<string>? KnownItems { get; set; }
    /// <summary>Hairstyle indices that exist, or null when the catalog doesn't track them.</summary>
    public HashSet<int>? KnownHairstyles { get; set; }
    public bool NpcExists(string name) => Npcs.Contains(name);
    public bool LocationExists(string name) => Locations.Contains(name);
    public AssetOrigin? FindAsset(string asset) => Assets.GetValueOrDefault(asset);
    public bool ColorSourceExists(string source) => ColorSources.Contains(source);
    public string? FindNpc(string name) => Npcs.TryGetValue(name, out var actual) ? actual : null;
    public bool AssetExists(string asset) => KnownAssets?.Contains(asset) ?? true;
    public bool ItemExists(string qualifiedItemId) => KnownItems?.Contains(qualifiedItemId) ?? true;
    public bool HairstyleExists(int index) => KnownHairstyles?.Contains(index) ?? true;
}
