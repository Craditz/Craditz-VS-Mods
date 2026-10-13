# Feral Kinship Companions 0.5.76

- Fixes freshly tamed companions remaining frozen or unavailable to right-click
  until the first whistle. Admission and naming now retry after provider
  ownership and native publication are ready.
- Keeps supported companions on their original native body during expeditions:
  Away bodies reject ordinary damage and suppress animal sounds/dialogue. Return
  handles unloaded source chunks, restart/interrupted placement and occupied
  destinations without replacement bodies or duplicated cargo/rewards.
- Fixes source-derived FOTSA/Critters recruitment, with matching species,
  supported habitat placement and persistent once-only reward receipts.
- Adds wellbeing Care Cards and mood details based on food, home/rest, health,
  safety/shelter, company/enrichment, workload, expedition outcomes and progress.
  Includes favourite foods, comfort-bed recovery and wellbeing-based work/rest
  readiness. Food gathering and essential care remain available during recovery.
- Adds opt-in crop reseeding, fertilizer tending and finite trough refilling.
  They use real seeds/supplies, honour claims and wellbeing/home readiness, and
  preserve in-progress trough cargo through saving.
- Applies the existing 10 HP adult Companion minimum to FOTSA animals while
  retaining higher native bases, injuries and zero HP during registration,
  reload and juvenile-to-adult growth.
- Adds source-derived FOTSA breeding/growth: exact family/sex offspring and
  native gestation/litter/cooldown definitions, with native/custom pregnancy
  arbitration and persisted birth completion to avoid duplicate births.
- Extends existing expedition species affinity, training/mastery and strict
  majority bonuses to explicitly mapped FOTSA adult species. Unknown codes and
  juveniles do not receive guessed bonuses. The existing bonus formulas remain.
- Reduces ambient vocalizations: restored adult sound tasks emit once per run;
  randomized 60-120 real-second cooldowns stagger each animal's first cue;
  native random idle chance is multiplied by 0.25. Client-native and server-task
  budgets are separate. Warning, panic, hurt/death, combat and command paths stay
  intact, and IdleSounds keeps its existing adult task-installation meaning.
- Improves Care Card, den/whistle heading and bonus-footer text layout.

Already in 0.5.75 and retained: Cart radius notice toggle, filtered roster
selection, Locate status, dead-record/provider recovery and production
milestone credit fixes. General FOTSA taming, core breeding, expedition species
bonuses/training and majority synergy existed before this release; the changes
above extend or harden those systems.

New settings in ModConfig/FeralKinshipCompanions.json:
AmbientIdleSoundChanceMultiplier=0.25,
AmbientIdleSoundMinCooldownSeconds=60, AmbientIdleSoundMaxCooldownSeconds=120,
FavoriteFoodCandidates=["*:*"], ReseedingUnlockWellbeing=80,
ReseedingPauseWellbeing=75, FertilizerTargetNutrient=80, FertilizerMaxOvershoot=10.
Extra duties are opt-in per companion. IdleSounds=true and StfuMode=false remain
the defaults. Restart the server after configuration changes.

The owner explicitly authorized release while accepting pending listening,
multiplayer audio, natural return and broader physical-fall/provider checks.
Prior private Sound1 has 107 sound checks and 18 inherited offline regression
runs; those results belong to that exact private DLL. Final public build/audit
and any exact-public-DLL checks are recorded separately. Offline mocked-world
tests do not establish full gameplay acceptance or every-provider audio.
