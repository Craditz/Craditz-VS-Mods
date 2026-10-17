# Companions — changes since 0.5.74

- Adds wellbeing Care Cards, favourite foods, comfort-bed recovery and clearer mood/work readiness information.
- Adds optional crop reseeding, fertilizer tending and trough refilling using real supplies, with claims and care requirements respected.
- Fixes newly tamed companions remaining frozen or unavailable until whistled; ownership and naming can finish before admission.
- Improves expedition return/recovery across unloaded chunks and restarts while retaining the original animal, cargo and once-only rewards. Improves supported FOTSA/Critters recruitment and habitat placement.
- Adds source-derived FOTSA breeding/growth and extends existing species training, expedition affinity and majority bonuses to explicitly supported FOTSA adults. Applies the existing adult 10 HP minimum while preserving higher native health and injuries.
- Reduces idle barking/howling and improves Care Card, den/whistle and bonus text layout.
- Adds a persistent Cart radius-notice toggle; fixes filtered roster selection, Locate status, dead/missing-provider records and production milestone credit.
- Guards the companion physics wrapper against partially initialized entities and missing references, addressing repeated null-reference failures that could interrupt physics ticks.
- Fixes legitimate FOTSA calf-to-adult growth being mistaken for a competing companion identity. Preserves owner, name and identity so new grown elephants remain visible and usable with the matching Tamables FOTSA 0.9.1 native-form/riding changes. Existing saved duplicate/retained Hold markers remain protected.
- Fixes missing food contracts for WolfTaming dogs and affected FOTSA/Critters forms. Restores native dog mouth-food consumption; Companion hunger credit follows actual consumption rather than pickup/obedience alone. Backpacks remain storage.
- Improves raw-food, kibble, fully baked pet-cookie and specialist-food matching without widening intended species diets. A blocked Dining Board no longer monopolizes retries when another eligible board is available; food diagnostics now show diet and board/task eligibility.
- Adds compatibility mappings for audited optional food preparations: **probably works now, not tested**.

Representative gameplay on Vintage Story 1.22.7 passed Dining Board and manual feeding for the representative WolfTaming dog, FOTSA cow and isolated Critters chipmunk. Both old and newly tamed dogs passed the board check. Elephant growth/riding regression also passed in the approved paired builds. This does not claim gameplay coverage for every species, food mod or multiplayer case. The chipmunk's earlier journal failure was a wrong-target interaction with the nearby invisible dragon; both chipmunk feeding routes passed after isolation.

TODO: the invisible bearded dragon remains unresolved and is outside these fixes. Recovery/pathing oddities and the cow texture warning remain separate observations.

Restart the game/server after replacing Companions; use matching approved Tamables FOTSA 0.9.1 where applicable. Existing dependency minima remain unchanged.
