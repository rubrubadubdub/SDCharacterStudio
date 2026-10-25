using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Metadata;

if (args.Length == 0 || args.Length > 2 || (args[0].StartsWith("--") && args[0] != "--list"))
{
    Console.Error.WriteLine("Usage: decomp --list [filter] | <full.type.name> [member]");
    return 1;
}

try
{
    string gamePath = Environment.GetEnvironmentVariable("GamePath")
        ?? @"C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley";
    string dllPath = Path.Combine(gamePath, Environment.GetEnvironmentVariable("DECOMP_DLL") ?? "Stardew Valley.dll");
    if (!File.Exists(dllPath))
        throw new FileNotFoundException($"Assembly not found: {dllPath}. Set GamePath or DECOMP_DLL to the installed assembly.");

    using var file = new PEFile(dllPath);
    var resolver = new UniversalAssemblyResolver(file.FileName, false, file.DetectTargetFrameworkId());
    resolver.AddSearchDirectory(gamePath);
    resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(dllPath))!);
    var decompiler = new CSharpDecompiler(file, resolver, new DecompilerSettings { ThrowOnAssemblyResolveErrors = false });
    if (args[0] == "--list")
    {
        foreach (var definition in decompiler.TypeSystem.MainModule.TypeDefinitions
                     .Where(t => args.Length < 2 || t.FullName.Contains(args[1], StringComparison.OrdinalIgnoreCase)))
            Console.WriteLine(definition.FullName);
        return 0;
    }

    var type = decompiler.TypeSystem.MainModule.TypeDefinitions.FirstOrDefault(t => t.FullName == args[0]);
    if (type is null)
        throw new ArgumentException($"Type not found: {args[0]}. Use --list [filter] to find a full name; nested types use dots.");
    if (args.Length == 1)
        Console.WriteLine(decompiler.DecompileTypeAsString(type.FullTypeName));
    else
    {
        var members = type.Members.Where(m => m.Name == args[1]).ToArray();
        if (members.Length == 0)
            throw new ArgumentException($"Member not found: {args[0]}.{args[1]}. Supply its exact name without parentheses.");
        foreach (var member in members)
            Console.WriteLine(decompiler.DecompileAsString(member.MetadataToken));
    }
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}
