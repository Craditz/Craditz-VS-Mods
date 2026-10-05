# Building this source subset

Use .NET SDK 10 and a separately installed Vintage Story version compatible with
the manifest of the chosen mod. Supply your own game DLLs. Download required
third-party mods from their official pages and extract their DLL locally outside
this source tree. Do not add game DLLs, PlayerModelLib or PetAI DLLs to Git.

External sources: [Vintage Story](https://www.vintagestory.at/),
[PlayerModelLib](https://mods.vintagestory.at/playermodellib),
[PetAI](https://mods.vintagestory.at/petai).

For a project listed below:

```powershell
dotnet build <project.csproj> -c Release -p:VintageStoryPath="<game-directory>" -p:PlayerModelLibPath="<external-PlayerModelLib.dll>" -p:PetAIPath="<external-PetAI.dll>"
```

Only supply dependency properties required by that project. The manifests retain
their selected release versions and credits. Dependency versions in these
manifests are release requirements, not a list of the latest published versions.
Runtime C# source is unchanged from the
selected commit; project path changes only accommodate this portable layout.

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
- `mods/feral-kinship/companions/FeralKinshipCompanions.csproj`

## Limits

Compiling does not reproduce the published ZIP or establish in-game behavior.
Model geometry, some generated assets, icons and copied/derived art are absent
where redistribution terms have not been verified. Some configs are retained only
when they match a selected local release payload. Content-pack folders may contain
only metadata or a partial config subset and cannot produce complete packages.

Requests includes runtime source, but its generation chain and donor inputs are
withheld. Core includes an optional body-center generator; it cannot run fully
without the withheld model inputs. Complete generators and their authored inputs
will need provenance review before packaging can be described as reproducible.

Ignore rules are preventive only; review the actual Git index before any upload.
Current selected assets are text. If approved large binary source is added later,
use Git LFS for specific asset paths and verify real payloads, never pointer text.
Keep downloadable release ZIPs as separate release assets.

Companions 0.5.73, Body Tools 1.6.17 and Radio 1.2.2 retain their released embedded
developer/diagnostic code. Their separate test/checker projects are excluded.
The approved Companions 0.5.74 local candidate is not this selected release baseline.
Companions links the retained parent Kinship race resolver and the shared config
migration source. Its UI constants remain unchanged; no alternate UI build was
selected. Body Tools uses external game/Harmony references. Radio uses external
game/protobuf references; its former local experiment-installation fallback was
replaced only in project configuration with `VINTAGE_STORY`.

All eleven selected code projects compiled against local external references.
Requests was compiled again after moving under `mods/animalica/requests`.
The three added folders have withheld content and are not complete mod packages.
