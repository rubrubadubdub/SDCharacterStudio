# M9 wardrobe fixture

This is a disposable Content Patcher fixture for the native compatibility pass. It is intentionally outside the installed `Mods` directory and contains no copied game pixels.

Install only in an isolated test Mods copy with Content Patcher 2.9.1 or newer. Its manifest ID is `Player.SDCS.M9WardrobeFixture`. The fixture adds:

- `Data/HairData` entry `9000`, using the vanilla `Characters/Farmer/hairstyles` asset at tile `(0, 0)`, with no unique left sprite, no covered style, and no bald flag. The data value is `hairstyles/0/0/false/-1/false`; the game prepends `Characters\\Farmer\\` before loading it.
- `Data/Hats` entry `Player.SDCS.M9TestHat`, using the vanilla `Characters\\Farmer\\hats` texture path and sprite index `0`.

The native LAN `-Wardrobe` check equips both entries on disposable farmers, records the parsed hair metadata, plays a live full-farmer pose through the embedded engine in each process, and asserts that the equipped hair/hat and player control/visibility return to their pre-scene values. `../SceneOverlay` supplies that pose and replaces only the copied bench scene. It is not a standalone mod. See `docs/m9-verification.md` for the executed result. This synthetic fixture is not an installed user mod or a test of a Fashion Sense custom outfit.

The HairData six-field value follows the installed 1.6.15 `HairStyleMetadata` contract: texture, tile X, tile Y, unique-left flag, covered index, bald flag. The hat value follows the installed CP `Data/Hats` shape observed in the owner's RSV/East Scarp/Rodney packs: name, description, show real hair, skip hairstyle offset, tags, display name, sprite index, texture suffix.
