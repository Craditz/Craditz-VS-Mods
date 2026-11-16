# Animalica Core 1.2.40

Animalica players can feed a selected FotSA tameable with an accepted diet food without eating the treat themselves first. Aim away from the animal to eat normally. PetAI and the tameable's own behavior still decide accepted foods, adult/juvenile eligibility, ownership, taming progress and cooldown.

This corrects an interaction gap in Core 1.2.39. The October Companions 0.5.92 feeding correction shipped and remains present in 0.5.102, but its wild-animal route explicitly recognizes vanilla `game` entities with the exact `PetAI.EntityBehaviorTameable` class. FotSA species use their own domains and a derived tameable behavior. Core now recognizes the selected entity's `tameable` behavior contract when routing Core-managed diet food. No direct feeding call or hard PetAI dependency was added.

Ordinary vanilla food outside Core's diet overrides retains its existing behavior. No diet values, models or runtime assets changed. The grazer construction/eating lifecycle correction from Core 1.2.39 is retained.

## Downloads and source

- `Animalica_Core_v1.2.40.zip`: SHA-256 `364376e98c3c3c7dcece7673ddd5e72f46e195983d4764ea2ef1505210f74bb6`.
- `Animalica_Core_v1.2.40_source.zip`: SHA-256 `5c4ac532a1276919f520eecba6be7cafc711fa55df51bd763a84d5f6dfd76faa`.
- `SHA256SUMS.txt` lists both files. Install external dependencies and the desired Animalica body pack separately.

The stable installer retains every tested DLL, PDB, runtime asset and icon byte from the qualified candidate. The manifest and release documentation identify stable 1.2.40; the retained assembly informational version is `1.2.40-petfeeding-preview.1`. A source rebuild is a separate artifact. `RELEASE_SOURCE_MAP.json` in the source download records this provenance. License and third-party notices are preserved.

## Verification and limits

Real Linux Docker client gameplay used Vintage Story 1.22.7 with its integrated singleplayer server and native mouse input. The reproduction compared Seraph and an animal player that can eat the held meat. The correction was checked with Wolf, Fox and Spotted Hyena players against timber wolves, American lions and Patagonian foxes, including Passive behavior. Accepted food advanced native taming; tested cooldown holds, juvenile restrictions and invalid managed foods did not become a player meal. Ordinary self-eating while aimed away, vanilla fox feeding, ownership conversion and persistence were also checked. Public and corrected packages each completed a warm reload. Focused automated lifecycle/dispatch checks passed.

The primary comparison used Tamables FOTSA 0.8.7, PetAI 5.1.2, Caninae 1.1.10, Pantherinae 1.2.18, Feral Kinship 0.5.7, Companions 0.5.102, Foxes/Wolves/Hyenas 0.6.8, PlayerModelLib 1.25.0, Trait Restrictions 1.1.0, JsonPatchesLib 1.5.9 and OverhaulLib 2.0.10. Final stable-package checks also cover the current accepted FotSA 0.9.1 and Kinship 0.5.8 stack and Hooved Animals 0.5.4: Hyena feeding/self-eating/rejections, Mouflon wild hen/hare grass feeding, firepit placement, ordinary grass eating and all five kiln grass layers passed. Pantherinae was omitted from that last stack; lion coverage belongs to the primary comparison.

These are controlled singleplayer regressions, not the reporter's complete 94-mod environment or dedicated multiplayer. Entity movement was held still by a separate private setup/observation fixture; native feeding and eating handlers were not called by that fixture. No private test tools, saves, logs or settings are included in the downloads. The reporter separately reported success. This release does not claim a new owner-performed gameplay session or exhaustive coverage of all species, mods or foods.
