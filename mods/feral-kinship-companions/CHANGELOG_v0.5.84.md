# Companions 0.5.84 — recovery, return safety and larger flaps

**Before updating, back up the complete world and retain its matching mods. Update Companions on the server and clients, keeping one Companions package per mod ID. Rollback requires the whole pre-update world backup with matching mods; swapping only an older DLL is unsafe. Recovery checkpoints are not a substitute for that backup.**

## Changes since the actual prior stable, 0.5.83

- Guard the reproduced return-transition reentry that could put a returning original on Hold. Retain completion proof for new returns, improve return/presentation reconciliation and keep detailed diagnostics.
- Add bounded, persistent recovery of independently verified eligible completed originals in old saves, including offline owners. Loaded eligible originals are recovered in place; deferred unloaded originals wait for natural loading. No outcome, loot or reward callback is replayed.
- Keep guarded admin repair for the observed completed-ledger/native-Hold mismatch, using the original native inventory, identity, current health and compatible progression with native/ledger checkpoints. Missing historical rewards are not inferred.
- Connect individual Developer Ledger Rebuild and selected-player Recover all to server-generated previews and explicit, expiring, one-use confirmation. Recheck each animal and the selected owner's permissions, report skipped/failed cases honestly and preserve active, unknown or conflicting authority. No global recovery is added.
- Retain the latest detailed diagnostics, safer owner-scoped selection/recovery and archive restoration. Previously released dedicated-startup corrections and Social → Wellbeing remain.
- Add passive flaps with 2×2 and 3×3 clear passages beside the existing 1×1 flap. Their frame footprints are 4×3 and 5×4. Bear clipping at the smaller frame is an accepted visual limitation.
- Correct raw placement-refusal messages with six native game-domain English aliases while retaining the original translations.
