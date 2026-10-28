# Editor guide

How to make cutscenes with the SDCharacterStudio editor, including Character Studio pose tools. New users can follow the illustrated [first-scene walkthrough](quickstart.md). Export/API details are in the [modder guide](modder-guide.md).

## 1. Start

Open `bin/Editor/SDCharacterStudio.Editor.exe` or the **SD Character Studio** desktop shortcut. See [installation](installation.md) to build the self-contained Windows release. Developers can also run `dotnet run --project src/SDCharacterStudio.Editor`.

The editor reads your real game from the **game export** that the SDCS Bridge writes: characters, sprite sheets, portraits, maps, items, sounds, and which mod adds or edits each one. To make or refresh it:

1. Install SDCharacterStudio and SDCharacterStudio.Bridge in the game's `Mods` folder (building the solution does this).
2. Start the game and load a save. The Bridge exports once, the first time: art at launch, maps at the first save load ([Bridge usage](bridge-preview.md)). The editor loads it by itself when it's done.
3. It never exports again by itself. After adding, removing or updating mods, the welcome page says the export is out of date; use **Preview › Ask the game to export again**.

The shell uses AvalonDock. Panels can be floated, docked, hidden and reopened; layouts persist per project and globally. **View › Reset layout** restores the default arrangement when a layout is off-screen or corrupted.

The pill at the top right is the Bridge light: green *Game ready*, yellow *Load a save*, blue *Game exporting…*, red *Game not running*. Editing works without the game; previewing needs it.

## 2. A project

- **File › New project** (Ctrl+N): pack name, your author name, the first scene's title and where it happens. *Advanced* shows the UniqueID and folder. The editor creates `Documents\SDCharacterStudio\<Name>\` with a `<Name>.sdcs` project file and the pack folder `[SDCS] <Name>\`.
- **File › Open project** (Ctrl+O) opens a `.sdcs`; **Open a pack folder** opens any SDCS pack, even one written by hand (the project file then goes to `%APPDATA%\SDCharacterStudio\Projects`).
- The pack folder is the real thing: Save (Ctrl+S) writes its JSON. Undo/Redo (Ctrl+Z / Ctrl+Y) cover 200 steps. Unsaved work is autosaved every 2 minutes and offered back if the editor closes unexpectedly.
- The **Cutscene** picker in the toolbar switches scenes; **+** adds one. Pack › Duplicate/Delete cutscene.

## 3. The window

| Area | What it does |
|---|---|
| Asset browser (left) | Categories on the rail: characters, outfits and sprite sheets, portraits, locations, items, hats, shirts, pants, boots, hairstyles, furniture, sounds, emotes, and the pack's own videos and poses. Search, and filter by source (vanilla or one mod). A coloured badge with initials means *provided by a mod* (using it adds a dependency); a grey dot means *vanilla, edited by a mod* (no dependency). Hover for a big preview. Double-click or **Use** does the obvious thing; drag onto the stage to place. |
| Stage (centre) | The location's real map, a tile grid, actors at their start tiles and the camera frame (the player's view at 1920×1080). |
| Properties (right) | Everything about the selection: the scene, an actor, a beat, a branch or a video. Problems show under the field they're about. |
| Storyboard (bottom) | The beats in order. The main list, each branch, and *If skipped* are lanes of cards. |
| Problems / Output (bottom tabs) | Validation results with jump-to and one-click fixes; messages from the Bridge and the game while previewing. |

Drag a panel's title to float it or use the docking targets to place it beside another panel. Drag the gaps to resize. Reopen hidden tools through **View**; use **View › Reset layout** to restore the default arrangement.

## 4. Staging

