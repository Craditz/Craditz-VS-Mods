# Feral Kinship Companions 0.5.77

- Fixes a null-reference failure in the companion physics safety prefix that
  could repeatedly interrupt physics worker ticks and leave entities frozen.
  Unrelated or partially initialized bodies are now safely excluded before
  companion lookup; missing inputs and optional diagnostic services are guarded.
- Preserves the existing finite-vector repair for Present companions and the
  retained expedition Away gate. Original game physics still runs and its own
  exceptions remain visible.
- Retains all 0.5.76 features, settings, dependency minima and supporting assets.
  This release changes only the physics safety wrapper and release metadata.
  No admin-cart or broader tame/hostile compatibility changes are included.

The exact null path was reproduced against the published 0.5.76 DLL. The same
unsafe prefix exists in 0.5.75; new introduction in 0.5.76 is not established.
Normal arrows use separate passive physics. Repeated failure in the controlled
worker loop can affect later entities, but the reported user's exact live
trigger remains unknown. This release does not claim every physics fault fixed.

Qualification: the frozen minimal private candidate passed native prefix 31,
Active-Away 16, fresh admission 69, retained core 13, lifecycle 1265 and concurrent
return 1244 assertions. Counts overlap; world/service boundaries are controlled
offline fixtures. Independent review passed 925 and package audit 728 checks.
The candidate's isolated native smoke passed ordinary arrow flight/impact/
recovery, Present movement, Away through full restart and natural return of
four healthy companions; 20 closed logs had no PhysicsSafetyPatch NRE/error.
Both game runs exited 0 and SQLite checks passed. The public release has its own
rebuilt DLL/hash and targeted verification receipts; private results are not
relabelled as exact-public-DLL gameplay. No broad new claim is made for every
provider, multiplayer, wellbeing, breeding, rewards, bonuses or audio.

Restart the server/client after replacing the previous Companions package.
Existing dependencies and configuration defaults are unchanged.
