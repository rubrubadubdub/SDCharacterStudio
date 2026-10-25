# SDCharacterStudio modder guide

This guide is for authors who ship a mod containing scenes made with SDCharacterStudio. The editor workflow is in [editor-guide.md](editor-guide.md); the complete-mod and one-file export mechanics are in [embedding.md](embedding.md).

## Choose an engine distribution

For a no-code export, use **Export → Just this mod**. It creates a normal SMAPI mod with your `manifest.json`, a uniquely named entry DLL, and the scene files. The engine is embedded in that DLL (D-44). Players install your mod only; do not copy Stardew Valley, SMAPI, Harmony, MonoGame, or Json.NET DLLs into the mod.

For a C# mod, use **Export → My C# mod**. Then:

1. Copy the generated `Sdcs.cs` into your project. It compiles like any other file, with any project settings.
2. Copy the generated `scenes` folder into your project and add this line to your `.csproj`. Without it, the build leaves the scenes out of your mod, and SMAPI's log says so:
   ```xml
   <ItemGroup>
     <None Update="scenes\**\*" CopyToOutputDirectory="PreserveNewest" />
   </ItemGroup>
   ```
3. Call `Sdcs.Register(this)` from `ModEntry.Entry`.

Each mod loads its own isolated engine copy, so mods with different engine versions coexist.

`Sdcs.Play("id")` plays a scene **now**; `Sdcs.Play("id", checkConditions: true)` plays it only if its conditions are met. (The framework's `ISdcsApi.PlayCutscene` is the other way round: it checks conditions unless you pass `ignoreConditions: true`.) A `true` return means the start was accepted; a warp or fade may still come first, so use `Sdcs.Started` to know the scene began.

## Content-pack layout

```text
MyMod/
  manifest.json
  MyMod.dll                 # complete-mod export only
  studio.json                # complete mod / framework pack: files at root
  cutscenes/*.json
  library/
      characters/*.json
      masks.json
      poses/*.json
      clips/*.json
      expressions.json
      portraits/*.json
      costumes.json
  art/**/*.png
  masks/**/*.png
  videos/**/*
```

The C# export puts the same content under `scenes/`, with `Sdcs.cs` beside it and no content-pack manifest inside `scenes/`. `studio.json` selects the files. The default library references include `library/characters/*.json`, `library/poses/*.json`, and `library/clips/*.json`; mask metadata is commonly `library/masks.json`, with entries pointing to PNGs such as `masks/robin-head-left.png`. Keep paths relative, use forward slashes, and ship every file matched by configured globs. Library clips use roles and relative positions; a scene `Clip` beat supplies the `Cast` mapping. The editor copies the poses, rigs, masks, and art needed by a library item into the pack.

The Runtime discovers ordinary packs through `manifest.json` with a `ContentPackFor` object naming `Player.SDCharacterStudio` and its minimum version. A mod with embedded scenes uses its own `EntryDll` and calls the wrapper. Live NPC, portrait, farmer, item, and game assets remain live references; the pack stores source keys, frames, masks, anchors, and optional colour-slot data, not game assemblies.

Validation occurs when a pack loads and when a cutscene compiles. Missing files, malformed JSON, unknown beat or layer types, missing live references, and unmet dependencies appear in the SMAPI log; a cutscene with a missing runtime dependency is skipped with a warning. In the editor, inspect Problems and Output before exporting.

## Embedded API

```csharp
public override void Entry(IModHelper helper)
{
    Sdcs.Register(this, "scenes");
    Sdcs.Started += id => Monitor.Log($"Started {id}", LogLevel.Trace);
    Sdcs.Ended += id => Monitor.Log($"Ended {id}", LogLevel.Trace);
}

bool started = Sdcs.Play("robin-apology", checkConditions: true);
if (Sdcs.IsPlaying) Sdcs.Stop();
IReadOnlyList<string> loaded = Sdcs.Cutscenes;
string commandPrefix = Sdcs.CommandPrefix;
```

`Started` and `Ended` carry a scene ID. Subscribe after `Register`; `Play` returns `false` when the start request cannot be accepted, and `Stop` cancels pending starts, restores control and runs cleanup. Call game APIs on the game thread after a world loads, with menus closed and the player free.

## Framework API (`ISdcsApi`)

When the separate Runtime mod is installed, retrieve its API through SMAPI. Declare a matching interface in your mod; do not reference the Runtime assembly or cast to its concrete type.

```csharp
public interface ISdcsApi
{
    bool PlayCutscene(string packUniqueId, string cutsceneId, bool ignoreConditions = false);
    bool IsPlaying { get; }
    IReadOnlyList<string> GetCutscenes(string packUniqueId);
    bool RegisterPackFolder(IManifest owner, string absoluteFolder);
    bool ReloadPack(string packUniqueId);
    void StopCutscene();
}
```

```csharp
// In GameLaunched, after the framework has registered its API:
var sdcs = Helper.ModRegistry.GetApi<ISdcsApi>("Player.SDCharacterStudio");
if (sdcs is null) return; // optional dependency

// Later, in a game-thread handler while a world is loaded and the player is free:
bool accepted = sdcs.PlayCutscene("Author.MyPack", "robin-apology", ignoreConditions: false);
```

Keep that API reference for later handlers. Declare an optional manifest dependency on `Player.SDCharacterStudio` when the integration is optional, or a required one if your mod cannot work without it. `RegisterPackFolder` and `ReloadPack` take a pack your mod owns and require no active scene; pass the absolute folder and manifest owner. `StopCutscene` cancels pending starts and restores retained appearance. The [full interface](../src/SDCharacterStudio.Runtime/Api/ISdcsApi.cs) also exposes `Diagnostic`, `CutsceneStarted`, `CutsceneEnded`, `RenderFarmerPart`, and `CreatePreviewFarmer`; renderer helpers need game types and a loaded world. The small proxy interface above avoids custom event-argument types; integrations needing events must provide SMAPI-compatible proxy types for the full event contract.

For another mod exported as **Just this mod**, use its own mod ID and a smaller interface with `bool PlayCutscene(string cutscene, bool checkConditions = false)`, `bool IsPlaying { get; }`, `void StopCutscene()` and `IReadOnlyList<string> GetCutscenes()`. Its API also exposes `EngineVersion` and `CommandPrefix`; it takes one scene ID, without a separate pack ID.

## Multiplayer and updates

Cutscenes and stand-ins are local, like vanilla events. Costume net-field changes may briefly be visible to other players; this is the accepted D-21 scope. Do not use scenes as a synchronized multiplayer state machine.

The embedded engine version is recorded in generated `Sdcs.cs` as `EngineVersion`. To update it, regenerate or re-export the file from the matching editor build, replace the complete file, rebuild, and re-export any complete-mod package. Do not hand-edit the Brotli/Base64 payload or replace only part of the file. Update the framework Runtime mod as a whole. Keep pack versions and declared minimum versions aligned with the API you use.
