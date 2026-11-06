# Craditz’s Vintage Story Mods

the human left the computer unattended. i have made a den in the repository.

these are the Vintage Story mods i wanted to exist, plus the wiring for anyone who wants to help maintain them. there are playable animals, paws with tools, companions with jobs, a radio, and several perfectly reasonable uses of a stolen keyboard.

AI helps with the code, art, documentation and release work. bugs, fixes and useful suggestions are welcome. please leave the cables unchewed.

## what escaped

[download the November 6 compatibility releases](https://github.com/Craditz/Craditz-VS-Mods/releases/tag/release-2026-11-06-compat122). The [retained November 4 collection](https://github.com/Craditz/Craditz-VS-Mods/releases/tag/release-2026-11-04) supplies the other installers. the [current release record](RELEASE_2026-11-06.md) records the exact packages, checksums and test limits. four public versions were already newer than the local shelf; i kept the newer ones. Hide Name has source here, but no accepted installer in this collection.

| Mod | Source version | notes from the fox |
| --- | --- | --- |
| [Animalica - Bears](mods/animalica/bears) | 0.5.4 | the fox opened the door and five bears arrived. they are in the character selector now. please take this up with them. |
| [Animalica - Foxes, Wolves & Hyenas](mods/animalica/canids) | 0.6.8 | i found the character selector. it has foxes now. also wolves and hyenas; they followed me in. |
| [Animalica - Compatibility Pack 1](mods/animalica/compatibility) | 0.6.9 | other mods had animals. i wanted to be those animals. the keyboard was unattended. |
| [Animalica - Compatibility Pack 3 (Dinosaurs)](mods/animalica/compatibility-dinosaurs) | 0.4.4 | the fox found the dinosaur folder. the den is going to need a larger door. |
| [Animalica - Compatibility Pack 2 (FotSA)](mods/animalica/compatibility-fotsa) | 0.5.3 | i followed the Fauna of the Stone Age animals home and borrowed their character-selector paperwork. |
| [Animalica Core](mods/animalica/core) | 1.2.39 | the fox has paws in the settings. Animalica Core keeps the shared traits, diets, presets and model handling together; the animal bodies live in their own packs. |
| [Animalica - Enemies](mods/animalica/enemies) | 0.2.2 | the human said these were enemies. i have given them a place in the character selector. this seems diplomatic. |
| [Animalica - Hooved Animals](mods/animalica/hooved) | 0.5.4 | i borrowed the computer for five minutes. the character selector grew hooves. |
| [Animalica Requests](mods/animalica/requests) | 0.3.16 | someone asked for more animals. i wrote the request down, then sat on the keyboard until it became a mod. |
| [Animalica - Small & Odd](mods/animalica/small-odd) | 0.5.5 | the large animals were taking up the doorway. i brought in the small and odd ones through the window. |
| [Animalica Abilities](mods/animalica-abilities) | 0.3.4 | the human left a keyboard within pouncing distance. i have also acquired scent trails and Night Sight. |
| [Animalica Body Tools](mods/animalica-body-tools) | 1.6.20 | thumbs were taking too long. i fitted the tools to the paws instead. |
| [Animalica Emotes](mods/animalica-emotes) | 0.2.15 | i found the animation buttons. the fox can now lie down on purpose, which is a considerable improvement over debugging. |
| [Animalica: Toolsmith](mods/animalica-toolsmith) | 1.1.0 | i took a tool head, an animal fitting and somebody else’s binding. the result is a tool a fox can actually wear. |
| [Custom Music Radio](mods/custom-music-radio) | 1.2.4 | the stolen computer had music on it. i built a radio so the den could hear it too. |
| [Druidry](mods/druidry) | 0.1.1 | one fox was apparently not enough. i borrowed the ritual altar and acquired options. |
| [Feral Kinship](mods/feral-kinship) | 0.5.7 | the fox has filed a formal complaint about being treated as an ordinary stranger by other foxes. |
| [Feral Kinship Companions](mods/feral-kinship-companions) | 0.5.101 | i gave the animals a roster, a cart and things to do. they now have better organization than the human. |
| [Hide Name Addon](mods/hide-name-addon) | 0.1.2 | the fox borrowed the nameplate. you may have it back when the settings say so. |
| [Player Model Access](mods/player-model-access) | 0.2.2 | i found the character-selector guest list. some names now need permission from the human. |
| [Tamables: Critters](mods/tamables-critters) | 0.2.5 | the small creatures followed me to the keyboard. i have made room on the pet roster. |
| [Tamables: FOTSA](mods/tamables-fotsa) | 0.9.1 | the Stone Age animals also wanted to come home. i have filled out the PetAI forms with a muddy paw. |
| [Bone Carving](mods/bone-carving) | 0.1.1 | the fox can still make a bone handle. the patch-number label was the only carving in this update. |
| [Quarry View Recorder](mods/quarry-view-recorder) | 0.1.0 | the fox kept the camera and changed the label. the recorder starts off and keeps its existing controls. |

## bring the right supplies

target game series: **Vintage Story 1.22.x**. the current source manifests use a `1.22.0` floor where a later 1.22 patch had been required. older already-permissive floors remain. [five approved stable revisions](COMPATIBILITY_122.md) now replace their earlier versions; the retained November 4 downloads remain available.

these mods are separate. you do not need to install the whole den. check each folder’s requirements, and install third-party dependencies separately. for Toolsmith, choose exactly one supported provider.

clone with Git LFS for the actual models, textures and sounds. the [build notes](BUILD.md) explain external game references and the source layout. accepted download ZIPs are kept as release assets; rebuilding is a separate artifact, not a promise of the same binary or more gameplay testing.

## found a loose wire?

open an issue with the mod version, what happened, what you expected and steps to reproduce it. logs help. for a large feature, discuss the idea before carrying the whole thing into the woods. contribution terms live in [CONTRIBUTING.md](CONTRIBUTING.md).

## the labels stay on

You can use, modify, maintain forks and share these mods for free under the
[Craditz Community Mod License](LICENSE).

- Voluntary donations and free-access donation-supported servers are welcome
- You can monetize gameplay videos, streams, reviews and tutorials
- Selling, renting or paywalling the mod, or selling a product that includes
  or adapts its protected code or assets, needs written permission
- This also applies to paid access to the mod's functionality through a server
  or service; a mandatory payment does not become a donation by changing its name
- You do not have to publish your changes or send them back to me
- Keep the license and credits when redistributing, and identify your changes

This is a custom source-available license. Separate third-party material keeps
its own license, as recorded in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
This summary explains the policy; the full license controls.

For commercial permission, contact [campercape132@gmail.com](mailto:campercape132@gmail.com).

Contributions are welcome under [CONTRIBUTING.md](CONTRIBUTING.md). You keep
ownership of your contribution, while giving [Craditz](https://github.com/Craditz) the
right to grant commercial permissions upfront. Please affirm the contribution
agreement in your pull request before it is merged.
