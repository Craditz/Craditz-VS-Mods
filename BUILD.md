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

Companions 0.5.76, Body Tools 1.6.19 and Radio 1.2.2 keep the developer and
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

### October 11 release update

FWH 0.6.8, Bears 0.5.4, Compatibility Pack 2 0.5.3, Requests 0.3.16 and
Companions 0.5.75 include the approved source and exact tested supporting assets.
Release ZIPs retain every tested runtime/asset byte. Documentation was refreshed;
Companions also has a numeric manifest version. No packaged DLL was rebuilt.
Scoped gameplay and independent source checks cover these candidates; see
[the release record](RELEASE_2026-10-11.md) for their limits.

## Adding files to the repo

Ignore rules help catch mistakes, but check the actual Git index before each
upload. The included model JSON, PNG and OGG assets already use Git LFS. Check
the real asset contents, not just the pointer files, and use Git LFS for any
additional large source assets where appropriate.

Keep downloadable release ZIPs as separate release assets.

### October 13 Companions release

Companions 0.5.76 derives from approved private Sound1 production source. The two unused offline suspension prototype/probe files are excluded; production retained suspension, existing developer features, assets and notices remain. The public project keeps its repository-relative shared-source links. Its final DLL is rebuilt and separately verified; prior private-DLL evidence is not relabelled as public-DLL gameplay acceptance. Set DOTNET_GENERATE_ASPNET_CERTIFICATE=false, DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 and DOTNET_CLI_TELEMETRY_OPTOUT=1 before building in a fresh CLI home.

### Companions 0.5.77 physics hotfix

This release preserves the 0.5.76 project and external references and changes only the physics safety wrapper and release metadata. Use the first-run certificate suppression above. Separate private test fixtures remain excluded from source and packages.

### October 16 Body Tools source publication

Body Tools 1.6.19 uses the accepted r3 production implementation and assets. Its project retains `Version=1.6.19` and `InformationalVersion=1.6.19-private.r3-startup`, with repository-relative shared-source links and `VintageStoryPath` external references. Build against Vintage Story 1.22.7 and keep dependency DLLs external. The accepted installer is preserved without rebuilding; a source build is a separate artifact. See [the release record](RELEASE_2026-10-16_BODY_TOOLS.md) for hashes, checks and the remaining remote multiplayer limitation. Private fixtures and evidence remain excluded.
