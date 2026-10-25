using SDCharacterStudio.Core.Models;
using SDCharacterStudio.Core.Validation;

namespace SDCharacterStudio.Core.Loading;

public sealed record SourceEntry<T>(string File,T Value);
public sealed class LoadedPack
{
    public Manifest? Manifest { get; set; }
    public StudioIndex? Index { get; set; }
    public List<SourceEntry<Cutscene>> Cutscenes { get; } = new();
    public List<SourceEntry<CharacterRig>> Characters { get; } = new();
    public List<SourceEntry<Pose>> Poses { get; } = new();
    public List<SourceEntry<Portrait>> Portraits { get; } = new();
    public List<SourceEntry<LibraryClip>> Clips { get; } = new();
    public SourceEntry<MaskLibrary>? Masks { get; set; }
    public SourceEntry<ExpressionLibrary>? Expressions { get; set; }
    public SourceEntry<CostumeLibrary>? Costumes { get; set; }
}
public sealed record PackReadResult(LoadedPack Pack,IReadOnlyList<ValidationIssue> Issues);
