# SDCharacterStudio

SDCharacterStudio (**SDCS**) is a toolkit for making **custom character cutscenes for Stardew Valley without writing code**:

- **Editor (Windows app):** a designer stages a scene and poses characters. They build new poses and outfits from live game art (including other mods' art) plus their own pixel art, animate it on a timeline, and write dialogue and choices.
- **Export:** create a complete SMAPI mod with its engine inside, a `Sdcs.cs` drop-in for your C# mod, or a framework content pack. Scene art references installed game/mod assets live.
- **Runtime (SMAPI mod):** plays framework packs; complete-mod exports include their own isolated engine.

**M0–M8 and embedded engine exports are implemented.** M9 adds a screenshot walkthrough, modder guide, runtime allocation improvements, repeatable native LAN checks and a self-contained Windows release.

Start with the [three-beat custom-pose walkthrough](docs/quickstart.md). Open `bin/Editor/SDCharacterStudio.Editor.exe` or the **SD Character Studio** desktop shortcut; see [installation](docs/installation.md), the [complete editor guide](docs/editor-guide.md), [modder guide](docs/modder-guide.md), and [embedding scenes in your mod](docs/embedding.md).

## Build and run

From this folder:

```powershell
dotnet build src\SDCharacterStudio.sln
dotnet run --project src\SDCharacterStudio.Editor
```

The build deploys Runtime to the game's `Mods\SDCharacterStudio` folder and Bridge to `Mods\SDCharacterStudio.Bridge`. Close the game before deploying. To compile while the game is running:

```powershell
dotnet build src\SDCharacterStudio.sln -p:EnableModDeploy=false
```

Run Core's and the Editor's tests with `dotnet test src\SDCharacterStudio.sln -p:EnableModDeploy=false`. The developer tools have separate usage instructions: [decompile](tools/decompile/README.md) and [xnbdump](tools/xnbdump/README.md).

## Architecture

Four .NET projects:
- **`SDCharacterStudio.Core`** (`net6.0` class library, no game references): the JSON models, validation, version migration, and the **compiler** that turns a cutscene into vanilla Stardew event script strings. Unit-testable outside the game.
- **`SDCharacterStudio.Runtime`** (SMAPI mod, `net6.0`): loads content packs, injects the compiled events into `Data/Events/<Location>`, registers custom event commands (`SDCS_*`), and renders. It renders **compositions** (stacks of live game-art layers, masks, the designer's pixels, colour slots and anchors) as stand-in sprites in place of hidden actors. It also manages player costume changes safely, plays videos and composited portraits, and exposes a C# API.
- **`SDCharacterStudio.Bridge`** (SMAPI mod, `net6.0`, dev-only): exports the **live, already-modded** game assets and data (characters, sprite sheets, portraits, player parts, location renders, origin/dependency info) to a local folder for the editor. It also hot-reloads and plays a cutscene in the running game for preview.
- **`SDCharacterStudio.Editor`** (`net8.0-windows` WPF app): a Canva-style asset browser on the left, canvas in the middle, layers and properties on the right, and a storyboard plus timeline at the bottom. It has pixel tools, named reusable masks, anchors, colour slots, expressions and portraits, and exports content packs.
