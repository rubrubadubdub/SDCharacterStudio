# Putting SDCharacterStudio scenes in your mod

Scenes made with the SDCharacterStudio editor play in any SMAPI mod. **Players install only your mod**: the engine travels inside it (Q-17, D-44). Content Patcher, SpaceCore or any other dependency your mod already has stays as it is.

## No code: export a complete mod

In the editor, **Export** (Ctrl+E) › **Just this mod**. The editor writes a finished SMAPI mod (by default `Export/<name>` next to your project):

- `manifest.json`: your pack's name, ID, version and the mods your scenes use;
- `<YourModId>.dll`: a small entry DLL with the engine inside, named after your mod so it never clashes with another;
- your scenes, poses, art and masks.

Put that folder in `Mods`, or share it. Scenes play when their conditions are met. Other mods can play them too, through SMAPI: declare an interface with the members they need and call `helper.ModRegistry.GetApi<IYourInterface>("your.mod.id")?.PlayCutscene("scene-id")`. The API offers `PlayCutscene(string cutscene, bool checkConditions = false)`, `IsPlaying`, `StopCutscene()`, `GetCutscenes()`, `EngineVersion` and `CommandPrefix`.

## C# mods: one file and one line

**Export** › **My C# mod** writes two things into a folder (by default `Export/<name> for my mod`):

1. **`Sdcs.cs`**: the whole engine in one file. Drop it into your mod's project; it compiles like any other file. No package, no extra DLL, no project settings.
2. **`scenes/`**: your scenes. Copy it into your mod and have its files copied to the build output:
   ```xml
   <None Update="scenes\**\*" CopyToOutputDirectory="PreserveNewest" />
   ```

Then, in your `ModEntry`:

```csharp
public override void Entry(IModHelper helper)
{
    Sdcs.Register(this);              // plays the scenes in your mod's "scenes" folder
}
```

Scenes play by themselves when their conditions are met. From your own code:

| Call | Does |
|---|---|
| `Sdcs.Play("robin-apology")` | Play a scene now (`checkConditions: true` only when its conditions are met). |
| `Sdcs.IsPlaying`, `Sdcs.Stop()` | Whether one of your scenes is playing; stop it and give the player back control. |
| `Sdcs.Cutscenes` | The IDs of your scenes that loaded. |
| `Sdcs.Started += id => …`, `Sdcs.Ended += id => …` | Your scenes starting and ending. |
| `Sdcs.Register(this, "other-folder")` | Scenes from another folder of your mod. |

## Why several mods with SDCS inside never collide

- **Private copies:** each mod's engine is loaded into a load context of its own, so copies never meet, even at different versions.
- **Shared game code:** the game, SMAPI, Harmony and Json.NET are the game's own copies.
- **Unique names:** each copy names its event commands and saved data after its mod (`SDCS1a2b3c4d_Clip`), so they never replace each other. The framework mod keeps the published `SDCS_` names.
- **Quiet:** the engine logs routine messages to the SMAPI log file only. Warnings and errors still appear in the console under your mod's name.

The `benches/` folder holds one mod of each kind and a script that plays both in the game, with and without the framework installed ([benches/README.md](../benches/README.md)).