- Pick the **Location** above the stage, or double-click a location in the asset browser.
- Drag a **character** onto a tile to add them. Drag an actor to move (it snaps to tiles; hold **Alt** for 1-pixel nudges). Double-click, or click the arrow on the name tag, to turn them. Right-click for more (remove, put the player here, centre the camera here). **Add player** puts the farmer on stage.
- Drag the **Camera** tab to move the camera, or set *Camera centre* in the scene's properties.
- Toggles: **Grid**, **Walkable** (tiles people can walk on), **Front** (roofs and tree tops drawn over actors), **Camera**. Zoom with the buttons, Ctrl+= / Ctrl+-, or Ctrl+wheel; middle-drag pans.
- When a mod (SVE, for example) redraws the map, a yellow bar says so. **Make a variant for installs without it** copies the scene: the original then plays only when that mod is installed, the copy only when it isn't. Restage the copy for the vanilla map. Share story progress between the two with story flags.
- Selecting a beat highlights who it involves and marks its target tile (a Move's destination, a Camera centre, an Effect).
- **The stage follows the storyboard (D-50):** select a beat and the stage shows the scene once that beat has played: everyone where their walks left them, facing the way they last turned, the camera where the last Camera beat put it. A selected walk also shows a faint copy where it starts and its path. A green bar names the beat shown; **Show the start** goes back to the start positions. Clicking an actor keeps the beat shown.
  - Dragging an actor who has walked changes where that walk ends; before any walk it changes where they start. Turning changes the Face beat that turned them, or adds one after a walk. Dragging the camera frame moves the last Camera beat.
  - With an actor selected, right-click a tile and choose **… walks here** to add a Move after the beat shown: plan a route one step at a time.
- **Items on the map:** drag an item (furniture, a plant, a gift…) from the asset browser onto the stage. It's placed there by a **Place an item** beat after the beat shown, and stands on the map from then until the scene ends. While dragging, the outline shows its whole picture. Drag it to move it, click it to select its beat, right-click to take it away later. (*Give an item* is different: it puts the item in the player's inventory.)
- **Costumes on the stage (D-49):** actors wear what they have on at the selected beat. An NPC's costume sheet shows as it is. A player outfit the game hasn't drawn yet shows the items as icons over their head. **Draw outfits in the game**, with the game running, draws each outfit exactly; the stage remembers it even with the game closed. Each outfit takes the game about ten seconds.
- **Places that move: the spouse's room (D-52).** Every player's house is different: the spouse's room moves with the house upgrade, with map mods, and with the spouse. In a farmhouse scene, an actor's start, a walk's end, the camera, an item or an effect can be **Relative to: the spouse's room** instead of the map (in Properties, above the tile). The tile then counts from the spouse's spot: *1, 0* is one tile right of it, *0, -2* two tiles above it. The game finds the room when the scene starts and everyone, the camera and the items go there.
  - Switch an actor to the spouse's room and the stage keeps them where they are. Actors, walks, items and the camera you add afterwards on that stage count from the room too; dragging keeps whatever a position counts from.
  - **House** (next to *Location*, in a farmhouse scene) picks the house the stage shows: yours as the game export has it, or the house **upgraded once** or **upgraded twice** as your mods make it, with your spouse's room (or the game's default room). A scene that uses the spouse's room is staged in the first upgraded house until you pick one. Positions from the room follow it from house to house; positions on the map don't.
  - A scene that uses the spouse's room plays by itself only when the player has one: married to (or living with) a villager, in a house upgraded at least once. Nothing to add to its conditions.
  - If a scene is played anyway without the room (a preview on a save without a spouse, or another mod playing it without its conditions), the SMAPI log says so and positions from the room count from its usual spot in a once-upgraded house, 32,5.
  - The upgraded houses come with the game export. An export made before they were exported doesn't have them: a blue bar says so. With a save loaded, use **Preview › Ask the game to export again**. Until then the stage puts the room at 32,5.
- **Clothes after the scene:** costumes last until the scene ends, then the player's own clothes come back. Turn on *Keep these clothes after the scene ends* on a Costume beat (or in the scene's ending) to keep them.

## 5. The storyboard

- Click a beat type in the **palette** (left of the lanes) to add it after the selected beat, or at the end of the selected lane. Or drag it onto a lane, or right-click a card › *Insert before / after*, or use a lane's **+**.
- Drag cards to reorder, or between lanes. Keys: Delete, Ctrl+D duplicate, Ctrl+←/→ move, ←/→ step through beats.
- A **Choice** comes with two answers, each with its own new branch. Name branches with the rename button (or F2); Choice and Go to beats that point at a branch follow the rename. A branch header says what leads to it; *Unreachable* means nothing does yet.
- A **Chance** (under *Checks & chance*) comes with two outcomes, each with its own branch: the game picks one by its chance. Set each outcome's chance; the percentage shows beside it (*Equal chances* resets them). *Same result if the day is replayed* (on) means reloading the morning can't reroll it.
- A **Check** (under *Checks & chance*) comes with one case and its own new branch: it goes to the branch of the first case whose conditions are true, otherwise to its *Otherwise* (keep going, or a branch). The card lists each case and where it goes.
- A branch that runs out continues after the choice or check that led to it, unless it ends with **End scene** or **Go to branch**.
- **Branch graph** draws Choice, Go to and return arrows. **Selection** centres the selected beat at readable size; **Fit** shows the whole graph. Drag the background to pan and use the wheel to zoom. Selecting a node opens its Properties. The card view also draws arrows to branch headers.
- Cards show problems: a red or yellow edge and an icon with the message.

## 6. Properties

Every beat type has friendly fields (who, where, how long, which item…), with pickers for actors, branches, items (with icons) and sounds. Notes and tips explain the less obvious ones. Useful details:

