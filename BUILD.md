# Building the mods

If you're here to build something, this is the bit you need.

## What you'll need

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

## What's still missing

A successful compile won't give you the published ZIP or verify how it behaves
in game. Model geometry, some generated assets, icons and copied or derived art
are left out where their redistribution terms haven't been verified. Some configs
were kept only where they match the selected local release files. Some content
packs contain just metadata or part of their configs, so these folders can't
produce complete packages.

Requests has runtime source here, but its generation tools and donor inputs are
left out. Core has an optional body-center generator, but it needs model inputs
that aren't included. The full generators and their authored inputs still need
checks on where they came from and what can be shared before complete package
builds can be called reproducible.

## Companions, Body Tools and Radio

Companions 0.5.73, Body Tools 1.6.17 and Radio 1.2.2 keep the developer and
diagnostic features that shipped with those versions. Their separate test and
checker projects stay out of this repo. The Companions 0.5.74 local candidate
isn't included.

- Companions keeps its released UI constants; no alternate UI build was selected
- Body Tools uses external game/Harmony references
- Radio uses external game/protobuf references. Its project configuration now
  uses `VINTAGE_STORY` instead of the old fallback to a local experimental game
  installation. That change is limited to project configuration

These three folders have content left out and aren't complete mod packages.

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

## Adding files to the repo

Ignore rules help catch mistakes, but check the actual Git index before each
upload. The assets included so far are text. If approved large binary source
files are added later, use Git LFS for their specific paths and check the real
file contents, not just the pointer files.

Keep downloadable release ZIPs as separate release assets.
