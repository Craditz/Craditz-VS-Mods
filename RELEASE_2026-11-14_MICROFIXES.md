# November 14, 2026 microfixes

## Craditz's Crooked Codex 0.11.12

Mana Gem, Travel Mark and Holy Hand Grenade now expose the vanilla tooltip
argument names expected by optional Trait Restrictions. This removes the three
Harmony binding failures while retaining the Party Foul tooltip safety fix.
No new dependency, spell or balance change. Requires Vintage Story 1.22.x and
separately installed Rustbound Magic 4.0.7.

Installer: `Craditzs_Crooked_Codex_v0.11.12.zip`.
SHA-256: `f0ce3197dbc7c87a011b67e1a496861a30a60bd15fcf8f126d83377ad9b7d0a6`.
Its DLL and assets retain the qualified preview bytes; the stable manifest and
changelog are updated. Installer and source ZIPs have separate checksum entries.

Validation: 4,814 offline assertions; stable/candidate/without-TR native controls;
the final archive enters VS 1.22.7 with real Trait Restrictions 1.1.0 hooks active.
All ten final native ClientMain tooltip calls pass. Actual visible inventory
hovers cover Mana Gem, marked Travel Mark and armed Holy Hand Grenade. The native
Mana Gem handbook page and hover display correctly. The portable public source
build passes with zero warnings/errors and all 1,862 method bodies match the
tested DLL. Fixtures assign real inventory states; UI input and production
tooltip methods are real. No mock provider or substitute tooltip result is used.

These are scoped agent-observed checks following the owner's release instruction,
not a new hands-on owner test or a full multiplayer/full-modpack acceptance.
Baseline Creative-join inventory-null and OpenAL errors remain separate issues;
the affected player's full Place on Slabs mod set is not independently retested.
Upstream notices remain unchanged. Local poke tests, private data, unrelated
unreleased projects and FOTSA model-pack changes are excluded. No ModDB upload
is claimed by this GitHub release record.
