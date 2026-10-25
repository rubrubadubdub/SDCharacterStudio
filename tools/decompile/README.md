# Local game decompiler

Run from the repository root with the .NET SDK and a local Stardew Valley installation. The tool targets .NET 6 and uses ICSharpCode.Decompiler 8.2.0.7535. The game does not need to be running.

```powershell
dotnet build tools/decompile/decomp.csproj
New-Item -ItemType Directory -Force tools/decompile/out | Out-Null
dotnet tools/decompile/bin/Debug/net6.0/decomp.dll --list Farmer
dotnet tools/decompile/bin/Debug/net6.0/decomp.dll StardewValley.Event |
    Set-Content -Encoding utf8 tools/decompile/out/Event.cs
dotnet tools/decompile/bin/Debug/net6.0/decomp.dll StardewValley.NPC tryToReceiveActiveObject
dotnet tools/decompile/bin/Debug/net6.0/decomp.dll StardewValley.FarmerRenderer.FarmerSpriteLayers
```

Arguments are `--list [filter]` or `<full.type.name> [member]`. Nested type names use dots. Member names are exact names without parentheses; all matching overloads are emitted. Listings accept a case-insensitive substring filter. Errors go to stderr and return exit code 1; successful commands return 0.

The default installation is `C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley`. Override its location or the assembly to inspect through environment variables:

```powershell
$env:GamePath = 'D:\Steam Library\steamapps\common\Stardew Valley'
$env:DECOMP_DLL = 'StardewValley.GameData.dll'
dotnet tools/decompile/bin/Debug/net6.0/decomp.dll StardewValley.GameData.Characters.CharacterAppearanceData
Remove-Item Env:DECOMP_DLL
```

`DECOMP_DLL` accepts an assembly filename relative to `GamePath`, or an absolute path. The game directory and the selected assembly's directory are both searched for dependencies.

Keep decompiled source in this tool's ignored `out/` directory. The project explicitly excludes `out/**` from compilation, so a later build does not compile the extracted game classes. Do not commit or redistribute game source. Run `./tools/verify-tools.ps1` from the repository root for the installed-game smoke checks.
