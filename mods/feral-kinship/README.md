# Feral Kinship

## 0.5.3 local release — 2026-09-30

Galeret cat kin now recognize FotSA Felinae, Pantherinae, and
Machairodontinae animals. The accepted r2 package restores the real root icon
bitmap and uses the short mod-list description. It was startup-tested with
Companions 0.5.69, Cats 5.0.2, and PetAI 5.1.2; the exact package was promoted
locally at SHA-256
`93E78988785701E5E84A85EB51286309834E7BCDA34181C1FBE3546868088179`.
The earlier package with the LFS-pointer icon was retained in Testing's
reusable folder. No public upload was made. See
`docs/RELEASE_2026-09-30.json`.

## 0.5.2 local release — 2026-09-22

Galeret/Cats kin taming, optional Butchering taming foods, and the Feverstone berry-feed duplication correction were manually accepted and promoted locally. Existing explicit taming lists must include `cat` to enable Cats. See `docs/RELEASE_2026-09-22.json`.

Feral Kinship is a separate universal code mod. Install it and PetAI on the
server and every client, or together in your singleplayer profile. The package
requires PetAI 5.1.1 or newer. The server decides taming outcomes and supplies authoritative
configuration. Feral Kinship does not modify Animalica.

Animalica Core owns Animalica's modular player diet and eater-dependent raw-food
nutrition system. Feral Kinship does not patch player food nutrition.

## Optional PlayerModelLib marker

PlayerModelLib 1.21.1 stores the selected model code in the player's synced
`WatchedAttributes` under `skinModel`. Its `PlayerSkinBehavior.CurrentModelCode`
property mirrors that value. Model changes are written on the server and the
`skinModel` path is marked dirty for synchronization.

PlayerModelLib is optional. When it is installed, Feral Kinship can recognize a
matching player and apply the kin taming bonus. Without it, players still have
access to the normal adult taming system; they simply do not receive a model-
based kin bonus unless another mod supplies the `feralKinshipRace` override.

Modern PlayerModelLib versions namespace that value as `domain:model`, such as
`feralpack:hare-male`. Feral Kinship accepts both namespaced and legacy bare
model codes. Normal aliases must be exact or followed by a model separator;
only Animalica's known joined fox, raccoon, and wolf carry variants accept a
joined suffix.

The mod recognizes the actual Animalica model codes and their carry variants,
plus a small set of compatible PlayerModelLib packs. FotSA Felinae,
Pantherinae, and Machairodontinae use optional entity-definition patches to
route their released flee, seek, and melee task codes through the same kin-aware
wrappers. Each operation is gated on that source mod's `modid`; its original
task configuration and base behavior remain intact. Other FotSA families are
not included.

| Player models | Kinship animals |
|---|---|
| `blackbear-*`, `brownbear-*`, `polarbear-*`, `giantpanda-*`, `sunbear-*` | Bears |
| `chicken-*` | Chickens, including babies |
| `cats-adult-cat`, `cats-kitten`, `cathay`, `galeret`, `suthay`, `suthayrath` | Native Cats, FotSA Felinae, Pantherinae, and Machairodontinae |
| `deer-*`, `feraldeer-*` | Deer, including babies |
| `feralfish` | Freshwater, reef, and saltwater fish |
| `feralfox*` | Foxes, including babies |
| `fox` | Foxes, including babies |
| `gazelle-*` | Gazelles, including babies |
| `goat-*` | Goats, including babies |
| `hare-*` | Hares, including babies |
| `spottedhyena-*` | Hyenas, including babies |
| `pig-*` | Pigs, including babies |
| `feralraccoon*` | Raccoons, including babies |
| `sheep-*` | Sheep, including babies |
| `feralwolf*` | Wolves, including babies |
| `lupine`, `wolp` | Wolves, including babies |
| `raccoonplayermodel` | Raccoons, including babies |

Anthro Players (`furry`) compatibility uses these additional model codes:

| Player models | Kinship animals |
|---|---|
| `furry:bear` | Bears |
| `furry:bird` | Chickens |
| `furry:deer`, `furry:deer_antler` | Deer |
| `furry:fox`, `foxxoplymdl:fox` | Foxes |
| `furry:hyena` | Hyenas |
| `furry:rabbit` | Hares |
| `furry:raccoon` | Raccoons |
| `furry:wolf` | Wolves |

Another mod may set the synced string attribute `feralKinshipRace` to override
the inferred player race without referencing PlayerModelLib's public classes.

## Behavior

Only vanilla AI tasks that can target a player are replaced. Passive animals
ignore matching players in flee targeting. Predators also ignore matching
players in normal seek and melee targeting. Other players, predators, prey,
bobbers, and unrelated threats remain valid targets.

When matching players damage an animal, that individual animal remembers each
recent attacker for at least 10 seconds and can use its normal flee or
retaliation behavior against those players. Existing emotion-state durations
may keep the reaction active longer.

The patch list is generated and validated against the selected Vintage Story
installation by `tools/Generate-VanillaKinshipPatches.ps1`. The current release
was checked against vanilla 1.22.5. Generation stops before writing files if an
expected AI task index now contains a different task code.

## Basic tamed animals

