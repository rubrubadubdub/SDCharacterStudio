# Bridge export and preview

Install both Runtime and Bridge for authoring. Players only need Runtime. Bridge exports the running game's final assets and metadata, including content edits from other mods.

## Export

Bridge starts a static export on GameLaunched. It exports current NPC/player appearances and locations on SaveLoaded by default. Work runs through a cooperative 4ms-per-tick queue; individual content loads, renders, encodes and final publication can take longer than that budget.

Configure `Mods/SDCharacterStudio.Bridge/config.json`:

```json
{
  "ExportFolder": "%LOCALAPPDATA%\\SDCharacterStudio\\Export",
  "Profile": "default",
  "AutoExportOnSaveLoaded": true,
  "ExportLocations": true,
  "PreviewPipeName": "SDCharacterStudio.Preview",
  "PreviewRequestFolder": "%LOCALAPPDATA%\\SDCharacterStudio\\Preview\\requests"
}
```

The output is `<ExportFolder>/<Profile>/`. Profiles use a single directory name. Environment variables expand in both configured folders. Console commands:

```text
sdcs_export static
sdcs_export save
sdcs_export all
sdcs_export_asset Characters/Robin
sdcs_bridge_status
```

`save` and `all` require a loaded world. Static exports include characters and all referenced default/appearance textures, wardrobe metadata, item icons, sounds, emotes, event commands, installed mods and origins. Save exports add current textures, `farmer/player.json`, an appearance folder with 678 native 32x48 part renders, and loaded location renders/passability/warps/layer metadata. They also add the upgraded farmhouse as mods make it (D-52): `HouseLayouts` in `catalog.json` lists `FarmHouse1` and `FarmHouse2` (the married house after one and two upgrades, `Maps/FarmHouse1_marriage` and `Maps/FarmHouse2_marriage` with every patch applied, and the player's spouse's room drawn in, or the game's default room), each with its size, pictures, walkable map and `"Places": { "SpouseRoom": [x, y] }`. The save's own farmhouse entry gets `Level` and, when it has a spouse's room, its `Places`. The editor stages farmhouse scenes on them. An export made before they existed lacks `HouseLayouts`; the automatic export never runs again for that (D-47), so the editor says so and **Ask the game to export again** brings them. A save-phase export initializes static metadata too when no prior catalog exists.

Image hashes and `export-state.json` support incremental export. Unchanged files retain their timestamps. A static pass rebuilds texture discovery, so disabled mods' old images can remain on disk without appearing in the current catalog. Files are staged until the phase succeeds; managed commit failures roll back prior replacements. Job failures leave the prior published catalog intact and write `last-export-error.json`; transport replies also report setup/commit failures. A successful phase writes `export-report.json` with `Complete:true`. If rollback cannot restore a locked file, the diagnostic identifies retained recovery data; keep that directory until the reported files are recovered.

Texture PNGs contain straight RGBA pixels, suitable for the editor. Missing display-only NPC art is represented by a null texture and a requested-asset diagnostic rather than a fabricated image. Location composites use native 16px tiles and the first frame of animated tiles.

Item icons use the game's source rectangles. If a mod's rectangle extends beyond its texture, the icon repeats edge pixels using native PointClamp sampling; `IconWarning`, `SourceRect` and `TextureSize` explain the case. Location cells use xTile's native source bounds, including TMX maps whose reported tile size is scaled for display.

### Offline sound audition

Static exports retain the compatible `sounds.json` cue list and add `catalog.json.SoundPreviews = "sound-previews.json"`. The supplemental file has `Format:1`, the installed `GameDirectory`, and `Cues` keyed by cue ID. Each entry records `Category`, `Looped`, `UseReverb`, `StreamedVorbis`, and `Variants:[{"File":"audio/<cue-hash>/0.ogg"}]`. Variants are export-relative copies of resolved `Data/AudioChanges` WAV/OGG sources, limited to 64 MiB per source. An unavailable or unsupported source records a cue-specific `Error` without preventing the other catalog exports.

Export while the game is running, then use the editor's sound browser or Sound beat **Play/Stop** controls with the game closed. The separate audio host reads vanilla XACT banks/MonoGame from the installed path and mod sources from the export. It enforces contained source paths and a 10-second audition limit, with `1200` as neutral pitch. No game assemblies or cached sound sources are added to authored packs. Offline audition does not reproduce in-game positional, category, or reverb routing.

## Preview

Preview requests may be sent while the game is running; the Bridge registers an isolated `.DevPreview` pack and reports progress and final status through the pipe or file-drop transport. Offline sound audition is a separate editor host: it requires the installed game path, reads vanilla XACT banks and exported mod OGG/WAV sources, and is intended for cue checks while the game is closed.

Both transports accept the same JSON objects. Add an optional `Id` to correlate replies. Successful final replies have `Ok:true` and `Final:true`; errors have `Ok:false` and `Error`. Export/render progress has `Op:"Progress"`, `Final:false`, and the originating request ID. Pipe clients may also receive `Op:"Log"` Runtime warning/error notifications.

| Op | Request fields | Result |
|---|---|---|
| Ping | none | Game version, SaveLoaded, Exporting |
| Export | Phase: static/save/all | Export folder and completed job count |
| RegisterDevPack | Folder: absolute pack directory | Manifest UniqueID and available scene IDs |
| PlayCutscene | Pack, Cutscene, IgnoreConditions | Reloads pack/event assets, warps if needed, replies when the scene actually starts |
| StopCutscene | none | Cancels queued starts and restores preview appearance, including retained authored costumes |
| RenderFarmer | Loadout: costume keys such as Hair, HairColor, Hat, Shirt, Pants | Export folder and relative PartsFolder for an owned fake farmer |
| ReExportAsset | Asset: live texture name | Re-export completion |

A preview needs a loaded world and a free player. Starting a new preview cleans up the previous preview and retained appearance first. Closing menus and finishing unrelated player actions may be necessary before it can start. Explicit preview stop/restoration does not change ordinary cutscenes' authored OnEnd retention behavior.

For file drop, write `requests/<guid>.json`; Bridge polls every 30 ticks and writes `responses/<guid>.json` in the sibling directory. The response can update with progress until Final becomes true. Completed request files are consumed. Write a temporary file and rename it to `.json` after writing when possible.

For example, after installing the Farmer Costume Demo content pack, this PowerShell one-liner requests its scene:

```powershell
$id=[guid]::NewGuid().ToString('N'); @{Op='PlayCutscene';Pack='Player.SDCS.FarmerDemo';Cutscene='farmer-costume';IgnoreConditions=$true} | ConvertTo-Json | Set-Content "$env:LOCALAPPDATA\SDCharacterStudio\Preview\requests\$id.json" -Encoding UTF8
```

For a development pack outside Mods, send RegisterDevPack with its absolute directory first; its manifest UniqueID becomes Pack in subsequent requests. Registration and playback re-read its files without restarting the game.

Named pipe clients connect to `\\.\pipe\SDCharacterStudio.Preview` and exchange UTF-8 JSON lines. Multiple requests can share a connection. Background transport code queues requests; game access and rendering execute on the game thread. Requests are bounded to 128KiB, and overload returns a correlated error or closes a connection that cannot consume reliable replies.

## Origins

The shipped 1.6.15 index contains 1,019 texture names, 259 map names and 2,373 vanilla data keys across 14 families. Regenerate it with `tools/xnbdump` when changing game versions; see [tool usage](../tools/xnbdump/README.md).

Vanilla references stay dependency-free when a mod repaints or replaces them. `EditedBy` records those edits. A non-vanilla asset/key with an identified Content Patcher provider records `ProvidedBy`. Nested Includes, ModId/Season expansion and remaining token patterns are inspected; conditional/unresolved matches are marked uncertain. Other frameworks can remain Unknown for the editor to resolve. This is origin evidence, rather than a second implementation of Content Patcher's condition engine.
