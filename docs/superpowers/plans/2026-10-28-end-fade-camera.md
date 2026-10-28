# End fade camera implementation plan

**Goal:** Preserve the authored scene view until the end fade is fully black.

**Scope:** Use the existing natural-end cleanup. Preserve stay/default return policy and private engine copies.

- [x] Reproduce the visible camera movement with per-tick viewport/fade evidence before editing the runtime.
- [x] Trace the custom End command and vanilla event cleanup; identify the pre-fade return pan.
- [x] Remove the return pan from EndCommand while preserving stay position/orientation and session cleanup.
- [x] Add opt-in per-tick recording to the existing bench driver.
- [x] Export neutral C# embedded and framework fixtures, covering stay/default, main/branch End, fixed/beat camera.
- [x] Verify all 16 cases hold the exact final view through full black and recover afterwards.
- [x] Run Core and Editor tests and both standard bench modes. Retain skipped/failed verdicts accurately.
- [x] Install verified artifacts; the runtime and C# engine match the in-game-tested files.
- [x] Independently review the final diff, verify commit identity and public content, and commit locally.

No push or main-branch merge is included.