Feral Kinship owns the PetAI-backed tame conversion layer for adult and optionally
baby bear, chicken, deer, moose, fox, gazelle, goat, hare, hyena, pig, raccoon, sheep,
and wolf entities. The adult and baby patches and basic tame entity definitions live
in this package. The
Companions package layers its shared social, command, combat, home, cargo, and
expedition behavior onto all thirteen core tame species. Without Companions,
Ctrl-right-click exposes PetAI-style Sit, Lie, Stay, Follow, Passive, Neutral,
Protective, and Aggressive commands. Each species uses its own vanilla-grounded
base damage, and core combat never targets players or other domesticated
animals. Tamed animals also retain the standard 24-hour mortally-wounded rescue
window and complete wounded/death animation support without requiring
Companions. Further hits cannot erase that rescue window; hold a vanilla
healing item, hold Ctrl, and right-click the animal to begin recovery. Core also
owns the shared lowercase animation vocabulary used by every species, so its
walking, running, attacking, lying, and wounded poses remain available with or
without Companions. Follow itself is a small Feral Kinship fallback that avoids
PetAI's broken constructor path. When Companions is installed, its overlay
removes that basic menu and replaces the whole task list with the advanced
journal-controlled AI.
Core tames retain their own vanilla hurt, death, and ambient voices. Deer and
goats keep type-specific sound families; fox and wolf vocalizations use low
ambient chances instead of remaining permanently silent after taming.
Fish and third-party compatibility-pack entities are deliberately excluded. Baby
taming is available for the vanilla land-animal babies that have matching adult
definitions; it is disabled by default and controlled by the server config below.

Tame entities use a companion-friendly movement collision width capped at 0.9
blocks so even large species can traverse ordinary one-block stairs and doors.
Their species-sized selection boxes remain unchanged for interaction and combat;
wild animals retain their vanilla collision dimensions.

Taming conversion preserves meaningful visual forms. Elder Eurasian pigs keep
their elder shape instead of becoming ordinary adults, while legacy tame pig
codes remain registered for existing saves. Vanilla sex-based model scaling is
retained for bears, deer, goats, pigs, and wolves. Deer antlers, goat horns,
pig tusks, and sheep horns retain their vanilla growth inventories and cycles
through matching tame-side behaviors.

## Taming behavior

All players can tame the supported adult animals using the normal PetAI food
rules. If baby taming is enabled, the same supported food rules apply to supported
vanilla babies. A baby that finishes taming remains owned and grows into the
matching Kinship tame-adult variant. Matching kin receive a built-in 3x progress bonus for ordinary accepted
foods. The bonus is applied per interaction, so the shared PetAI treat values
remain unchanged for ordinary players.

Perfect PetAI cookies are the premium kinship shortcut. Matching kin can tame
with one perfect cookie interaction. Herbivores accept the perfect veggie
cookie, carnivores accept the perfect meat cookie, and omnivores accept either.
Raw dough and part-baked cookies are not taming treats.

The matching-player check still affects kinship AI targeting. Basic taming
remains available without PlayerModelLib. A supported synced `feralKinshipRace`
override is checked before `skinModel` and can still provide matching kin
recognition and bonuses without PlayerModelLib. Unrelated players do not
automatically count as kin.

Galeret's four feline model keys also match the FotSA Felinae, Pantherinae, and
Machairodontinae entity-code families as cat kin. These optional animal packs
remain dependency-free; unrelated FotSA families keep their existing race.

## Server taming allowlist

The server creates `ModConfig/FeralKinship.json` with every supported taming
group enabled:

```json
{
  "AllowAdultTaming": true,
  "AllowBabyTaming": false,
  "EnabledTamingAnimals": [
    "bear",
    "chicken",
    "deer",
    "fox",
    "gazelle",
    "goat",
    "hare",
    "hyena",
    "cat",
    "pig",
    "raccoon",
    "sheep",
    "wolf"
  ]
}
```

Set `AllowAdultTaming` and `AllowBabyTaming` independently: adults only is the
default, babies only is possible with adult taming set to `false`, and both can be
enabled together. Remove an entry to disable taming for that group at either age.
The list is server-owned and
validated when the server starts; names are case-insensitive, duplicates are
removed, and unknown entries are ignored with a server-log warning. If a
non-empty list contains no valid names, the server keeps all supported taming
enabled. An explicitly empty list disables all supported taming. `deer`
covers the deer family, including moose and its variants. This setting affects
taming only; kinship recognition and AI behavior continue to work for disabled
groups. New config changes require a server restart.

Feral Kinship and its companion package update their JSON and commented name
configuration files by adding missing settings while preserving existing user
values and unknown/custom entries. Name-pool lists are not automatically
expanded when a release adds a new default name; the server owner remains in
control of those lists.

Build the two packages:

```powershell
dotnet build FeralKinship.csproj -c Release -p:VintageStoryPath="A:\Vintagestory"
dotnet build companions/FeralKinshipCompanions.csproj -c Release -p:VintageStoryPath="A:\Vintagestory"
```

## Build

The project targets the .NET version used by Vintage Story 1.22. Set
`VintageStoryPath` if the game is not installed at `A:\Vintagestory`:

```powershell
dotnet build -c Release -p:VintageStoryPath="A:\Vintagestory"
```

The release archive includes a built `FeralKinship.dll`. Rebuild it
after changing the source, then place the new DLL at the mod folder root before
packaging.

The stabilization change list and remaining PetAI boundaries are recorded in
`docs/STABILIZATION_CHANGELOG_v0.3.2_v0.1.35.md`.

## Current local release — 2026-10-04

Version **0.5.4** is promoted by explicit owner approval with partial QA and waived multiplayer. See [CHANGELOG_v0.5.4.txt](CHANGELOG_v0.5.4.txt) and [release record](docs/RELEASE_2026-10-04.md).

Ordinary commands on a dedicated tame type were observed. The core fox retained slight local movement during Stay. Actual pending-path races, timeout, dimension mismatch and owned-pet death cleanup were not established. Prior Wolp/Lupine ordinary-food progression and broader mismatch remain unresolved.
