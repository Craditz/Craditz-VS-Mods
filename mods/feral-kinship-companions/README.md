# Feral Kinship Companions 0.5.76

Source and matching assets for the owner-authorized 0.5.76 release.

This update fixes fresh-tame admission/naming and retained expedition/return
handling, adds source-derived provider recruitment, wellbeing Care Cards and
opt-in care duties, extends FOTSA health/breeding/expedition bonuses, and reduces
ambient barking and howling. The complete delta and configurable defaults are
in [CHANGELOG_v0.5.76.md](CHANGELOG_v0.5.76.md).

Required dependencies: `game >= 1.22.0, feralkinship >= 0.4.3`.
Feral Kinship is a separate required mod. Optional animal providers retain
their own requirements; compatibility does not bundle provider DLLs.

The owner authorized this release while accepting pending listening,
multiplayer audio, natural return and broader physical-fall/provider checks.
The public DLL is rebuilt and separately audited. Prior private Sound1 test
results are evidence for that private DLL, not live acceptance of this rebuild.
Offline native fixtures use mocked world boundaries.

Client-native and server-task ambient cooldowns are separate per-animal
budgets. Warning, panic, hurt/death, combat, commands and whistles retain their
own paths. Extra care duties are opt-in; IdleSounds retains its existing scope.

The 0.5.75 Cart radius toggle, filtered roster, Locate, provider/dead-record and
production milestone fixes remain. See [its historical changelog](CHANGELOG_v0.5.75.md).

Install Git LFS for model and binary source assets. Build instructions are in
[BUILD.md](../../BUILD.md). The [license](../../LICENSE), contribution terms,
[third-party notices](../../THIRD_PARTY_NOTICES.md) and sound credits are retained;
third-party material keeps its original rights and conditions.
