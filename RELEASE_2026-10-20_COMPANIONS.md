# Companions 0.5.91 - user Restore availability

## Changes since local stable 0.5.90

- User Restore finds the verified original at its saved location and brings that same body to safe loaded space near its owner in the same dimension. Native chunk membership transfers before the temporary remote source can unload.
- Restore completion now waits for native world/chunk registration, the normal command lookup and usable presentation near the owner. Pending arrival, timeout, no safe space and other-dimension failures are reported honestly.
- Cart feedback distinguishes an unloaded animal, an unavailable cart, unresolved owner/expedition authority and different dimensions. Stale cached native IDs or objects cannot masquerade as loaded companions.
- Living originals retain their native ID, health, inventory and progression; no replacement or copied cargo is created. Existing owner authorization, duplicate/stale-ID refusals and Emergency/Undo/escrow safeguards remain.

The promoted 0.5.90 DLL reproduced success followed by remote source unload and cart failure in a controlled fixture. This supports the unload mechanism; it does not independently establish the complete affected-world cause from the supplied recording.

## Qualification and release decision

The owner explicitly requested release for server use after the private candidate. All **103 controlled groups passed**: 91 retained recovery regressions and 12 user-Restore groups. Coverage includes the non-admin pack request/confirmation route, delayed native teleport, source unload followed by a cart request after 60 simulated seconds, repeated Restore, disk fixture reload, changed owner, incomplete/throwing chunk registration, absent loader callback, no safe space, dimensions, genuine native-ID drift, conflicting IDs, duplicates and stale caches.

These use production handlers and real Vintage Story 1.22.7 assemblies with controlled world/chunk services. They are not rendered GUI testing or a full affected-world/provider/multiplayer run. **Affected-server gameplay, FotSA/PetAI behavior, visible client arrival and full engine ticking remain unverified.** No affected-server acceptance is claimed. The production build had zero warnings/errors. Private fixtures and reporter data remain local.

Stable finalization preserves the tested 0.5.91-userrestore-preview.1 DLL and all 350 runtime assets byte-for-byte; only the installer manifest and release documentation are finalized. The DLL retains its preview informational version for traceability. A new build from source is a separate artifact and must not replace the retained tested installer bytes.

## Updating and rollback

Use matching Companions versions on server and clients, with one installed archive per mod ID. Manifest minimums remain game 1.22.0 and Feral Kinship 0.4.3; this candidate was built against game 1.22.7. Optional providers and their requirements remain external.

Ledger schema **57** is unchanged from 0.5.90. Keep a complete pre-update world backup and its matching mods. Older schema readers cannot safely downgrade the world by swapping a DLL; per-animal Undo is not a whole-world rollback.

No server installation, live-save/modpack mutation or ModDB upload was performed. The stable ZIP is prepared for the owner to upload. Trademark changes remain deferred; there are no unrelated fixes.

## Public source synchronization

The previous public source was 0.5.84; the actual prior local stable was 0.5.90. This publication catches up the already locally released 0.5.86-0.5.90 recovery lifecycle implementation as well as the 0.5.91 user Restore change. It does not claim that those intermediate versions were previously synchronized publicly.

That catch-up includes guarded native-ID relinking (automatic relinking remains off by default), confirmed admin Emergency force reconstruction, persistent per-animal Undo and separately retained originals/cargo, engine-assigned native-ID binding, one selected active generation across reload, legacy-history reconciliation, species formatting and exact total 2x2/3x3 flaps. It introduces no new recovery bypass beyond the approved implementation.

Only matching production source, assets, build metadata and sanitized release documentation are published. Private tests, logs, reporter IDs/recordings, local paths, binaries, archives, credentials and unreleased projects remain excluded. Existing licenses, sound credits and upstream notices are retained.

Stable ZIP SHA256: `1739bd00070d23a6deaac68936089ce65fffba5564cb9c228584770ef85afa66`. DLL SHA256: `8ca3e3f4a891959eac8d11a6cb4ff5e7911fd4479cc54ff914e3f0bc82d52b12`. Complete source ZIP SHA256: `bc978f0bf73ab539839c1cf0aaf49496b62e4c42c97b4f8639d064cb9e54332f`.
