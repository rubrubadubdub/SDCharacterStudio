# Farmer costume and live head demo

Copy this folder into `Mods` beside SDCharacterStudio, then load a farm. These repeatable examples are console-only and do not trigger naturally.

```text
sdcs_play Player.SDCS.FarmerDemo farmer-costume
sdcs_play Player.SDCS.FarmerDemo floating-head
```

The wardrobe clip keeps the original hat initially, removes it at 1200 ms, changes the displayed shirt at 2400 ms, and hides arms/pants at 3000 ms. At 3600 ms its temporary changes revert. Normal completion and skipping restore the original equipment references. Actual shirt and pants items stay equipped throughout.

The second scene composes the player's live head, eyes, hair, accessory and hat over the included painted body. The source is a 32x48 canvas. Its down-standing frame 0 neck anchor is `[16,32]`: the base frame starts at `[8,16]` and vanilla frame 0 has a one-pixel feature offset. The body PNG contains only original example art; player pixels come from the running game.

Costume `Preset` values come from `library/costumes.json`. Individual Parts keys can set `Persist:true`, and a Clip beat can set `Hold:true`, to retain their result after the clip. The cutscene's default `OnEnd.RestoreCostume:true` still restores the player's original look at scene end.

```text
sdcs_farmer_parts 0 down
sdcs_restore_costume
```

The first command exports part PNGs into the runtime mod's `debug` directory. The second restores any outstanding costume and clears active part overrides. Shoes are native base pixels: hiding them recolors those pixels to skin; it does not change equipment or draw a new barefoot shape. Swimming water, tools, held items, and mounts are outside the composed part set.
