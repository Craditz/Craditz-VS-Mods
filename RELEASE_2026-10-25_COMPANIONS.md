# Companions 0.5.94 source synchronization

Previous public ModDB runtime:0.5.93. Previous repository source:0.5.92.
This source update explicitly synchronizes the already-public0.5.93 baseline,
then adds only the accepted Optimum-aware compatibility adapter for0.5.94.
No new recovery/return feature is introduced by0.5.94; schema61 and all352assets
remain the public0.5.93 baseline. Other mod source is unchanged.

See mods/feral-kinship-companions/CHANGELOG_v0.5.94.txt and RELEASE_NOTES.md
for behavior, tested versions, backup instructions and qualification limits.
Stable finalization passed15,014 normalized type/member/resource comparisons:
only informational-version metadata changes from the accepted preview.
The exact stable runtime was retested headlessly under vanilla and Optimum,
including save/quit/cold reload, focused8/67checks and415SQL/stress checks.
SQL failure tests use an auxiliary DB with controlled production finalizers,
not an injected running-world chunk-worker failure. GUI cloud results are on
the equivalent preview; broader multiplayer/providers remain unqualified.

Stable runtime ZIP SHA256:d734e3d25f593ebb9da0334a44a185f72ad20c17fc4c948370acd5d5df673fb1
Stable DLL SHA256:f0a90592f39a667931682456f8491be061e7111217194f82ddba0996588c2850
Matching source ZIP SHA256:ca9b23f36fcefc0861f007d90909ba76e4e899289ac08adac19e11a55b1a6665

GitHub source synchronization and the local release shelf are separate from
ModDB upload. ModDB upload remains the owner's handoff; this note is not a
ModDB publication receipt. Existing upstream licenses/notices are preserved.
