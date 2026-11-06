# the fox lowers the 1.22 doorstep

Target: **Vintage Story 1.22.x**; affected game minima: **1.22.0**.

All three public revisions are owner-approved and promoted. Runtime DLLs/assets match the accepted candidates. The [release record](RELEASE_2026-11-06.md) gives the stable identities, checksums and qualification limits. Existing lower minima, third-party requirements and native checks remain.

| Mod | Stable version | Installer |
| --- | --- | --- |
| [Custom Music Radio](mods/custom-music-radio) | 1.2.4 | `CustomMusicRadio_v1.2.4.zip` |
| [Feral Kinship Companions](mods/feral-kinship-companions) | 0.5.101 | `FeralKinshipCompanions_v0.5.101.zip` |
| [Animalica - Compatibility Pack 3 (Dinosaurs)](mods/animalica/compatibility-dinosaurs) | 0.4.4 | `Animalica_Dinosaurs_v0.4.4.zip` |

## Docker smoke — November 6

the three public candidates passed an offline Docker Client D smoke on VS **1.22.7**: client/integrated-server initialization, world entry, normal save/quit, new-process cold reload of the same world and a second normal save/quit. both native game processes exited 0; the closed SQLite save passed `quick_check`. Companions’ native engine contract qualified.

the first optional dinosaur fixture omitted Ocean Tyrant, causing nine unique DinoCore missing-mosasaur patch errors (18 emitted entries). the first run still entered and exited normally. adding the matching retained Ocean Tyrant source module corrected the fixture without changing any candidate. the completed run, including Horrible Hands and Ocean Tyrant providers, had **zero Client/Server Error or Fatal entries** and ten compatible Player Model Lib race entries.

Docker’s original mod set, working data directories, server files and main clientsettings were restored and hash-verified, the CPU limit was restored, and the container is stopped with exit 0/OOMKilled=false. isolation passed 15/15. Windows Testing and release packages were unchanged. This is load/save/reload coverage on 1.22.7; earlier 1.22 patches, radio playback, companion jobs and exhaustive model gameplay remain outside this smoke. the owner subsequently accepted and promoted the three public stable revisions; see the release record.
