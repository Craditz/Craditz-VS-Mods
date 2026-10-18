# Companions 0.5.80 - stable release

Fixes expedition-start audit refusals with attached add-ons. Unknown/changed add-on implementations are admitted by default while native identity/save/cargo, known-clock, duplicate/Hold and return protection remain. Patch installation stays bounded to previously audited implementations.

The owner reports both affected users confirmed the fix and explicitly approved release. This package finalizes 0.5.80-compat-first-preview.2 without rebuilding: the DLL, PDB and every runtime asset remain byte-identical. Manifest version and release documentation change only. Stable metadata itself has not been separately replayed in gameplay.

Unknown callbacks and effects may continue, expire or change state while Away. See TECHNICAL_NOTES_v0.5.80.md for 55 controlled plus 21 native checks and pending active-effect/real-cargo/gameplay tests.

Tested preview ZIP SHA256: 60a940ddb4a71e9c8ff4b5a44c7ed939f086895545aea4711cb767218a110033
Unchanged DLL SHA256: cb1bb3af837b4b2f94449544dbb241c3d268fe66059b5c6267204cb0c59982b5

Restart the game/server when replacing the mod. License, notices, sound credits and dependencies remain unchanged. No ModDB upload or Discord announcement is included.
