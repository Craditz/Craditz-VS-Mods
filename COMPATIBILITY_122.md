# the fox lowers the 1.22 doorstep

target game series: **Vintage Story 1.22.x**.

the patch number was being made to guard the door. the human asked for the whole 1.22 series, so the fox has lowered later 1.22 game minima to `1.22.0`. existing lower minima stay. `modinfo.json` stores a minimum version; `1.22.x` is the readable label, not the dependency string.

## what changed

the current public-source manifests for **Custom Music Radio**, **Feral Kinship Companions** and **Animalica Dinosaurs** now use `game: 1.22.0`. all 22 mod readmes and description previews show the 1.22.x target. other mod versions and dependency minima are retained.

the local owned-mod audit covered 27 authoritative manifests. **Bone Carving** and **Quarry View Recorder**, which are local development mods, received the same floor change. they are not added to this public collection.

## the revised packages

| Mod | Metadata-only candidate |
| --- | --- |
| Custom Music Radio | `1.2.4-compat122-preview.1` |
| Feral Kinship Companions | `0.5.101-compat122-preview.1` |
| Animalica Dinosaurs | `0.4.4-compat122-preview.1` |
| Bone Carving | `0.1.1-compat122-preview.1` |
| Quarry View Recorder | `0.1.0-preview.5-compat122` |

each candidate keeps every original DLL, PDB, asset and other file byte-identical. only the root manifest changes: the game floor and candidate revision. archive CRC, root manifest, safe forward-slash paths, unique normalized entry names and runtime payload equality passed. all five were installed in the Testing allowlist with matching hashes and their required dependencies. no compile or new manual acceptance is claimed. The load smoke below provides runtime coverage.

the promoted shelf and November 4 GitHub installer downloads are retained unchanged. these revised candidates are packaged and installed, awaiting the existing acceptance/promotion gate. the source edits here do not silently replace those published installers.

Companions still checks the loaded engine's actual native contract. provider mods keep their own requirements. this metadata change does not bypass those checks, change runtime code or rewrite the versions recorded in historical test receipts.

## further detail

- [manifest format and minimum semantics](https://wiki.vintagestory.at/Modding:Mod_Packaging)
- [retained published package inventory](RELEASE_INVENTORY.json)
- [previous release record](RELEASE_2026-11-04.md)

## Docker smoke — November 6

all five exact candidates passed an offline Docker Client D smoke on VS **1.22.7**: client/integrated-server initialization, world entry, normal save/quit, new-process cold reload of the same world and a second normal save/quit. both native game processes exited 0; the closed SQLite save passed `quick_check`. Companions’ native engine contract qualified.

the first optional dinosaur fixture omitted Ocean Tyrant, causing nine unique DinoCore missing-mosasaur patch errors (18 emitted entries). the first run still entered and exited normally. adding the matching retained Ocean Tyrant source module corrected the fixture without changing any candidate. the completed run, including Horrible Hands and Ocean Tyrant providers, had **zero Client/Server Error or Fatal entries** and ten compatible Player Model Lib race entries.

Docker’s original mod set, working data directories, server files and main clientsettings were restored and hash-verified, the CPU limit was restored, and the container is stopped with exit 0/OOMKilled=false. isolation passed 15/15. Windows Testing and release packages were unchanged. This is load/save/reload coverage on 1.22.7; earlier 1.22 patches, radio playback, carving, recorder capture, companion jobs and exhaustive model gameplay remain outside this smoke. candidates remain unpromoted.
