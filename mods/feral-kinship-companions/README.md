# Feral Kinship Companions

Source for version **0.5.73**, from commit `941adfb2`.

The manifest and runtime C# are kept as they were in that version, including the
developer and diagnostic features that shipped with it. Separate tests, fixtures,
checkers, private notes and alternate source versions stay out of this repo.

This folder has runtime source and selected configs, language files and patches.
Models, art, audio and other generated or externally sourced data are left out
where their source-sharing terms or complete generation process haven't been
established. These files won't reproduce a complete release ZIP.

See [BUILD.md](../../BUILD.md) for build requirements and the limits of the compilation checks.

A successful compile doesn't verify an exact match with the release package or
how it behaves in game. No license has been applied, and the original manifest
credits are preserved.

[Feral Kinship](../feral-kinship/README.md) lives next door. Companions' manifest
still requires `feralkinship >= 0.4.3`. At build time, it links Kinship's unchanged
race resolver and the shared config migration source at the repository root.
