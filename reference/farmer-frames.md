# Farmer base frames (Stardew Valley 1.6.15)

These entries are verified from the installed game's `FarmerSprite.faceDirection`,
`UpdateSourceRect`, and animation constructors. Standing frames, plus requested frames 0/6/12/18 across both genders and all four facings, also passed native pixel comparisons and a visual Full/Head/Body matrix in M4. The other animation labels below remain source-derived.

| Pose | Base frame | Facing | Mirror horizontally |
| --- | ---: | --- | --- |
| Stand down | 0 | down (2) | no |
| Stand right | 6 | right (1) | no |
| Stand up | 12 | up (0) | no |
| Stand left | 6 | left (3) | yes |
| Eat, first raised-food frame | 84 | down (2) | no |
| Drink, first frame | 90 | down (2) | no |
| Tired / head lowered | 16 | down (2) | no |
| Pass out, final frame | 5 | down (2) | no |
| Sick | 104, 105 | down (2) | no |

There are 126 base frames, numbered 0–125. The base frame's rectangle is
`(frame * 16 % 96, frame * 16 / 96 * 32, 16, 32)`: six columns, with integer
division. Frame 18 is not the left-standing default; left standing mirrors frame 6.

`new FarmerSprite.AnimationFrame(frame, milliseconds)` uses primary arms
(`armOffset = 6`), zero position and x offsets, and no mirroring. Secondary arms use
`armOffset = 12`; animations may supply other offsets or hide arms. The runtime
parts renderer uses the live animation's offsets when rendering its current base
frame; other requested frames use the primary-arm defaults. Facing left supplies
mirroring without changing the farmer's facing or sprite state.

The runtime returns a 32×48 canvas with the 16×32 base frame at (8, 16). The inclusive
neck row is `15 + FarmerRenderer.featureYOffsetPerFrame[frame]` in base-frame
coordinates; the neck anchor in canvas coordinates is
`(16 + signedFeatureXOffset, 31 + featureYOffset)`, where the x offset is negated for
left facing. The `GetNeckAnchor(Farmer,frame,facing)` overload adds live animation position/x offsets, matching the selected rendered frame. The overload without a Farmer returns this base-frame anchor.

The remaining tool, sitting, carrying and item-holding poses need visual labels
before inclusion in an editor pose gallery.
