# Companions 0.5.94 release notes

The sole change from public ModDB 0.5.93 is the Optimum-aware native save adapter,
plus stable version metadata and matching release documentation. Existing
public 0.5.93 recovery/return work is the baseline, not a new fix in this release.
No assets or save schema change. See CHANGELOG_v0.5.94.txt for behavior details.

## Qualification

The exact accepted preview passed Windows VS 1.22.7 / .NET 10.0.13 vanilla and
official Optimum 0.3.21 headless startup, generation/unload, ordinary saves,
clean shutdown and repeated cold reload. Companion fixtures retained identity,
health, ledger progression and a marked native attribute ItemStack tree.
This is not natural-taming or native-backpack validation.

Real SQLite abort/rollback and partial-commit retries through native GameDatabase
were tested in a separate auxiliary DB with controlled production retirement
finalizers and real native queue/lease primitives. All 415 SQL/stress checks
passed, including 128 two-thread dispatch races. This is not an injected
running-world chunk-worker SQL failure, disk-full test or exhaustive proof.
Focused Optimum 67, vanilla 8, five contract-mutation outcomes, current recovery
150, startup 22 and expedition/migration 153 checks passed. Eleven old Undo
fixtures fail identically in public 0.5.93 and remain explicit historical limits.

Independent cloud qualification passed Linux vanilla/Optimum headless
create/save/quit/cold reload and SQLite integrity. Optimum GUI low-graphics
world entry, Bramble guide, save and full client restart passed. All 21 author
mods passed headless smoke; a full graphical stack was resource-limited under
both runtimes, while five smaller graphical stacks passed. Unrelated warnings
were not folded into this change. Stable relabel verification is recorded in
the release manifest; preview results are not claimed as direct stable GUI QA.

Multiplayer, optional integrations, ordinary companion feeding/backpacks, the
reporter's different DLL and exhaustive concurrency remain unverified. Other
Optimum builds are not blanket-approved. Native guards remain enabled.

## Backup and publication

Writer schema remains 61 with its existing supported readers. Keep complete
matching backups. Restore world/profile/mods together when rolling back.
The owner approved local promotion and matching GitHub source export separately.
ModDB upload remains the owner's handoff and is not claimed completed here.
No private support messages, logs, fixtures or credentials belong in public
source or the installable package. Upstream notices are preserved.