- Dialogue fields take Enter for a new line; **Ctrl+Enter** commits. *Translation key* stores an i18n key instead of text.
- **Music** in scene settings and Music beats has **Import music…** for your own WAV (16-bit PCM, mono/stereo) or OGG Vorbis files, up to 64 MiB. Import copies the file into the pack and selects it; the original file can then be moved. Imported tracks appear by name in the picker and travel with every export. **Loop music** repeats the track; changing it affects every scene using that imported track. **Play music** auditions it for up to 10 seconds with the game closed (the game must still be installed); **Stop preview** ends the audition. Undo removes both the import and its assignment.
- **Pick on stage** (the target button next to tile fields) lets you click the tile. For a position relative to the spouse's room, the tile you click becomes tiles from the room's spot.
- The scene's **When does it play?** section (click an empty spot on the stage to see the scene's settings). New scenes play *By itself* the first time the player enters the location; add conditions to choose the moment. The options: *By itself* when its **Conditions** rows are true (hearts, story flags, the season, an item, an installed mod…), play once, or *Only when started by preview or another mod*. Scenes made with the game's older event preconditions still show those below the rows.
- **Conditions (D-51)** are rows you build from lists, in a Check's cases and the scene's *Conditions*: *hearts with · Abigail · at least · 6*, *story flag · trusts-you · is set*, *season · is · fall*… Pick **＋ Add a condition…** for people, story, player, world, where and other checks. With two or more rows, choose **all** or **any** of them. Every comparison has its opposite (*isn't*, *less than*). **As a game query** shows the same conditions as the game's own text; paste or edit one there, and text the rows can't show stays as a *game query* row.
- **Give an item** › *One of these, at random* turns the item into a bucket: add items, set how many and each one's chance (shown as a percentage). In the message, `{item}` becomes the item they got. *Remember which one* sets a story flag per item for a Check later.
- A **Choice** answer can be offered **Only if…**: turn it on under the answer and build its conditions with the same rows. The card shows it ("· only if Abigail ≥ 6♥"). With no answer offered, the scene simply goes on after the Choice. A random item can have *Only if…* too (the rare hat only from year 2).
- **Change a number** (under *Checks & chance*) adds to, takes from or sets a **story number**: jokes told, times you helped Robin. Pick a number the project uses or type a new name; *Or a random amount up to* makes the amount random. A Check's *story number* row compares it (*at least 3*). Numbers start at 0 for every player and never go below it.
- **Say** and **Message** › *Add a line*: the game says one of all the lines at random, each time. The card shows "1 of 3 lines".
- **Story flag** beats remember what happened. *Use a name for this scene* suggests `SDCS.<pack>.<scene>.Done`.
- **▶ Preview from this beat** is on every beat.
- Select a sound in the asset browser and click **Play**, or use **Play** beside a Sound beat's cue. **Stop** ends it early; auditions stop after 10 seconds. After a fresh Bridge export, vanilla and exported mod OGG/WAV cues play with the game closed. Keep the game installed at the exported path. Offline audition does not reproduce in-game positional, category, or reverb routing.

## 6.1 Story

**View › Story** (a tab beside the Storyboard) lists every story flag and story number in the project: where it's set or changed, where it's checked, and a note you write about what it means. Click a place to go there. What looks wrong comes first, outlined in yellow: a flag that's checked but nothing in the project sets (often a typo), or a number that's compared but never changed. *Nothing checks it yet* is only a reminder.

## 7. Problems

The editor checks the pack the way the game will read it, half a second after each change. Errors block preview and export; warnings don't. Double-click a problem to jump to it. One-click fixes: replace `/` and quotes with look-alikes, use the NPC name the game expects (names are case-sensitive in events), sort keys, and open Dependencies for content whose mod the export can't tell.

**Positions on the map (D-54).** A start, a walk, the camera, an item or an effect off the scene's map is a warning: *Abigail starts at 53,24, which is off the FarmHouse map (12×12).* Starting on or walking to a tile nobody can walk on (a wall, furniture) is a gentler hint, since a scene may mean it. Positions relative to the spouse's room aren't checked (the game finds the room). The farmhouse and cabins change with every upgrade, so their positions are checked only against an upgraded house picked in **House** above the stage. Using the spouse's room outside the farmhouse is a warning too.

## 8. Preview in the game

With the game running, a save loaded and the Bridge light green:

- **Preview in game** (F5) plays the current cutscene, unsaved edits included. Switch to the game to watch.
- **Shift+F5** (or *Preview from here*) starts at the selected beat, with actors where earlier moves left them.
- **Ctrl+F5** stops it and gives the player back control, restoring their look.

Previews use a separate copy of the pack (`<UniqueID>.DevPreview` under `%LOCALAPPDATA%\SDCharacterStudio\Preview`), so nothing needs saving or installing, and an installed copy of the same pack doesn't clash. Messages from the game appear in **Output**.

**Try it as…** (beside *Preview in game*) lists the scene's Checks, Chances and random items. Pick how each should land (a case, an outcome, an item) and the next previews play it that way; *Let the game decide everywhere* clears the picks. Only the preview's copy changes: the pack and your save keep their real conditions. The button counts your picks, and the preview's message says when they're in effect.

## 9. Dependencies and export

**Pack › Dependencies** shows what other mods the pack needs, worked out from what the scenes use:

- *Mods your pack uses*: their content only exists with them. Required by default. Switch on **Only some scenes need it** to make the mod optional: the pack then loads without it, and only the scenes that use it skip themselves.
- *Which mod adds these?*: content the export couldn't trace; choose the mod, or *Not a dependency*.
- *Changed by mods*: vanilla content another mod restyles. No dependency; tick *List as optional* if you want SMAPI to load that mod first.
- *Extra dependencies*: anything the editor can't see.

**Export** (Ctrl+E) checks the pack, writes the dependencies (with reasons) into `manifest.json` and `studio.json`, and saves. Then it writes what players install (*What players install*):

- **Just this mod** (recommended): a complete SMAPI mod in `Export/<name>` next to the project, with the scenes and the engine inside its own DLL. Players install only this mod (plus the mods the scenes use).
- **My C# mod**: `Sdcs.cs` (the engine in one file), a `scenes` folder and a read-me, in `Export/<name> for my mod`. Copy them into your mod's project and call `Sdcs.Register(this)`; see [Putting SDCharacterStudio scenes in your mod](embedding.md). Your mod has no manifest from the editor, so the read-me and the export's message list the mods your scenes need (add them to your manifest's `Dependencies`) and the maps you staged as another mod changes them. Conditions about your own scenes and story numbers keep working under your mod's ID (D-53).
- **A content pack for the SDCharacterStudio framework**: the pack folder itself; players install the framework too.

The line under the folder box names the folder it writes. For a complete mod, the dialog points out any scene set to *Only when started by preview or another mod*: nothing would start it in a mod on its own. **Show me** opens that scene's settings. After exporting, **Open folder** shows the result, and the dialog says what to do with it.

*Browse* chooses another folder: an empty one, or an earlier export of this pack (the editor never overwrites anything else). Optionally it also makes a zip, or copies the mod into the game's `Mods` folder (restart the game afterwards); it refuses when another folder there has the same mod ID.

**Without the window.** `sdcs.exe` (built from `src/SDCharacterStudio.Cli`) runs the same checks and exports from a terminal or a script:

```text
sdcs check  <project.sdcs> [--game-export <folder>]
sdcs export <project.sdcs> --as mod|csharp|pack --to <folder> [--game-export <folder>]
```

`check` prints each problem on a line (`error V8 cutscenes/x.json › Beats[2].Then: Unknown branch 'b'.`) and a count. `export` checks first and, like **Export**, saves the pack folder with its dependencies before writing `--to` (`pack` copies the content pack into that folder). Both use the editor's game export unless `--game-export` names a profile folder. Exit codes: 0 no errors, 1 errors (nothing exported), 2 the command couldn't run. From the repository: `dotnet run --project src/SDCharacterStudio.Cli -- check MyScenes.sdcs`.

## 10. Settings and files

**File › Settings**: the Bridge export folder and profile, the game's Mods folder, where new projects go, and your author name. Settings live in `%APPDATA%\SDCharacterStudio\editor.json`; crash details in `crash.log` next to it. Set `SDCS_EDITOR_HOME` to keep settings, previews and logs in another folder.

## 11. Character Studio (M8)

Sprites, poses and animations are made in the **Character Studio**. Switch to it with **Character Studio** at the top of the window; **Scenes** switches back. Everything you make is part of the pack, saves with Ctrl+S and undoes with Ctrl+Z like any scene edit. With nothing open, the studio lists what you can make, each with a line on what it is. Items show by name (*Wave*); the ID the pack and beats use (*abigail-wave*) is in the tooltip.

### 11.1 The left panel
- **Character**: who you're working on: anyone in the game export, or the player. Their standing frames show below; with Advanced on, so do the rig status and **Rig…**, which opens the character's rig (it's made automatically the first time you need it).
- **This pack** lists the pack's poses, outfits, animations, masks, rigs, expressions and portraits; **+** on a shelf makes a new one. Double-click opens it in a tab. Right-click: *Duplicate*, *Make a copy for <character>* (poses), *Use in the current scene*, *Save to My library*, *Delete*.
- **My library** holds your own poses and animations for every project (§11.6).
- Search filters both; *Only this character's items* hides the rest.

### 11.2 Poses
**Edit what you see (D-48).** A new pose opens with the character's real standing frame and the **Select** tool. Each frame is **one picture**: whatever you do changes that picture, and no layers appear.
- **Move a part:** drag a box around it, then drag inside the box. To lower the head, select the head and drag it down. Ctrl+drag copies instead.
- **Erase:** the eraser removes any pixel you see, even the character's own.
- **Draw:** the pencil draws new pixels on top.

**Tools and keys (D-49)** work like Aseprite's:
- **The tool rail** on the left shows each tool's key: M select, B pencil, E eraser, D shade, G fill, I eyedropper, L line, U rectangle, Shift+U ellipse.
- **Picking colours:** hold **Alt** to pick a colour with any tool. The eyedropper goes back to your tool after one pick.
- **Brushes:** `[` and `]` change the brush size. The options bar above the canvas has *Pixel-perfect* (no doubled corners on diagonals) and *Symmetry*.
- **Zoom and pan:** in poses, expressions and masks, the wheel zooms at the pointer in crisp whole-pixel steps. Space+drag or a middle drag pans; Ctrl+0 fits the frame. In poses, tapping Space or pressing Enter plays.
- **More drawing room:** click **Focus canvas**, or press **Tab while the canvas has focus**, to hide the library and side panels. Repeat to restore them with your splitter widths intact.
- **Display settings:** the pose toolbar keeps play, zoom, Fit and Grid visible. **View** contains onion skin, standing-frame ghost, background and **Mirror the view** (Shift+M). Mirroring shows the canvas flipped to see the drawing fresh; only the view changes, and clicks still land on the pixel you point at.
- **Cancel safely:** press **Esc** during a brush stroke, selection or drag to put it back. Losing mouse capture or leaving the editor also cancels an unfinished stroke. Undo (Ctrl+Z) reverses a completed stroke in one step.
- **Onion skin:** open **View > Onion skin settings** to choose the frames before and after, opacity and red/blue tint.
- **Colours:** the panel shows the character's colours, every colour **in this picture**, **recent** ones and a colour picker. Click a swatch to paint with it (right click: the right button). **Click the big colour square** (or double-click any swatch) to mix a new colour: you paint with it as you mix, the hex box takes a code, and *Done* closes it. Colours picked from the sprite paint as the character's colours, so they follow outfits. **Named colours** are covered in §11.8.
- **Inside the character only:** with a painting tool, tick *Inside the character only* in the options bar. The brush, shapes, fill and shading then touch only pixels where the character (or your paint) already is, so a pattern or a fold stays inside their outline. With several frames picked, each frame keeps to its own outline.
- **Shade (D):** left click makes what's under the brush one step darker, right click one step lighter, each pixel once per stroke.
  - On her own colours it steps along her colour (her skin mid to skin dark) and paints her colour, so it still follows sprite mods.
  - On a named colour it steps through its dark, mid and light.
  - Anything else, or past the end, it shades by eye: darker leans blue, lighter leans yellow.
  - With several frames picked it shades the same spot on each, from that frame's own colours.
- **Outline what you painted:** the outline button on the tool rail puts her outline colour around your paint wherever it meets empty space. It works on every picked frame as one undo step, and corners stay clean. Use it after painting clothes past her edges (a hem, a cape).
- **Selecting:** M is a box, Q a lasso, W a magic wand (Ctrl: that colour everywhere). Shift adds to a selection and Alt takes away. Painting, erasing and fill stay inside it. **Isolate** (Shift+I) dims everything else.
- **Transform:** Ctrl+T floats what the selection shows.
  - Drag inside to move it, a corner or an edge to scale it, and the round handle to rotate it. Shift keeps proportions, or snaps rotation to 15°.
  - Drag the crosshair to rotate about another point.
  - The options bar has the angle, the size, flips and quarter turns. Shift+R turns a quarter right without opening the box.
  - Enter applies and Esc puts it back.
  - Her pixels stay hers when rotated: no new colours appear, and they still follow outfits.
- **Moving:** V moves what's selected, or the whole picture when nothing is.
- **Animating:**
  - **The preview** at the top right keeps playing the pose (1×, 2× or game size) while you edit.
  - **Plays:** under the frames, choose *Loop all*, *Once, then loop from frame N*, *Once, then hold the last*, or *Back and forth* (forwards, then backwards: 1 2 3 4 3 2 1 2..., for a wave or a sway). Coloured bands on the frames show it: amber plays once, green loops. New and moved frames keep the order.
  - **Several frames at once:** Shift or Ctrl+click frames to pick several. Drawing, erasing, a move, transform, flip or clear then applies to all of them, as one undo step. *Reverse them* swaps the picked frames end for end, in their places.
  - **Linked frames:** pick frames and press *Link them*, and they share the current frame's picture: draw on one and all of them change (a hold, a blink's open frames). Linked frames show a chain in the same colour under them. *Unlink this frame* gives a frame its own copy again. Duplicating the pose, or copying it for someone else, keeps the links.
  - **Reordering:** drag a frame to move it; Ctrl+drag copies it.
  - **A breathing loop:** *Make a breathing loop* adds a frame with her upper body a pixel lower and loops the two slowly.
- **Her outfits:** the **Outfit** picker under the character shows your own outfits for her first (§11.7), then every outfit the game knows for her: seasons, festivals (the Flower Dance, Spirit's Eve...) and mods' outfits, named with the mod that adds them. The whole studio then shows her in it. Poses stay live: in the game she wears whatever the day gives her. *Always wear the outfit shown* pins one to a pose; a mod's outfit makes that mod a requirement.
- **Game frames:** *Game frames* shows every frame of the character's sprite in the outfit shown, including festival frames on their main sheet. Click one to start this frame from it; Ctrl+click adds it as a new frame. **Shift+click** shows it on the canvas: select part of it (a raised arm, a turned head) and *Copy into this frame*. They all stay live. Pastes (Ctrl+V) float too: drag, turn or flip them, then Enter.
- **Body parts:** *Head*, *Body*, *Arms* and *Legs* select that part of the character. *Show parts* colours the parts; if one is wrong (long hair that belongs to the head), paint it right with the part brush. The fix is saved for the character and that facing.
- **Ready-made poses** are drawn on her and made from her body. There are still poses (bow, look down, tilt, lean, slump, shrug, crouch, kneel, beg) and little animations (nod, shake the head, laugh, jump for joy, shiver, sob). Point at one to watch it in the **Preview** (its title says which); click it to make this frame that pose (an animated one adds its frames after this one).
- **Frames:** Shift or Ctrl+click picks several in the strip; the delete button (or Delete in the strip) removes them all. Picking every frame keeps frame 1.
- **Backdrop:** the drop-down next to *Ghost* puts checks, dark, light, or *Where the scene plays* behind the pose. The last one shows the floor of the current scene's map where that character stands.
- **The right panel** folds: *Ready-made poses*, *Body parts*, *Game frames* and *Outfit* open and close with their titles and stay that way. A portrait shows none of them (they are the sprite's); a short note says what a portrait is instead.
- **Plays** (under the strip, for a pose of two frames or more) picks the order: *Loop all*, *Once, then loop from frame…*, *Once, then hold the last*, or *Back and forth*. A frame bigger than the canvas opens fitted.
- **All keys:** press **F1** or **?**.

Her real pixels keep following sprite mods and seasonal outfits, even after you move them. A pose made of layers in an older version shows **Make it one picture**; it looks the same afterwards.

Under **Advanced**, **Split into parts** turns the character into head, arms, body and legs (side views: head, body and legs). Press on a part and drag it: the head down, an arm forward; hover a part to see its name. The parts are still the real sprite, so they follow sprite mods; the original stays hidden underneath as a reference. **Redraw the body, keep the real head** hides the body and gives you an empty one to draw. Layers, anchors, masks and the rig are under **Advanced** in the canvas toolbar; the rest of this section describes them.

**New pose** starts from the character's real standing frame for the facing (a live layer, so it follows sprite mods and seasonal outfits). A pose is a set of frames; **Intro +**, **Loop +** and **Outro +** under the frame strip say which frames play once, repeat while the pose holds, and play when it ends. The frame strip's numbers are each frame's time in ms.

- **Tools** (left): pencil `B` (Shift+click draws a line from the last point), eraser `E`, fill `G` (Ctrl+click: every pixel of that colour), line `L` (Shift: 45°), rectangle `U` (Shift: square; *Filled rectangles*), colour picker `I` (or Alt+click), select and move `M` (drag inside the selection to move it; Ctrl+drag copies; arrows nudge; `H` mirrors; Ctrl+C/X/V; Delete clears), anchor `N` (places Neck, Hand… on the frame).
- **Colours**: left click picks the left-button colour, right click the right one; `X` swaps them. The **reserved slots** (Skin, Hair, Shirt… dark/mid/light, Outline, Custom, Erase) become the character's real colours in the game, so a body painted in Skin mid gets Robin's skin with SCC installed and vanilla skin without. *Show the character's real colours* previews that on the swatches; **Slot colours** (`P`) on the canvas shows them as painted.
- **Canvas**: the wheel or zoom buttons zoom in whole steps; **Grid** toggles pixel boundaries. Under **View**, use **Onion skin** (`O`, the frames before and after) and **Standing frame ghost**. **All frames** applies layer changes and anchors to the same layer in every frame. Space plays the pose; `,` and `.` step frames.
- **Layers** (right, top is drawn last): a live character or live head, paint (your pixels), an image from a PNG (one cell per frame), or an effect. The eye hides a layer, the lock stops accidental drawing. Drawing on a live layer adds a paint layer above it.
- **Isolate head** is the quick way to a new body: the full character is hidden but kept, their live head hangs from the Neck anchor (at the rig's neck point), and an empty *Body* paint layer below it has the skin and clothes slots set up. Draw the body; place the Neck on each frame; the head follows sprite mods.
- **Properties** (right, bottom): the pose's ID, rig, drawn facing (*Mirror for the opposite facing* reuses it flipped) and pivot (the point that stands on the actor's tile); the frame's time, sound, dust and anchors; the layer's source, part, mask, anchor, offset, flip, opacity and colour slots. A player layer has a part checklist (*Player head only* is one click). *Convert to paint* bakes a live layer (it then stops following sprite mods).
- **Edit in external editor…** (a paint or image layer) writes all its frames as one PNG strip with a JSON note and opens it in Aseprite, LibreSprite or your image app (set it in `%APPDATA%\SDCharacterStudio\studio.json` as `ExternalEditor`; otherwise Windows' default app). Saving there brings the change back as one undo step. `sdcs-slots.gpl` next to it is the reserved-slot palette for those apps. Keep the strip's size.

### 11.3 Animations
**Flipbooks (D-48).** In the simple editor a new animation is a row of steps, played left to right: *Show a pose* (one of your poses, or a ready-made one: picking it makes her own pose of it, e.g. `abigail-nod-down`, facing the way the step faces or she last walked), *Walk* (an arrow and a number of tiles; the character walks with their real walking frames) or *Their own look*. Each step has a time in seconds and can turn the character to face a way. **Add step** adds one; the arrows on a card move it; the stage shows the result as it plays. *Repeat* plays it again from the start. The timeline below is under **Advanced**.

An **animation** is reusable choreography: it moves *roles* (for example `robin`) instead of particular actors, and its positions count from where each role starts, so any scene can cast it on anyone, anywhere.

- The **preview stage** shows the cast on a floor. Drag an actor to move where it starts in the preview. Turn on **Record** (`R`) and drag to make a position key at the playhead.
- The **timeline** has a row for each track of each role: position, facing, pose, visible, clothes/parts (player), expression, emote, sound, effect. Click to scrub, double-click a row to add a key there (or **Key** for the selected row), drag a key to move it, Delete removes it, drag the yellow end to change the length. Space plays; Home/End jump.
- A key's properties edit it: where (tiles from the start) and how it gets there; a pose and how it plays; for the player, *Change clothes or look* (Hat none, Shirt…) and part visibility, which revert after the clip unless *Keep after the clip*.
- With no key selected, the properties show the animation: title, ID, tags, length, steps (smooth or 12/8/6 fps), loop, and **roles** (a character, the player, or anyone). **Preview** settings try it *as* another character, **Mirror**ed, or with a pose swapped; they change only the preview.
- A scene's own clip opens here too (*Edit the clip in Character Studio* on its Clip beat); it moves that scene's actors on absolute tiles, and *Save to library…* turns it into an animation.

### 11.4 Masks, rigs, expressions and portraits

Expression tools use **B / E / G / I** for pencil, eraser, fill and picker; **X** swaps colours. Mask tools use **B / E / W** for brush, eraser and wand. Both support wheel zoom, Space/middle-drag pan, Ctrl+0 Fit and Escape to cancel a stroke. Keyboard shortcuts leave text fields and normal button operation alone.

- **Masks** cut part of a live sprite (a head, a body part). Paint over the live frame (red = selected) with the brush, the wand (tolerance; Shift subtracts), *Above the neck line*, Grow, Shrink, Invert. *Give it its own mask* makes a mask just for the shown frame (a walk frame where the head bobs); *Mirrored copy* makes …-right from …-left. The **fit check** at the bottom shows every installed variant of the character (vanilla and mod outfits) with the mask applied, and over the open pose that uses it.
- **Rigs** say where a character's neck and face are for each facing (click with *Neck*, drag with *Face box*), which mask is the head, and where slot colours are sampled. *Portrait* edits the portrait's neck, face box and head mask. A new rig finds where the character's own head ends in each facing (the chin; the hair in the back view) from her sprite, so the head masks, neck points and face boxes fit her; the ready-made poses move that head. A rig made by an older version gets the same fix for any head mask nobody edited.
- **Expressions** paint closed eyes, blushes or mouths over the live face (sprite or portrait), anywhere on it: the face box is only where the paint is anchored. Alt+click takes a colour from the face as you see it, and the character's colours paint as they look. **Remove pixels** paints with Erase, which removes the face's own pixels; the Eraser tool only takes back your own paint. Painting with Transparent says so. Several frames with times animate (a blink); Shift or Ctrl+click picks several to delete.
- **Portraits** use the pose tools with one frame per emotion (neutral, happy, sad, unique, love, angry, or numbers). Isolate head floats the live portrait's head over a drawn body; Say beats use it as *Composed portrait*.

### 11.5 Using them in scenes
- **Use in the scene** (top right of the open item) puts it in the cutscene you have open, after the selected beat, on its character (who joins the scene beside the last actor if they aren't in it), then shows the scene: a pose plays (Pose beat), an outfit is worn (Costume beat), an animation plays (Clip beat), a portrait or an expression shows in a line (Say beat). The library's right-click *Use in the current scene* does the same.
- **Poses**: *My poses* in the asset browser (Use adds a Pose beat), or the Pose beat's properties.
- **Animations**: *My animations* in the asset browser. Select an actor and **Use**, drop it on an actor on the stage, or drop it on the storyboard: a Clip beat appears with its roles cast. The Clip beat's properties change the cast, **Mirror** it, and swap poses for whoever plays it (*Make a copy for <character>* on a pose makes that copy: it switches the live layers, masks and colour samples to the new character and tells you if their rig lacks a mask).
- **Outfits**: a **Costume** beat with *Wear an outfit you made* puts one on (pick it under *Outfit*); *Back to their usual look* takes it off. An actor can also wear one from the start (*Outfit at the start*). The stage shows them in it from that beat on.
- **Preview in game** (F5) in the Character Studio plays the open pose, animation, expression or portrait in a short temporary scene at the location of the scene you have open, with the preview's cast and Mirror.

### 11.6 My library
*Save to My library* (right-click a pose or animation) copies it, with everything it needs (its art, masks, rigs, and for animations the poses and expressions it plays), into `%APPDATA%\SDCharacterStudio\Library`. In any project, **My library › Use** copies it into that pack: items the pack already has unchanged are reused, a clash gets a new name (`bow-2`), and a pack that already rigs the character keeps its own rig. The pack then works without the library. Using an item again offers **Update it** (refresh the copy from the library) or **Add a copy**.

### 11.7 Outfits
An **outfit** is your own version of a villager's whole sprite sheet: new clothes for Abigail, a raincoat for Robin.
- **Make one:** press **+** beside *Outfit* under the character (or on the *Outfits* shelf) and name it. It starts from what the studio shows: her own look, a game outfit (the Flower Dance…), or another of your outfits. Every frame of her sheet is there, in the game's places: the four walking directions first, then her special frames.
- **Paint the clothes on:** each frame is one picture, like a pose. Pick the four steps of a walk in the strip (Shift+click) and draw once to draw on all of them; then fix each step where she moves. Her face and hair stay live, so they follow sprite mods under your paint. The preview walks her in the direction of the frame you're on. Frames can't be added, removed or moved: the game picks them by their place.
- **The frame strip** labels the groups: *Walking down*, *Walking right*, *Walking up*, *Walking left*, then *Other frames*.
- **See her in it:** your outfits come first in the *Outfit* picker (marked *your outfit*). Picked, the whole studio shows her in it: the character card, poses, *Game frames* and the ready-made poses.
- **Poses in your outfit:** with your outfit shown, start a pose's frame from *Game frames*. The pose then always wears that outfit (the *Outfit* section says so), and stays linked: paint more on the outfit and every pose made from it shows it.
- **In scenes:** see §11.5. In the game the outfit replaces her sheet while she wears it, so walking and the game's own animations keep working.
- **In the pack:** an outfit is a pose of kind `sheet`. A pose wears one with a live layer whose source is `{ "Kind": "Outfit", "Outfit": "<its ID>" }`. An outfit can be made from another outfit, never from itself (Problems says so).

### 11.8 Named colours and recolouring
**Named colours** are colours you can change later, everywhere at once.
- **Make one:** pick a colour, then press **+** beside *Named colours* in the colours panel and name it ("Coat"). You get its dark, mid and light, worked out like the shading brush.
- **Paint with it:** like any swatch; the shading brush steps through its shades.
- **Change it:** the pencil on its row (or right-click, *Change it everywhere…*) opens the colour picker. Pick the new colour and press *Change Coat everywhere*. Every pixel painted with it, in every frame of every outfit and pose, portraits and expressions, changes as one undo step. Shift+click one shade to change just that shade. *Forget the name* leaves the paint as it is.

**Recolour** (the *Recolour* section on the right) changes one of her own colours, still live: the same dress in red, her hair dyed.
- *Recolour one of their colours* starts with the clothes (shirt, then pants, shoes, hair) and the left colour. Change which colour with the list; change the new colour by clicking it (a picker opens right there, *Recolour* applies it) or with *Use the left colour*.
- *That colour and its shades* is right for most clothes. Choose *Just that colour* if it catches too much of her, or *Nearby colours too* if some shades stay as they were. Her skin is left alone unless you recolour her skin.
- It applies to every frame of the pose or outfit. Poses wearing an outfit show the outfit's recolours. Sprite mods still draw her underneath, so a mod that redraws her dress gets recoloured too.

## Keyboard shortcuts

| Keys | Action |
|---|---|
| F5 / Shift+F5 / Ctrl+F5 | Preview / preview from the selected beat / stop |
| Ctrl+S, Ctrl+Z, Ctrl+Y | Save, undo, redo |
| Ctrl+N, Ctrl+O, Ctrl+E | New project, open project, export |
| Delete, Ctrl+D, Ctrl+←/→, ←/→, F2 | Storyboard: delete, duplicate, move, step, rename branch |
| Ctrl+= / Ctrl+-, Ctrl+wheel, middle-drag | Stage zoom and pan |
| Alt+drag, double-click, Delete, Esc | Stage: pixel nudge, turn, remove actor, cancel tile pick |
| Enter / Ctrl+Enter | Commit a field / commit a dialogue box |
| B E G L U I M N | Character Studio tools: pencil, eraser, fill, line, rectangle, picker, select, anchor |
| X, H, O, P, Space, `,` / `.` | Swap colours, mirror selection, onion skin, slot colours, play, previous/next frame |
| Ctrl+A/C/X/V, arrows, Delete, Esc | Selection: all, copy, cut, paste, nudge, clear, cancel |
| Space, R, Home/End, ←/→, Delete | Animation: play, record, start/end, scrub 100 ms, delete key |
