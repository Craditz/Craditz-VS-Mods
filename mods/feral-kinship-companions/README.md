# Feral Kinship Companions 0.5.94

Optimum-aware save compatibility update, based on public 0.5.93. See
CHANGELOG_v0.5.94.txt and RELEASE_NOTES.md for the exact scope and test limits.
The already-released 0.5.93 recovery/return behavior is retained without new
changes. Writer schema remains 61, admitting the same known 57–60 payloads.

Requires Vintage Story 1.22.7, Feral Kinship 0.5.5 and its PetAI requirement.
Use matching Companions packages on clients and server. The tested Optimum
set is 0.3.21. Native structural/behavioral contracts remain fail-closed.

Build using .NET 10 and your own game references:
`dotnet build mods/feral-kinship-companions/FeralKinshipCompanions.csproj -c Release -p:VintageStoryPath="<your game installation>"`
Paths and version metadata are reproducible; engine/provider binaries, tests,
saves, logs and private support material are not distributed in source exports.
Existing licenses, sound credits and third-party notices are preserved.

Keep complete matching world/profile/mod backups; restoring a DLL alone is not
a safe rollback. Do not force an unsupported startup or downgrade save schema.
