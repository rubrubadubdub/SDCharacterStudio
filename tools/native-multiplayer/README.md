# Native multiplayer smoke harness

`Driver/NativeMultiplayerDriver.csproj` is a SMAPI driver built against the installed Stardew 1.6.15 assemblies. Build it with `-p:EnableModDeploy=false`; export `Bench.SdcsNoCode` with the `ExportNoCodeBench` editor test before launching.

`Invoke-MultiplayerSmoke.ps1` creates a GUID-named private run under the active worktree, prepares separate host/client Mods roots, and launches only when `-Launch` is supplied and no Stardew or SMAPI process exists. It backs up existing SMAPI logs, records after-run logs, restores the exact preexisting files, and cleans up only its owned PIDs. It never uses owner save paths.

The driver creates the disposable host world, cabin, and farmhand in memory. It starts the normal LAN server with `Game1.Multiplayer.StartServer()` and `initializeHost()`. The client uses native `LidgrenClient`, `InitClient`, `FarmhandMenu`, and `FarmhandSlot.Activate` to complete the real handshake. Requests are atomically written JSON files and responses are preserved as evidence.

Optional compatibility directories use one PowerShell array argument, for example:

```powershell
-ExtraMods @('C:\fixtures\ContentPatcher','C:\fixtures\FashionSense')
```

The successful run is `37db44b2fd6643fa950a66a6d2e3405b`; its evidence is retained under the private run root. The orchestrator now waits for native hidden-state settling before assertions.
