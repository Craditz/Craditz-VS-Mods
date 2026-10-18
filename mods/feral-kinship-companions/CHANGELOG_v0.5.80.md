# Companions 0.5.80 - changes since 0.5.79

- Fix expedition-start refusals caused by attached add-on entity, behavior or AI implementations missing from the retained-body audit, including the reported XSkills/Rustbound cases.
- Admit unknown or changed add-on implementations by default, while retaining native identity, save/cargo authority, known clocks, duplicate/Hold and once-only return safeguards.
- Keep patch installation limited to previously audited code; no arbitrary add-on patching, behavior replacement or new dependencies.

The owner reports both affected users confirmed the fix and explicitly approved release. This is owner-reported confirmation, not comprehensive gameplay validation by the release preparer.

Unknown add-on callbacks and effects may continue, expire or mutate state while Away. See TECHNICAL_NOTES_v0.5.80.md for tested scope and incomplete active-effect/cargo checks. Finalization from 0.5.80-compat-first-preview.2 changes manifest/documentation only; DLL, PDB and runtime assets are unchanged.
