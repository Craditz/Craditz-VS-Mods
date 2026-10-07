# Building the mods

If you're here to build something, this is the bit you need.

## What you'll need

- Git LFS, installed before cloning; run `git lfs pull` if assets appear as pointer text
- .NET SDK 10
- Your own Vintage Story installation, at a version compatible with the chosen mod's manifest
- Any third-party mod DLLs that project needs, downloaded from their official pages and extracted locally outside this source tree

Don't add game DLLs, PlayerModelLib or PetAI DLLs to Git.

Official downloads: [Vintage Story](https://www.vintagestory.at/),
[PlayerModelLib](https://mods.vintagestory.at/playermodellib),
[PetAI](https://mods.vintagestory.at/petai).

## Build a code project

Use one of the projects listed below:

```powershell
dotnet build <project.csproj> -c Release -p:VintageStoryPath="<game-directory>" -p:PlayerModelLibPath="<external-PlayerModelLib.dll>" -p:PetAIPath="<external-PetAI.dll>"
```

Only pass the dependency properties that project needs. Check its manifest for
the required dependency versions. Those are the requirements for that release;
they aren't a list of whichever versions are newest.

The manifests keep their selected release versions and credits. The runtime C#
is unchanged from the selected commits. Project paths were adjusted to fit this
repository's layout.

- `mods/animalica-abilities/ScentTrails.csproj`
- `mods/animalica/core/src/AnimalicaCore/AnimalicaCore.csproj`
- `mods/animalica/requests/src/AnimalicaRequests.csproj`
- `mods/feral-kinship/FeralKinship.csproj`
- `mods/player-model-access/src/PlayerModelAccess/PlayerModelAccess.csproj`
- `mods/tamables-critters/TamablesCritters.csproj`
- `mods/tamables-fotsa/TamablesFotsa.csproj`
- `mods/hide-name-addon/src/HideNameAddon/HideNameAddon.csproj`
- `mods/animalica-body-tools/src/AnimalicaBodyTools/AnimalicaBodyTools.csproj`
- `mods/custom-music-radio/CustomMusicRadio.csproj`
- `mods/feral-kinship-companions/FeralKinshipCompanions.csproj`

## Assets and packages

The supporting assets and package icons from the matching retained releases are
included. Eight content packs need their manifest and assets; code mods also
need their compiled mod DLL. Use the project assembly name and keep `modinfo.json`
at the ZIP root, alongside `assets/` and the package icon where present. Archive
entry paths must use forward slashes.

Use the included selected assets for packaging. Complete regeneration from
original model/art inputs is a separate workflow: this repository does not
contain every historical generator or donor input. Dependency DLLs are installed
separately and must not be bundled into these packages.

An asset presence/hash check or successful compile does not establish an exact
release binary match or in-game acceptance. Hide Name has no retained latest ZIP
for comparison. Existing selected versions and dependency requirements are kept.

## Companions, Body Tools and Radio

Companions 0.5.74, Body Tools 1.6.17 and Radio 1.2.2 keep the developer and
diagnostic features that shipped with those versions. Their separate test and
checker projects stay out of this repo. Companions 0.5.74 includes the accepted
crunch-sound removal; no alternate UI build was selected.

- Companions keeps its released UI constants; no alternate UI build was selected
- Body Tools uses external game/Harmony references
- Radio uses external game/protobuf references. Its project configuration now
  uses `VINTAGE_STORY` instead of the old fallback to a local experimental game
  installation. That change is limited to project configuration

Their supporting assets are included from the matching selected release packages.

### Companions and Feral Kinship

Companions lives in `mods/feral-kinship-companions`. Its manifest still requires
`feralkinship >= 0.4.3`; the Feral Kinship source here is 0.5.4.

It links the unchanged Kinship race resolver and shared config migration source
at build time:

- `../feral-kinship/src/FeralRaceResolver.cs`
- `../../shared/VintageStoryConfigMigration.cs`

Companions and Feral Kinship compile as separate projects.

## What has been checked

All eleven code projects listed above compiled with local external references.
Requests was compiled again after its move to `mods/animalica/requests`.
Those checks cover compilation; complete package builds and in-game behaviour
weren't established by them.

### October 7 release update

Requests 0.3.14 and Companions 0.5.74 are included with their released manifests,
matching source changes and supporting assets. Both source projects compiled again.
Their accepted candidates passed a Vintage Story 1.22.7 Linux world start/save/shutdown
smoke test; Requests also passed Female Lupines absent and Hooved absent. Focused
and native PlayerModelLib checks covered the corrected gate and existing model loading.
This evidence does not establish exhaustive gameplay or binary identity of a rebuild.

## Adding files to the repo

Ignore rules help catch mistakes, but check the actual Git index before each
upload. The included model JSON, PNG and OGG assets already use Git LFS. Check
the real asset contents, not just the pointer files, and use Git LFS for any
additional large source assets where appropriate.

Keep downloadable release ZIPs as separate release assets.
