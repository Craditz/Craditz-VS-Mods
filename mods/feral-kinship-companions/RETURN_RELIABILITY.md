# Expedition returns

Normal returns preserve the original animal and native cargo. No replacement is created.

1. Safe ground near the owner's validated pack cart.
2. Safe ground near that companion's still-assigned, verified bed.
3. Safe ground near the player only while inside the cart's current work territory in the same dimension. The condition is checked again on arrival.
4. Stay suspended, show/log the blocker, and retry about once per minute.

When a cart is confirmed missing, automatic placement pauses before bed/player fallback. A popup offers "Place near me" or "Wait for a replacement cart". Placement consent explicitly permits arrival near an exploring player. Closing chooses waiting. Offline owners keep the pending choice until reconnect. A merely unloaded or unverifiable cart does not count as missing.

Loading uses bounded, temporary requests with KeepLoaded=false. The mod cannot cancel a native engine request whose callback never acknowledges. It checks for actual publication/completion and avoids overlapping teleports or flooding duplicate loads. A genuinely unresponsive engine request remains diagnosed and suspended rather than spawning a duplicate. Actual identity/snapshot conflicts remain protected.

Schema 61 preserves return receipts alongside the integrated schema 58/59 recovery journals. Public 57 and prior return schema 60 also upgrade. Use a complete matching world/mod backup for rollback. Do not edit schema headers to force older readers.

The controlled test packet covers callbacks, consent/network routes, source loading, destination fallback, interruption/save-order states, clocks and critical injury. It does not prove real native-database hard-crash ordering, rendered popup layout, multiplayer/client arrival, optional-provider behavior or gameplay performance. The user reports an expedition test of the preceding return candidate. The user subsequently tested both rebuild paths and expedition detachment. Broader multiplayer validation remains pending. No game client/server was launched for validation.
