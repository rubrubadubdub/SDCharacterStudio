using Newtonsoft.Json.Linq;
using SDCharacterStudio.Core.Validation;

namespace SDCharacterStudio.Core.Migration;

/// <summary>A version-to-version transform. The pipeline updates Format after Apply succeeds.</summary>
public interface IFormatMigration
{
    string FromVersion { get; }
    string ToVersion { get; }
    void Apply(JObject data);
}
public sealed record MigrationResult(JObject? Data,bool Changed,IReadOnlyList<ValidationIssue> Issues);
public sealed class PackMigrator
{
    private readonly IReadOnlyList<IFormatMigration> migrations;
    public PackMigrator(IEnumerable<IFormatMigration>? migrations=null) => this.migrations=(migrations??Array.Empty<IFormatMigration>()).ToArray();
    public MigrationResult Migrate(JObject input,string file,string targetVersion="1.0")
    {
        var data=(JObject)input.DeepClone();var changed=false;var visited=new HashSet<string>(StringComparer.Ordinal);
        var current=data["Format"]?.Type==JTokenType.String?(string?)data["Format"]:null;
        while(current!=targetVersion)
        {
            if(current==null||!visited.Add(current))return Error("Missing format or migration cycle.");
            var candidates=migrations.Where(m=>m.FromVersion==current).ToArray();
            if(candidates.Length!=1)return Error($"No unique migration from format '{current}' to '{targetVersion}'.");
            var migration=candidates[0];
            try{migration.Apply(data);current=migration.ToVersion;data["Format"]=current;changed=true;}
            catch(Exception ex){return Error($"Migration from '{current}' failed: {ex.Message}");}
        }
        return new(data,changed,Array.Empty<ValidationIssue>());
        MigrationResult Error(string message)=>new(null,false,new[]{new ValidationIssue(ValidationSeverity.Error,file,"Format",message,"V1")});
    }
}
