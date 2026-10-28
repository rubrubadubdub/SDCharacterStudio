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
  assets/audio/*.wav         # imported music (OGG Vorbis also supported)
  videos/**/*
```

The C# export puts the same content under `scenes/`, with `Sdcs.cs` beside it and no content-pack manifest inside `scenes/`. `studio.json` selects the files. The default library references include `library/characters/*.json`, `library/poses/*.json`, and `library/clips/*.json`; mask metadata is commonly `library/masks.json`, with entries pointing to PNGs such as `masks/robin-head-left.png`. Keep paths relative, use forward slashes, and ship every file matched by configured globs. Library clips use roles and relative positions; a scene `Clip` beat supplies the `Cast` mapping. The editor copies the poses, rigs, masks, and art needed by a library item into the pack.

The Runtime discovers ordinary packs through `manifest.json` with a `ContentPackFor` object naming `Player.SDCharacterStudio` and its minimum version. A mod with embedded scenes uses its own `EntryDll` and calls the wrapper. Live NPC, portrait, farmer, item, and game assets remain live references; the pack stores source keys, frames, masks, anchors, and optional colour-slot data, not game assemblies.

Validation occurs when a pack loads and when a cutscene compiles. Missing files, malformed JSON, unknown beat or layer types, missing live references, and unmet dependencies appear in the SMAPI log; a cutscene with a missing runtime dependency is skipped with a warning. In the editor, inspect Problems and Output before exporting.

Imported music is declared in `studio.json`, for example `"Music": { "evening": { "Name": "Evening theme", "File": "assets/audio/evening.ogg", "Loop": true } }`. A scene's `Music` or a Music beat's `Cue` uses `"music:evening"`. `Loop` defaults to true. The runtime registers a unique native cue for each pack and track, so game music volume and event music restoration apply. Preview packs have separate cue IDs. Export again with the updated editor to include music support in embedded engines; framework content packs need the updated Runtime.

## Positions that follow a place (D-52)

Every position in a scene (an actor's `Start`, a Move's `To`, a Camera beat's `Center`, the scene's `Camera`, a Prop's or Effect's `At`, a clip camera key's `Center`) is a map tile:

```json
{ "Tile": [12, 8], "Px": [0, 0] }
```

Add `"From"` and the tile counts from a **place** the game finds when the scene starts instead: tiles right and down of its spot (negative: left and up). `Px` is still a pixel nudge.

```json
"Actors": [
  { "Id": "farmer",  "Kind": "Farmer", "Start": { "From": "SpouseRoom", "Tile": [0, 2] }, "Facing": "up" },
  { "Id": "Abigail", "Kind": "Npc", "Npc": "Abigail", "Start": { "From": "SpouseRoom", "Tile": [0, 0] }, "Facing": "down" }
],
"Beats": [
  { "Type": "Move", "Actor": "Abigail", "To": { "From": "SpouseRoom", "Tile": [1, 1] } },
  { "Type": "Camera", "Center": { "From": "SpouseRoom", "Tile": [0, 1] }, "Duration": 800 },
  { "Type": "Prop", "Item": "(F)1130", "At": { "From": "SpouseRoom", "Tile": [-1, 0] } }
]
```

The scene's own camera takes the same shape: `"Camera": { "From": "SpouseRoom", "Tile": [0, 1] }`.

| Place | Its spot | Found when |
|---|---|---|
| `SpouseRoom` | The spouse's spot in the farmhouse the scene plays in (the game's `FarmHouse.GetSpouseRoomSpot()`): the spouse's room's own marked tile (its `Paths` tile 7; vanilla rooms mark one 3 right and 7 down of the room's corner, so Abigail stands at 32,8 once upgraded and 53,27 twice), else the room's corner plus 3,4. The corner is the map's `SpouseRoomPosition`, or 29,1 once upgraded and 50,20 after. It follows the upgrade level, map mods and the spouse. | The scene plays in a farmhouse (or cabin) upgraded at least once, where a villager lives as the player's spouse or roommate. |

What the engine does:

- **Trigger:** a scene with a trigger that uses a place plays by itself only when the place is there. The compiled event key gets the game state query `<prefix>_HAS_PLACE SpouseRoom` (`SDCS_HAS_PLACE` in the framework; each embedded copy uses its own prefix).
- **Start:** the compiled event header still needs map tiles, so it starts everyone at the place's fallback spot plus their offset (`SpouseRoom`: 32,5, the game's spot in a once-upgraded house whose room marks none). The engine's first command, before the scene fades in, moves anchored actors (and the opening camera) to the real spot.
- **During the scene:** walks, Camera beats (cuts and pans), clip camera keys, items and effects with `From` go to the spot found at the start.
- **Missing place:** when a scene plays anyway without its place (a preview, or `Play` without checking conditions on a save without a spouse), the SMAPI log says so once and positions from it count from the fallback spot.
- **Validation:** an unknown place is an error (V20); `SpouseRoom` in a scene whose `Location` isn't `FarmHouse` is a warning.

Clip position keys aren't places: a key is a map tile, or relative to where the actor stood when the clip began (`"Relative": true`), which follows a place when the actor started from one.

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
