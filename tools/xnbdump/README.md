# Vanilla XNB inspection

Run from the repository root with the .NET SDK and a local Stardew Valley installation. The game does not need to be running. This standalone .NET 6 tool reads vanilla assets directly from `Content`; SMAPI and mods are not involved.

```powershell
dotnet build tools/xnbdump/xnbdump.csproj
dotnet tools/xnbdump/bin/Debug/net6.0/xnbdump.dll strings Strings/StringsFromMaps AnimalShop.
dotnet tools/xnbdump/bin/Debug/net6.0/xnbdump.dll map Maps/ScienceHouse
dotnet tools/xnbdump/bin/Debug/net6.0/xnbdump.dll maplayout Maps/Beach 'tools/xnbdump/out/Beach layout.tsv'
dotnet tools/xnbdump/bin/Debug/net6.0/xnbdump.dll texture Characters/Robin 'tools/xnbdump/out/Robin pixels.raw'
dotnet tools/xnbdump/bin/Debug/net6.0/xnbdump.dll backtypes Maps/Beach
dotnet tools/xnbdump/bin/Debug/net6.0/xnbdump.dll vanilla-index src/SDCharacterStudio.Bridge/assets/vanilla-index-1.6.15.json
```

| Mode | Output |
| --- | --- |
| `strings <asset> <keyPrefix>` | Dictionary entries whose keys start with the case-sensitive prefix, as `key<TAB>value`, sorted by key. |
| `map <asset>` | Buildings-layer Action properties, as `x<TAB>y<TAB>action`. |
| `maplayout <asset> <out.tsv>` | Header `layer, x, y, sheet, index` separated by tabs, then every coordinate of every layer. Empty tiles have empty sheet and index fields. |
| `texture <asset> <out.raw>` | Three little-endian Int32 values: width, height, surface format; followed by the first mip level's bytes. Format 0 is premultiplied RGBA, four bytes per pixel. Other formats retain their stored surface encoding. |
| `backtypes <asset>` | Sorted Back-layer Type counts as `type<TAB>count`. Per-tile properties override tile sheet properties; nonempty tiles without Type are counted as `(none)`. |
| `vanilla-index <output.json>` | Every vanilla Texture2D and xTile map asset name plus keys from Characters, Hats, Shirts, Pants, HairData, Boots, Objects, Locations, Furniture, BigCraftables, Weapons, Tools, Trinkets and Mannequins. Uses installed XNB primary readers and dictionary readers; writes only names/keys and the installed game version. |

Text fields escape backslashes, tabs, and line breaks as `\\`, `\t`, `\r`, and `\n`. Asset names are relative to `Content` with an optional `.xnb` extension. File output creates missing parent directories and replaces an existing destination. Errors go to stderr with exit code 1; successful commands return 0.

The default installation is `C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley`. Set `GamePath` before both building and running for a different installation:

```powershell
$env:GamePath = 'D:\Steam Library\steamapps\common\Stardew Valley'
dotnet build tools/xnbdump/xnbdump.csproj
dotnet tools/xnbdump/bin/Debug/net6.0/xnbdump.dll backtypes Maps/Beach
```

References to the installed MonoGame, xTile, and game assemblies are compile-only. They remain in the game directory and are resolved there at runtime; no game DLLs are copied into the tool's output. Texture extraction uses the installed MonoGame ContentManager's private XNB reader factory for XNB validation and LZX/LZ4 decompression, then reads the texture payload without constructing a GraphicsDevice. A future MonoGame API change may require updating that factory lookup.

Keep extracted data and pixels in this tool's ignored `out/` directory; do not commit or redistribute game assets. `out/**` is excluded from compilation. Run `./tools/verify-tools.ps1` from the repository root to check all five modes against the installed vanilla fixtures, including the complete Beach layout and the Robin texture's dimensions and byte count.
