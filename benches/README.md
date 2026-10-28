# SDCharacterStudio benches

Two deliberately simple mods that play scenes made with SDCharacterStudio, the way creators will ship them (Q-17, D-44). Players install only the mod; nothing from SDCharacterStudio. Keep using them as test benches as the engine grows.

| Bench | What it is | How it gets the engine |
|---|---|---|
| `SdcsBench.CodeMod/` | A C# mod (`Bench.SdcsCode`) written as a modder would: `ModEntry` calls `Sdcs.Register(this)`. Robin steps left and bows (a library animation with a custom pose), then speaks. | `Sdcs.cs` dropped into the project (the engine in one file); the editor's exported `scenes/` folder copied in. |
| `SdcsBench.NoCode/` | An editor project (`NoCode Bench/NoCode Bench.sdcs`, ID `Bench.SdcsNoCode`). Demetrius hops right and cheers, then speaks. | *Export › Just this mod*: the editor writes a complete mod (`export/Bench.SdcsNoCode/`) with the engine inside its DLL. |
| Walkthrough mod | The mod a first-time creator makes by following [docs/quickstart.md](../docs/quickstart.md): Robin in a painted blue-scarf pose says one line. Made by `QuickstartWorkflowTests` through the editor's own actions, exported with *Just this mod* to `benches/.run/walkthrough/`. | Nobody calls it: the bench leaves Robin's house and walks back in, and the scene must start by itself. |
| Studio bench | The simple Character Studio (D-48, D-49): Robin's pose as one flat picture, her head selected by her body map and dragged down, her legs copied in live from one of her walking frames (so the engine draws pixels from two of her frames). It plays in a flipbook (that pose, a walk two tiles left, her own look). Made by `EmbedExportTests.ExportStudioBench`, exported with *Just this mod* to `benches/.run/studio/`. | Played through its API; Robin must end two tiles left with her stand-in shown on the way. |
| `BenchDriver/` | A test-only mod with **no** SDCharacterStudio reference. It makes a disposable in-memory farmer in ScienceHouse (no save is loaded or written) and plays the benches' scenes through their SMAPI APIs. | — |

## Run them

```powershell
benches/Run-Benches.ps1            # both runs
benches/Run-Benches.ps1 -PlayersOnly
```

The script builds everything in Release, embeds an **older** engine (`0.0.9-bench`) in the code bench and the current one in the no-code bench, exports the no-code bench with the editor, and starts the game twice with its own `--mods-path` under `benches/.run/`:

1. **players**: the two benches and the driver only. No SDCharacterStudio installed.
2. **with-framework**: the same plus the SDCharacterStudio framework mod.

`NativeBenchTests` then checks, in each run:
- each bench reports its own engine version and its own command prefix;
- each scene plays, with the actor's stand-in (its custom pose) showing and the animation moving them to the expected tile;
- the walkthrough mod's scene starts by itself when the player walks into Robin's house;
- no event command was overwritten;
- no SMAPI errors (apart from Steam achievements in an isolated launch);
- no console lines from the benches at Info level or above.

A separate check, `tools/native-bridge/Test-ExportOnce.ps1`, proves the Bridge's automatic export happens once (D-47).

`tools/native-bridge/Test-Costumes.ps1 -Suite camera` checks 16 endings in a disposable world:
the C# embedded engine and framework, `stay` and default, an End beat in the main list or a branch,
and a fixed scene camera or a Camera beat. It exports the fixtures through the editor's exporter.
The driver retains every tick from the last message through full black; the viewport must stay exactly
where the scene left it. Props must remain until black, then disappear, and the game must release the
camera and return the player to the ending's expected tile. Traces and the verdict are in `.run/camera/`.

Evidence goes to `benches/.run/<run>/` (`benches-native.log`, `SMAPI.txt`, `test-output.txt`). It never starts while another Stardew Valley or SMAPI process is running.

## Change their scenes

The scenes are made with the editor by `EmbedExportTests.WriteBenchContent`:

```powershell
$env:SDCS_BENCH_WRITE = '1'; dotnet test src/SDCharacterStudio.Editor.Tests --filter WriteBenchContent; Remove-Item Env:\SDCS_BENCH_WRITE
```

It rewrites `SdcsBench.CodeMod/scenes/` and the `SdcsBench.NoCode/NoCode Bench/` project. Or open `NoCode Bench.sdcs` in the editor and edit it by hand.
