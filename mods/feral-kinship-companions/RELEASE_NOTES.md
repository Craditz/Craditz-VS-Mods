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

## Recovery limits and admin follow-up

Automatic recovery targets verified eligible old records; it does not guarantee every missing animal. It preserves the verified native original and never creates a guessed body because an original is unloaded. Ambiguous cases require admin inspection/recovery and may need restored source mods or a backup. True deletion, missing assets, conflicting ownership/trips/outcomes, incompatible snapshots/progression and unresolved checkpoints are not silently overridden.

Use `/companionmigration` or `/companionmigration native:<ID>` to inspect progress, and `/companiondiag` for details. `/companionmigration retry native:<ID>` only queues another guarded check. For eligible loaded originals, the Rebuild/Recover all preview provides the validated admin repair without typing `/companionrepair`; that command remains available. A native stale Away flag is handled by eligible repair. A genuinely Away/Returning ledger or an active/unknown journey remains protected. If migration fixes an original first, reconstruction is normally unnecessary; a preview made before its state changes must be requested again.

Rollback must restore the **whole backup with matching mods**, because older versions do not understand the new settlement receipts and may omit journals or hide an animal again.

## Evidence and remaining verification

The owner explicitly requested a stable release after receiving the preview recommendation and limitations. This is a release decision, not a claim of exhaustive gameplay acceptance.

The exact recovery-flaps-preview.2 DLL passed **1,623 controlled assertions**: 70 button/routing/security/confirmation checks, 315 recovery and fresh-process regressions, 590 flap lifecycle/collision checks, 560 native asset/recipe checks and 88 localization checks. The production build was clean. Stable finalization preserves that DLL, PDB and all 323 assets byte-for-byte; only manifest and release documentation change.

Manual repair succeeded for one reporter, corroborated by a visible wolfdog and dialogue. Complete inventory/progression/health/restart preservation in that reporter's world is not independently confirmed. Automatic migration has **no affected real-save confirmation**. The new Rebuild/Recover all dialog has controlled handler/protocol coverage but **no in-game rendered verification**.

Forwarded singleplayer QA on the identical standalone flap implementation/assets covered Creative availability/placement, player passage, observed bear/fox passage, tested frame-part break/one-drop/replacement, obstruction refusal, native room-enclosure controls and graceful world unload/reload. This did not cover multiplayer, full client-process restart, crafting gameplay, every species/orientation or quantitative thermal/cellar performance. Bear visual clipping was accepted. The raw obstruction message is corrected and native-localization-tested; its corrected game overlay has not been newly viewed.

No new game launch, live-save/modpack edit, Discord message or ModDB upload was performed for this release. The ZIP and HTML description are upload-ready for the owner.

## Version history and source publication

The prior actual local stable is **0.5.83**, following 0.5.81/0.5.82 dedicated-startup changes and the 0.5.83 Wellbeing shortcut. GitHub's previous selected Companions source was **0.5.80**. This source publication catches up those already released changes as well as 0.5.84; it does not rewrite the historical records or claim the intermediate versions were previously synchronized there.

Separate test fixtures, reporter data, logs, private edit notes, binaries and unreleased projects remain excluded from the public repository. Existing licenses, sound credits and upstream notices remain intact. Rebuilding public source is a separate artifact and does not replace the retained tested installer bytes.
