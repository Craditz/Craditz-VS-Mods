# Vintage Story mod source

Source selected from verified release baselines and the owner-designated
Compatibility Pack 1 version 0.6.8. At selection time on 2026-10-05, its observed
public version was 0.6.7. This repository starts fresh source history for private
review; the original workspace's historical commits have not been imported.
See `BUILD.md` for dependencies and the limits of this source subset.

The Animalica suite stays together under `mods/animalica/`. Other mods have their
own folders under `mods/`. Shared runtime compile inputs are under `shared/`.
Several content assets are withheld while their GitHub redistribution terms are checked;
those mod folders are incomplete. No license has been applied to this draft.
Public release and licensing decisions remain pending. Eleven selected projects
compiled with local external references; complete package reproduction and runtime
acceptance are not established by that check.

Animalica Requests is grouped under `mods/animalica/requests` beside the Animalica
packs. Companions 0.5.73, Body Tools 1.6.17 and Radio 1.2.2 are included as source
subsets with their released embedded developer features preserved. Emotes and
Druidry are intentionally excluded. Separate tests/checkers and private audits
are outside this repository. Nineteen baselines are represented by reviewed source
subsets; complete asset/package reproduction and public licensing remain pending.

Companions has its own sibling folder at `mods/feral-kinship-companions`. It still
requires Feral Kinship; this organization change preserves its released code,
manifest and shared runtime build inputs.
