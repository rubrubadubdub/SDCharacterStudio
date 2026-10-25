# Installation

## Launch the editor

Open `bin/Editor/SDCharacterStudio.Editor.exe`, or use the **SD Character Studio** desktop shortcut. The Windows x64 release includes its .NET runtime, AudioHost and embedded export engine. A separate .NET installation is unnecessary.

Editing works with the game closed. Populate the asset catalog by installing Bridge and exporting from your modded game first; live previews require a running game with a save loaded. Offline sound audition needs that exported audio bank. See [Bridge setup](bridge-preview.md) and the [first-scene walkthrough](quickstart.md).

## Build a release from source

Building requires the .NET SDKs/Windows desktop targeting packs used by the solution, the Stardew Valley 1.6 game installation, and SMAPI. From the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\publish-release.ps1 -CreateShortcut
```

This publishes self-contained `win-x64` Editor and AudioHost applications into `bin/Editor`, prepares both embedded exporter resources, and creates the desktop shortcut. Builds explicitly disable mod deployment. The script refuses to overwrite an unrelated shortcut or an existing Editor folder without its `sdcs-release.json` ownership marker. Keep that marker when republishing.

To choose another release directory, including a path containing spaces:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\publish-release.ps1 -OutputRoot 'C:\SDCS Builds\latest'
```

The script builds and validates in staging before replacing the previous editor. Launching the published EXE does not require the source checkout.

## Install game mods

The release writes `bin/packages/SDCharacterStudio.Runtime.zip` and `SDCharacterStudio.Bridge.zip`. Each zip has a top-level mod folder. Extract those folders into the game's `Mods` directory for authoring and live preview. Bridge requires Runtime. Close the game before replacing mod files.

Players receiving **Export › Just this mod** install only the exported mod and its listed dependencies; the engine is inside its DLL. A framework content pack instead requires the separate Runtime. See [the modder guide](modder-guide.md) for all three export targets.

Release packages exclude Stardew Valley, SMAPI, Harmony, MonoGame and xTile assemblies. They use the game's installed copies.
