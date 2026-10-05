# Third party notices

Project: Craditz-VS-Mods

Status: source snapshot notices, reviewed 2026-10-05. Asset provenance and
complete-package checks remain incomplete; this is not a rights-clearance
certificate for release packages.

The Craditz Community Mod License applies only to material expressly offered
under it by someone with the necessary rights. Third-party material keeps its
own license. A listing here is a record, not a substitute for permission.

## Covered project material

Craditz adopts version 1.0 of the Craditz Community Mod License for the original
project code, documentation, build configuration, scripts, authored configs,
language entries and patches included in this repository, to the extent Craditz
holds or is authorized to license the relevant rights. Third-party protected
parts and material under separate terms are excluded from that grant. Original
manifest credits are preserved. No right in a dependency or an omitted release
asset is granted merely because a project references it.

## Dependencies installed separately

- [Vintage Story](https://www.vintagestory.at/) supplies the game APIs and game
  libraries used for builds. Its code, models, textures and other game material
  retain their separate terms. No game or vendor DLLs are bundled here
- [PlayerModelLib](https://mods.vintagestory.at/playermodellib), credited to
  Maltiez/Caliber in the local dependency manifest, is an external API/runtime
  dependency. No PlayerModelLib DLL or copied clothing-replacer tables are
  included in this snapshot
- [PetAI](https://mods.vintagestory.at/petai) is installed separately for the
  projects that reference it. That dependency keeps its own license. No PetAI
  DLL is bundled here
- Other library references resolved from the user's game installation keep
  their own terms. Follow [BUILD.md](BUILD.md) for the required external paths
  and the selected mod manifests for dependency versions

## Existing contributions and compatibility material

- Animalica Body Tools credits **xthatguyx** in `mods/animalica-body-tools/modinfo.json`.
  The owner reports the quenchable-head contribution was given unconditionally.
  That existing permission is recorded without asserting a copyright assignment
  or treating the new contribution agreement as retroactive assent. Credits
  remain intact; any independently sourced game or provider assets retain their
  separate rights
- The owner reports Carried integration material was also given unconditionally.
  Core's optional integration code is included; the carry-profile assets and
  their full generation inputs have not been added to this snapshot. Recording
  the grant does not claim ownership of Carried itself or its dependency files
- Compatibility-model permissions reported by the owner are conditional on
  the material remaining part of a compatibility mod. Those permissions are
  preserved as separate grants, including the Lupines compatibility permission.
  They are not a blanket grant to relicense donor models, textures or animation
  data under this project's license or offer commercial exceptions for them.
  The corresponding adapted model and art payloads remain outside this snapshot
- Donor families referenced by the compatibility packs include the Pack 1
  providers, Fauna of the Stone Age modules and dinosaur modules. Referencing
  an independently installed donor mod is distinct from distributing its
  protected source or assets. Any future included donor material needs its
  exact paths, original source, permission text and required notices recorded
- Radio's historical recording and artwork, Companions' omitted sound and art
  payloads, and other models, textures, icons and generated data omitted from
  this source snapshot are not relicensed here. Each must be reviewed before
  inclusion. U.S. public-domain evidence for a recording is not represented as
  worldwide clearance

For any new third-party component, record its name/version, exact paths,
author, original URL and retrieval date, full license or permission, required
notices, modifications, redistribution conditions, commercial authority and
review date before inclusion.

## Intake and release policy

1. Inventory copied or adapted code, bundled libraries, artwork, audio, fonts,
   models, documentation and any other third-party material included in a release
2. Include it only when an existing license or specific permission permits the
   intended distribution and all required conditions are satisfied
3. Keep its original notices and license; do not relabel it as project-owned
4. If rights are missing, unclear or incompatible, leave it out, replace it, or
   obtain permission before release. Attribution alone is not permission
5. A dependency that users install separately should be documented with its
   official source and license. Do not copy its files merely for convenience
6. Compatibility with another mod alone does not require contacting its author;
   copying or adapting that author's protected material requires a rights check
7. Track AI-generated or AI-assisted assets separately, with source tools,
   known terms and any human-authored modifications. Do not claim copyright
   merely because an AI asset is present in the project
8. If a future artist uses separate terms, identify the affected paths and keep
   that exception explicit. Do not promise commercial permission on their behalf
   unless they have granted that authority

## Separate rights and commercial requests

A commercial permission from the project steward covers only rights the steward
owns or may sublicense. The recipient must also satisfy applicable third-party
licenses or obtain any additional permissions. The steward should identify
excluded components when granting a commercial exception.

## Existing contributions

New contribution terms do not apply retroactively. Record existing permissions
for earlier contributions. Obtain a specific new grant only where necessary,
or omit the affected material. An earlier MIT-licensed file, for example,
does not lose the permissions already granted to its recipients merely because
the surrounding project later uses a different license.
