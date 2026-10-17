# Approved Companions and Tamables FOTSA release — 2026-10-17

Mode: Release. Owner explicitly approved these tested builds. The local grab-and-upload shelf is `F:/VSmodding/releases/latest`; the owner handles ModDB upload. This source publication does not create a GitHub binary release or claim a ModDB upload. No visibility/access change, history rewrite or unrelated project update is included.

## Accepted identities

| Mod | Embedded version | Archive SHA256 |
|---|---|---|
| Companions | `0.5.79-food-compat-preview.1` | `A1BBFC659BDBD3DA7430D597F7C55AFC31934DC9BF29D35B1A3E83FF8B15C065` |
| Tamables FOTSA | `0.9.1` | `D90B2F91964E7A734CF32EAD2E2D2717E12AA8123A839AB0BAA1EE425D5CE0AF` |

Installer names remain `FeralKinshipCompanions_v0.5.79-food-compat-preview.1.zip` and `TamablesFOTSA_v0.9.1-native-forms-render-preview.zip`. Original preview markers and package notes are retained to preserve the exact tested bytes. No packaging/code change extends the gameplay qualification. Both embedded versions are higher than the existing local promoted versions, 0.5.76 and 0.8.7.

The cumulative [Companions changelog](mods/feral-kinship-companions/CHANGELOG_since_0.5.74.md) uses the actual downloaded public 0.5.74 archive (SHA256 `1C819A8641373BDFB3F3FA1332712E51AB388E754C66D91FB9C2FC82FA46F6D5`), byte-identical to the retained local archive. The supplied authenticated edit URL was not accessible; the official public API returned release 60244/file 128279, its 0.5.74 manifest and changelog. The 0.5.75–0.5.77 source/changelogs and the accepted growth/food deltas were reconciled. The [Tamables changelog](mods/tamables-fotsa/CHANGELOG_v0.9.1.md) covers its matching native-form/riding work.

## Qualification

Parent cloud gameplay on the exact Companions archive passed old/new WolfTaming dining-board feeding (kibble 64 to 58; old-b Well fed approximately 83%), old-b manual raw meat (Starving to approximately 25% after normal AI time, board empty), FOTSA cow carrot (approximately 25% to 55%) and board plant kibble (55% to 83%), and isolated Critters chipmunk board plant kibble (1 to 0; Food 0.262 to 0.535) and manual carrot (1 to 0; Food 0.535 to 0.829). The paired elephant growth/riding regression also passed. The chipmunk journal failure is fully retracted: interaction targeted the nearby invisible dragon.

Modded-food compatibility: **probably works now, not tested**. The broader enumerated food-asset audit is offline evidence, not every-species, every-mod-food or multiplayer gameplay proof. 9,945 food/growth assertions previously passed on the exact candidate. FOTSA native-form/render checks previously passed 16,577 assertions; their frozen-world boundaries do not establish every GPU/lifecycle case. Separate test projects, fixtures, saves and user diagnostics remain private/local and are excluded here.

The invisible bearded dragon remains [TODO](mods/feral-kinship-companions/TODO.md), outside this release's fixes. Recovery/pathing oddity and cow texture warning remain separate observations. Saved ambiguous duplicate/retained Hold markers stay protected; no blanket save repair is performed.

Both public-layout projects compile against local Vintage Story 1.22.7 with zero warnings/errors. Tamables' project adds the same external VSSurvivalMod reference used by its accepted native-riding source; no runtime code changes for publication. The isolated Windows dedicated-server check loaded the exact accepted Companions/Tamables ZIPs with Kinship 0.5.4 and PetAI 5.1.2, reached RunGame and “Dedicated Server now running”, then saved and exited 0 after `/stop`. It emitted no startup exception; the blocked-mod-list network request and one slow-tick warning are environmental limits, not tested feeding behavior.

Historical warning: GitHub marks 0.5.76 withdrawn and reports dedicated-server startup failures in 0.5.76/0.5.77. That warning is preserved; its unknown cause is not claimed fixed by this release. The exact current pair passed the Windows dedicated startup check above, while other server platforms and configurations remain unqualified. No historical release/tag was altered.

## Source, assets and notices

Runtime source matches the accepted source snapshots after the repository's existing LF normalization. Public project paths retain the established external-dependency/shared-source layout; any public-layout rebuild is a separate artifact and never substitutes into the tested installer. Companions assets are unchanged from the selected prior public payload and were checked against actual accepted asset bytes, including Git LFS OIDs. Tamables has no runtime asset payload in its accepted ZIP. Existing license, contribution terms, sound credits, third-party notices and private/unreleased-project exclusions remain intact. No third-party archives, game/provider DLLs, private tools, fixtures, diagnostics or unreleased projects are added.

Both release descriptions were reviewed and updated, with editable `MODDB_DESCRIPTION.html.in` sources, a small regeneration script and local copy-button previews. Existing concurrent canonical files were left untouched; the release-specific copies live beside this selected source. Preparing them does not upload them to ModDB.
