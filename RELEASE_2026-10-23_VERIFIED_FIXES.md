# Verified source update - 23 October 2026

Matching source, assets and release documentation for four accepted updates are synchronized here. ModDB publication remains separate; this commit is not a ModDB release receipt.

| Mod | Previous public ModDB version | Updated source version |
| --- | --- | --- |
| Feral Kinship Companions | 0.5.91 | 0.5.92 |
| Feral Kinship | 0.5.4 | 0.5.5 |
| Animalica Body Tools | 1.6.19 | 1.6.20 |
| Animalica Compatibility Pack 1 | 0.6.8 | 0.6.9 |

Each mod's CHANGELOG_v*.txt records the delta from that public baseline. Companions retains schema 57 and the accepted presentation, diagnostics, storage, flap, feeding and startup fixes; private recovery changes are excluded. Kinship corrects 23 client-behavior patch operations and requirements/override wording, with its executable unchanged. Body Tools corrects optional-metal texture references and scopes relaxed recipe restrictions; its dismantling/durability/audio work was already public. Pack1 corrects the unsupported playable-Equus claim without gameplay changes.

## Frozen installable-byte provenance

These SHA256 values identify the technically accepted installable ZIPs. Binaries are not included in this source commit, and the hashes do not imply ModDB publication.

| Mod | ZIP SHA256 |
| --- | --- |
| Companions 0.5.92 | `86bc938b2b159767402be7c413b0f8300825373738298e0c9cfd713d5c2b25e6` |
| Kinship 0.5.5 | `d3d13b7bb7f03c29f6f3eebafbcb8ac8928b37aa5ad6ec3f07e5a9410ea3451b` |
| Body Tools 1.6.20 | `3163154a72f25091edda9b71b3b3401194a649fffa5150d1b73943a853078574` |
| Compatibility Pack 1 0.6.9 | `fbd74a0774b89a6f92170f5dac99bb5de333e585c92cbbcacfaa8028f41b4d1a` |

The accepted Companions DLL SHA256 is `d9b475b37d63d593e0fa45c7c2e38d026b5fea367536fe9c2cd0130952550591`. Its build uses neutral reproducible source/debug paths. Body Tools and Kinship source builds were checked against their selected executables; Pack1's gameplay assets are unchanged. Scoped controlled and single-player validation is recorded in the mod release documentation; it is not a blanket multiplayer, every-provider, every-material-visual or audible-sound claim.

## Source scope

Only these four mod paths and this release document are updated. Existing license, contributor and third-party notices remain in force. Shared/legal context files and unrelated projects are preserved. Assets continue to use the existing Git LFS rules.

Private maps, standalone tests/injectors, logs/evidence, dependency/game binaries, runtime ZIPs, settings/saves and support-case material are excluded. No new Core, Hooved, Canids, Bears or Griffon update is included. No private schema 58/59 recovery, provisional health, camera, failed lighting, egg-design, pathfinding/follow, model-stat or NyxNav work is included.
