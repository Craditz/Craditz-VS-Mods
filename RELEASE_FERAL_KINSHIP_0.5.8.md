# Feral Kinship 0.5.8 — bear raw-fish microfix

Adult bears now accept vanilla raw-fish fillets through actual held-item input,
with their normal 25% taming progress and three-hour care cooldown. On accepted
feeding, inventory synchronization and held-food handling prevent delayed player
eating when the completion Pet Profile closes. Existing diets and ordinary
eating when no treat is accepted remain.

Installer `FeralKinship_v0.5.8.zip` SHA-256:
`df716b111064b2a29680501ed47617d0513721ab2218d5707eb67d055b0ab82d`.
Runtime DLL SHA-256:
`bb5d784b5bc21a0b1c8fc0e36622e9a6bed955a41b3e63d5269f125b99993eb0`.
The released installer retains the exact native-tested bytes; source rebuilds
are separate artifacts. Source ZIP and checksum file accompany the release.

Production build passes without warnings/errors; 596 compiled wolf/bear checks
pass. Native offline Docker testing used VS 1.22.7 and PetAI 5.1.2 with PML,
JsonPatches and Overhaul. Four actual short feeds consumed exactly four fish,
progressed to native tame ownership, synchronized counts with a lagging client
calendar, rejected a cooldown repeat, and retained four fish after Pet Profile
closed. Save/reload preserved ownership, progress, exact cooldown and inventory.
Raw-meat and no-target ordinary-eating controls pass.

A continued 1.75-second hold can still feed once, then eat ordinarily during
cooldown while the input remains held. That control consumed two fish with one
progress increase, with actual native caller stacks distinguishing the meal
from the treat. This release corrects the unintended delayed meal after a short
completion click; it does not change ordinary held-input eating semantics.

The native fixture places and freezes only marked disposable QA bears, controls
inventory/calendar and keeps the test actor alive; feeding and eating use real
player input and production code. Compiled checks use doubles, and are not
substituted for native acceptance. Native gameplay coverage is one brown female;
other bear sexes/types and configuration boundaries have compiled checks.
Full multiplayer, the affected player's complete modpack and new owner hands-on
acceptance are not claimed. Install third-party dependencies separately.

FOTSA model-pack work, unrelated sources and Windows Testing are excluded. No
ModDB upload is represented by this GitHub/local-shelf release.
